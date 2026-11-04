using BE;
using DAL;

namespace BLL;

public class BLLPrediccion
{
    private readonly AccesoDatos _accesoDatos;
    public BLLPrediccion(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    private class ConteoNivel
    {
        public string NivelRiesgo { get; set; } = string.Empty;
        public int Total { get; set; }
    }

    public async Task<(int Alto, int Medio, int Bajo)> ObtenerResumenNivelesRiesgo(CancellationToken ct = default)
    {
        var filas = (await _accesoDatos.Leer<ConteoNivel>(
            "SELECT NivelRiesgo, COUNT(*) AS Total FROM Prediccion GROUP BY NivelRiesgo",
            ct: ct)).ToList();

        int Contar(string nivel) => filas.Where(f => f.NivelRiesgo == nivel).Sum(f => f.Total);

        return (Contar(NivelesRiesgo.Alto), Contar(NivelesRiesgo.Medio), Contar(NivelesRiesgo.Bajo));
    }

    public Task<List<Prediccion>> ObtenerPredicciones(CancellationToken ct = default)
        => ObtenerFiltradasYOrdenadasAsync(NivelesRiesgo.Todos, CriteriosOrden.Probabilidad, ct);

    public Task<List<Prediccion>> ObtenerPrediccionesSegunFiltro(
        string nivelRiesgo, string columna = CriteriosOrden.Probabilidad, CancellationToken ct = default)
        => ObtenerFiltradasYOrdenadasAsync(nivelRiesgo, columna, ct);

    public Task<List<Prediccion>> ObtenerPrediccionesOrdenadas(
        string columna, string nivelRiesgo = NivelesRiesgo.Todos, CancellationToken ct = default)
        => ObtenerFiltradasYOrdenadasAsync(nivelRiesgo, columna, ct);

    private async Task<List<Prediccion>> ObtenerFiltradasYOrdenadasAsync(
        string nivelRiesgo, string columna, CancellationToken ct)
    {
        IEnumerable<Prediccion> consulta = await ObtenerPrediccionesConDatosAsync(ct);

        if (nivelRiesgo != NivelesRiesgo.Todos)
            consulta = consulta.Where(p => p.NivelRiesgo == nivelRiesgo);

        
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
                $"{BLLCliente.SelectClienteConPlanYSede} WHERE c.IdCliente IN ({clausulaClientes})",
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
    public async Task<Prediccion?> ObtenerScoreChurn(int idCliente, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Prediccion>(
            @"SELECT IdPrediccion, IdCliente, IdFactorRiesgo, IdModelo, NivelRiesgo, ProbabilidadAbandono
          FROM Prediccion WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct);

        return resultado.FirstOrDefault();
    }
}
