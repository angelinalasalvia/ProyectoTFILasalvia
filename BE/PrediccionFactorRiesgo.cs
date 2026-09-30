using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

// Una fila por cada factor que influyó en una Prediccion puntual (hoy, 8 por predicción).
// Se llena una sola vez, en EntrenarYPredecirAsync, con la contribución real que el modelo
// le asignó a cada variable (CalculateFeatureContribution de ML.NET). BLLFactorRiesgo.ObtenerFactorRiesgo
// ya no recalcula nada: solo lee estas filas.
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

    // Puede ser negativo: es la contribución real del modelo, normalizada a una escala de
    // -100 a 100 (no el z-score truncado 0-100 de antes). Positivo = empujó hacia el riesgo,
    // negativo = jugó a favor del cliente (factor protector).
    public decimal? Impacto { get; set; }

    [MaxLength(255)]
    public string? Descripcion { get; set; }
}