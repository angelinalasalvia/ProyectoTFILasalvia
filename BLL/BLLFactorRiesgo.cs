using BE;
using DAL;

namespace BLL;

public class BLLFactorRiesgo
{
    private readonly IDALCliente _dalCliente;
    public BLLFactorRiesgo(IDALCliente dalCliente) => _dalCliente = dalCliente;

    public async Task<List<FactorRiesgo>> ObtenerFactorRiesgo(int idCliente, string periodo = "mes", CancellationToken ct = default)
    {
        var todos = await _dalCliente.ObtenerClientesConEventosAsync(ct);
        if (!todos.Any(c => c.IdCliente == idCliente)) return new List<FactorRiesgo>();

        int dias = BLLCliente.DiasSegunPeriodo(periodo);
        var fechaReferencia = DateTime.Now;

        var featuresPorCliente = todos.ToDictionary(
            c => c.IdCliente,
            c => FeatureEngineering.Construir(c, c.Eventos.ToList(), fechaReferencia, dias));

        var estadisticas = CalcularEstadisticas(featuresPorCliente.Values);
        var featuresCliente = featuresPorCliente[idCliente];

        var resultado = new List<FactorRiesgo>();
        foreach (var (nombre, selector, altoEsRiesgo) in FeatureEngineering.IndicadoresDeRiesgo)
        {
            var (media, desvio) = estadisticas[nombre];
            double valor = selector(featuresCliente);
            double zScore = desvio < 0.0001 ? 0 : (valor - media) / desvio;
            if (!altoEsRiesgo) zScore *= -1;

            double impacto = Math.Clamp(zScore * 15, -50, 50);

            resultado.Add(new FactorRiesgo
            {
                Nombre = nombre,
                Impacto = (decimal)Math.Round(impacto, 1),
                Descripcion = GenerarDescripcion(nombre, valor, media)
            });
        }

        return resultado.OrderByDescending(f => Math.Abs(f.Impacto ?? 0)).ToList();
    }

    private static Dictionary<string, (double Media, double Desvio)> CalcularEstadisticas(IEnumerable<ChurnInputData> datos)
    {
        var lista = datos.ToList();
        var resultado = new Dictionary<string, (double, double)>();
        foreach (var (nombre, selector, _) in FeatureEngineering.IndicadoresDeRiesgo)
        {
            var valores = lista.Select(d => (double)selector(d)).ToList();
            double media = valores.Average();
            double desvio = Math.Sqrt(valores.Sum(v => Math.Pow(v - media, 2)) / Math.Max(1, valores.Count - 1));
            resultado[nombre] = (media, desvio);
        }
        return resultado;
    }

    private static string GenerarDescripcion(string nombreFactor, double valorCliente, double media)
    {
        string comparacion = valorCliente < media ? "por debajo de" : "por encima de";
        return $"{Math.Round(valorCliente, 1)} vs. una media de {Math.Round(media, 1)} en el segmento ({comparacion} lo esperado).";
    }
}