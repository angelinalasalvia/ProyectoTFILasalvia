using BLL;
using BE;
using DAL;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree; // FastForest (Random Forest) vive acá

namespace BLL;

public class ChurnModelService
{
    // La BLL depende de la INTERFAZ del repositorio (IChurnRepository), no de
    // ChurnDbContext ni de EF Core directamente. Así, esta capa no sabe (ni le
    // importa) cómo se guardan los datos - solo le pide cosas al repositorio.
    private readonly IChurnRepository _repositorio;
    private readonly ILogger<ChurnModelService> _logger;
    private readonly MLContext _mlContext;

    public ChurnModelService(IChurnRepository repositorio, ILogger<ChurnModelService> logger)
    {
        _repositorio = repositorio;
        _logger = logger;
        // La "seed" fija hace que el entrenamiento sea reproducible (mismos
        // resultados en cada corrida con los mismos datos) - útil para el TP.
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// Punto de entrada único: entrena el modelo con TODOS los clientes
    /// (activos e inactivos, porque necesita ejemplos de ambos casos para
    /// aprender), predice sobre los clientes actualmente activos, y guarda
    /// el resultado a través del repositorio.
    /// </summary>
    public async Task EntrenarYPredecirAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando ciclo de entrenamiento y predicción de churn...");

        var fechaReferencia = DateTime.Now;

        // 1) Traer todos los clientes con sus eventos (vía DAL).
        var clientes = await _repositorio.ObtenerClientesConEventosAsync(cancellationToken);

        if (clientes.Count < 20)
        {
            _logger.LogWarning("Muy pocos clientes ({Count}) para entrenar un modelo confiable. Se aborta el ciclo.", clientes.Count);
            return;
        }

        // 2) Convertir cada cliente en un registro de features.
        var todasLasFeatures = clientes
            .Select(c => new ClienteConFeatures
            {
                IdCliente = c.IdCliente,
                Features = FeatureEngineering.Construir(c, c.Eventos.ToList(), fechaReferencia)
            })
            .ToList();

        // 3) Entrenar el modelo con el dataset completo (activos + inactivos).
        var (modeloEntrenado, metrica) = EntrenarModelo(todasLasFeatures.Select(x => x.Features));
        _logger.LogInformation(
            "Modelo entrenado. AUC: {AUC:P1} | Accuracy: {Acc:P1} | F1Score: {F1:P1}",
            metrica.AreaUnderRocCurve, metrica.Accuracy, metrica.F1Score);

        // 4) Calcular estadísticas de la población (para el heurístico de "factor principal").
        var estadisticas = CalcularEstadisticasPoblacion(todasLasFeatures.Select(x => x.Features));

        // 5) Predecir SOLO sobre los clientes activos.
        var clientesActivos = todasLasFeatures
            .Where(x => clientes.First(c => c.IdCliente == x.IdCliente).EstadoRegistro == "Socio Activo")
            .ToList();

        var predictionEngine = _mlContext.Model.CreatePredictionEngine<ChurnInputData, ChurnPredictionResult>(modeloEntrenado);

        var resultados = new List<(int IdCliente, decimal Probabilidad, string NivelRiesgo, string Factor, decimal Impacto)>();
        foreach (var item in clientesActivos)
        {
            var prediccion = predictionEngine.Predict(item.Features);
            decimal probabilidad = (decimal)Math.Round(prediccion.Probability * 100, 2);
            string nivel = NivelesRiesgo.Clasificar(probabilidad);
            var (factor, impacto) = FeatureEngineering.DeterminarFactorPrincipal(item.Features, estadisticas);

            resultados.Add((item.IdCliente, probabilidad, nivel, factor, impacto));
        }

        // 6) Guardar todo a través del repositorio (DAL).
        await GuardarResultadosAsync(resultados, cancellationToken);

        _logger.LogInformation("Ciclo completado. {Count} clientes activos re-evaluados.", resultados.Count);
    }

    private (ITransformer Modelo, BinaryClassificationMetrics Metricas) EntrenarModelo(IEnumerable<ChurnInputData> datos)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(datos);

        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        var pipeline = _mlContext.Transforms.Categorical.OneHotEncoding(new[]
            {
                new InputOutputColumnPair("PlanSocioEncoded", nameof(ChurnInputData.PlanSocio)),
                new InputOutputColumnPair("SedeEncoded", nameof(ChurnInputData.Sede)),
                new InputOutputColumnPair("SexoEncoded", nameof(ChurnInputData.Sexo)),
            })
            .Append(_mlContext.Transforms.Concatenate("Features",
                nameof(ChurnInputData.VisitasUltimos30Dias),
                nameof(ChurnInputData.UsoAppUltimos30Dias),
                nameof(ChurnInputData.ReservasUltimos30Dias),
                nameof(ChurnInputData.CancelacionesUltimos30Dias),
                nameof(ChurnInputData.DiasDesdeUltimaActividad),
                nameof(ChurnInputData.PagosVencidosUltimos60Dias),
                nameof(ChurnInputData.ConsultasSoporteUltimos90Dias),
                nameof(ChurnInputData.AntiguedadDias),
                "PlanSocioEncoded", "SedeEncoded", "SexoEncoded"))
            // Random Forest: FastForest es la implementación de ML.NET de un
            // bosque de árboles de decisión para clasificación binaria.
            .Append(_mlContext.BinaryClassification.Trainers.FastForest(
                labelColumnName: nameof(ChurnInputData.Abandono),
                featureColumnName: "Features",
                numberOfTrees: 100,
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 5))
            // FastForest da un "Score" crudo pero no calibra la probabilidad
            // automáticamente. Este paso agrega la columna "Probability"
            // (0 a 1) a partir de ese Score, que es lo que después usamos
            // para el % de riesgo de abandono.
            .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: nameof(ChurnInputData.Abandono),
                scoreColumnName: "Score"));

        var modeloDeEvaluacion = pipeline.Fit(split.TrainSet);
        var predsTest = modeloDeEvaluacion.Transform(split.TestSet);
        var metricas = _mlContext.BinaryClassification.Evaluate(predsTest, labelColumnName: nameof(ChurnInputData.Abandono));

        var modeloFinal = pipeline.Fit(dataView);

        return (modeloFinal, metricas);
    }

    private Dictionary<string, (double Media, double Desvio)> CalcularEstadisticasPoblacion(IEnumerable<ChurnInputData> datos)
    {
        var lista = datos.ToList();
        var resultado = new Dictionary<string, (double, double)>();

        foreach (var (nombre, selector, _) in FeatureEngineering.IndicadoresDeRiesgo)
        {
            var valores = lista.Select(d => (double)selector(d)).ToList();
            double media = valores.Average();
            double desvio = Math.Sqrt(valores.Sum(v => Math.Pow(v - media, 2)) / Math.Max(1, valores.Count - 1));
            resultado[nombre] = (media, desvio);
        }

        return resultado;
    }

    private async Task GuardarResultadosAsync(
        List<(int IdCliente, decimal Probabilidad, string NivelRiesgo, string Factor, decimal Impacto)> resultados,
        CancellationToken cancellationToken)
    {
        var nombresFactor = FeatureEngineering.IndicadoresDeRiesgo.Select(i => i.NombreFactor);
        var factoresPorNombre = await _repositorio.AsegurarCatalogoFactoresAsync(nombresFactor, cancellationToken);

        var modelo = await _repositorio.ObtenerOCrearModeloAsync(NombresModelo.RandomForestChurn, cancellationToken);

        var nuevasPredicciones = resultados.Select(r => new Prediccion
        {
            IdCliente = r.IdCliente,
            IdFactorRiesgo = factoresPorNombre[r.Factor].IdFactorRiesgo,
            IdModelo = modelo.IdModelo,
            NivelRiesgo = r.NivelRiesgo,
            ProbabilidadAbandono = r.Probabilidad
        });

        var idsClientes = resultados.Select(r => r.IdCliente);
        await _repositorio.ReemplazarPrediccionesAsync(idsClientes, nuevasPredicciones, cancellationToken);
    }
}
