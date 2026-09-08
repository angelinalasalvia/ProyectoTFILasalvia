using BE;
using DAL;

namespace BLL;

public class BLLIncentivo
{
    private readonly IAccesoDatos _accesoDatos;
    public BLLIncentivo(IAccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<List<Incentivo>> ObtenerIncentivos(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Incentivo>("SELECT * FROM Incentivo ORDER BY Nombre", ct: ct);
        return resultado.ToList();
    }
}
