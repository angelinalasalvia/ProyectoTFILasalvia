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

    // ------------------------------------------------------------------------------------
    // Columnas numéricas que arma "Features" (BLLModelo.EntrenarModelo), en orden.
    // ------------------------------------------------------------------------------------
    // Es la fuente única de verdad: BLLModelo la usa para construir el Concatenate() del
    // pipeline, y MapearContribuciones la usa para saber a qué variable corresponde cada
    // posición del vector que devuelve CalculateFeatureContribution (que respeta ese mismo
    // orden). Después de estas 11 columnas el vector sigue con PlanSocioEncoded, SedeEncoded
    // y SexoEncoded (variable, según cuántas categorías haya); esas posiciones no se listan acá
    // a propósito, quedan fuera de los "factores" que ve el usuario (ver conversación: por ahora
    // no se muestran plan/sede/sexo como factor).
    //
    // NombreFactor es null en las columnas que el modelo usa mejorar la predicción pero que no
    // tienen hoy un factor propio en el catálogo (ReservasUltimos30Dias, PagosRegistradosUltimos60Dias,
    // AntiguedadDias): su contribución existe pero no se muestra por separado.
    public static readonly (string NombreColumna, string? NombreFactor)[] ColumnasNumericas =
    {
        (nameof(ChurnInputData.VisitasUltimos30Dias), NombresFactorRiesgo.BajaFrecuenciaAsistencia),
        (nameof(ChurnInputData.UsoAppUltimos30Dias), NombresFactorRiesgo.BajaInteraccionApp),
        (nameof(ChurnInputData.ReservasUltimos30Dias), null),
        (nameof(ChurnInputData.CancelacionesUltimos30Dias), NombresFactorRiesgo.CancelacionesFrecuentes),
        (nameof(ChurnInputData.DiasDesdeUltimaActividad), NombresFactorRiesgo.InactividadReciente),
        (nameof(ChurnInputData.PagosVencidosUltimos60Dias), NombresFactorRiesgo.HistorialPagosVencidos),
        (nameof(ChurnInputData.PagosRegistradosUltimos60Dias), null),
        (nameof(ChurnInputData.ProporcionPagosVencidos), NombresFactorRiesgo.AltaProporcionPagosVencidos),
        (nameof(ChurnInputData.ConsultasSoporteUltimos90Dias), NombresFactorRiesgo.AltaConsultaSoporte),
        (nameof(ChurnInputData.AntiguedadDias), null),
        (nameof(ChurnInputData.TendenciaVisitas), NombresFactorRiesgo.TendenciaNegativaAsistencia),
    };

    // ------------------------------------------------------------------------------------
    // Traduce la explicación real del modelo (CalculateFeatureContribution) a la lista de
    // factores que se guarda en Prediccion_FactorRiesgo.
    // ------------------------------------------------------------------------------------
    // 'contribuciones' viene de ChurnPredictionConContribuciones.FeatureContributions: un valor
    // por cada posición del vector "Features", en el mismo orden que ColumnasNumericas (+ el
    // one-hot de plan/sede/sexo al final, que acá se ignora).
    //
    // El signo ya viene correcto desde ML.NET (no hace falta el truco de "AltoEsRiesgo" que
    // usaba el z-score viejo): positivo empuja hacia el riesgo, negativo lo reduce. Se
    // normaliza a -100..100 repartiendo proporcionalmente el "peso" entre los 8 factores
    // mostrados, para que la barra de la pantalla (que espera un número tipo porcentaje)
    // siga teniendo sentido.
    public static List<(string NombreFactor, decimal Impacto, string Descripcion)> MapearContribuciones(
        float[] contribuciones,
        ChurnInputData features,
        Dictionary<string, (double Media, double Desvio)> estadisticasSegmento)
    {
        var crudos = new List<(string NombreFactor, double Contribucion)>();
        for (int i = 0; i < ColumnasNumericas.Length && i < contribuciones.Length; i++)
        {
            var nombreFactor = ColumnasNumericas[i].NombreFactor;
            if (nombreFactor is null) continue;
            crudos.Add((nombreFactor, contribuciones[i]));
        }

        double sumaAbsolutas = crudos.Sum(c => Math.Abs(c.Contribucion));

        var resultado = new List<(string, decimal, string)>();
        foreach (var (nombreFactor, contribucion) in crudos)
        {
            double impactoRelativo = sumaAbsolutas < 0.0001 ? 0 : 100 * contribucion / sumaAbsolutas;

            var selector = IndicadoresDeRiesgo.First(i => i.NombreFactor == nombreFactor).Selector;
            double valorCliente = selector(features);
            var (media, _) = estadisticasSegmento[nombreFactor];

            resultado.Add((
                nombreFactor,
                (decimal)Math.Round(impactoRelativo, 1),
                GenerarDescripcion(nombreFactor, valorCliente, media)));
        }

        // Los que más empujaron hacia el riesgo primero; los factores protectores (impacto
        // negativo) quedan al final.
        return resultado.OrderByDescending(f => f.Item2).ToList();
    }

    // Antes vivía en BLLFactorRiesgo (se calculaba ahí, contra el segmento, cada vez que se
    // abría la pantalla de detalle). Ahora se llama una sola vez por cliente, en
    // EntrenarYPredecirAsync, y el resultado queda guardado.
    public static string GenerarDescripcion(string nombreFactor, double valorCliente, double media)
    {
        if (nombreFactor == NombresFactorRiesgo.InactividadReciente)
        {
            return $"{Math.Round(valorCliente, 1)} días desde la última actividad vs. " +
                   $"{Math.Round(media, 1)} días en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.BajaFrecuenciaAsistencia)
        {
            return $"{Math.Round(valorCliente, 1)} visitas en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.BajaInteraccionApp)
        {
            return $"{Math.Round(valorCliente, 1)} interacciones con la App en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.HistorialPagosVencidos)
        {
            string pagosCliente = Math.Round(valorCliente, 1) == 1 ? "pago vencido" : "pagos vencidos";
            string pagosMedia = Math.Round(media, 1) == 1 ? "pago vencido" : "pagos vencidos";

            return $"{Math.Round(valorCliente, 1)} {pagosCliente} en el período vs. " +
                   $"{Math.Round(media, 1)} {pagosMedia} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.CancelacionesFrecuentes)
        {
            return $"{Math.Round(valorCliente, 1)} cancelaciones en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.AltaConsultaSoporte)
        {
            return $"{Math.Round(valorCliente, 1)} consultas a soporte en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.AltaProporcionPagosVencidos)
        {
            return $"{Math.Round(valorCliente * 100, 1)}% de los pagos vencieron vs. " +
                   $"{Math.Round(media * 100, 1)}% en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.TendenciaNegativaAsistencia)
        {
            string tendenciaCliente = valorCliente > 0
                ? "aumento"
                : valorCliente < 0
                    ? "disminución"
                    : "sin cambios";

            string tendenciaMedia = media > 0
                ? "aumento"
                : media < 0
                    ? "disminución"
                    : "sin cambios";

            return $"{tendenciaCliente} de {Math.Abs(Math.Round(valorCliente, 1))} visitas " +
                   $"respecto al período anterior vs. {tendenciaMedia} de " +
                   $"{Math.Abs(Math.Round(media, 1))} en el segmento.";
        }

        return $"{Math.Round(valorCliente, 1)} vs. una media de " +
               $"{Math.Round(media, 1)} en el segmento.";
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

