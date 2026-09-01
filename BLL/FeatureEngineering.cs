using BLL;
using BE;

namespace BLL;

// Acá está la parte "artesanal" de todo modelo de ML: convertir filas crudas
// (eventos con fecha) en números que resuman el comportamiento de cada cliente.
// A esto se lo llama "feature engineering" y en la práctica es lo que más
// impacta en qué tan bueno termina siendo el modelo (más que el algoritmo en sí).
public static class FeatureEngineering
{
    /// <summary>
    /// Construye las features de un cliente a partir de su lista de eventos.
    /// "fechaReferencia" es el momento desde el cual contamos "hace cuántos días" -
    /// normalmente DateTime.Now, pero lo recibimos como parámetro para que el
    /// cálculo sea determinístico y testeable.
    /// </summary>
    public static ChurnInputData Construir(Cliente cliente, List<EventoCliente> eventos, DateTime fechaReferencia, int? diasVentana = null)
    {
        // Cuando el entrenamiento del modelo llama a este método (diasVentana
        // = null), se mantienen las ventanas fijas originales (30/60/90 días)
        // que ya venían funcionando. Cuando CU02 llama a este método con un
        // período elegido por el usuario (semana/mes/trimestre/año), todas
        // las ventanas usan esa misma cantidad de días.
        var ultimos30 = fechaReferencia.AddDays(-(diasVentana ?? 30));
        var ultimos60 = fechaReferencia.AddDays(-(diasVentana ?? 60));
        var ultimos90 = fechaReferencia.AddDays(-(diasVentana ?? 90));

        float diasDesdeUltimaActividad = eventos.Count == 0
            ? 999f
            : (float)(fechaReferencia - eventos.Max(e => e.Fecha)).TotalDays;

        float antiguedadDias = eventos.Count == 0
            ? 0f
            : (float)(fechaReferencia - eventos.Min(e => e.Fecha)).TotalDays;

        return new ChurnInputData
        {
            VisitasUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.VisitaGimnasio && e.Fecha >= ultimos30),
            UsoAppUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.UsoApp && e.Fecha >= ultimos30),
            ReservasUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.ReservaClase && e.Fecha >= ultimos30),
            CancelacionesUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.CancelacionReserva && e.Fecha >= ultimos30),
            DiasDesdeUltimaActividad = diasDesdeUltimaActividad,
            PagosVencidosUltimos60Dias = eventos.Count(e => e.Evento == TipoEvento.PagoVencido && e.Fecha >= ultimos60),
            ConsultasSoporteUltimos90Dias = eventos.Count(e => e.Evento == TipoEvento.ConsultaSoporte && e.Fecha >= ultimos90),
            AntiguedadDias = antiguedadDias,
            PlanSocio = cliente.PlanSocio,
            Sede = cliente.Sede,
            Sexo = cliente.Sexo,
            Abandono = cliente.EstadoRegistro == "Inactivo"
        };
    }

    // Definición de qué features consideramos "factores de riesgo" candidatos,
    // en qué dirección son malos (alto valor = riesgo, o bajo valor = riesgo),
    // y a qué fila del catálogo FactorRiesgo corresponden.
    //
    // NOTA IMPORTANTE (para que lo entiendas y lo puedas explicar en la defensa):
    // Random Forest no te dice automáticamente "a ESTE cliente en particular lo
    // está afectando la variable X" - eso (explicabilidad por instancia) requiere
    // técnicas extra como SHAP, que son bastante más pesadas de implementar.
    // Como versión inicial, usamos una heurística razonable y transparente:
    // para cada cliente, comparamos sus valores contra el promedio de la
    // población y elegimos como "factor principal" aquel donde el cliente se
    // aleja más (en la dirección riesgosa) de lo normal. Es más simple que SHAP
    // pero totalmente explicable, y es una mejora que se puede documentar como
    // trabajo futuro en tu TP.
    public static readonly List<(string NombreFactor, Func<ChurnInputData, float> Selector, bool AltoEsRiesgo)> IndicadoresDeRiesgo = new()
    {
        (NombresFactorRiesgo.InactividadReciente, f => f.DiasDesdeUltimaActividad, true),
        (NombresFactorRiesgo.BajaFrecuenciaAsistencia, f => f.VisitasUltimos30Dias, false),
        (NombresFactorRiesgo.BajaInteraccionApp, f => f.UsoAppUltimos30Dias, false),
        (NombresFactorRiesgo.HistorialPagosVencidos, f => f.PagosVencidosUltimos60Dias, true),
        (NombresFactorRiesgo.CancelacionesFrecuentes, f => f.CancelacionesUltimos30Dias, true),
        (NombresFactorRiesgo.AltaConsultaSoporte, f => f.ConsultasSoporteUltimos90Dias, true),
    };

    /// <summary>
    /// Dado un cliente y las estadísticas (media/desvío) de la población de
    /// referencia, determina cuál es su "factor principal de riesgo" y un
    /// porcentaje de impacto aproximado (0-100).
    /// </summary>
    public static (string NombreFactor, decimal ImpactoPorcentual) DeterminarFactorPrincipal(
        ChurnInputData features,
        Dictionary<string, (double Media, double Desvio)> estadisticasPoblacion)
    {
        string mejorFactor = IndicadoresDeRiesgo[0].NombreFactor;
        double mejorZScore = double.MinValue;

        foreach (var (nombre, selector, altoEsRiesgo) in IndicadoresDeRiesgo)
        {
            var (media, desvio) = estadisticasPoblacion[nombre];
            double valor = selector(features);

            // Si el desvío es ~0 (todos los clientes tienen el mismo valor),
            // esa variable no aporta información para diferenciar a nadie.
            if (desvio < 0.0001) continue;

            double zScore = (valor - media) / desvio;
            if (!altoEsRiesgo) zScore *= -1; // invertimos si "bajo valor" es lo riesgoso

            if (zScore > mejorZScore)
            {
                mejorZScore = zScore;
                mejorFactor = nombre;
            }
        }

        // Convertimos el z-score a un "impacto porcentual" aproximado y acotado
        // entre 0 y 100, solo para tener un número presentable en la UI.
        double impacto = Math.Clamp(50 + mejorZScore * 15, 0, 100);
        return (mejorFactor, (decimal)Math.Round(impacto, 2));
    }
}

