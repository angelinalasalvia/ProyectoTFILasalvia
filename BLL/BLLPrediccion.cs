using BE;
using DAL;

namespace BLL;

public class BLLPrediccion
{
    private readonly IDALPrediccion _dalPrediccion;
    public BLLPrediccion(IDALPrediccion dalPrediccion) => _dalPrediccion = dalPrediccion;

    public async Task<(int Alto, int Medio, int Bajo)> ObtenerResumenNivelesRiesgo(CancellationToken ct = default)
    {
        var todas = await _dalPrediccion.ObtenerPrediccionesAsync(ct);
        return (
            todas.Count(p => p.NivelRiesgo == "Alto"),
            todas.Count(p => p.NivelRiesgo == "Medio"),
            todas.Count(p => p.NivelRiesgo == "Bajo")
        );
    }

    public async Task<List<Prediccion>> ObtenerPredicciones(CancellationToken ct = default)
    {
        var lista = await _dalPrediccion.ObtenerPrediccionesAsync(ct);
        return lista.OrderByDescending(p => p.ProbabilidadAbandono).ToList();
    }

    public async Task<List<Prediccion>> ObtenerPrediccionesSegunFiltro(string nivelRiesgo, CancellationToken ct = default)
    {
        var lista = await ObtenerPredicciones(ct);
        return nivelRiesgo == "Todos" ? lista : lista.Where(p => p.NivelRiesgo == nivelRiesgo).ToList();
    }

    public async Task<List<Prediccion>> ObtenerPrediccionesOrdenadas(string columna, CancellationToken ct = default)
    {
        var lista = await _dalPrediccion.ObtenerPrediccionesAsync(ct);
        return columna switch
        {
            "nombre" => lista.OrderBy(p => p.Cliente!.Nombre).ThenBy(p => p.Cliente!.Apellido).ToList(),
            "nivel" => lista.OrderBy(p => OrdenNivel(p.NivelRiesgo)).ToList(),
            _ => lista.OrderByDescending(p => p.ProbabilidadAbandono).ToList(),
        };
    }

    private static int OrdenNivel(string nivel) => nivel switch { "Alto" => 0, "Medio" => 1, "Bajo" => 2, _ => 3 };

    public async Task<Prediccion?> ObtenerPrediccionPorCliente(int idCliente, CancellationToken ct = default)
        => await _dalPrediccion.ObtenerPrediccionPorClienteAsync(idCliente, ct);
}
