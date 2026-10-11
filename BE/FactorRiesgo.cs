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
    public const string InactividadReciente = "Días desde la última actividad";
    public const string BajaFrecuenciaAsistencia = "Frecuencia de asistencia";
    public const string BajaInteraccionApp = "Interacción con la app";
    public const string HistorialPagosVencidos = "Pagos vencidos";
    public const string CancelacionesFrecuentes = "Cancelaciones de reservas";
    public const string AltaConsultaSoporte = "Consultas a soporte";
    public const string AltaProporcionPagosVencidos = "Proporción de pagos vencidos";
    public const string TendenciaNegativaAsistencia = "Tendencia de asistencia";
    public const string OtrosFactores = "Otros factores";
}
