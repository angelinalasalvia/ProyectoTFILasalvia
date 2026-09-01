using BE;
using Microsoft.EntityFrameworkCore;

namespace DAL;

public interface IDALPrediccion
{
    Task ReemplazarPrediccionesAsync(IEnumerable<int> idsClientes, IEnumerable<Prediccion> nuevasPredicciones, CancellationToken ct = default);
    Task<List<Prediccion>> ObtenerPrediccionesAsync(CancellationToken ct = default);
    Task<Prediccion?> ObtenerPrediccionPorClienteAsync(int idCliente, CancellationToken ct = default);
}

public class DALPrediccion : IDALPrediccion
{
    private readonly ChurnDbContext _db;
    public DALPrediccion(ChurnDbContext db) => _db = db;

    public async Task ReemplazarPrediccionesAsync(IEnumerable<int> idsClientes, IEnumerable<Prediccion> nuevasPredicciones, CancellationToken ct = default)
    {
        var idsList = idsClientes.ToList();
        var previas = _db.Predicciones.Where(p => idsList.Contains(p.IdCliente));
        _db.Predicciones.RemoveRange(previas);
        await _db.SaveChangesAsync(ct);
        _db.Predicciones.AddRange(nuevasPredicciones);
        await _db.SaveChangesAsync(ct);
    }

    // Nota: esta clase NO ordena ni filtra - eso es una decisión de negocio
    // (qué orden por defecto, qué filtros existen) y le corresponde a la BLL,
    // no a la DAL. La DAL solo trae los datos con sus relaciones cargadas.
    public async Task<List<Prediccion>> ObtenerPrediccionesAsync(CancellationToken ct = default)
        => await _db.Predicciones.Include(p => p.Cliente).Include(p => p.FactorRiesgo).ToListAsync(ct);

    public async Task<Prediccion?> ObtenerPrediccionPorClienteAsync(int idCliente, CancellationToken ct = default)
        => await _db.Predicciones.Include(p => p.Cliente).Include(p => p.FactorRiesgo)
            .FirstOrDefaultAsync(p => p.IdCliente == idCliente, ct);
}