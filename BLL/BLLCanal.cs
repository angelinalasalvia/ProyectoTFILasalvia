using BE;
using DAL;

namespace BLL;

public class BLLCanal
{
    private readonly IAccesoDatos _accesoDatos;
    public BLLCanal(IAccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<List<Canal>> ObtenerCanales(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Canal>("SELECT * FROM Canal ORDER BY Nombre", ct: ct);
        return resultado.ToList();
    }
}
