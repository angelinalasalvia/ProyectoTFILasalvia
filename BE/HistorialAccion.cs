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
    public string Resultado { get; set; } = string.Empty;
    public string Estado { get; set; } = string.Empty;

    // "Fecha y hora de la acción" del paso 6: la más específica disponible
    // (conversión > lectura > envío). Viene de un COALESCE en el SELECT.
    public DateTime FechaAccion { get; set; }

    public string NombreCliente { get; set; } = string.Empty;
    public string ApellidoCliente { get; set; } = string.Empty;
    public string PlanCliente { get; set; } = string.Empty;
}
