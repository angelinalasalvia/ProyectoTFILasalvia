using BE;
using DAL;

namespace BLL;

public class BLLCampana
{
    private readonly AccesoDatos _accesoDatos;
    public BLLCampana(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    private const string SelectBase = @"
        SELECT camp.IdCampaña AS IdCampania, camp.Asunto AS Nombre, ca.Nombre AS Canal, ca.IdCanal,
               camp.Objetivo, camp.TasaExito, camp.ClientesAlcanzados, camp.Meta,
               camp.AsuntoEmail, camp.Mensaje, camp.IdIncentivo
        FROM Campaña camp
        JOIN Canal ca ON ca.IdCanal = camp.IdCanal";

    public async Task<List<Campana>> ObtenerCampanas(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Campana>($"{SelectBase} ORDER BY camp.IdCampaña DESC", ct: ct);
        return resultado.ToList();
    }

    public async Task<List<Campana>> ObtenerCampanasXFiltro(string? canal, string? nombre, CancellationToken ct = default)
    {
        var condiciones = new List<string>();
        var parametros = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(canal))
        {
            condiciones.Add("ca.Nombre = @canal");
            parametros["canal"] = canal;
        }
        if (!string.IsNullOrWhiteSpace(nombre))
        {
            condiciones.Add("camp.Asunto LIKE @nombre");
            parametros["nombre"] = $"%{nombre}%";
        }

        var where = condiciones.Count > 0 ? " WHERE " + string.Join(" AND ", condiciones) : "";
        var resultado = await _accesoDatos.Leer<Campana>(
            $"{SelectBase}{where} ORDER BY camp.IdCampaña DESC",
            parametros.Count > 0 ? parametros : null, ct: ct);
        return resultado.ToList();
    }

    // Usado también por CU05 (CampanaDetalle.razor). El DS de CU07 lo llama
    // "ObtenerCampaña", pero es la misma operación que ya está en uso acá con
    // este nombre - unificar el nombre en la documentación de CU07 en vez de
    // duplicar el método.
    public async Task<Campana?> ObtenerCampañaPorId(int id, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Campana>($"{SelectBase} WHERE camp.IdCampaña = @id", new { id }, ct: ct);
        return resultado.FirstOrDefault();
    }

    // CU06 - GuardarCampaña(string nombre, int idcanal, string mensaje, int idincentivo, int objetivo): int
    // Se agregan "objetivoDescripcion" y "asuntoEmail" porque son columnas NOT NULL / usadas en
    // CU04 (Asunto y Objetivo de texto) que el diagrama no detalla en la firma abreviada.
    public async Task<int> GuardarCampana(
        string nombre, int idCanal, string mensaje, int idIncentivo, int metaClientes,
        string objetivoDescripcion, string? asuntoEmail, CancellationToken ct = default)
    {
        return await _accesoDatos.Escribir(
            @"INSERT INTO Campaña (Asunto, Objetivo, IdCanal, IdIncentivo, Mensaje, AsuntoEmail, Meta)
              VALUES (@nombre, @objetivoDescripcion, @idCanal, @idIncentivo, @mensaje, @asuntoEmail, @metaClientes)",
            new { nombre, objetivoDescripcion, idCanal, idIncentivo, mensaje, asuntoEmail, metaClientes }, ct: ct);
    }

    public async Task<bool> ExisteCampanaConNombre(string nombre, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<IdSolamente>(
            "SELECT IdCampaña AS Id FROM Campaña WHERE LTRIM(RTRIM(Asunto)) = @nombre",
            new { nombre = nombre.Trim() }, ct: ct);
        return resultado.Any();
    }

    // CU07 - ModificarCampaña(int id, int idcanal, string mensaje, int idincentivo, int objetivo): int
    // El nombre (Asunto) no se toca: en el formulario ya queda deshabilitado en modo edición.
    public async Task<int> ModificarCampana(
        int idCampania, int idCanal, string mensaje, int idIncentivo, int metaClientes,
        string objetivoDescripcion, string? asuntoEmail, CancellationToken ct = default)
    {
        return await _accesoDatos.Modificar(
            @"UPDATE Campaña
              SET Objetivo = @objetivoDescripcion, IdCanal = @idCanal, IdIncentivo = @idIncentivo,
                  Mensaje = @mensaje, AsuntoEmail = @asuntoEmail, Meta = @metaClientes
              WHERE IdCampaña = @idCampania",
            new { objetivoDescripcion, idCanal, idIncentivo, mensaje, asuntoEmail, metaClientes, idCampania }, ct: ct);
    }

    // Elimina la campaña. Si tiene acciones (HistorialAcciones) o reglas asociadas,
    // la base de datos rechaza el borrado por la FK; ese caso se traduce en la UI.
    public async Task EliminarCampana(int idCampania, CancellationToken ct = default)
    {
        await _accesoDatos.Eliminar("DELETE FROM Campaña WHERE IdCampaña = @idCampania", new { idCampania }, ct: ct);
    }

    private class IdSolamente { public int Id { get; set; } }

    // CU13 - ObtenerEfectividadIncentivos.
    // Costo total = suma del costo del incentivo de la campaña por cada cliente rescatado.
    // "Sin incentivo" no entra en el ranking.
    public async Task<DatosIncentivos> ObtenerEfectividadIncentivos(DateTime desde, DateTime hasta, CancellationToken ct = default)
    {
        var desdeInclusive = desde.Date;
        var hastaExclusive = hasta.Date.AddDays(1);
        var desdeAnterior = desdeInclusive - (hastaExclusive - desdeInclusive);

        var filas = (await _accesoDatos.Leer<AccionConIncentivo>(
            @"SELECT i.Nombre AS Incentivo, i.Costo, ha.FechaEnvio, ha.Resultado
          FROM HistorialAcciones ha
          JOIN Campaña camp ON camp.IdCampaña = ha.IdCampaña
          JOIN Incentivo i ON i.IdIncentivo = camp.IdIncentivo
          WHERE ha.FechaEnvio >= @desdeAnterior AND ha.FechaEnvio < @hastaExclusive
            AND ha.EstadoEnvio <> @pendiente AND ha.EstadoEnvio <> @fallido",
            new { desdeAnterior, hastaExclusive, pendiente = HistorialAccion.EnvioPendiente, fallido = HistorialAccion.EnvioFallido },
            ct: ct)).ToList();

        var actuales = filas.Where(f => f.FechaEnvio >= desdeInclusive).ToList();
        var anteriores = filas.Where(f => f.FechaEnvio < desdeInclusive).ToList();
        bool Rescatada(AccionConIncentivo a) => a.Resultado == HistorialAccion.ResultadoRescatado;

        return new DatosIncentivos
        {
            CostoTotal = actuales.Where(Rescatada).Sum(a => a.Costo),
            CostoTotalAnterior = anteriores.Where(Rescatada).Sum(a => a.Costo),
            Incentivos = actuales.Where(a => a.Incentivo != "Sin incentivo")
                .GroupBy(a => a.Incentivo)
                .Select(g => new EfectividadIncentivo
                {
                    Nombre = g.Key,
                    Enviados = g.Count(),
                    Rescatados = g.Count(Rescatada)
                })
                .OrderByDescending(x => x.Rescatados).ThenByDescending(x => x.Enviados)
                .ToList()
        };
    }

    private class AccionConIncentivo
    {
        public string Incentivo { get; set; } = string.Empty;
        public decimal Costo { get; set; }
        public DateTime FechaEnvio { get; set; }
        public string Resultado { get; set; } = string.Empty;
    }
}
