using BE;
using DAL;

namespace BLL;

public class BLLFactorRiesgo
{
    private readonly AccesoDatos _accesoDatos;
    public BLLFactorRiesgo(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<List<FactorRiesgo>> ObtenerFactorRiesgo(int idCliente, string periodo = "mes", CancellationToken ct = default)
    {
        var prediccion = (await _accesoDatos.Leer<Prediccion>(
            "SELECT TOP 1 * FROM Prediccion WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct)).FirstOrDefault();

        if (prediccion == null) return new List<FactorRiesgo>();

        var filas = (await _accesoDatos.Leer<PrediccionFactorRiesgo>(
            "SELECT * FROM Prediccion_FactorRiesgo WHERE IdPrediccion = @idPrediccion",
            new { idPrediccion = prediccion.IdPrediccion }, ct: ct)).ToList();

        if (filas.Count == 0) return new List<FactorRiesgo>();

        var idsFactor = filas.Select(f => f.IdFactorRiesgo).Distinct().ToList();
        var (clausulaIn, parametrosIn) = _accesoDatos.ConstruirClausulaIn("idf", idsFactor);

        var catalogo = (await _accesoDatos.Leer<FactorRiesgo>(
                $"SELECT * FROM FactorRiesgo WHERE IdFactorRiesgo IN ({clausulaIn})",
                parametrosIn, ct: ct))
            .ToDictionary(f => f.IdFactorRiesgo);

        // Orden: los que más empujaron hacia el riesgo primero (ver MapearContribuciones); los
        // factores protectores (impacto negativo) quedan al final.
        return filas
            .OrderByDescending(f => f.Impacto ?? 0)
            .Select(f => new FactorRiesgo
            {
                IdFactorRiesgo = f.IdFactorRiesgo,
                Nombre = catalogo.TryGetValue(f.IdFactorRiesgo, out var factorCatalogo)
                    ? factorCatalogo.Nombre
                    : "Factor desconocido",
                Impacto = f.Impacto,
                Descripcion = f.Descripcion
            })
            .ToList();
    }
}
