using BE;
using DAL;

namespace BLL;

public class BLLHistorialAcciones
{
    private readonly AccesoDatos _accesoDatos;

    // Umbral asumido para considerar que una campaña "alcanzó su KPI objetivo".
    // La tabla Campaña no tiene un campo de meta numérica explícito; ajustar si
    // definen un criterio distinto (por ejemplo, un campo MetaTasaExito).
    private const decimal UmbralKpi = 50m;

    public BLLHistorialAcciones(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public Task<MetricasCampana> ObtenerMetricasGlobales(CancellationToken ct = default)
        => ObtenerMetricasGlobalesXPeriodo(DateTime.Now.AddDays(-30), DateTime.Now, ct);

    public async Task<MetricasCampana> ObtenerMetricasGlobalesXPeriodo(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var campanasDelPeriodo = await _accesoDatos.Leer<CampanaConCanal>(
            @"SELECT DISTINCT camp.IdCampaña AS IdCampania, camp.TasaExito, ca.Nombre AS Canal
              FROM Campaña camp
              JOIN Canal ca ON ca.IdCanal = camp.IdCanal
              JOIN HistorialAcciones ha ON ha.IdCampaña = camp.IdCampaña
              WHERE ha.FechaEnvio BETWEEN @desde AND @hasta",
            new { desde, hasta }, ct: ct);

        var lista = campanasDelPeriodo.Where(c => c.TasaExito.HasValue).ToList();

        if (lista.Count == 0)
        {
            return new MetricasCampana
            {
                ExitoGlobal = 0,
                PorcentajeCampanasConKpiAlcanzado = 0,
                CanalMasEfectivo = "Sin datos",
                TasaCanalMasEfectivo = 0,
                ImpactoReduccionChurn = 0
            };
        }

        var exitoGlobal = Math.Round(lista.Average(c => c.TasaExito!.Value), 1);
        var porcentajeKpi = Math.Round((decimal)lista.Count(c => c.TasaExito!.Value >= UmbralKpi) / lista.Count * 100, 1);

        var mejorCanal = lista
            .GroupBy(c => c.Canal)
            .Select(g => new { Canal = g.Key, Tasa = g.Average(x => x.TasaExito!.Value) })
            .OrderByDescending(g => g.Tasa)
            .First();

        return new MetricasCampana
        {
            ExitoGlobal = exitoGlobal,
            PorcentajeCampanasConKpiAlcanzado = porcentajeKpi,
            CanalMasEfectivo = mejorCanal.Canal,
            TasaCanalMasEfectivo = Math.Round(mejorCanal.Tasa, 1),
            // Ver nota en BE.MetricasCampana: no hay historial de Prediccion para calcular esto todavía.
            ImpactoReduccionChurn = 0
        };
    }

    private class CampanaConCanal
    {
        public int IdCampania { get; set; }
        public decimal? TasaExito { get; set; }
        public string Canal { get; set; } = string.Empty;
    }

    private const string SelectHistorialConCliente = @"
        SELECT ha.IdHistorial, ha.EstadoEnvio, ha.FechaEnvio, ha.FechaLectura, ha.FechaConversion,
               ha.TipoAccion, ha.IdCampaña AS IdCampania, ha.IdCliente, ha.IdUsuario, ha.Resultado, ha.Estado,
               COALESCE(ha.FechaConversion, ha.FechaLectura, ha.FechaEnvio) AS FechaAccion,
               c.Nombre AS NombreCliente, c.Apellido AS ApellidoCliente, c.PlanSocio AS PlanCliente
        FROM HistorialAcciones ha
        JOIN Cliente c ON c.IdCliente = ha.IdCliente";

    // Paso 2 y 6-7: historial completo de la campaña, ordenado por fecha de acción (más reciente primero).
    public async Task<List<HistorialAccion>> ObtenerHistorialCampania(int idCampania, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<HistorialAccion>(
            $"{SelectHistorialConCliente} WHERE ha.IdCampaña = @idCampania ORDER BY FechaAccion DESC",
            new { idCampania }, ct: ct);
        return resultado.ToList();
    }

    // Whitelist de columnas ordenables - evita concatenar texto arbitrario del usuario
    // directo en el ORDER BY (eso sería una puerta abierta a SQL injection).
    private static readonly Dictionary<string, string> ColumnasOrdenables = new()
    {
        ["nombre"] = "c.Nombre",
        ["plan"] = "c.PlanSocio",
        ["resultado"] = "ha.Resultado",
        ["fecha"] = "FechaAccion",
    };

    // Pasos 8-9: reordenar según la columna elegida.
    public async Task<List<HistorialAccion>> OrdenarHistorialCampania(int idCampania, string columna, CancellationToken ct = default)
    {
        var columnaSql = ColumnasOrdenables.GetValueOrDefault(columna, "FechaAccion");
        var resultado = await _accesoDatos.Leer<HistorialAccion>(
            $"{SelectHistorialConCliente} WHERE ha.IdCampaña = @idCampania ORDER BY {columnaSql} DESC",
            new { idCampania }, ct: ct);
        return resultado.ToList();
    }

    // Pasos 10-11: filtro por resultado y/o plan (agregué idCampania, ver nota arriba).
    public async Task<List<HistorialAccion>> FiltrarPorClientes(int idCampania, string? resultado, string? plan, CancellationToken ct = default)
    {
        var condiciones = new List<string> { "ha.IdCampaña = @idCampania" };
        var parametros = new Dictionary<string, object?> { ["idCampania"] = idCampania };

        if (!string.IsNullOrWhiteSpace(resultado) && resultado != "Todos")
        {
            condiciones.Add("ha.Resultado = @resultado");
            parametros["resultado"] = resultado;
        }
        if (!string.IsNullOrWhiteSpace(plan) && plan != "Todos")
        {
            condiciones.Add("c.PlanSocio = @plan");
            parametros["plan"] = plan;
        }

        var where = " WHERE " + string.Join(" AND ", condiciones);
        var resultadoLista = await _accesoDatos.Leer<HistorialAccion>(
            $"{SelectHistorialConCliente}{where} ORDER BY FechaAccion DESC",
            parametros, ct: ct);
        return resultadoLista.ToList();
    }
}

