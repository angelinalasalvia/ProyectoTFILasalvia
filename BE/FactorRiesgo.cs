using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

[Table("FactorRiesgo")]
public class FactorRiesgo
{
    [Key]
    public int IdFactorRiesgo { get; set; }

    [Required, MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Descripcion { get; set; }

    public decimal? Impacto { get; set; }
}

public static class NombresFactorRiesgo
{
    public const string InactividadReciente = "Inactividad reciente";
    public const string BajaFrecuenciaAsistencia = "Baja frecuencia de asistencia";
    public const string BajaInteraccionApp = "Baja interacción con la app";
    public const string HistorialPagosVencidos = "Historial de pagos vencidos";
    public const string CancelacionesFrecuentes = "Cancelaciones frecuentes de reservas";
    public const string AltaConsultaSoporte = "Alta cantidad de consultas a soporte";
    public const string AltaProporcionPagosVencidos = "Alta proporción de pagos vencidos";
    public const string TendenciaNegativaAsistencia = "Tendencia negativa de asistencia";
}
