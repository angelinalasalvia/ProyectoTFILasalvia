using BE;
using Microsoft.EntityFrameworkCore;

namespace DAL;

public interface IChurnRepository
{
    Task<List<Cliente>> ObtenerClientesConEventosAsync(CancellationToken ct = default);
    Task<Dictionary<string, FactorRiesgo>> AsegurarCatalogoFactoresAsync(IEnumerable<string> nombresFactor, CancellationToken ct = default);
    Task<Modelo> ObtenerOCrearModeloAsync(string nombreModelo, CancellationToken ct = default);
    Task ReemplazarPrediccionesAsync(IEnumerable<int> idsClientes, IEnumerable<Prediccion> nuevasPredicciones, CancellationToken ct = default);
    Task<DateTime?> ObtenerUltimaEjecucionAsync(string nombreModelo, CancellationToken ct = default);
    // Devuelven la entidad de BE directamente, con Cliente y FactorRiesgo
    // ya cargados (EF arma el JOIN por vos al usar .Include()).
    Task<List<Prediccion>> ObtenerPrediccionesAsync(CancellationToken ct = default);
    Task<Prediccion?> ObtenerPrediccionPorClienteAsync(int idCliente, CancellationToken ct = default);
}

public class ChurnRepository : IChurnRepository
{
    private readonly ChurnDbContext _db;
    public ChurnRepository(ChurnDbContext db)
    {
        _db = db;
    }

    public async Task<List<Cliente>> ObtenerClientesConEventosAsync(CancellationToken ct = default)
    {
        return await _db.Clientes.Include(c => c.Eventos).ToListAsync(ct);
    }

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

    public async Task ReemplazarPrediccionesAsync(IEnumerable<int> idsClientes, IEnumerable<Prediccion> nuevasPredicciones, CancellationToken ct = default)
    {
        var idsList = idsClientes.ToList();
        var previas = _db.Predicciones.Where(p => idsList.Contains(p.IdCliente));
        _db.Predicciones.RemoveRange(previas);
        await _db.SaveChangesAsync(ct);
        _db.Predicciones.AddRange(nuevasPredicciones);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<Prediccion>> ObtenerPrediccionesAsync(CancellationToken ct = default)
    {
        return await _db.Predicciones
            .Include(p => p.Cliente)
            .Include(p => p.FactorRiesgo)
            .OrderByDescending(p => p.ProbabilidadAbandono)
            .ToListAsync(ct);
    }

    public async Task<Prediccion?> ObtenerPrediccionPorClienteAsync(int idCliente, CancellationToken ct = default)
    {
        return await _db.Predicciones
            .Include(p => p.Cliente)
            .Include(p => p.FactorRiesgo)
            .FirstOrDefaultAsync(p => p.IdCliente == idCliente, ct);
    }

    public async Task<DateTime?> ObtenerUltimaEjecucionAsync(string nombreModelo, CancellationToken ct = default)
    {
        var modelo = await _db.Modelos.FirstOrDefaultAsync(m => m.Nombre == nombreModelo, ct);
        return modelo?.UltimaEjecucion;
    }
}