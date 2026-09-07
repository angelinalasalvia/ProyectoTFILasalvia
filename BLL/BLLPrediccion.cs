using BE;
using DAL;

namespace BLL;

public class BLLPrediccion
{
    private readonly IAccesoDatos _accesoDatos;
    public BLLPrediccion(IAccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<(int Alto, int Medio, int Bajo)> ObtenerResumenNivelesRiesgo(CancellationToken ct = default)
    {
        var todas = await ObtenerPrediccionesConDatosAsync(ct);
        return (
            todas.Count(p => p.NivelRiesgo == "Alto"),
            todas.Count(p => p.NivelRiesgo == "Medio"),
            todas.Count(p => p.NivelRiesgo == "Bajo")
        );
    }

    public async Task<List<Prediccion>> ObtenerPredicciones(CancellationToken ct = default)
    {
        var lista = await ObtenerPrediccionesConDatosAsync(ct);
        return lista.OrderByDescending(p => p.ProbabilidadAbandono).ToList();
    }

    public async Task<List<Prediccion>> ObtenerPrediccionesSegunFiltro(string nivelRiesgo, CancellationToken ct = default)
    {
        var lista = await ObtenerPredicciones(ct);
        return nivelRiesgo == "Todos" ? lista : lista.Where(p => p.NivelRiesgo == nivelRiesgo).ToList();
    }

    public async Task<List<Prediccion>> ObtenerPrediccionesOrdenadas(string columna, CancellationToken ct = default)
    {
        var lista = await ObtenerPrediccionesConDatosAsync(ct);
        return columna switch
        {
            "nombre" => lista.OrderBy(p => p.Cliente!.Nombre).ThenBy(p => p.Cliente!.Apellido).ToList(),
            "nivel" => lista.OrderBy(p => OrdenNivel(p.NivelRiesgo)).ToList(),
            _ => lista.OrderByDescending(p => p.ProbabilidadAbandono).ToList(),
        };
    }

    private static int OrdenNivel(string nivel) => nivel switch { "Alto" => 0, "Medio" => 1, "Bajo" => 2, _ => 3 };

    public async Task<Prediccion?> ObtenerPrediccionPorCliente(int idCliente, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Prediccion>(
            "SELECT * FROM Prediccion WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct);

        var prediccion = resultado.FirstOrDefault();
        if (prediccion == null) return null;

        await CompletarClienteYFactorAsync(new List<Prediccion> { prediccion }, ct);
        return prediccion;
    }

    // Antes esto lo resolvía EF con .Include(p => p.Cliente).Include(p => p.FactorRiesgo).
    // Con el DAL genérico, se trae cada tabla por separado y se arma el join en memoria.
    private async Task<List<Prediccion>> ObtenerPrediccionesConDatosAsync(CancellationToken ct)
    {
        var predicciones = (await _accesoDatos.Leer<Prediccion>("SELECT * FROM Prediccion", ct: ct)).ToList();
        if (predicciones.Count == 0) return predicciones;

        await CompletarClienteYFactorAsync(predicciones, ct);
        return predicciones;
    }

    private async Task CompletarClienteYFactorAsync(List<Prediccion> predicciones, CancellationToken ct)
    {
        var idsClientes = predicciones.Select(p => p.IdCliente).Distinct().ToList();
        var idsFactores = predicciones.Select(p => p.IdFactorRiesgo).Distinct().ToList();

        var (clausulaClientes, parametrosClientes) = ConsultasComunes.ConstruirClausulaIn("idc", idsClientes);
        var (clausulaFactores, parametrosFactores) = ConsultasComunes.ConstruirClausulaIn("idf", idsFactores);

        var clientes = (await _accesoDatos.Leer<Cliente>(
                $"SELECT * FROM Cliente WHERE IdCliente IN ({clausulaClientes})",
                parametrosClientes, ct: ct))
            .ToDictionary(c => c.IdCliente);

        var factores = (await _accesoDatos.Leer<FactorRiesgo>(
                $"SELECT * FROM FactorRiesgo WHERE IdFactorRiesgo IN ({clausulaFactores})",
                parametrosFactores, ct: ct))
            .ToDictionary(f => f.IdFactorRiesgo);

        foreach (var prediccion in predicciones)
        {
            clientes.TryGetValue(prediccion.IdCliente, out var cliente);
            prediccion.Cliente = cliente;

            factores.TryGetValue(prediccion.IdFactorRiesgo, out var factor);
            prediccion.FactorRiesgo = factor;
        }
    }
}
