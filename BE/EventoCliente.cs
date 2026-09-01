using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

[Table("EventosCliente")]
public class EventoCliente
{
    [Key]
    public int IdEventosCliente { get; set; }

    [Required, MaxLength(255)]
    public string Evento { get; set; } = string.Empty;

    [Required]
    public DateTime Fecha { get; set; }

    [Required]
    public int IdCliente { get; set; }

    [ForeignKey(nameof(IdCliente))]
    public Cliente? Cliente { get; set; }
}

public static class TipoEvento
{
    public const string VisitaGimnasio = "Visita al gimnasio";
    public const string UsoApp = "Uso de app";
    public const string PagoRegistrado = "Pago registrado";
    public const string PagoVencido = "Pago vencido";
    public const string ReservaClase = "Reserva de clase";
    public const string CancelacionReserva = "Cancelación de reserva";
    public const string ConsultaSoporte = "Consulta a soporte";
}
