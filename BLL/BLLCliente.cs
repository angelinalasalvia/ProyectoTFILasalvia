using BE;
using DAL;

namespace BLL;

public class BLLCliente
{
    private readonly AccesoDatos _accesoDatos;
    public BLLCliente(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    // Porcentaje por debajo de la media del segmento a partir del cual se marca una anomalía.
    public const int UmbralAnomalia = 50;

    private const string EstadoSocioActivo = "Socio Activo";

    // --- Parámetros del Engagement Score (0 a 100) ---
    private const double PesoVisitas = 50.0;
    private const double PesoApp = 30.0;
    private const double PesoPago = 20.0;
    private const int MaxVisitasPorBucket = 10;
    private const int MaxUsosAppPorBucket = 10;
    private const int CantidadBuckets = 6;

    public static async Task<List<Cliente>> ObtenerClientesConEventosAsync(AccesoDatos accesoDatos, CancellationToken ct = default)
    {
        var clientes = (await accesoDatos.Leer<Cliente>("SELECT * FROM Cliente", ct: ct)).ToList();
        var eventos = (await accesoDatos.Leer<EventoCliente>("SELECT * FROM EventosCliente", ct: ct)).ToList();

        var eventosPorCliente = eventos.ToLookup(e => e.IdCliente);
        foreach (var cliente in clientes)
            cliente.Eventos = eventosPorCliente[cliente.IdCliente].ToList();

        return clientes;
    }

    public async Task<Cliente?> ObtenerCliente(int idCliente, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Cliente>(
            "SELECT * FROM Cliente WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct);

        var cliente = resultado.FirstOrDefault();
        if (cliente == null) return null;

        var eventos = await _accesoDatos.Leer<EventoCliente>(
            "SELECT * FROM EventosCliente WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct);
        cliente.Eventos = eventos.ToList();

        return cliente;
    }

    // Compara al cliente contra la media de los socios activos de su mismo plan (últimos 30 días).
    // Solo se cargan los clientes y eventos del plan del cliente, no toda la base.
    public async Task<(double UsoInstalaciones, double InteraccionesApp)> ObtenerMetricasRelativasMedia(
        int idCliente, CancellationToken ct = default)
    {
        var cliente = await ObtenerCliente(idCliente, ct);
        if (cliente == null) return (0, 0);

        var segmento = await ObtenerSegmentoActivoConEventosAsync(cliente.PlanSocio, ct);
        if (segmento.Count == 0) return (0, 0);

        var ahora = DateTime.Now;
        var fCliente = FeatureEngineering.Construir(cliente, cliente.Eventos.ToList(), ahora);
        var fSegmento = segmento
            .Select(c => FeatureEngineering.Construir(c, c.Eventos.ToList(), ahora))
            .ToList();

        return (
            CalcularPorcentajeRelativo(fCliente.VisitasUltimos30Dias, fSegmento.Average(f => f.VisitasUltimos30Dias)),
            CalcularPorcentajeRelativo(fCliente.UsoAppUltimos30Dias, fSegmento.Average(f => f.UsoAppUltimos30Dias))
        );
    }

    private async Task<List<Cliente>> ObtenerSegmentoActivoConEventosAsync(string planSocio, CancellationToken ct)
    {
        var parametros = new { planSocio, estado = EstadoSocioActivo };

        var clientes = (await _accesoDatos.Leer<Cliente>(
            "SELECT * FROM Cliente WHERE PlanSocio = @planSocio AND EstadoRegistro = @estado",
            parametros, ct: ct)).ToList();
        if (clientes.Count == 0) return clientes;

        var eventos = (await _accesoDatos.Leer<EventoCliente>(
            @"SELECT e.* FROM EventosCliente e
              INNER JOIN Cliente c ON c.IdCliente = e.IdCliente
              WHERE c.PlanSocio = @planSocio AND c.EstadoRegistro = @estado",
            parametros, ct: ct)).ToList();

        var eventosPorCliente = eventos.ToLookup(e => e.IdCliente);
        foreach (var c in clientes)
            c.Eventos = eventosPorCliente[c.IdCliente].ToList();

        return clientes;
    }

    private static double CalcularPorcentajeRelativo(double valorCliente, double media)
    {
        if (media == 0) return valorCliente == 0 ? 0 : 100;
        return Math.Round((valorCliente - media) / media * 100, 1);
    }

    // Una anomalía es que el uso de instalaciones o de la app esté al menos
    // UmbralAnomalia % por debajo de la media del segmento.
    public (bool HayAnomalia, string Detalle) VerificarAnomalias(
        (double UsoInstalaciones, double InteraccionesApp) metricas)
    {
        const double umbral = -UmbralAnomalia;
        var motivos = new List<string>();

        if (metricas.UsoInstalaciones <= umbral)
            motivos.Add($"el uso de instalaciones está {Math.Abs(metricas.UsoInstalaciones)}% por debajo de la media");
        if (metricas.InteraccionesApp <= umbral)
            motivos.Add($"las interacciones con la app están {Math.Abs(metricas.InteraccionesApp)}% por debajo de la media");

        return motivos.Count > 0
            ? (true, string.Join(" y ", motivos) + ".")
            : (false, "");
    }

    public DateTime? ObtenerUltimaActividad(Cliente cliente) => cliente.Eventos
        .Where(e => e.Evento == TipoEvento.VisitaGimnasio || e.Evento == TipoEvento.UsoApp)
        .Select(e => (DateTime?)e.Fecha).Max();

    public DateTime? ObtenerUltimoPago(Cliente cliente) => cliente.Eventos
        .Where(e => e.Evento == TipoEvento.PagoRegistrado)
        .Select(e => (DateTime?)e.Fecha).Max();

    // Evolución del Engagement Score en CantidadBuckets tramos del período elegido.
    // Una lista vacía significa que el cliente no tiene historial en ese período.
    public List<(string Etiqueta, double Score)> ObtenerEvolucionEngagement(Cliente cliente, string periodo)
    {
        var ahora = DateTime.Now;
        var desde = ahora.AddDays(-DiasSegunPeriodo(periodo));
        var eventosEnRango = cliente.Eventos.Where(e => e.Fecha >= desde).ToList();

        if (eventosEnRango.Count == 0) return new();

        var duracionBucket = (ahora - desde) / CantidadBuckets;
        var puntos = new List<(string Etiqueta, double Score)>();

        for (int i = 0; i < CantidadBuckets; i++)
        {
            var inicio = desde.Add(duracionBucket * i);
            var fin = i == CantidadBuckets - 1 ? ahora : desde.Add(duracionBucket * (i + 1));
            var delBucket = eventosEnRango.Where(e => e.Fecha >= inicio && e.Fecha < fin).ToList();

            int visitas = delBucket.Count(e => e.Evento == TipoEvento.VisitaGimnasio);
            int usosApp = delBucket.Count(e => e.Evento == TipoEvento.UsoApp);
            bool huboPago = delBucket.Any(e => e.Evento == TipoEvento.PagoRegistrado);

            double score =
                Math.Min((double)visitas / MaxVisitasPorBucket, 1.0) * PesoVisitas +
                Math.Min((double)usosApp / MaxUsosAppPorBucket, 1.0) * PesoApp +
                (huboPago ? PesoPago : 0);

            puntos.Add((EtiquetaBucket(fin, periodo), Math.Round(score, 1)));
        }

        return puntos;
    }

    private static string EtiquetaBucket(DateTime fecha, string periodo) => periodo switch
    {
        "año" => fecha.ToString("MMM yy"),
        _ => fecha.ToString("dd/MM") // semana, mes y trimestre: con "MMM" los meses se repetían
    };

    public static int DiasSegunPeriodo(string periodo) => periodo switch
    {
        "semana" => 7,
        "trimestre" => 90,
        "año" => 365,
        _ => 30
    };

    // ---------------------------------------------------------------------------
    // CU08 - Ver listado de clientes
    // ---------------------------------------------------------------------------

    // Un pago "Al día" es uno registrado dentro del último ciclo de facturación.
    private const int DiasCicloPago = 30;

    // Una fila por cliente con última interacción (uso de app), última visita y estado de pagos,
    // todo en la misma consulta (sin una consulta por cliente).
    private const string BaseListado = @"
SELECT c.IdCliente, c.Nombre, c.Apellido, c.Email, c.EstadoRegistro, c.PlanSocio, c.Sexo, c.Sede, c.Telefono,
       ev.UltimaInteraccion, ev.UltimaVisita,
       CASE
           WHEN ev.UltimoPagoVencido IS NOT NULL
                AND (ev.UltimoPago IS NULL OR ev.UltimoPagoVencido > ev.UltimoPago) THEN @Vencido
           WHEN ev.UltimoPago >= @LimiteAlDia THEN @AlDia
           ELSE @Pendiente
       END AS HistorialPagos
FROM Cliente c
LEFT JOIN (
    SELECT IdCliente,
           MAX(CASE WHEN Evento = @UsoApp         THEN Fecha END) AS UltimaInteraccion,
           MAX(CASE WHEN Evento = @Visita         THEN Fecha END) AS UltimaVisita,
           MAX(CASE WHEN Evento = @PagoRegistrado THEN Fecha END) AS UltimoPago,
           MAX(CASE WHEN Evento = @PagoVencido    THEN Fecha END) AS UltimoPagoVencido
    FROM EventosCliente
    GROUP BY IdCliente
) ev ON ev.IdCliente = c.IdCliente";

    // Columnas ordenables (lista cerrada: nunca se concatena texto del usuario en el ORDER BY).
    private static readonly Dictionary<string, string> ColumnasOrden = new()
    {
        ["nombre"] = "l.Nombre, l.Apellido",
        ["plan"] = "l.PlanSocio, l.Nombre, l.Apellido",
        ["interaccion"] = "CASE WHEN l.UltimaInteraccion IS NULL THEN 1 ELSE 0 END, l.UltimaInteraccion DESC, l.Nombre",
        ["visita"] = "CASE WHEN l.UltimaVisita IS NULL THEN 1 ELSE 0 END, l.UltimaVisita DESC, l.Nombre",
        ["pagos"] = $"CASE l.HistorialPagos WHEN '{EstadoPago.Vencido}' THEN 0 WHEN '{EstadoPago.Pendiente}' THEN 1 ELSE 2 END, l.Nombre, l.Apellido"
    };

    private static Dictionary<string, object?> ParametrosListado() => new()
    {
        ["UsoApp"] = TipoEvento.UsoApp,
        ["Visita"] = TipoEvento.VisitaGimnasio,
        ["PagoRegistrado"] = TipoEvento.PagoRegistrado,
        ["PagoVencido"] = TipoEvento.PagoVencido,
        ["Vencido"] = EstadoPago.Vencido,
        ["AlDia"] = EstadoPago.AlDia,
        ["Pendiente"] = EstadoPago.Pendiente,
        ["LimiteAlDia"] = DateTime.Now.AddDays(-DiasCicloPago)
    };

    private async Task<List<Cliente>> LeerListado(string where, string orderBy,
        Dictionary<string, object?> parametros, CancellationToken ct)
    {
        var sql = $"SELECT l.* FROM ({BaseListado}) l {where} ORDER BY {orderBy}";
        var filas = await _accesoDatos.Leer<Cliente>(sql, parametros, ct: ct);
        return filas.ToList();
    }

    // DS: ObtenerClientes(): List<Cliente>  (orden alfabético por nombre, CU08 paso 4)
    public Task<List<Cliente>> ObtenerClientes(CancellationToken ct = default)
        => LeerListado("", ColumnasOrden["nombre"], ParametrosListado(), ct);

    // DS: ObtenerClientesOrdenados(string columna): List<Cliente>
    public Task<List<Cliente>> ObtenerClientesOrdenados(string columna, CancellationToken ct = default)
    {
        var orden = ColumnasOrden.GetValueOrDefault(columna ?? "", ColumnasOrden["nombre"]);
        return LeerListado("", orden, ParametrosListado(), ct);
    }

    // DS: BuscarCliente(string criterio): List<Cliente>  (por nombre, apellido o email)
    public Task<List<Cliente>> BuscarCliente(string criterio, CancellationToken ct = default)
    {
        var parametros = ParametrosListado();
        parametros["Patron"] = "%" + (criterio ?? "").Trim()
            .Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";

        const string where = @"WHERE (l.Nombre LIKE @Patron OR l.Apellido LIKE @Patron
                              OR (l.Nombre + ' ' + l.Apellido) LIKE @Patron OR l.Email LIKE @Patron)";
        return LeerListado(where, ColumnasOrden["nombre"], parametros, ct);
    }

    public Task<List<Cliente>> ObtenerClientesSegunFiltro(string? plan, string? estadoPago, CancellationToken ct = default)
    {
        var parametros = ParametrosListado();
        var condiciones = new List<string>();

        if (!string.IsNullOrWhiteSpace(plan))
        {
            condiciones.Add("l.PlanSocio = @Plan");
            parametros["Plan"] = plan;
        }
        if (!string.IsNullOrWhiteSpace(estadoPago))
        {
            condiciones.Add("l.HistorialPagos = @EstadoPagoFiltro");
            parametros["EstadoPagoFiltro"] = estadoPago;
        }

        var where = condiciones.Count == 0 ? "" : "WHERE " + string.Join(" AND ", condiciones);
        return LeerListado(where, ColumnasOrden["nombre"], parametros, ct);
    }
    // CU13 - ObtenerDatosFinancierosRetencion.
    // MRR recuperado = suma del precio del plan de los clientes rescatados en el período.
    // LTV recuperado = MRR x meses de vida esperada (constante de abajo).
    public const int MesesVidaEsperadaLtv = 12;

    public async Task<DatosFinancieros> ObtenerDatosFinancierosRetencion(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var desdeInclusive = desde.Date;
        var hastaExclusive = hasta.Date.AddDays(1);
        var desdeAnterior = desdeInclusive - (hastaExclusive - desdeInclusive);

        var rescates = (await _accesoDatos.Leer<RescateConPrecio>(
            @"SELECT ha.FechaEnvio, p.Precio
          FROM HistorialAcciones ha
          JOIN Cliente c ON c.IdCliente = ha.IdCliente
          JOIN [Plan] p ON p.Nombre = c.PlanSocio
          WHERE ha.Resultado = @rescatado
            AND ha.FechaEnvio >= @desdeAnterior AND ha.FechaEnvio < @hastaExclusive",
            new { rescatado = HistorialAccion.ResultadoRescatado, desdeAnterior, hastaExclusive }, ct: ct)).ToList();

        var mrr = rescates.Where(r => r.FechaEnvio >= desdeInclusive).Sum(r => r.Precio);
        var mrrAnterior = rescates.Where(r => r.FechaEnvio < desdeInclusive).Sum(r => r.Precio);

        return new DatosFinancieros
        {
            MrrRecuperado = mrr,
            MrrRecuperadoAnterior = mrrAnterior,
            LtvRecuperado = mrr * MesesVidaEsperadaLtv,
            LtvRecuperadoAnterior = mrrAnterior * MesesVidaEsperadaLtv
        };
    }

    private class RescateConPrecio
    {
        public DateTime FechaEnvio { get; set; }
        public decimal Precio { get; set; }
    }

    private static readonly string[] NombresMeses =
    { "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre" };

    // CU14 - CalcularAnalisisCohortes(periodo, segmento).
    // Cohorte = mes del primer evento del cliente. El período define cuántos meses hacia atrás se muestran (30d: 3, 90d: 6, Año: 12).
    // Celda "Mes N" = % de la cohorte que seguía sin darse de baja al comenzar el N-ésimo mes después del mes de alta.
    // Si ese momento todavía no llegó, la celda queda vacía (null). Las cohortes sin clientes no se muestran.
    public async Task<List<CohorteRetencion>> CalcularAnalisisCohortes(int dias, string? segmento = null, CancellationToken ct = default)
    {
        const int columnas = 6; // Mes 0 a Mes 5
        var mesesAtras = dias <= 30 ? 3 : dias <= 90 ? 6 : 12;
        var hoy = DateTime.Today;
        var primerMes = new DateTime(hoy.Year, hoy.Month, 1).AddMonths(-(mesesAtras - 1));

        var clientes = await ObtenerCiclosDeVidaAsync(_accesoDatos, segmento, ct);
        var cohortes = new List<CohorteRetencion>();

        for (var inicio = primerMes; inicio <= hoy; inicio = inicio.AddMonths(1))
        {
            var desdeMes = inicio;
            var hastaMes = inicio.AddMonths(1);
            var miembros = clientes.Where(c => c.PrimerEvento >= desdeMes && c.PrimerEvento < hastaMes).ToList();
            if (miembros.Count == 0) continue;

            var valores = new List<int?>();
            for (var n = 0; n < columnas; n++)
            {
                var referencia = desdeMes.AddMonths(n);
                if (referencia > hoy) { valores.Add(null); continue; }

                var retenidos = miembros.Count(c => c.FechaBaja is null || c.FechaBaja.Value >= referencia);
                valores.Add((int)Math.Round(retenidos * 100.0 / miembros.Count));
            }

            cohortes.Add(new CohorteRetencion
            {
                Nombre = $"{NombresMeses[desdeMes.Month - 1]} {desdeMes.Year}",
                Tamano = miembros.Count,
                Valores = valores
            });
        }

        return cohortes;
    }
    public static async Task<List<ClienteCicloVida>> ObtenerCiclosDeVidaAsync(
    AccesoDatos accesoDatos, string? segmento, CancellationToken ct = default)
    {
        var (condicion, valor) = SegmentoRetencion.Resolver(segmento);

        var filas = await accesoDatos.Leer<ClienteCicloVida>(
            $@"SELECT c.IdCliente, c.EstadoRegistro, MIN(e.Fecha) AS PrimerEvento, MAX(e.Fecha) AS UltimoEvento
           FROM Cliente c
           JOIN EventosCliente e ON e.IdCliente = c.IdCliente
           WHERE {condicion}
           GROUP BY c.IdCliente, c.EstadoRegistro",
            new { segmentoValor = valor }, ct: ct);

        return filas.ToList();
    }
}
