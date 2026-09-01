using BE;
using Microsoft.EntityFrameworkCore;

namespace DAL;

public interface IDALCliente
{
    Task<List<Cliente>> ObtenerClientesConEventosAsync(CancellationToken ct = default);
    Task<Cliente?> ObtenerClientePorIdAsync(int idCliente, CancellationToken ct = default);
}

public class DALCliente : IDALCliente
{
    private readonly ChurnDbContext _db;
    public DALCliente(ChurnDbContext db) => _db = db;

    public async Task<List<Cliente>> ObtenerClientesConEventosAsync(CancellationToken ct = default)
        => await _db.Clientes.Include(c => c.Eventos).ToListAsync(ct);

    public async Task<Cliente?> ObtenerClientePorIdAsync(int idCliente, CancellationToken ct = default)
        => await _db.Clientes.Include(c => c.Eventos).FirstOrDefaultAsync(c => c.IdCliente == idCliente, ct);
}
