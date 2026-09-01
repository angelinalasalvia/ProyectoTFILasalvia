using BE;
using Microsoft.EntityFrameworkCore;

namespace DAL;

public interface IDALFactorRiesgo
{
    Task<Dictionary<string, FactorRiesgo>> AsegurarCatalogoFactoresAsync(IEnumerable<string> nombresFactor, CancellationToken ct = default);
}

public class DALFactorRiesgo : IDALFactorRiesgo
{
    private readonly ChurnDbContext _db;
    public DALFactorRiesgo(ChurnDbContext db) => _db = db;

    public async Task<Dictionary<string, FactorRiesgo>> AsegurarCatalogoFactoresAsync(IEnumerable<string> nombresFactor, CancellationToken ct = default)
    {
        var existentes = await _db.FactoresRiesgo.ToListAsync(ct);
        var porNombre = existentes.ToDictionary(f => f.Nombre);
        foreach (var nombre in nombresFactor)
        {
            if (!porNombre.ContainsKey(nombre))
            {
                var nuevo = new FactorRiesgo { Nombre = nombre, Descripcion = nombre };
                _db.FactoresRiesgo.Add(nuevo);
                porNombre[nombre] = nuevo;
            }
        }
        await _db.SaveChangesAsync(ct);
        return porNombre;
    }
}
