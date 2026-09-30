namespace BE;

public class MetricasCampana
{
    public decimal ExitoGlobal { get; set; }
    public decimal PorcentajeCampanasConKpiAlcanzado { get; set; }
    public string CanalMasEfectivo { get; set; } = "Sin datos";
    public decimal TasaCanalMasEfectivo { get; set; }

    // Pendiente: requiere una tabla de snapshots históricos de Prediccion para poder
    // comparar la probabilidad de abandono antes/después de una campaña. Hoy Prediccion
    // se sobrescribe en cada corrida del modelo (ver BLLModelo.ReemplazarPrediccionesAsync),
    // así que no hay historial contra el cual medir este impacto todavía.
    public decimal ImpactoReduccionChurn { get; set; }
}