using BE;
using DAL;

namespace BLL;

public class BLLCliente
{
    private readonly IAccesoDatos _accesoDatos;
    public BLLCliente(IAccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public async Task<Cliente?> ObtenerCliente(int idCliente, CancellationToken ct = default)
    {
        var resultado = await _accesoDatos.Leer<Cliente>(
            "SELECT * FROM Cliente WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct);

        var cliente = resultado.FirstOrDefault();
        if (cliente == null) return null;

        // Si en algún punto necesitás los eventos de ESTE cliente puntual (no de todos),
        // conviene un método aparte más liviano en vez de reusar ConsultasComunes:
        var eventos = await _accesoDatos.Leer<EventoCliente>(
            "SELECT * FROM EventosCliente WHERE IdCliente = @idCliente",
            new { idCliente }, ct: ct);
        cliente.Eventos = eventos.ToList();

        return cliente;
    }

    public async Task<(double UsoInstalaciones, double InteraccionesApp)> ObtenerMetricasRelativasMedia(int idCliente, string periodo = "mes", CancellationToken ct = default)
    {
        var todos = await ConsultasComunes.ObtenerClientesConEventosAsync(_accesoDatos, ct);
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
