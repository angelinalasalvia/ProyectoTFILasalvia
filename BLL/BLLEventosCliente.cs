using BE;
using DAL;

namespace BLL;

public class BLLEventosCliente
{
    private readonly AccesoDatos _accesoDatos;

    public BLLEventosCliente(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    /// <summary>Con menos eventos que estos no se generan gráficos ni métricas (CU09 4.1).</summary>
    public const int MinEventosParaAnalisis = 5;

    // DS: ObtenerHistorialCliente(int id) -> más reciente primero, de a tandas (carga progresiva, CU09 pasos 8-9)
    public async Task<List<EventoCliente>> ObtenerHistorialCliente(int idCliente, int saltar = 0, int cantidad = 15,
        CancellationToken ct = default)
    {
        const string sql = @"
        SELECT IdEventosCliente, Evento, Fecha, IdCliente
        FROM EventosCliente
        WHERE IdCliente = @IdCliente
        ORDER BY Fecha DESC, IdEventosCliente DESC
        OFFSET @Saltar ROWS FETCH NEXT @Cantidad ROWS ONLY";
        var filas = await _accesoDatos.Leer<EventoCliente>(sql,
            new { IdCliente = idCliente, Saltar = saltar, Cantidad = cantidad }, ct: ct);
        return filas.ToList();
    }

    public async Task<int> ContarEventos(int idCliente, CancellationToken ct = default)
    {
        const string sql = "SELECT COUNT(*) AS Valor FROM EventosCliente WHERE IdCliente = @IdCliente";
        var filas = await _accesoDatos.Leer<ValorEscalar>(sql, new { IdCliente = idCliente }, ct: ct);
        return filas.FirstOrDefault()?.Valor ?? 0;
    }

    // DS: CalcularMétricasCliente(int id) -> últimos 30 días contra los 30 anteriores, en una sola consulta
    public async Task<MetricasCliente> CalcularMetricasCliente(int idCliente, CancellationToken ct = default)
    {
        const string sql = @"
        SELECT
            (SELECT COUNT(*) FROM EventosCliente WHERE IdCliente = @IdCliente) AS TotalEventos,
            COUNT(DISTINCT CASE WHEN Fecha >= @Desde30 AND Evento IN (@Visita, @UsoApp)
                                THEN CAST(Fecha AS date) END) AS DiasActivos,
            COUNT(DISTINCT CASE WHEN Fecha >= @Desde60 AND Fecha < @Desde30 AND Evento IN (@Visita, @UsoApp)
                                THEN CAST(Fecha AS date) END) AS DiasActivosAnterior,
            SUM(CASE WHEN Fecha >= @Desde30 AND Evento = @UsoApp THEN 1 ELSE 0 END) AS InteraccionesApp,
            SUM(CASE WHEN Fecha >= @Desde60 AND Fecha < @Desde30 AND Evento = @UsoApp THEN 1 ELSE 0 END) AS InteraccionesAppAnterior,
            SUM(CASE WHEN Fecha >= @Desde30 AND Evento = @Visita THEN 1 ELSE 0 END) AS Visitas,
            SUM(CASE WHEN Fecha >= @Desde60 AND Fecha < @Desde30 AND Evento = @Visita THEN 1 ELSE 0 END) AS VisitasAnterior
        FROM EventosCliente
        WHERE IdCliente = @IdCliente AND Fecha >= @Desde60";

        var ahora = DateTime.Now;
        var filas = await _accesoDatos.Leer<MetricasCliente>(sql, new
        {
            IdCliente = idCliente,
            Desde30 = ahora.AddDays(-MetricasCliente.DiasVentana),
            Desde60 = ahora.AddDays(-2 * MetricasCliente.DiasVentana),
            Visita = TipoEvento.VisitaGimnasio,
            UsoApp = TipoEvento.UsoApp
        }, ct: ct);

        return filas.FirstOrDefault() ?? new MetricasCliente();
    }

    // DS: ObtenerHistorialClienteSegúnPeríodo() -> eventos de los últimos N días (incluye hoy)
    public async Task<List<EventoCliente>> ObtenerHistorialClienteSegunPeriodo(int idCliente, int dias,
        CancellationToken ct = default)
    {
        const string sql = @"
        SELECT IdEventosCliente, Evento, Fecha, IdCliente
        FROM EventosCliente
        WHERE IdCliente = @IdCliente AND Fecha >= @Desde
        ORDER BY Fecha";
        var filas = await _accesoDatos.Leer<EventoCliente>(sql,
            new { IdCliente = idCliente, Desde = DateTime.Today.AddDays(-(dias - 1)) }, ct: ct);
        return filas.ToList();
    }
}