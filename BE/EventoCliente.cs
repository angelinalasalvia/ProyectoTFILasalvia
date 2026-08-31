using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

// Mapea 1 a 1 con tu tabla [dbo].[EventosCliente]
// Esta es la tabla más importante para el modelo: acá está el comportamiento
// real de cada cliente (visitas, pagos, uso de app, etc.) del cual vamos a
// derivar las "features" (variables de entrada) del Random Forest.
[Table("EventosCliente")]
public class EventoCliente
{
    [Key]
    public int IdEventosCliente { get; set; }

    // Valores observados en tu BD: "Visita al gimnasio", "Uso de app",
    // "Pago registrado", "Pago vencido", "Reserva de clase",
    // "Cancelación de reserva", "Consulta a soporte"
    [Required, MaxLength(255)]
    public string Evento { get; set; } = string.Empty;

    [Required]
    public DateTime Fecha { get; set; }

    [Required]
    public int IdCliente { get; set; }

    [ForeignKey(nameof(IdCliente))]
    public Cliente? Cliente { get; set; }
}

// Constantes con los nombres exactos de evento tal como están en la BD,
// para no tipearlos "a mano" (y con riesgo de errores de tipeo) en el resto del código.
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
