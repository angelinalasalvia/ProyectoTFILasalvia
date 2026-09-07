namespace BE;

public class Campana
{
    public int IdCampania { get; set; }
    public string Nombre { get; set; } = string.Empty;   
    public string Canal { get; set; } = string.Empty; 
    public int IdCanal { get; set; }                    
    public string Objetivo { get; set; } = string.Empty; 
    public decimal? TasaExito { get; set; }
    public int? ClientesAlcanzados { get; set; }
    public int? Meta { get; set; }
    public string? AsuntoEmail { get; set; }            
    public string Mensaje { get; set; } = string.Empty;
    public int? IdIncentivo { get; set; }
}