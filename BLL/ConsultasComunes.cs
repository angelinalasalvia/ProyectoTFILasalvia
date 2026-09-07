using BE;
using DAL;

namespace BLL;

/// <summary>
/// Consultas que usa más de un BLL. Evita repetir la misma lógica de "join en memoria"
/// en BLLCliente, BLLFactorRiesgo y BLLModelo.
///
/// Con EF Core esto se resolvía con .Include(c => c.Eventos). Con el DAL genérico,
/// como Leer&lt;T&gt; solo mapea columnas planas, hay que traer las dos tablas por
/// separado y unirlas del lado del BLL.
/// </summary>
internal static class ConsultasComunes
{
    public static async Task<List<Cliente>> ObtenerClientesConEventosAsync(IAccesoDatos accesoDatos, CancellationToken ct = default)
    {
        var clientes = (await accesoDatos.Leer<Cliente>("SELECT * FROM Cliente", ct: ct)).ToList();
        var eventos = (await accesoDatos.Leer<EventoCliente>("SELECT * FROM EventosCliente", ct: ct)).ToList();

        var eventosPorCliente = eventos.ToLookup(e => e.IdCliente);
        foreach (var cliente in clientes)
            cliente.Eventos = eventosPorCliente[cliente.IdCliente].ToList();

        return clientes;
    }

    // Arma una cláusula "IN (@id0, @id1, ...)" ya que SqlParameter no acepta listas directamente.
    public static (string Clausula, Dictionary<string, object?> Parametros) ConstruirClausulaIn(string prefijo, List<int> valores)
    {
        var nombres = new List<string>();
        var parametros = new Dictionary<string, object?>();
        for (int i = 0; i < valores.Count; i++)
        {
            var nombreParametro = $"{prefijo}{i}";
            nombres.Add($"@{nombreParametro}");
            parametros[nombreParametro] = valores[i];
        }
        return (string.Join(", ", nombres), parametros);
    }
}
