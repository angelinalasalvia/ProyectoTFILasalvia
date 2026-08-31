using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

// Mapea 1 a 1 con tu tabla [dbo].[Prediccion]
// Esta es la salida final que va a alimentar CU01 (Ver Predicciones) y
// CU02 (Ver detalle de predicción).
[Table("Prediccion")]
public class Prediccion
{
    [Key]
    public int IdPrediccion { get; set; }

    [Required]
    public int IdCliente { get; set; }
    [ForeignKey(nameof(IdCliente))]
    public Cliente? Cliente { get; set; }

    [Required]
    public int IdFactorRiesgo { get; set; }
    [ForeignKey(nameof(IdFactorRiesgo))]
    public FactorRiesgo? FactorRiesgo { get; set; }

    [Required]
    public int IdModelo { get; set; }
    [ForeignKey(nameof(IdModelo))]
    public Modelo? Modelo { get; set; }

    // "Alto" / "Medio" / "Bajo" (según tu CU01: >75% alto, 40-75% medio, <40% bajo)
    [Required, MaxLength(50)]
    public string NivelRiesgo { get; set; } = string.Empty;

    // 0.00 a 100.00 (decimal(18,2) en la BD)
    [Required]
    public decimal ProbabilidadAbandono { get; set; }
}

public static class NivelesRiesgo
{
    public const string Alto = "Alto";
    public const string Medio = "Medio";
    public const string Bajo = "Bajo";

    public static string Clasificar(decimal probabilidadAbandono)
    {
        if (probabilidadAbandono > 75m) return Alto;
        if (probabilidadAbandono >= 40m) return Medio;
        return Bajo;
    }
}

