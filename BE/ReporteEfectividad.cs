namespace BE;

public class MetricasEfectividad
{
    public int TotalAcciones { get; set; }
    public int TotalAccionesAnterior { get; set; }
    public int Entregadas { get; set; }
    public int EntregadasAnterior { get; set; }
    public int Rescatados { get; set; }
    public int RescatadosAnterior { get; set; }
    public List<MetricasCanal> Canales { get; set; } = new();

    public decimal TasaRecuperacion => Entregadas == 0 ? 0 : Math.Round(Rescatados * 100m / Entregadas, 1);
    public decimal TasaRecuperacionAnterior => EntregadasAnterior == 0 ? 0 : Math.Round(RescatadosAnterior * 100m / EntregadasAnterior, 1);
}

public class MetricasCanal
{
    public string Canal { get; set; } = string.Empty;
    public int Entregadas { get; set; }
    public int Leidas { get; set; }
    public int Clics { get; set; }
    public int Rescatados { get; set; }

    public decimal TasaApertura => Entregadas == 0 ? 0 : Math.Round(Leidas * 100m / Entregadas, 1);
    public decimal TasaClics => Entregadas == 0 ? 0 : Math.Round(Clics * 100m / Entregadas, 1);
    public decimal TasaRecuperacion => Entregadas == 0 ? 0 : Math.Round(Rescatados * 100m / Entregadas, 1);
}

public class DatosFinancieros
{
    public decimal MrrRecuperado { get; set; }
    public decimal MrrRecuperadoAnterior { get; set; }
    public decimal LtvRecuperado { get; set; }
    public decimal LtvRecuperadoAnterior { get; set; }
}

public class DatosIncentivos
{
    public List<EfectividadIncentivo> Incentivos { get; set; } = new();
    public decimal CostoTotal { get; set; }
    public decimal CostoTotalAnterior { get; set; }
}

public class EfectividadIncentivo
{
    public string Nombre { get; set; } = string.Empty;
    public int Enviados { get; set; }
    public int Rescatados { get; set; }
}