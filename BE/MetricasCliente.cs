namespace BE;

public class MetricasCliente
{
    public const int DiasVentana = 30;

    public int TotalEventos { get; set; }

    public int DiasActivos { get; set; }            // últimos 30 días con visita o uso de app
    public int DiasActivosAnterior { get; set; }    // los 30 días previos
    public int InteraccionesApp { get; set; }
    public int InteraccionesAppAnterior { get; set; }
    public int Visitas { get; set; }
    public int VisitasAnterior { get; set; }

    public decimal TasaAdopcion => Math.Min(100m, Math.Round(DiasActivos * 100m / DiasVentana, 1));
    public decimal TasaAdopcionAnterior => Math.Min(100m, Math.Round(DiasActivosAnterior * 100m / DiasVentana, 1));
    public decimal SesionesPromedioDia => Math.Round(Visitas / (decimal)DiasVentana, 2);
    public decimal SesionesPromedioDiaAnterior => Math.Round(VisitasAnterior / (decimal)DiasVentana, 2);
}
