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

        var cliente = todos.FirstOrDefault(c => c.IdCliente == idCliente);
        if (cliente == null) return new List<FactorRiesgo>();

        var fechaReferencia = DateTime.Now;

        var segmento = todos
            .Where(c => c.PlanSocio == cliente.PlanSocio)
            .ToList();

        int dias = BLLCliente.DiasSegunPeriodo(periodo);

        var featuresPorCliente = segmento.ToDictionary(
            c => c.IdCliente,
            c => FeatureEngineering.ConstruirParaPeriodo(
                c,
                c.Eventos.ToList(),
                fechaReferencia,
                dias));

        var estadisticas = CalcularEstadisticas(featuresPorCliente.Values);
        var featuresCliente = featuresPorCliente[idCliente];

        //temporal
        Console.WriteLine($"[DEBUG] Visitas: {featuresCliente.VisitasUltimos30Dias}");
        Console.WriteLine($"[DEBUG] Tendencia: {featuresCliente.TendenciaVisitas}");

        var resultado = new List<FactorRiesgo>();
        foreach (var (nombre, selector, altoEsRiesgo) in FeatureEngineering.IndicadoresDeRiesgo)
        {
            var (media, desvio) = estadisticas[nombre];
            double valor = selector(featuresCliente);
            double zScore = desvio < 0.0001 ? 0 : (valor - media) / desvio;
            if (!altoEsRiesgo) zScore *= -1;

            double impacto = Math.Clamp(50 + zScore * 15, 0, 100);

            //temporal
            if (nombre == NombresFactorRiesgo.TendenciaNegativaAsistencia)
            {
                Console.WriteLine(
                    $"[DEBUG TENDENCIA] Cliente: {valor}, Media: {media}, " +
                    $"Desvío: {desvio}, ZScore: {zScore}, Impacto: {impacto}");
            }

            string nombreMostrar = nombre;

            if (nombre == NombresFactorRiesgo.TendenciaNegativaAsistencia)
            {
                if (valor < 0)
                {
                    nombreMostrar = "Tendencia negativa de asistencia";
                }
                else if (valor > 0 && valor < media)
                {
                    nombreMostrar = "Tendencia de asistencia inferior al segmento";
                }
                else if (valor > 0 && valor > media)
                {
                    nombreMostrar = "Tendencia positiva de asistencia";
                }
                else
                {
                    nombreMostrar = "Tendencia estable de asistencia";
                }
            }

            resultado.Add(new FactorRiesgo
            {
                Nombre = nombreMostrar,
                Impacto = (decimal)Math.Round(impacto, 1),
                Descripcion = GenerarDescripcion(nombre, valor, media)
            });
        }

        return resultado.OrderByDescending(f => f.Impacto ?? 0).ToList();
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

    private static string GenerarDescripcion(
    string nombreFactor,
    double valorCliente,
    double media)
    {
        if (nombreFactor == NombresFactorRiesgo.InactividadReciente)
        {
            return $"{Math.Round(valorCliente, 1)} días desde la última actividad vs. " +
                   $"{Math.Round(media, 1)} días en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.BajaFrecuenciaAsistencia)
        {
            return $"{Math.Round(valorCliente, 1)} visitas en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.BajaInteraccionApp)
        {
            return $"{Math.Round(valorCliente, 1)} interacciones con la App en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.HistorialPagosVencidos)
        {
            string pagosCliente = Math.Round(valorCliente, 1) == 1 ? "pago vencido" : "pagos vencidos";
            string pagosMedia = Math.Round(media, 1) == 1 ? "pago vencido" : "pagos vencidos";

            return $"{Math.Round(valorCliente, 1)} {pagosCliente} en el período vs. " +
                   $"{Math.Round(media, 1)} {pagosMedia} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.CancelacionesFrecuentes)
        {
            return $"{Math.Round(valorCliente, 1)} cancelaciones en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.AltaConsultaSoporte)
        {
            return $"{Math.Round(valorCliente, 1)} consultas a soporte en el período vs. " +
                   $"{Math.Round(media, 1)} en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.AltaProporcionPagosVencidos)
        {
            return $"{Math.Round(valorCliente * 100, 1)}% de los pagos vencieron vs. " +
                   $"{Math.Round(media * 100, 1)}% en el segmento.";
        }

        if (nombreFactor == NombresFactorRiesgo.TendenciaNegativaAsistencia)
        {
            string tendenciaCliente = valorCliente > 0
                ? "aumento"
                : valorCliente < 0
                    ? "disminución"
                    : "sin cambios";

            string tendenciaMedia = media > 0
                ? "aumento"
                : media < 0
                    ? "disminución"
                    : "sin cambios";

            return $"{tendenciaCliente} de {Math.Abs(Math.Round(valorCliente, 1))} visitas " +
                   $"respecto al período anterior vs. {tendenciaMedia} de " +
                   $"{Math.Abs(Math.Round(media, 1))} en el segmento.";
        }

        return $"{Math.Round(valorCliente, 1)} vs. una media de " +
               $"{Math.Round(media, 1)} en el segmento.";
    }
}