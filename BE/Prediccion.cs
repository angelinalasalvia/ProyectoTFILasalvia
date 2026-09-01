using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

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

    [Required, MaxLength(50)]
    public string NivelRiesgo { get; set; } = string.Empty;

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

