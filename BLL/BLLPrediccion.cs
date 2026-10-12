using BE;
using DAL;

namespace BLL;

public class BLLPrediccion
{
    private readonly AccesoDatos _accesoDatos;
    public BLLPrediccion(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    // Clase auxiliar para mapear el resultado del GROUP BY con el DAL genérico.
    private class ConteoNivel
    {
        public string NivelRiesgo { get; set; } = string.Empty;
        public int Total { get; set; }
    }

    // Una sola consulta agregada en lugar de traer todas las predicciones y contarlas en memoria.
    public async Task<(int Alto, int Medio, int Bajo)> ObtenerResumenNivelesRiesgo(CancellationToken ct = default)
    {
        var filas = (await _accesoDatos.Leer<ConteoNivel>(
            "SELECT NivelRiesgo, COUNT(*) AS Total FROM Prediccion GROUP BY NivelRiesgo",
            ct: ct)).ToList();

        int Contar(string nivel) => filas.Where(f => f.NivelRiesgo == nivel).Sum(f => f.Total);

        return (Contar(NivelesRiesgo.Alto), Contar(NivelesRiesgo.Medio), Contar(NivelesRiesgo.Bajo));
    }

    // Lista por defecto: todos los niveles, ordenada por probabilidad descendente.
    public Task<List<Prediccion>> ObtenerPredicciones(CancellationToken ct = default)
        => ObtenerFiltradasYOrdenadasAsync(NivelesRiesgo.Todos, CriteriosOrden.Probabilidad, ct);

    // Filtro por nivel de riesgo. Respeta el criterio de orden vigente en la pantalla.
    public Task<List<Prediccion>> ObtenerPrediccionesSegunFiltro(
        string nivelRiesgo, string columna = CriteriosOrden.Probabilidad, CancellationToken ct = default)
        => ObtenerFiltradasYOrdenadasAsync(nivelRiesgo, columna, ct);

    // Orden por columna. Respeta el filtro de nivel de riesgo vigente en la pantalla.
    public Task<List<Prediccion>> ObtenerPrediccionesOrdenadas(
        string columna, string nivelRiesgo = NivelesRiesgo.Todos, CancellationToken ct = default)
        => ObtenerFiltradasYOrdenadasAsync(nivelRiesgo, columna, ct);

    private async Task<List<Prediccion>> ObtenerFiltradasYOrdenadasAsync(
        string nivelRiesgo, string columna, CancellationToken ct)
    {
        IEnumerable<Prediccion> consulta = await ObtenerPrediccionesConDatosAsync(ct);

        if (nivelRiesgo != NivelesRiesgo.Todos)
            consulta = consulta.Where(p => p.NivelRiesgo == nivelRiesgo);

        // En todos los criterios, el desempate es por probabilidad descendente
        // para que el orden dentro de un mismo nivel / nombre sea estable.
        consulta = columna switch
        {
            CriteriosOrden.Nombre => consulta
                .OrderBy(p => p.Cliente?.Nombre).ThenBy(p => p.Cliente?.Apellido)
                .ThenByDescending(p => p.ProbabilidadAbandono),
            CriteriosOrden.Nivel => consulta
                .OrderBy(p => OrdenNivel(p.NivelRiesgo))
                .ThenByDescending(p => p.ProbabilidadAbandono),
            _ => consulta.OrderByDescending(p => p.ProbabilidadAbandono),
        };

        return consulta.ToList();
    }

    private static int OrdenNivel(string nivel) => nivel switch
    {
        NivelesRiesgo.Alto => 0,
        NivelesRiesgo.Medio => 1,
        NivelesRiesgo.Bajo => 2,
        _ => 3
    };

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

        var (clausulaClientes, parametrosClientes) = _accesoDatos.ConstruirClausulaIn("idc", idsClientes);
        var (clausulaFactores, parametrosFactores) = _accesoDatos.ConstruirClausulaIn("idf", idsFactores);

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
