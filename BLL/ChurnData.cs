namespace BLL;

// Esta clase es la "fila de entrada" que le damos al modelo: un conjunto de
// números (y algunas categorías) que resumen el comportamiento reciente de
// UN cliente. A esto se le llama "features" (variables/atributos).
//
// Importante: NO incluye IdCliente a propósito. ML.NET no debe "ver" el ID
// del cliente como si fuera información útil para predecir (no lo es, y si
// se lo diéramos, el modelo podría "memorizar" en vez de aprender patrones
// reales). El ID lo llevamos aparte, correlacionado por posición en una lista.
public class ChurnInputData
{
    // --- Actividad reciente (últimos 30 días) ---
    public float VisitasUltimos30Dias { get; set; }
    public float UsoAppUltimos30Dias { get; set; }
    public float ReservasUltimos30Dias { get; set; }
    public float CancelacionesUltimos30Dias { get; set; }

    // --- Señales de alarma ---
    public float DiasDesdeUltimaActividad { get; set; }       // cualquier evento
    public float PagosVencidosUltimos60Dias { get; set; }
    public float ConsultasSoporteUltimos90Dias { get; set; }

    // --- Contexto del cliente ---
    public float AntiguedadDias { get; set; }                  // desde su primer evento registrado
    public string PlanSocio { get; set; } = string.Empty;
    public string Sede { get; set; } = string.Empty;
    public string Sexo { get; set; } = string.Empty;

    // Label (solo se usa durante el ENTRENAMIENTO; en predicción se ignora).
    // true = el cliente abandonó (EstadoRegistro = "Inactivo")
    public bool Abandono { get; set; }
}

// Lo que ML.NET nos devuelve al pedirle una predicción para un cliente.
public class ChurnPredictionResult
{
    // Clasificación binaria: ¿el modelo predice que va a abandonar?
    public bool PredictedLabel { get; set; }

    // Esto es lo que realmente nos importa: la PROBABILIDAD de abandono,
    // como número entre 0 y 1. FastForest (Random Forest) la calcula
    // calibrada automáticamente = literalmente "de los N árboles, cuántos
    // votaron abandono".
    public float Probability { get; set; }

    public float Score { get; set; }
}

// Wrapper para no perder la referencia al cliente mientras armamos los datos.
public class ClienteConFeatures
{
    public int IdCliente { get; set; }
    public ChurnInputData Features { get; set; } = new();
}

