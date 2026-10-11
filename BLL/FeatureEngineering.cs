using BLL;
using BE;
using System.Numerics;

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
    // pipeline. Después de estas 11 columnas el vector sigue con PlanSocioEncoded, SedeEncoded
    // y SexoEncoded (variable, según cuántas categorías haya).
    //
    // NombreFactor es null en las columnas que el modelo usa para mejorar la predicción pero que no
    // tienen hoy un factor propio en el catálogo (ReservasUltimos30Dias, PagosRegistradosUltimos60Dias,
    // AntiguedadDias): influyen en la predicción pero no se muestran como factor.
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
    // Explicación de la predicción: cuántos puntos de probabilidad aporta cada factor
    // ------------------------------------------------------------------------------------
    // La explicación se calcula con el PROPIO modelo (método de Shapley), sin depender de las
    // contribuciones internas de ML.NET:
    //
    //  1) "Referencia" es el valor típico de cada indicador en el segmento (plan) del socio.
    //  2) Para cada combinación de los 8 indicadores (2^8 = 256) se arma una copia del socio con el
    //     valor REAL en los indicadores elegidos y el valor TÍPICO en el resto, y se le pide la
    //     probabilidad al modelo.
    //  3) El impacto de un factor es cuánto cambia, en promedio, la probabilidad al pasar ese
    //     indicador del valor típico al valor real del socio (promediando sobre todas las
    //     combinaciones de los otros indicadores, con los pesos de Shapley).
    //
    // Resultado: la suma de los impactos es EXACTAMENTE
    //     probabilidad final (todo real) - probabilidad de partida (todo típico).
    // La pantalla muestra: riesgo de partida + puntos de cada factor = probabilidad de abandono.
    //
    // Lo que no es uno de los 8 indicadores (plan, sede, sexo, reservas, pagos registrados y
    // antigüedad) queda con los valores reales del socio en todas las combinaciones: no se explica
    // por separado, pero forma parte tanto del riesgo de partida como del final.
    //
    // Impacto: puntos de probabilidad con signo (positivo = empuja hacia el riesgo, negativo = lo
    // reduce). Cabe en Prediccion_FactorRiesgo.Impacto (decimal(18,2)).
    //
    // 'predecirProbabilidades' recibe una lista de socios y devuelve, en el mismo orden, la
    // probabilidad (0..1) que da el modelo para cada uno.
    public static (double ProbabilidadFinal, double ProbabilidadBase,
                   List<(string NombreFactor, decimal Impacto, string Descripcion)> Factores)
        MapearFactores(
            ChurnInputData cliente,
            Dictionary<string, float> referencia,
            Func<IReadOnlyList<ChurnInputData>, float[]> predecirProbabilidades)
    {
        var nombres = IndicadoresDeRiesgo.Select(i => i.NombreFactor).ToArray();
        int n = nombres.Length;
        int combinaciones = 1 << n;

        // Cada número 'mask' representa una combinación: el bit i encendido = el indicador i
        // vale lo que tiene el socio; apagado = vale lo típico del segmento.
        var filas = new List<ChurnInputData>(combinaciones);
        for (int mask = 0; mask < combinaciones; mask++)
        {
            var fila = Copiar(cliente);
            for (int i = 0; i < n; i++)
                if ((mask & (1 << i)) == 0)
                    Asignadores[nombres[i]](fila, referencia[nombres[i]]);
            filas.Add(fila);
        }

        // v[mask] = probabilidad del modelo para esa combinación.
        // v[0] = todo típico (riesgo de partida); v[combinaciones - 1] = todo real (predicción final).
        float[] v = predecirProbabilidades(filas);

        // Peso de Shapley para una combinación de tamaño s: s! * (n - s - 1)! / n!
        static double Fact(int k) { double r = 1; for (int j = 2; j <= k; j++) r *= j; return r; }
        var peso = new double[n];
        for (int s = 0; s < n; s++) peso[s] = Fact(s) * Fact(n - s - 1) / Fact(n);

        var resultado = new List<(string NombreFactor, decimal Impacto, string Descripcion)>();
        for (int i = 0; i < n; i++)
        {
            // Aporte promedio del factor i: diferencia de probabilidad al "encenderlo", sobre todas
            // las combinaciones que no lo incluyen.
            double aporte = 0;
            for (int mask = 0; mask < combinaciones; mask++)
            {
                if ((mask & (1 << i)) != 0) continue;
                int s = BitOperations.PopCount((uint)mask);
                aporte += peso[s] * (v[mask | (1 << i)] - v[mask]);
            }

            var indicador = IndicadoresDeRiesgo.First(x => x.NombreFactor == nombres[i]);
            resultado.Add((
                nombres[i],
                (decimal)Math.Round(aporte * 100, 2),
                GenerarDescripcion(nombres[i], indicador.Selector(cliente), referencia[nombres[i]])));
        }

        // El que más empujó hacia el riesgo primero (BLLModelo usa el primero como "factor
        // principal"); los factores protectores (impacto negativo) quedan al final.
        resultado = resultado.OrderByDescending(f => f.Impacto).ToList();

        return (v[combinaciones - 1], v[0], resultado);
    }

    // Cómo asignar el valor de cada indicador sobre una copia del socio (para armar las combinaciones).
    private static readonly Dictionary<string, Action<ChurnInputData, float>> Asignadores = new()
    {
        [NombresFactorRiesgo.InactividadReciente] = (d, v) => d.DiasDesdeUltimaActividad = v,
        [NombresFactorRiesgo.BajaFrecuenciaAsistencia] = (d, v) => d.VisitasUltimos30Dias = v,
        [NombresFactorRiesgo.BajaInteraccionApp] = (d, v) => d.UsoAppUltimos30Dias = v,
        [NombresFactorRiesgo.HistorialPagosVencidos] = (d, v) => d.PagosVencidosUltimos60Dias = v,
        [NombresFactorRiesgo.CancelacionesFrecuentes] = (d, v) => d.CancelacionesUltimos30Dias = v,
        [NombresFactorRiesgo.AltaConsultaSoporte] = (d, v) => d.ConsultasSoporteUltimos90Dias = v,
        [NombresFactorRiesgo.AltaProporcionPagosVencidos] = (d, v) => d.ProporcionPagosVencidos = v,
        [NombresFactorRiesgo.TendenciaNegativaAsistencia] = (d, v) => d.TendenciaVisitas = v,
    };

    private static ChurnInputData Copiar(ChurnInputData d) => new()
    {
        VisitasUltimos30Dias = d.VisitasUltimos30Dias,
        UsoAppUltimos30Dias = d.UsoAppUltimos30Dias,
        ReservasUltimos30Dias = d.ReservasUltimos30Dias,
        CancelacionesUltimos30Dias = d.CancelacionesUltimos30Dias,
        DiasDesdeUltimaActividad = d.DiasDesdeUltimaActividad,
        PagosVencidosUltimos60Dias = d.PagosVencidosUltimos60Dias,
        ConsultasSoporteUltimos90Dias = d.ConsultasSoporteUltimos90Dias,
        PagosRegistradosUltimos60Dias = d.PagosRegistradosUltimos60Dias,
        ProporcionPagosVencidos = d.ProporcionPagosVencidos,
        TendenciaVisitas = d.TendenciaVisitas,
        AntiguedadDias = d.AntiguedadDias,
        PlanSocio = d.PlanSocio,
        Sede = d.Sede,
        Sexo = d.Sexo,
        Abandono = d.Abandono
    };

    // Antes vivía en BLLFactorRiesgo (se calculaba ahí, contra el segmento, cada vez que se
    // abría la pantalla de detalle). Ahora se llama una sola vez por cliente, en
    // EntrenarYPredecirAsync, y el resultado queda guardado.
    public static string GenerarDescripcion(string nombreFactor, double valorCliente, double media)
    {
        double v = Math.Round(valorCliente, 1);
        string Plural(string singular, string plural) => v == 1 ? singular : plural;

        if (nombreFactor == NombresFactorRiesgo.InactividadReciente)
            return $"{v} días desde la última actividad registrada.";
        if (nombreFactor == NombresFactorRiesgo.BajaFrecuenciaAsistencia)
            return $"{v} {Plural("visita", "visitas")} al gimnasio en los últimos 30 días.";
        if (nombreFactor == NombresFactorRiesgo.BajaInteraccionApp)
            return $"{v} {Plural("uso", "usos")} de la app en los últimos 30 días.";
        if (nombreFactor == NombresFactorRiesgo.HistorialPagosVencidos)
            return $"{v} {Plural("pago vencido", "pagos vencidos")} en los últimos 60 días.";
        if (nombreFactor == NombresFactorRiesgo.CancelacionesFrecuentes)
            return $"{v} {Plural("cancelación", "cancelaciones")} de reservas en los últimos 30 días.";
        if (nombreFactor == NombresFactorRiesgo.AltaConsultaSoporte)
            return $"{v} {Plural("consulta", "consultas")} a soporte en los últimos 90 días.";
        if (nombreFactor == NombresFactorRiesgo.AltaProporcionPagosVencidos)
            return $"{Math.Round(valorCliente * 100, 1)}% de los pagos de los últimos 60 días vencieron.";
        if (nombreFactor == NombresFactorRiesgo.TendenciaNegativaAsistencia)
            return v == 0
                ? "Sin cambios en las visitas: últimos 30 días vs. los 30 días previos."
                : $"{(v > 0 ? "+" : "")}{v} visitas: últimos 30 días vs. los 30 días previos.";

        return $"{v}.";
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
