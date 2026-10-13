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
}
