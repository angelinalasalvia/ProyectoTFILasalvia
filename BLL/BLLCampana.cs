using BE;
using DAL;

namespace BLL;

public class BLLCampana
{
    private readonly IAccesoDatos _accesoDatos;
    public BLLCampana(IAccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

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

        if (!string.IsNullOrWhiteSpace(canal) && canal != "Todos los canales")
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

    public async Task<Campana?> ObtenerCampañaPorId(int id, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Campana>($"{SelectBase} WHERE camp.IdCampaña = @id", new { id }, ct: ct);
        return resultado.FirstOrDefault();
    }

    // CU06: crea la campaña y devuelve el id generado.
    public async Task<int> CrearCampana(Campana campana, CancellationToken ct = default)
    {
        var insertados = await _accesoDatos.Leer<IdSolamente>(
            @"INSERT INTO Campaña (Asunto, Objetivo, IdCanal, IdIncentivo, Mensaje, AsuntoEmail, Meta)
              OUTPUT INSERTED.IdCampaña AS Id
              VALUES (@Nombre, @Objetivo, @IdCanal, @IdIncentivo, @Mensaje, @AsuntoEmail, @Meta)",
            new
            {
                campana.Nombre,
                campana.Objetivo,
                campana.IdCanal,
                campana.IdIncentivo,
                campana.Mensaje,
                campana.AsuntoEmail,
                campana.Meta
            }, ct: ct);

        return insertados.First().Id;
    }

    // CU07: actualiza. El nombre (Asunto) no se toca - en el formulario ya
    // queda deshabilitado en modo edición, es la identidad de la campaña.
    public async Task ActualizarCampana(Campana campana, CancellationToken ct = default)
    {
        await _accesoDatos.Modificar(
            @"UPDATE Campaña
              SET Objetivo = @Objetivo, IdCanal = @IdCanal, IdIncentivo = @IdIncentivo,
                  Mensaje = @Mensaje, AsuntoEmail = @AsuntoEmail, Meta = @Meta
              WHERE IdCampaña = @IdCampania",
            new
            {
                campana.Objetivo,
                campana.IdCanal,
                campana.IdIncentivo,
                campana.Mensaje,
                campana.AsuntoEmail,
                campana.Meta,
                campana.IdCampania
            }, ct: ct);
    }

    public async Task<List<Incentivo>> ObtenerIncentivosDisponibles(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Incentivo>("SELECT * FROM Incentivo", ct: ct);
        return resultado.ToList();
    }

    public async Task<int> ObtenerIdCanalPorNombre(string nombreCanal, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<IdSolamente>(
            "SELECT IdCanal AS Id FROM Canal WHERE Nombre = @nombreCanal",
            new { nombreCanal }, ct: ct);
        return resultado.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException($"No existe el canal '{nombreCanal}'.");
    }

    private class IdSolamente { public int Id { get; set; } }
}