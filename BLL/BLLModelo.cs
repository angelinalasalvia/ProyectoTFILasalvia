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

        var (modeloConContribuciones, metrica) = EntrenarModelo(panelEntrenamiento);
        _logger.LogInformation(
            "Modelo entrenado con panel histórico: {Filas} filas ({Positivos} abandonos, {Tasa:P1}). AUC: {AUC:P1} | Accuracy: {Acc:P1} | F1Score: {F1:P1}",
            panelEntrenamiento.Count, panelEntrenamiento.Count(f => f.Abandono),
            (double)panelEntrenamiento.Count(f => f.Abandono) / panelEntrenamiento.Count,
            metrica.AreaUnderRocCurve, metrica.Accuracy, metrica.F1Score);

        // Estadísticas por segmento (plan), no por toda la población: son las que se usan para
        // redactar la descripción de cada factor ("X vs Y en el segmento"), igual que antes.
        var clientesPorId = clientes.ToDictionary(c => c.IdCliente);
        var estadisticasPorSegmento = todasLasFeatures
            .GroupBy(x => clientesPorId[x.IdCliente].PlanSocio)
            .ToDictionary(g => g.Key, g => CalcularEstadisticasPoblacion(g.Select(x => x.Features)));

        var clientesActivos = todasLasFeatures
            .Where(x => clientesPorId[x.IdCliente].EstadoRegistro == "Socio Activo")
            .ToList();

        // Prediction engine "explicada": además de Probability/Score, trae FeatureContributions,
        // la contribución real que el modelo (ya entrenado con el 100% de los datos) le asignó a
        // cada variable para este cliente puntual. Es la pieza que reemplaza al z-score heurístico.
        var predictionEngine = _mlContext.Model
            .CreatePredictionEngine<ChurnInputData, ChurnPredictionConContribuciones>(modeloConContribuciones);

        var resultados = new List<(int IdCliente, decimal Probabilidad, string NivelRiesgo,
            List<(string NombreFactor, decimal Impacto, string Descripcion)> Factores)>();

        foreach (var item in clientesActivos)
        {
            var prediccion = predictionEngine.Predict(item.Features);
            decimal probabilidad = (decimal)Math.Round(prediccion.Probability * 100, 2);
            string nivel = NivelesRiesgo.Clasificar(probabilidad);

            var estadisticasSegmento = estadisticasPorSegmento[clientesPorId[item.IdCliente].PlanSocio];
            var factores = FeatureEngineering.MapearContribuciones(
                prediccion.FeatureContributions, item.Features, estadisticasSegmento);

            resultados.Add((item.IdCliente, probabilidad, nivel, factores));
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

    // Devuelve el modelo YA envuelto con el calculador de contribuciones (ITransformer que,
    // al transformar, agrega la columna FeatureContributions), listo para crear la prediction
    // engine "explicada" que se usa en EntrenarYPredecirAsync.
  
    private (ITransformer ModeloConContribuciones, BinaryClassificationMetrics Metricas) EntrenarModelo(IEnumerable<ChurnInputData> datos)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(datos);
        var split = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2, seed: 42);

        // Columnas numéricas centralizadas en FeatureEngineering.ColumnasNumericas:
        // es el mismo orden que usa MapearContribuciones para interpretar el vector.
        var columnasFeatures = FeatureEngineering.ColumnasNumericas
            .Select(c => c.NombreColumna)
            .Concat(new[] { "PlanSocioEncoded", "SedeEncoded", "SexoEncoded" })
            .ToArray();

        // Pipeline base: transformación + FastForest.
        // Separamos el FastForest del calibrador Platt porque
        // CalculateFeatureContribution necesita recibir el transformer
        // del modelo que soporta contribuciones.
        var pipelineBase = _mlContext.Transforms.Categorical.OneHotEncoding(new[]
            {
            new InputOutputColumnPair(
                "PlanSocioEncoded",
                nameof(ChurnInputData.PlanSocio)),

            new InputOutputColumnPair(
                "SedeEncoded",
                nameof(ChurnInputData.Sede)),

            new InputOutputColumnPair(
                "SexoEncoded",
                nameof(ChurnInputData.Sexo)),
        })
            .Append(_mlContext.Transforms.Concatenate(
                "Features",
                columnasFeatures))
            .Append(_mlContext.BinaryClassification.Trainers.FastForest(
                labelColumnName: nameof(ChurnInputData.Abandono),
                featureColumnName: "Features",
                numberOfTrees: 100,
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 5));

        // Pipeline completo: FastForest + calibración Platt.
        var pipeline = pipelineBase
            .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: nameof(ChurnInputData.Abandono),
                scoreColumnName: "Score"));

        // Modelo para evaluación.
        var modeloDeEvaluacion = pipeline.Fit(split.TrainSet);

        var predsTest = modeloDeEvaluacion.Transform(split.TestSet);

        var metricas = _mlContext.BinaryClassification.Evaluate(
            predsTest,
            labelColumnName: nameof(ChurnInputData.Abandono));

        // Modelo final entrenado con el 100% de los datos.
        var modeloFinal = pipeline.Fit(dataView);

        // ------------------------------------------------------------
        // CONTRIBUCIONES DE FEATURES
        // ------------------------------------------------------------
        // Entrenamos también el pipeline SIN Platt para obtener
        // directamente el transformer FastForest.
        var modeloBase = pipelineBase.Fit(dataView);

        // El LastTransformer de modeloBase ahora sí es FastForest,
        // no el PlattCalibrator.
        var transformerFastForest = modeloBase.LastTransformer;

        // Fila de referencia ya transformada por el modelo base.
        var filaDeReferencia = modeloBase.Transform(
            _mlContext.Data.TakeRows(dataView, 1));

        // Calculador nativo de contribuciones de ML.NET.
        var calculadoraContribuciones = _mlContext.Transforms
            .CalculateFeatureContribution(
                transformerFastForest,
                normalize: false)
            .Fit(filaDeReferencia);

        // Agregamos las contribuciones al modelo final,
        // que sigue conservando la calibración Platt.
        var modeloConContribuciones =
            modeloFinal.Append(calculadoraContribuciones);

        return (modeloConContribuciones, metricas);
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
        List<(int IdCliente, decimal Probabilidad, string NivelRiesgo,
            List<(string NombreFactor, decimal Impacto, string Descripcion)> Factores)> resultados,
        CancellationToken ct)
    {
        var nombresFactor = FeatureEngineering.IndicadoresDeRiesgo.Select(i => i.NombreFactor);
        var factoresPorNombre = await AsegurarCatalogoFactoresAsync(nombresFactor, ct);
        var modelo = await ObtenerOCrearModeloAsync(NombresModelo.RandomForestChurn, ct);

        // El factor principal (la "insignia" del listado, Prediccion.IdFactorRiesgo) es
        // simplemente el primero de la lista: MapearContribuciones ya la deja ordenada por
        // impacto descendente, así que es el que más empujó hacia el riesgo para ese cliente.
        var nuevasPredicciones = resultados.Select(r => new Prediccion
        {
            IdCliente = r.IdCliente,
            IdFactorRiesgo = factoresPorNombre[r.Factores.First().NombreFactor].IdFactorRiesgo,
            IdModelo = modelo.IdModelo,
            NivelRiesgo = r.NivelRiesgo,
            ProbabilidadAbandono = r.Probabilidad
        }).ToList();

        // ReemplazarPrediccionesAsync inserta en el mismo orden que 'nuevasPredicciones' (que
        // a su vez viene del mismo orden que 'resultados'), así que se puede reasociar cada
        // predicción recién insertada (ya con su IdPrediccion) con su lista completa de factores
        // por posición.
        var prediccionesInsertadas = await ReemplazarPrediccionesAsync(
            resultados.Select(r => r.IdCliente), nuevasPredicciones, ct);

        await GuardarFactoresRiesgoAsync(prediccionesInsertadas, resultados, factoresPorNombre, ct);
    }

    // Guarda, para cada predicción recién insertada, la lista completa de factores calculados
    // por MapearContribuciones. Antes esto se recalculaba al vuelo cada vez que alguien abría
    // el detalle del cliente (BLLFactorRiesgo.ObtenerFactorRiesgo); ahora esa pantalla pasa a
    // ser un SELECT contra Prediccion_FactorRiesgo.
    private async Task GuardarFactoresRiesgoAsync(
        List<Prediccion> prediccionesInsertadas,
        List<(int IdCliente, decimal Probabilidad, string NivelRiesgo,
            List<(string NombreFactor, decimal Impacto, string Descripcion)> Factores)> resultados,
        Dictionary<string, FactorRiesgo> factoresPorNombre,
        CancellationToken ct)
    {
        for (int i = 0; i < prediccionesInsertadas.Count; i++)
        {
            var idPrediccion = prediccionesInsertadas[i].IdPrediccion;

            foreach (var factor in resultados[i].Factores)
            {
                await _accesoDatos.Escribir(
                    @"INSERT INTO Prediccion_FactorRiesgo (IdPrediccion, IdFactorRiesgo, Impacto, Descripcion)
                      VALUES (@IdPrediccion, @IdFactorRiesgo, @Impacto, @Descripcion)",
                    new
                    {
                        IdPrediccion = idPrediccion,
                        IdFactorRiesgo = factoresPorNombre[factor.NombreFactor].IdFactorRiesgo,
                        factor.Impacto,
                        factor.Descripcion
                    }, ct: ct);
            }
        }
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
    // Devuelve las predicciones insertadas, en el mismo orden que 'nuevasPredicciones', con su
    // IdPrediccion ya asignado (vía OUTPUT INSERTED), para poder colgarles sus factores.
    private async Task<List<Prediccion>> ReemplazarPrediccionesAsync(
        IEnumerable<int> idsClientes, IEnumerable<Prediccion> nuevasPredicciones, CancellationToken ct)
    {
        var idsList = idsClientes.Distinct().ToList();
        if (idsList.Count > 0)
        {
            var (clausulaIn, parametrosIn) = _accesoDatos.ConstruirClausulaIn("id", idsList);
            // ON DELETE CASCADE en Prediccion_FactorRiesgo se encarga de borrar también los
            // factores guardados de las predicciones anteriores de estos clientes.
            await _accesoDatos.Eliminar($"DELETE FROM Prediccion WHERE IdCliente IN ({clausulaIn})", parametrosIn, ct: ct);
        }

        // Nota: se inserta de a una porque Escribir()/Leer() manejan su propia transacción por
        // llamada. Para un volumen grande de clientes convendría un INSERT masivo (table-valued
        // parameter o SqlBulkCopy), pero eso ya excede el alcance de "DAL genérico simple" del
        // diagrama.
        var insertadas = new List<Prediccion>();
        foreach (var prediccion in nuevasPredicciones)
        {
            var insertada = await _accesoDatos.Leer<Prediccion>(
                @"INSERT INTO Prediccion (IdCliente, IdFactorRiesgo, IdModelo, NivelRiesgo, ProbabilidadAbandono)
                  OUTPUT INSERTED.IdPrediccion, INSERTED.IdCliente, INSERTED.IdFactorRiesgo, INSERTED.IdModelo,
                         INSERTED.NivelRiesgo, INSERTED.ProbabilidadAbandono
                  VALUES (@IdCliente, @IdFactorRiesgo, @IdModelo, @NivelRiesgo, @ProbabilidadAbandono)",
                new
                {
                    prediccion.IdCliente,
                    prediccion.IdFactorRiesgo,
                    prediccion.IdModelo,
                    prediccion.NivelRiesgo,
                    prediccion.ProbabilidadAbandono
                }, ct: ct);

            insertadas.Add(insertada.First());
        }

        return insertadas;
    }
}