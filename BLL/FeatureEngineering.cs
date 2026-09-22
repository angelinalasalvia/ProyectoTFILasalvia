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

        var inicioPeriodoAnterior30 = fechaReferencia.AddDays(-60);

        int visitasUltimos30 = eventos.Count(e =>
            e.Evento == TipoEvento.VisitaGimnasio &&
            e.Fecha >= ultimos30);

        int visitasPeriodoAnterior30 = eventos.Count(e =>
            e.Evento == TipoEvento.VisitaGimnasio &&
            e.Fecha >= inicioPeriodoAnterior30 &&
            e.Fecha < ultimos30);

        int pagosRegistradosUltimos60 = eventos.Count(e =>
            e.Evento == TipoEvento.PagoRegistrado &&
            e.Fecha >= ultimos60);

        int pagosVencidosUltimos60 = eventos.Count(e =>
            e.Evento == TipoEvento.PagoVencido &&
            e.Fecha >= ultimos60);

        int totalPagos = pagosRegistradosUltimos60 + pagosVencidosUltimos60;

        float proporcionPagosVencidos = totalPagos == 0
            ? 0f
            : (float)pagosVencidosUltimos60 / totalPagos;

        float tendenciaVisitas = visitasUltimos30 - visitasPeriodoAnterior30;

        float diasDesdeUltimaActividad = eventos.Count == 0
            ? 999f
            : (float)(fechaReferencia - eventos.Max(e => e.Fecha)).TotalDays;

        float antiguedadDias = eventos.Count == 0
            ? 0f
            : (float)(fechaReferencia - eventos.Min(e => e.Fecha)).TotalDays;

        return new ChurnInputData
        {
            VisitasUltimos30Dias = visitasUltimos30,
            UsoAppUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.UsoApp && e.Fecha >= ultimos30),
            ReservasUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.ReservaClase && e.Fecha >= ultimos30),
            CancelacionesUltimos30Dias = eventos.Count(e => e.Evento == TipoEvento.CancelacionReserva && e.Fecha >= ultimos30),
            DiasDesdeUltimaActividad = diasDesdeUltimaActividad,
            PagosVencidosUltimos60Dias = pagosVencidosUltimos60,
            PagosRegistradosUltimos60Dias = pagosRegistradosUltimos60,
            ProporcionPagosVencidos = proporcionPagosVencidos,
            ConsultasSoporteUltimos90Dias = eventos.Count(e => e.Evento == TipoEvento.ConsultaSoporte && e.Fecha >= ultimos90),
            AntiguedadDias = antiguedadDias,
            TendenciaVisitas = tendenciaVisitas,
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
        (NombresFactorRiesgo.AltaProporcionPagosVencidos, f => f.ProporcionPagosVencidos, true),
        (NombresFactorRiesgo.TendenciaNegativaAsistencia, f => f.TendenciaVisitas, false),
    };

    public static (string NombreFactor, decimal ImpactoPorcentual) DeterminarFactorPrincipal(
        ChurnInputData features,
        Dictionary<string, (double Media, double Desvio)> estadisticasPoblacion)
    {
        string mejorFactor = IndicadoresDeRiesgo[0].NombreFactor;
        double mejorZScore = 0;
        bool encontroFactor = false;

        foreach (var (nombre, selector, altoEsRiesgo) in IndicadoresDeRiesgo)
        {
            var (media, desvio) = estadisticasPoblacion[nombre];
            double valor = selector(features);

            if (desvio < 0.0001) continue;

            double zScore = (valor - media) / desvio;
            if (!altoEsRiesgo) zScore *= -1;

            if (!encontroFactor || zScore > mejorZScore)
            {
                mejorZScore = zScore;
                mejorFactor = nombre;
                encontroFactor = true;
            }
        }

        double impacto = Math.Clamp(50 + mejorZScore * 15, 0, 100);
        return (mejorFactor, (decimal)Math.Round(impacto, 2));
    }

    public static ChurnInputData ConstruirParaPeriodo(
    Cliente cliente,
    List<EventoCliente> eventos,
    DateTime fechaReferencia,
    int diasPeriodo)
    {
        var inicioPeriodoActual = fechaReferencia.AddDays(-diasPeriodo);
        var inicioPeriodoAnterior = fechaReferencia.AddDays(-(diasPeriodo * 2));

        int visitasPeriodoActual = eventos.Count(e =>
            e.Evento == TipoEvento.VisitaGimnasio &&
            e.Fecha >= inicioPeriodoActual);

        int visitasPeriodoAnterior = eventos.Count(e =>
            e.Evento == TipoEvento.VisitaGimnasio &&
            e.Fecha >= inicioPeriodoAnterior &&
            e.Fecha < inicioPeriodoActual);

        int pagosRegistrados = eventos.Count(e =>
            e.Evento == TipoEvento.PagoRegistrado &&
            e.Fecha >= inicioPeriodoActual);

        int pagosVencidos = eventos.Count(e =>
            e.Evento == TipoEvento.PagoVencido &&
            e.Fecha >= inicioPeriodoActual);

        int totalPagos = pagosRegistrados + pagosVencidos;

        float proporcionPagosVencidos = totalPagos == 0
            ? 0f
            : (float)pagosVencidos / totalPagos;

        float tendenciaVisitas =
            visitasPeriodoActual - visitasPeriodoAnterior;

        float diasDesdeUltimaActividad = eventos.Count == 0
            ? 999f
            : (float)(fechaReferencia - eventos.Max(e => e.Fecha)).TotalDays;

        return new ChurnInputData
        {
            VisitasUltimos30Dias = visitasPeriodoActual,

            UsoAppUltimos30Dias = eventos.Count(e =>
                e.Evento == TipoEvento.UsoApp &&
                e.Fecha >= inicioPeriodoActual),

            ReservasUltimos30Dias = eventos.Count(e =>
                e.Evento == TipoEvento.ReservaClase &&
                e.Fecha >= inicioPeriodoActual),

            CancelacionesUltimos30Dias = eventos.Count(e =>
                e.Evento == TipoEvento.CancelacionReserva &&
                e.Fecha >= inicioPeriodoActual),

            DiasDesdeUltimaActividad = diasDesdeUltimaActividad,

            PagosVencidosUltimos60Dias = pagosVencidos,

            PagosRegistradosUltimos60Dias = pagosRegistrados,

            ProporcionPagosVencidos = proporcionPagosVencidos,

            ConsultasSoporteUltimos90Dias = eventos.Count(e =>
                e.Evento == TipoEvento.ConsultaSoporte &&
                e.Fecha >= inicioPeriodoActual),

            AntiguedadDias = eventos.Count == 0
                ? 0f
                : (float)(fechaReferencia - eventos.Min(e => e.Fecha)).TotalDays,

            TendenciaVisitas = tendenciaVisitas,

            PlanSocio = cliente.PlanSocio,
            Sede = cliente.Sede,
            Sexo = cliente.Sexo,
            Abandono = cliente.EstadoRegistro == "Inactivo"
        };
    }
    // ------------------------------------------------------------------------------------
    // Panel de entrenamiento con fecha de corte
    // ------------------------------------------------------------------------------------
    // Antes, cada cliente aportaba UNA fila con su estado ACTUAL (Abandono = está inactivo hoy),
    // así que el modelo aprendía a reconocer a quienes ya se habían ido, no a quienes están por irse
    // (con esa etiqueta, "días desde la última actividad" separa casi perfectamente a los dos grupos).
    //
    // Acá cada cliente aporta VARIAS filas, una por cada corte semanal de su historia. En cada corte
    // las variables se calculan solo con los eventos anteriores a esa fecha (así no hay fuga de datos
    // del futuro) y la etiqueta pasa a ser prospectiva: "¿el cliente dejó de venir dentro de los
    // 'horizonteDias' siguientes a este corte?". El nivel de riesgo y sus umbrales (Alto/Medio/Bajo)
    // no cambian: lo único que cambia es cómo se entrena el modelo que calcula la probabilidad.
    //
    // Proxy de "fecha de baja": no hay una columna con la fecha exacta en que un cliente se dio de
    // baja, así que se usa su último evento registrado (solo para los clientes hoy Inactivos). Es una
    // aproximación: el cliente pudo haber dejado de venir unos días antes de ese último evento.
    public static List<ChurnInputData> ConstruirPanelEntrenamiento(
        List<Cliente> clientes, DateTime fechaActual, int horizonteDias = 30, int diasEntreCortes = 7, int diasHistoriaMinima = 30)
    {
        var filas = new List<ChurnInputData>();
        var ultimoCorteValido = fechaActual.AddDays(-horizonteDias);

        foreach (var cliente in clientes)
        {
            var eventos = cliente.Eventos.OrderBy(e => e.Fecha).ToList();
            if (eventos.Count == 0) continue;

            var primerEvento = eventos[0].Fecha;
            var fechaBaja = cliente.EstadoRegistro == "Inactivo" ? eventos[^1].Fecha : (DateTime?)null;

            for (var corte = primerEvento.AddDays(diasHistoriaMinima); corte <= ultimoCorteValido; corte = corte.AddDays(diasEntreCortes))
            {
                // Si para este corte ya se había ido, el corte no aporta información nueva: se descarta
                // (un modelo de churn predice la baja, no confirma una que ya pasó).
                if (fechaBaja is not null && corte >= fechaBaja.Value) continue;

                var eventosHastaElCorte = eventos.Where(e => e.Fecha <= corte).ToList();
                var fila = Construir(cliente, eventosHastaElCorte, corte);
                fila.Abandono = fechaBaja is not null && fechaBaja.Value <= corte.AddDays(horizonteDias);
                filas.Add(fila);
            }
        }

        return filas;
    }
}

