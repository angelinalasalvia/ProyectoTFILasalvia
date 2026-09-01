namespace BLL;

public class ChurnInputData
{
    public float VisitasUltimos30Dias { get; set; }
    public float UsoAppUltimos30Dias { get; set; }
    public float ReservasUltimos30Dias { get; set; }
    public float CancelacionesUltimos30Dias { get; set; }

    public float DiasDesdeUltimaActividad { get; set; }      
    public float PagosVencidosUltimos60Dias { get; set; }
    public float ConsultasSoporteUltimos90Dias { get; set; }

    public float AntiguedadDias { get; set; }                 
    public string PlanSocio { get; set; } = string.Empty;
    public string Sede { get; set; } = string.Empty;
    public string Sexo { get; set; } = string.Empty;

   
    public bool Abandono { get; set; }
}

public class ChurnPredictionResult
{
    public bool PredictedLabel { get; set; }

    public float Probability { get; set; }

    public float Score { get; set; }
}

public class ClienteConFeatures
{
    public int IdCliente { get; set; }
    public ChurnInputData Features { get; set; } = new();
}

