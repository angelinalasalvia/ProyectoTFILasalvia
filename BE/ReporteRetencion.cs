namespace BE;

public class MetricasRetencion
{
    public int ClientesRecuperados { get; set; }
    public int ClientesRecuperadosAnterior { get; set; }
    public int RecuperadosHoy { get; set; }
    public decimal MrrSalvado { get; set; }
    public decimal MrrSalvadoAnterior { get; set; }
}

public class PuntoChurn
{
    public DateTime Desde { get; set; }
    public DateTime Hasta { get; set; }          // inclusive
    public int ActivosAlInicio { get; set; }
    public int Bajas { get; set; }
    public decimal Churn => ActivosAlInicio == 0 ? 0 : Math.Round(Bajas * 100m / ActivosAlInicio, 2);
}

public class TendenciaChurn
{
    public bool HistorialSuficiente { get; set; }
    public int ClientesEnSegmento { get; set; }

    public int ActivosAlInicio { get; set; }
    public int Bajas { get; set; }
    public int ActivosAlInicioAnterior { get; set; }
    public int BajasAnterior { get; set; }

    public decimal ChurnHistorico => ActivosAlInicio == 0 ? 0 : Math.Round(Bajas * 100m / ActivosAlInicio, 1);
    public decimal ChurnHistoricoAnterior => ActivosAlInicioAnterior == 0 ? 0 : Math.Round(BajasAnterior * 100m / ActivosAlInicioAnterior, 1);

    public List<PuntoChurn> Puntos { get; set; } = new();
    public List<PuntoChurn> PuntosAnterior { get; set; } = new();
}

public class CohorteRetencion
{
    public string Nombre { get; set; } = string.Empty;
    public int Tamano { get; set; }
    public List<int?> Valores { get; set; } = new();   // Mes 0..5; null = todavía no ocurrió
}

// Ciclo de vida de un cliente a partir de sus eventos. Misma convención que FeatureEngineering:
// el cliente "Inactivo" se dio de baja en su último evento registrado.
public class ClienteCicloVida
{
    public int IdCliente { get; set; }
    public string EstadoRegistro { get; set; } = string.Empty;
    public DateTime PrimerEvento { get; set; }
    public DateTime UltimoEvento { get; set; }

    public DateTime? FechaBaja => EstadoRegistro == "Inactivo" ? UltimoEvento : null;
    public bool ActivoEn(DateTime fecha) => PrimerEvento < fecha && (FechaBaja is null || FechaBaja.Value >= fecha);
}

// Valor del desplegable de segmento: "Todos", "plan:Plan Premium" o "sede:Central".
public static class SegmentoRetencion
{
    public const string Todos = "Todos";
    public const string PrefijoPlan = "plan:";
    public const string PrefijoSede = "sede:";

    // Condición SQL sobre el alias de Cliente + valor del parámetro @segmentoValor.
    public static (string Condicion, string? Valor) Resolver(string? segmento, string alias = "c")
    {
        if (string.IsNullOrWhiteSpace(segmento) || segmento == Todos) return ("1 = 1", null);
        if (segmento.StartsWith(PrefijoPlan)) return ($"{alias}.PlanSocio = @segmentoValor", segmento[PrefijoPlan.Length..]);
        if (segmento.StartsWith(PrefijoSede)) return ($"{alias}.Sede = @segmentoValor", segmento[PrefijoSede.Length..]);
        return ("1 = 1", null);
    }

    public static string Descripcion(string? segmento)
    {
        if (string.IsNullOrWhiteSpace(segmento) || segmento == Todos) return "Todos los segmentos";
        if (segmento.StartsWith(PrefijoPlan)) return segmento[PrefijoPlan.Length..];
        if (segmento.StartsWith(PrefijoSede)) return "Sede " + segmento[PrefijoSede.Length..];
        return "Todos los segmentos";
    }
}