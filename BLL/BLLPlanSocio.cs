using BE;
using DAL;

namespace BLL;

public class BLLPlanSocio
{
    private readonly AccesoDatos _accesoDatos;
    public BLLPlanSocio(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<List<PlanSocio>> ObtenerPlanes(CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<PlanSocio>("SELECT * FROM [Plan] ORDER BY Nombre", ct: ct);
        return resultado.ToList();
    }
}
