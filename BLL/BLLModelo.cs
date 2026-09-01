using BE;
using DAL;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace BLL;

public class BLLModelo
{
    private readonly IDALCliente _dalCliente;
    private readonly IDALFactorRiesgo _dalFactorRiesgo;
    private readonly IDALModelo _dalModelo;
    private readonly IDALPrediccion _dalPrediccion;
    private readonly ILogger<BLLModelo> _logger;
    private readonly MLContext _mlContext;

    public BLLModelo(IDALCliente dalCliente, IDALFactorRiesgo dalFactorRiesgo, IDALModelo dalModelo,
                      IDALPrediccion dalPrediccion, ILogger<BLLModelo> logger)
    {
        _dalCliente = dalCliente;
        _dalFactorRiesgo = dalFactorRiesgo;
        _dalModelo = dalModelo;
        _dalPrediccion = dalPrediccion;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
    }

    // Corresponde a ObtenerFechaUltimaEjecucion(int id) del diagrama.
    // Simplificación: como hoy hay un único modelo activo, lo busco por
    // nombre en vez de por id (evita tener que conocer el id de antemano
    // desde la UI). Si más adelante manejás varios modelos, se puede
    // agregar una sobrecarga que reciba el id.

    public async Task<string> ObtenerNombreModelo(int idModelo, CancellationToken ct = default)
        => await _dalModelo.ObtenerNombrePorIdAsync(idModelo, ct) ?? "Modelo desconocido";
    public async Task<DateTime?> ObtenerFechaUltimaEjecucion(CancellationToken ct = default)
        => await _dalModelo.ObtenerFechaUltimaEjecucionAsync(NombresModelo.RandomForestChurn, ct);

    public async Task EntrenarYPredecirAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando ciclo de entrenamiento y predicción de churn...");
        var fechaReferencia = DateTime.Now;

        var clientes = await _dalCliente.ObtenerClientesConEventosAsync(cancellationToken);
        if (clientes.Count < 20)
        {
            _logger.LogWarning("Muy pocos clientes ({Count}) para entrenar un modelo confiable.", clientes.Count);
            return;
        }

        var todasLasFeatures = clientes.Select(c => new ClienteConFeatures
        {
            IdCliente = c.IdCliente,
            Features = FeatureEngineering.Construir(c, c.Eventos.ToList(), fechaReferencia)
        }).ToList();

        var (modeloEntrenado, metrica) = EntrenarModelo(todasLasFeatures.Select(x => x.Features));
        _logger.LogInformation("Modelo entrenado. AUC: {AUC:P1} | Accuracy: {Acc:P1} | F1Score: {F1:P1}",
            metrica.AreaUnderRocCurve, metrica.Accuracy, metrica.F1Score);

        var estadisticas = CalcularEstadisticasPoblacion(todasLasFeatures.Select(x => x.Features));

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
                nameof(ChurnInputData.VisitasUltimos30Dias), nameof(ChurnInputData.UsoAppUltimos30Dias),
                nameof(ChurnInputData.ReservasUltimos30Dias), nameof(ChurnInputData.CancelacionesUltimos30Dias),
                nameof(ChurnInputData.DiasDesdeUltimaActividad), nameof(ChurnInputData.PagosVencidosUltimos60Dias),
                nameof(ChurnInputData.ConsultasSoporteUltimos90Dias), nameof(ChurnInputData.AntiguedadDias),
                "PlanSocioEncoded", "SedeEncoded", "SexoEncoded"))
            .Append(_mlContext.BinaryClassification.Trainers.FastForest(
                labelColumnName: nameof(ChurnInputData.Abandono), featureColumnName: "Features",
                numberOfTrees: 100, numberOfLeaves: 20, minimumExampleCountPerLeaf: 5))
            .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: nameof(ChurnInputData.Abandono), scoreColumnName: "Score"));

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
        var factoresPorNombre = await _dalFactorRiesgo.AsegurarCatalogoFactoresAsync(nombresFactor, cancellationToken);
        var modelo = await _dalModelo.ObtenerOCrearModeloAsync(NombresModelo.RandomForestChurn, cancellationToken);

        var nuevasPredicciones = resultados.Select(r => new Prediccion
        {
            IdCliente = r.IdCliente,
            IdFactorRiesgo = factoresPorNombre[r.Factor].IdFactorRiesgo,
            IdModelo = modelo.IdModelo,
            NivelRiesgo = r.NivelRiesgo,
            ProbabilidadAbandono = r.Probabilidad
        });

        await _dalPrediccion.ReemplazarPrediccionesAsync(resultados.Select(r => r.IdCliente), nuevasPredicciones, cancellationToken);
    }
}
