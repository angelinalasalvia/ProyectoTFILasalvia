namespace BE;
public class Regla
{
    public int IdRegla { get; set; }
    public string Estado { get; set; } = string.Empty; // Ver BE.EstadosRegla
    public DateTime FechaCreacion { get; set; }
    public int IdCampania { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Canal { get; set; } = string.Empty; // Nombre del canal de la campaña asociada (Email / WhatsApp)
    public string NombreCampania { get; set; } = string.Empty;
    public int? IdSede { get; set; }
    public string? NombreSede { get; set; }
    public int? IdPlan { get; set; }
    public string? NombrePlan { get; set; }
    public string CondicionResumen { get; set; } = string.Empty;
    public int VecesEjecutada { get; set; }
    public List<Condicion> Condiciones { get; set; } = new();
}

public static class EstadosRegla
{
    public const string Activa = "Activa";
    public const string Pausada = "Pausada";
}