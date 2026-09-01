using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BE;

[Table("Cliente")]
public class Cliente
{
    [Key]
    public int IdCliente { get; set; }

    [Required, MaxLength(100)]
    public string Nombre { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Apellido { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string EstadoRegistro { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string PlanSocio { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Sexo { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Sede { get; set; } = string.Empty;
    public ICollection<EventoCliente> Eventos { get; set; } = new List<EventoCliente>();
}

