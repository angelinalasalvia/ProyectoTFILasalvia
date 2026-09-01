using BLL;
using BE;

namespace BLL;

public static class FeatureEngineering
{
   
    public static ChurnInputData Construir(Cliente cliente, List<EventoCliente> eventos, DateTime fechaReferencia, int? diasVentana = null)
    {
        
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

    public static readonly List<(string NombreFactor, Func<ChurnInputData, float> Selector, bool AltoEsRiesgo)> IndicadoresDeRiesgo = new()
    {
        (NombresFactorRiesgo.InactividadReciente, f => f.DiasDesdeUltimaActividad, true),
        (NombresFactorRiesgo.BajaFrecuenciaAsistencia, f => f.VisitasUltimos30Dias, false),
        (NombresFactorRiesgo.BajaInteraccionApp, f => f.UsoAppUltimos30Dias, false),
        (NombresFactorRiesgo.HistorialPagosVencidos, f => f.PagosVencidosUltimos60Dias, true),
        (NombresFactorRiesgo.CancelacionesFrecuentes, f => f.CancelacionesUltimos30Dias, true),
        (NombresFactorRiesgo.AltaConsultaSoporte, f => f.ConsultasSoporteUltimos90Dias, true),
    };

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

            if (desvio < 0.0001) continue;

            double zScore = (valor - media) / desvio;
            if (!altoEsRiesgo) zScore *= -1; 

            if (zScore > mejorZScore)
            {
                mejorZScore = zScore;
                mejorFactor = nombre;
            }
        }

        double impacto = Math.Clamp(50 + mejorZScore * 15, 0, 100);
        return (mejorFactor, (decimal)Math.Round(impacto, 2));
    }
}

