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

    public float PagosRegistradosUltimos60Dias { get; set; }

    public float ProporcionPagosVencidos { get; set; }

    public float TendenciaVisitas { get; set; }

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

// Igual que ChurnPredictionResult, pero además trae FeatureContributions: un valor por cada
// posición del vector "Features" con cuánto empujó esa variable la predicción de este cliente
// puntual (CalculateFeatureContribution de ML.NET). El orden coincide con
// FeatureEngineering.ColumnasNumericas + el one-hot de plan/sede/sexo al final.
public class ChurnPredictionConContribuciones
{
    public bool PredictedLabel { get; set; }

    public float Probability { get; set; }

    public float Score { get; set; }

    public float[] FeatureContributions { get; set; } = Array.Empty<float>();
}

public class ClienteConFeatures
{
    public int IdCliente { get; set; }
    public ChurnInputData Features { get; set; } = new();
}

