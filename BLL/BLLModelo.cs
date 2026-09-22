using BE;
using DAL;
using Microsoft.Extensions.Logging;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace BLL;

public class BLLModelo
{
    private readonly AccesoDatos _accesoDatos;
    private readonly BLLRegla _bllRegla;
    private readonly ILogger<BLLModelo> _logger;
    private readonly MLContext _mlContext;

    public BLLModelo(AccesoDatos accesoDatos, BLLRegla bllRegla, ILogger<BLLModelo> logger)
    {
        _accesoDatos = accesoDatos;
        _bllRegla = bllRegla;
        _logger = logger;
        _mlContext = new MLContext(seed: 42);
    }

    public async Task<string> ObtenerNombreModelo(int idModelo, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Modelo>(
            "SELECT * FROM Modelo WHERE IdModelo = @idModelo",
            new { idModelo }, ct: ct);
        return resultado.FirstOrDefault()?.Nombre ?? "Modelo desconocido";
    }

    public async Task<DateTime?> ObtenerFechaUltimaEjecucion(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Modelo>(
            "SELECT * FROM Modelo WHERE Nombre = @nombre",
            new { nombre = NombresModelo.RandomForestChurn }, ct: ct);
        return resultado.FirstOrDefault()?.UltimaEjecucion;
    }

    public async Task EntrenarYPredecirAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Iniciando ciclo de entrenamiento y predicción de churn...");
        var fechaReferencia = DateTime.Now;

        var clientes = await BLLCliente.ObtenerClientesConEventosAsync(_accesoDatos, cancellationToken);
        if (clientes.Count < 20)
        {
            _logger.LogWarning("Muy pocos clientes ({Count}) para entrenar un modelo confiable.", clientes.Count);
            return;
        }

        // Snapshot de HOY de cada cliente: se usa para las estadísticas de población (factor principal
        // de riesgo) y como entrada de la predicción real. Su Abandono (= está inactivo hoy) no se usa
        // para entrenar; ver ConstruirPanelEntrenamiento.
        var todasLasFeatures = clientes.Select(c => new ClienteConFeatures
        {
            IdCliente = c.IdCliente,
            Features = FeatureEngineering.Construir(c, c.Eventos.ToList(), fechaReferencia)
        }).ToList();

        // Entrenamiento con fecha de corte: cada cliente aporta varias filas históricas (una por semana
        // de su historia), con las variables calculadas solo hasta esa fecha y la etiqueta "¿abandonó
        // dentro de los 30 días siguientes?". Evita que el modelo entrene con datos del futuro.
        var panelEntrenamiento = FeatureEngineering.ConstruirPanelEntrenamiento(clientes, fechaReferencia);
        if (panelEntrenamiento.Count < 20)
        {
            _logger.LogWarning("Panel de entrenamiento insuficiente ({Count} filas) para entrenar un modelo confiable.", panelEntrenamiento.Count);
            return;
        }

        var (modeloEntrenado, metrica) = EntrenarModelo(panelEntrenamiento);
        _logger.LogInformation(
            "Modelo entrenado con panel histórico: {Filas} filas ({Positivos} abandonos, {Tasa:P1}). AUC: {AUC:P1} | Accuracy: {Acc:P1} | F1Score: {F1:P1}",
            panelEntrenamiento.Count, panelEntrenamiento.Count(f => f.Abandono),
            (double)panelEntrenamiento.Count(f => f.Abandono) / panelEntrenamiento.Count,
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

        // Con las predicciones ya actualizadas, se evalúan las reglas activas (CU10) contra los
        // niveles de riesgo recién calculados y se registran los envíos automáticos que correspondan.
        try
        {
            var (enviados, cerrados) = await _bllRegla.EjecutarReglas(fechaReferencia, cancellationToken);
            _logger.LogInformation(
                "Motor de reglas ejecutado tras la predicción: {Enviados} envíos nuevos, {Cerrados} envíos anteriores cerrados con resultado.",
                enviados.Count, cerrados);
        }
        catch (Exception ex)
        {
            // Un error del motor de reglas no debe invalidar el ciclo de predicción que ya se guardó.
            _logger.LogError(ex, "Error ejecutando el motor de reglas tras la predicción.");
        }
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
            nameof(ChurnInputData.PagosRegistradosUltimos60Dias),
            nameof(ChurnInputData.ProporcionPagosVencidos),

            nameof(ChurnInputData.ConsultasSoporteUltimos90Dias),
            nameof(ChurnInputData.AntiguedadDias),

            nameof(ChurnInputData.TendenciaVisitas),

            "PlanSocioEncoded",
            "SedeEncoded",
            "SexoEncoded"))
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
        CancellationToken ct)
    {
        var nombresFactor = FeatureEngineering.IndicadoresDeRiesgo.Select(i => i.NombreFactor);
        var factoresPorNombre = await AsegurarCatalogoFactoresAsync(nombresFactor, ct);
        var modelo = await ObtenerOCrearModeloAsync(NombresModelo.RandomForestChurn, ct);

        var nuevasPredicciones = resultados.Select(r => new Prediccion
        {
            IdCliente = r.IdCliente,
            IdFactorRiesgo = factoresPorNombre[r.Factor].IdFactorRiesgo,
            IdModelo = modelo.IdModelo,
            NivelRiesgo = r.NivelRiesgo,
            ProbabilidadAbandono = r.Probabilidad
        });

        await ReemplazarPrediccionesAsync(resultados.Select(r => r.IdCliente), nuevasPredicciones, ct);
    }

    // Antes vivía en DALFactorRiesgo (AsegurarCatalogoFactoresAsync). La lógica de
    // "buscar, y si no existe insertar" ahora es responsabilidad del BLL: el DAL
    // genérico solo sabe ejecutar la consulta que se le pasa.
    private async Task<Dictionary<string, FactorRiesgo>> AsegurarCatalogoFactoresAsync(
        IEnumerable<string> nombresFactor, CancellationToken ct)
    {
        var existentes = (await _accesoDatos.Leer<FactorRiesgo>("SELECT * FROM FactorRiesgo", ct: ct)).ToList();
        var porNombre = existentes.ToDictionary(f => f.Nombre);

        foreach (var nombre in nombresFactor.Distinct())
        {
            if (porNombre.ContainsKey(nombre)) continue;

            // OUTPUT INSERTED.* permite recuperar la fila recién insertada (con su Id)
            // a través del mismo Leer<T>, sin necesitar un método extra en el DAL.
            var insertados = await _accesoDatos.Leer<FactorRiesgo>(
                @"INSERT INTO FactorRiesgo (Nombre, Descripcion)
                  OUTPUT INSERTED.IdFactorRiesgo, INSERTED.Nombre, INSERTED.Descripcion, INSERTED.Impacto
                  VALUES (@Nombre, @Descripcion)",
                new { Nombre = nombre, Descripcion = nombre }, ct: ct);

            porNombre[nombre] = insertados.First();
        }

        return porNombre;
    }

    // Antes vivía en DALModelo (ObtenerOCrearModeloAsync).
    private async Task<Modelo> ObtenerOCrearModeloAsync(string nombreModelo, CancellationToken ct)
    {
        var actualizados = await _accesoDatos.Leer<Modelo>(
            @"UPDATE Modelo SET UltimaEjecucion = @fecha
              OUTPUT INSERTED.IdModelo, INSERTED.Nombre, INSERTED.UltimaEjecucion
              WHERE Nombre = @nombreModelo",
            new { fecha = DateTime.Now, nombreModelo }, ct: ct);

        var modelo = actualizados.FirstOrDefault();
        if (modelo != null) return modelo;

        var insertados = await _accesoDatos.Leer<Modelo>(
            @"INSERT INTO Modelo (Nombre, UltimaEjecucion)
              OUTPUT INSERTED.IdModelo, INSERTED.Nombre, INSERTED.UltimaEjecucion
              VALUES (@nombreModelo, @fecha)",
            new { nombreModelo, fecha = DateTime.Now }, ct: ct);

        return insertados.First();
    }

    // Antes vivía en DALPrediccion (ReemplazarPrediccionesAsync): borra las predicciones
    // previas de los clientes recalculados e inserta las nuevas.
    private async Task ReemplazarPrediccionesAsync(
        IEnumerable<int> idsClientes, IEnumerable<Prediccion> nuevasPredicciones, CancellationToken ct)
    {
        var idsList = idsClientes.Distinct().ToList();
        if (idsList.Count > 0)
        {
            var (clausulaIn, parametrosIn) = _accesoDatos.ConstruirClausulaIn("id", idsList);
            await _accesoDatos.Eliminar($"DELETE FROM Prediccion WHERE IdCliente IN ({clausulaIn})", parametrosIn, ct: ct);
        }

        // Nota: se inserta de a una porque Escribir() maneja su propia transacción por llamada.
        // Para un volumen grande de clientes convendría un INSERT masivo (table-valued parameter
        // o SqlBulkCopy), pero eso ya excede el alcance de "DAL genérico simple" del diagrama.
        foreach (var prediccion in nuevasPredicciones)
        {
            await _accesoDatos.Escribir(
                @"INSERT INTO Prediccion (IdCliente, IdFactorRiesgo, IdModelo, NivelRiesgo, ProbabilidadAbandono)
                  VALUES (@IdCliente, @IdFactorRiesgo, @IdModelo, @NivelRiesgo, @ProbabilidadAbandono)",
                new
                {
                    prediccion.IdCliente,
                    prediccion.IdFactorRiesgo,
                    prediccion.IdModelo,
                    prediccion.NivelRiesgo,
                    prediccion.ProbabilidadAbandono
                }, ct: ct);
        }
    }
}
