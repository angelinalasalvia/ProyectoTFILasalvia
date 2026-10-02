using System.Text.Json;

namespace BE;

// Mapea la tabla Integracion. Las credenciales de cada tipo viven en tablas separadas
// (IntegracionBD, IntegracionAPI, IntegracionCRM) y se completan solo en las consultas que
// hacen LEFT JOIN (BLLIntegracion.ObtenerIntegracion). El listado (ObtenerIntegraciones) no las trae.
public class Integracion
{
    public int IdIntegracion { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public DateTime FechaCreacion { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;
    public int IdUsuario { get; set; }                 // usuario que configuró la integración (DER: Usuario 1 - * Integración)
    public DateTime? UltimaSincronizacion { get; set; }
    public int? CantidadRegistros { get; set; }
    public string? Mapeo { get; set; }                 // "externo=plataforma;externo=plataforma"
    public string? MensajeError { get; set; }

    // Solo se completan al leer una integración puntual. 'Secreto' sale siempre enmascarado.
    public string? Url { get; set; }
    public string? Usuario { get; set; }
    public string? Secreto { get; set; }
    public string? BaseDatos { get; set; }

    // Valores que se guardan en la columna Tipo (coinciden con los de la pantalla de alta).
    public const string TipoCRM = "CRM";
    public const string TipoBD = "Base de Datos";
    public const string TipoAPI = "API Personalizada";

    // Valores de la columna Estado (CU14: "Conexión Activa / Error de Autenticación").
    public const string EstadoActiva = "Conexión Activa";
    public const string EstadoError = "Error de Autenticación";
}

// Es lo que viaja como "string credenciales" en RegistrarNuevaIntegracion / ActualizarConfiguracion
// (diagramas de secuencia CU15 y CU16): se serializa a JSON y la BLL lo reparte en la tabla del tipo.
public class CredencialesIntegracion
{
    public string Nombre { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;        // BD: servidor ("host" o "host,puerto") | API: endpoint | CRM: URL de login/token
    public string Usuario { get; set; } = string.Empty;    // BD: usuario | CRM: Client ID | API: no aplica
    public string Secreto { get; set; } = string.Empty;    // BD: contraseña | API: API Key | CRM: Client Secret
    public string? BaseDatos { get; set; }                 // solo BD

    public string Serializar() => JsonSerializer.Serialize(this);

    public static CredencialesIntegracion Deserializar(string json) =>
        JsonSerializer.Deserialize<CredencialesIntegracion>(json)
        ?? throw new ArgumentException("Las credenciales de la integración no son válidas.");
}
