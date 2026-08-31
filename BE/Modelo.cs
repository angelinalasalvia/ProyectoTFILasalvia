using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

// Mapea 1 a 1 con tu tabla [dbo].[Modelo]
// Representa "una versión/instancia del modelo predictivo" (por eso tu CU01
// pide mostrar "fecha y hora de la última ejecución del modelo").
[Table("Modelo")]
public class Modelo
{
    [Key]
    public int IdModelo { get; set; }

    [Required, MaxLength(150)]
    public string Nombre { get; set; } = string.Empty;

    public DateTime? UltimaEjecucion { get; set; }
}

public static class NombresModelo
{
    public const string RandomForestChurn = "Random Forest - Predicción de Churn";
}
