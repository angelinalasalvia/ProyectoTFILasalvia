using BE;
using DAL;

namespace BLL;

public class BLLCliente
{
    private readonly IDALCliente _dalCliente;
    public BLLCliente(IDALCliente dalCliente) => _dalCliente = dalCliente;

    public async Task<Cliente?> ObtenerCliente(int idCliente, CancellationToken ct = default)
        => await _dalCliente.ObtenerClientePorIdAsync(idCliente, ct);

    public async Task<(double UsoInstalaciones, double InteraccionesApp)> ObtenerMetricasRelativasMedia(int idCliente, string periodo = "mes", CancellationToken ct = default)
    {
        var todos = await _dalCliente.ObtenerClientesConEventosAsync(ct);
        var cliente = todos.FirstOrDefault(c => c.IdCliente == idCliente);
        if (cliente == null) return (0, 0);

        var desde = DateTime.Now.AddDays(-DiasSegunPeriodo(periodo));
        var segmento = todos.Where(c => c.PlanSocio == cliente.PlanSocio).ToList();

        double usoCliente = cliente.Eventos.Count(e => e.Evento == TipoEvento.VisitaGimnasio && e.Fecha >= desde);
        double usoMedia = segmento.Average(c => c.Eventos.Count(e => e.Evento == TipoEvento.VisitaGimnasio && e.Fecha >= desde));

        double appCliente = cliente.Eventos.Count(e => e.Evento == TipoEvento.UsoApp && e.Fecha >= desde);
        double appMedia = segmento.Average(c => c.Eventos.Count(e => e.Evento == TipoEvento.UsoApp && e.Fecha >= desde));

        return (
            CalcularPorcentajeRelativo(usoCliente, usoMedia),
            CalcularPorcentajeRelativo(appCliente, appMedia)
        );
    }

    private static double CalcularPorcentajeRelativo(double valorCliente, double media)
    {
        if (media == 0) return valorCliente == 0 ? 0 : 100;
        return Math.Round((valorCliente - media) / media * 100, 1);
    }

    public static int DiasSegunPeriodo(string periodo) => periodo switch
    {
        "semana" => 7,
        "trimestre" => 90,
        "año" => 365,
        _ => 30 
    };
}
