using System.Text.Json;

namespace BE;

public record CampoPlataforma(string Nombre, string Tipo, bool Obligatorio);

// Qué se puede mapear: los datos del cliente y los eventos que necesita el modelo de predicción.
public static class DefinicionMapeo
{
    public const string EntidadCliente = "Cliente";
    public const string EntidadEvento = "Evento";

    public const string ClaveEmail = "Email";
    public const string ClaveIdExterno = "IdExterno";

    // Tipos de dato: "texto", "fecha", "numero". Un campo de texto de la plataforma acepta cualquier tipo de la fuente.
    public static readonly IReadOnlyList<CampoPlataforma> CamposCliente = new List<CampoPlataforma>
    {
        new("Nombre", "texto", true),
        new("Apellido", "texto", true),
        new("Email", "texto", true),
        new("IdExterno", "texto", false),   // identificador del cliente en la fuente (obligatorio si es la clave)
        new("Telefono", "texto", false),
        new("Estado", "texto", false),      // se traduce a Socio Activo / Inactivo
        new("Plan", "texto", false),
        new("Sexo", "texto", false),
        new("Sede", "texto", false)
    };

    public static readonly IReadOnlyList<CampoPlataforma> CamposEvento = new List<CampoPlataforma>
    {
        new("Cliente", "texto", true),      // valor que identifica al cliente (debe coincidir con la clave elegida)
        new("Evento", "texto", true),       // el tipo de evento; sus valores se traducen a los tipos del modelo
        new("Fecha", "fecha", true)
    };

    // Los únicos tipos de evento que entiende el modelo (BE.TipoEvento).
    public static readonly IReadOnlyList<string> TiposEvento = new List<string>
    {
        TipoEvento.VisitaGimnasio,
        TipoEvento.UsoApp,
        TipoEvento.PagoRegistrado,
        TipoEvento.PagoVencido,
        TipoEvento.ReservaClase,
        TipoEvento.CancelacionReserva,
        TipoEvento.ConsultaSoporte
    };

    public static readonly IReadOnlyList<string> EstadosCliente = new List<string> { "Socio Activo", "Inactivo" };
}

// Mapeo completo de una integración. Se guarda como JSON en Integracion.Mapeo, y es el "string mapeo"
// que viaja en RegistrarNuevaIntegracion / ActualizarConfiguracion.
//
// Los campos de la fuente se nombran "Origen.Campo": el origen es la tabla (BD), el objeto (CRM)
// o "Clientes"/"Eventos" (API, un endpoint por cada uno).
public class MapeoIntegracion
{
    public string OrigenClientes { get; set; } = string.Empty;
    public string OrigenEventos { get; set; } = string.Empty;

    // Cómo se reconoce que un evento es de un cliente: por Email o por el ID que tiene en la fuente.
    public string ClaveCliente { get; set; } = DefinicionMapeo.ClaveEmail;

    public Dictionary<string, string> Clientes { get; set; } = new();       // campo de la plataforma -> campo de la fuente
    public Dictionary<string, string> Eventos { get; set; } = new();        // campo de la plataforma -> campo de la fuente
    public Dictionary<string, string> ValoresEvento { get; set; } = new();  // valor en la fuente -> tipo de evento
    public Dictionary<string, string> ValoresEstado { get; set; } = new();  // valor en la fuente -> estado del cliente

    public string Serializar() => JsonSerializer.Serialize(this);

    // Si el texto está vacío o tiene un formato anterior, se devuelve un mapeo vacío (hay que volver a mapear).
    public static MapeoIntegracion Deserializar(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MapeoIntegracion();
        try
        {
            return JsonSerializer.Deserialize<MapeoIntegracion>(json) ?? new MapeoIntegracion();
        }
        catch (JsonException)
        {
            return new MapeoIntegracion();
        }
    }

    // "Tabla.Columna" -> "Tabla" / "Columna". Se corta en el primer punto.
    public static string Origen(string nombreCalificado)
    {
        var i = nombreCalificado.IndexOf('.');
        return i < 0 ? nombreCalificado : nombreCalificado[..i];
    }

    public static string Campo(string nombreCalificado)
    {
        var i = nombreCalificado.IndexOf('.');
        return i < 0 ? nombreCalificado : nombreCalificado[(i + 1)..];
    }
}