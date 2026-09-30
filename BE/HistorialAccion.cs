namespace BE;

// Mapea la tabla HistorialAcciones. Las últimas 3 propiedades solo se
// completan cuando la consulta hace JOIN con Cliente (igual que
// Campana.Canal se completa vía JOIN con Canal en BLLCampana).
public class HistorialAccion
{
    public int IdHistorial { get; set; }
    public string EstadoEnvio { get; set; } = string.Empty;
    public DateTime FechaEnvio { get; set; }
    public DateTime? FechaLectura { get; set; }
    public DateTime? FechaConversion { get; set; }
    public string TipoAccion { get; set; } = string.Empty;
    public int IdCampania { get; set; }
    public int IdCliente { get; set; }
    public int IdUsuario { get; set; }
    public int? IdRegla { get; set; } // regla que originó la acción (NULL en acciones manuales o anteriores al motor)
    public string Resultado { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;

    // "Fecha y hora de la acción" del paso 6: la más específica disponible
    // (conversión > lectura > envío). Viene de un COALESCE en el SELECT.
    public DateTime FechaAccion { get; set; }

    public string NombreCliente { get; set; } = string.Empty;
    public string ApellidoCliente { get; set; } = string.Empty;
    public string PlanCliente { get; set; } = string.Empty;



    // Valores que usa el motor de reglas (constantes de la clase; no son columnas).
    public const string TipoEnvioAutomatico = "Envío automático";
    public const string EstadoActivo = "Activo";          // envío en observación (sin resultado todavía)
    public const string EstadoFinalizada = "Finalizada";  // envío cerrado
    public const string EnvioEntregado = "Entregado";
    public const string EnvioLeido = "Leído";
    public const string ResultadoRescatado = "Rescatado";
    public const string ResultadoClic = "Clic en enlace";
    public const string ResultadoSinAccion = "Sin acción";
}
