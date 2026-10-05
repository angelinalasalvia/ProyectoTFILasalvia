namespace BE;

public class MetricasCampana
{
    public bool HayCampanasEnPeriodo { get; set; }
    public decimal ExitoGlobal { get; set; }
    public decimal PorcentajeCampanasConKpiAlcanzado { get; set; }
    public string CanalMasEfectivo { get; set; } = "Sin datos";
    public decimal TasaCanalMasEfectivo { get; set; }
}