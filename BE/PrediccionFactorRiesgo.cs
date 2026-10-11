using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

[Table("Prediccion_FactorRiesgo")]
public class PrediccionFactorRiesgo
{
    [Key]
    public int IdPrediccionFactorRiesgo { get; set; }

    [Required]
    public int IdPrediccion { get; set; }
    [ForeignKey(nameof(IdPrediccion))]
    public Prediccion? Prediccion { get; set; }

    [Required]
    public int IdFactorRiesgo { get; set; }
    [ForeignKey(nameof(IdFactorRiesgo))]
    public FactorRiesgo? FactorRiesgo { get; set; }
    public decimal? Impacto { get; set; }

    [MaxLength(255)]
    public string? Descripcion { get; set; }
}