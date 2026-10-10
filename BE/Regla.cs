namespace BE;
public class Regla
{
    public int IdRegla { get; set; }
    public string Estado { get; set; } = string.Empty; 
    public DateTime FechaCreacion { get; set; }
    public int IdCampania { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Prioridad { get; set; } = PrioridadPorDefecto; 
    public string Canal { get; set; } = string.Empty; 
    public string NombreCampania { get; set; } = string.Empty;
    public int? IdSede { get; set; }
    public string? NombreSede { get; set; }
    public int? IdPlan { get; set; }
    public string? NombrePlan { get; set; }
    public string CondicionResumen { get; set; } = string.Empty;
    public int VecesEjecutada { get; set; }
    public List<Condicion> Condiciones { get; set; } = new();

    public const int PrioridadMinima = 1;
    public const int PrioridadMaxima = 999;
    public const int PrioridadPorDefecto = 100;
    public const int LargoMaximoNombre = 50; 
}

public static class EstadosRegla
{
    public const string Activa = "Activa";
    public const string Pausada = "Pausada";
}