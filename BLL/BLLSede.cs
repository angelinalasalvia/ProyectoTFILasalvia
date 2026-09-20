using BE;
using DAL;

namespace BLL;

public class BLLSede
{
    private readonly AccesoDatos _accesoDatos;
    public BLLSede(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<List<Sede>> ObtenerSedes(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Sede>("SELECT * FROM Sede ORDER BY Nombre", ct: ct);
        return resultado.ToList();
    }
}