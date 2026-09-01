using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

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
