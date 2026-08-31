using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

// Mapea 1 a 1 con tu tabla [dbo].[FactorRiesgo]
// Es un catálogo: cada fila describe UN posible motivo de abandono
// (ej: "Baja frecuencia de asistencia"). El modelo va a elegir, para cada
// cliente, cuál de estos factores es el que más está empujando su riesgo.
[Table("FactorRiesgo")]
public class FactorRiesgo
{
    [Key]
    public int IdFactorRiesgo { get; set; }

    [Required, MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Descripcion { get; set; }

    // Impacto promedio/último calculado (0-100), decimal(18,2) en la BD
    public decimal? Impacto { get; set; }
}

// Nombres canónicos que el sistema va a usar/crear automáticamente
// en la tabla FactorRiesgo (si no existen) para poder asociarlos a las predicciones.
public static class NombresFactorRiesgo
{
    public const string InactividadReciente = "Inactividad reciente";
    public const string BajaFrecuenciaAsistencia = "Baja frecuencia de asistencia";
    public const string BajaInteraccionApp = "Baja interacción con la app";
    public const string HistorialPagosVencidos = "Historial de pagos vencidos";
    public const string CancelacionesFrecuentes = "Cancelaciones frecuentes de reservas";
    public const string AltaConsultaSoporte = "Alta cantidad de consultas a soporte";
}
