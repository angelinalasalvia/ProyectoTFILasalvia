using BE;
using Microsoft.EntityFrameworkCore;

namespace DAL;

public interface IDALModelo
{
    Task<Modelo> ObtenerOCrearModeloAsync(string nombreModelo, CancellationToken ct = default);
    Task<DateTime?> ObtenerFechaUltimaEjecucionAsync(string nombreModelo, CancellationToken ct = default);
    Task<string?> ObtenerNombrePorIdAsync(int idModelo, CancellationToken ct = default);
}

public class DALModelo : IDALModelo
{
    private readonly ChurnDbContext _db;
    public DALModelo(ChurnDbContext db) => _db = db;

    public async Task<Modelo> ObtenerOCrearModeloAsync(string nombreModelo, CancellationToken ct = default)
    {
        var modelo = await _db.Modelos.FirstOrDefaultAsync(m => m.Nombre == nombreModelo, ct);
        if (modelo == null)
        {
            modelo = new Modelo { Nombre = nombreModelo };
            _db.Modelos.Add(modelo);
        }
        modelo.UltimaEjecucion = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return modelo;
    }

    public async Task<DateTime?> ObtenerFechaUltimaEjecucionAsync(string nombreModelo, CancellationToken ct = default)
    {
        var modelo = await _db.Modelos.FirstOrDefaultAsync(m => m.Nombre == nombreModelo, ct);
        return modelo?.UltimaEjecucion;
    }

    public async Task<string?> ObtenerNombrePorIdAsync(int idModelo, CancellationToken ct = default)
    {
        var modelo = await _db.Modelos.FindAsync(new object[] { idModelo }, ct);
        return modelo?.Nombre;
    }
}