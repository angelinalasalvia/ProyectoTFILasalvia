using System.Globalization;
using System.Text;
using BE;
using DAL;

namespace BLL;

internal record ClienteImportado(
    string Clave, string Nombre, string Apellido, string Email,
    string? Telefono, string? Estado, string? Plan, string? Sexo, string? Sede, string? IdExterno);

internal record EventoImportado(string ClaveCliente, string Evento, DateTime Fecha);

// Traduce las filas crudas de la fuente (texto) a clientes y eventos de la plataforma según el mapeo.
// No toca la base de datos.
internal static class TransformadorMapeo
{
    public record Resultado(
        List<ClienteImportado> Clientes,
        List<EventoImportado> Eventos,
        int ClientesOmitidos,    // sin nombre, email o clave
        int EventosIgnorados,    // su tipo no fue asignado a ninguno del modelo (a propósito)
        int EventosInvalidos);   // sin cliente o con fecha inválida

    public static Resultado Normalizar(MapeoIntegracion mapeo,
        List<Dictionary<string, string?>> filasClientes, List<Dictionary<string, string?>> filasEventos)
    {
        var porClave = new Dictionary<string, ClienteImportado>();
        var clientesOmitidos = 0;

        foreach (var fila in filasClientes)
        {
            var nombre = Texto(fila, mapeo.Clientes, "Nombre", 100);
            var apellido = Texto(fila, mapeo.Clientes, "Apellido", 100) ?? string.Empty;
            var email = Texto(fila, mapeo.Clientes, "Email", 150);
            var idExterno = Texto(fila, mapeo.Clientes, "IdExterno", 100);

            var clave = mapeo.ClaveCliente == DefinicionMapeo.ClaveIdExterno ? idExterno : email?.ToLowerInvariant();
            if (clave is null || nombre is null || email is null)
            {
                clientesOmitidos++;
                continue;
            }

            var estadoOrigen = Texto(fila, mapeo.Clientes, "Estado", 100);
            string? estado = null;
            if (estadoOrigen is not null && mapeo.ValoresEstado.TryGetValue(estadoOrigen, out var traducido)
                && !string.IsNullOrEmpty(traducido))
                estado = traducido;

            // Si el mismo cliente aparece dos veces en la fuente, queda la última fila.
            porClave[clave] = new ClienteImportado(
                clave, nombre, apellido, email,
                Texto(fila, mapeo.Clientes, "Telefono", 30), estado,
                Texto(fila, mapeo.Clientes, "Plan", 100), Texto(fila, mapeo.Clientes, "Sexo", 50),
                Texto(fila, mapeo.Clientes, "Sede", 50), idExterno);
        }

        var eventos = new List<EventoImportado>();
        var ignorados = 0;
        var invalidos = 0;

        foreach (var fila in filasEventos)
        {
            var tipoOrigen = Texto(fila, mapeo.Eventos, "Evento", 255);
            if (tipoOrigen is null || !mapeo.ValoresEvento.TryGetValue(tipoOrigen, out var tipo) || string.IsNullOrEmpty(tipo))
            {
                ignorados++;
                continue;
            }

            var claveCliente = Texto(fila, mapeo.Eventos, "Cliente", 150);
            var fechaTexto = Texto(fila, mapeo.Eventos, "Fecha", 50);
            if (claveCliente is null || fechaTexto is null || !TryParseFecha(fechaTexto, out var fecha))
            {
                invalidos++;
                continue;
            }

            if (mapeo.ClaveCliente == DefinicionMapeo.ClaveEmail) claveCliente = claveCliente.ToLowerInvariant();
            eventos.Add(new EventoImportado(claveCliente, tipo, fecha));
        }

        return new Resultado(porClave.Values.ToList(), eventos, clientesOmitidos, ignorados, invalidos);
    }

    // Valor de un campo de la plataforma, recortado y sin espacios; null si no está mapeado o viene vacío.
    private static string? Texto(Dictionary<string, string?> fila, Dictionary<string, string> mapa, string campo, int maxLargo)
    {
        if (!mapa.TryGetValue(campo, out var externo)) return null;

        var valor = fila.GetValueOrDefault(externo)?.Trim();
        if (string.IsNullOrEmpty(valor)) return null;
        return valor.Length > maxLargo ? valor[..maxLargo] : valor;
    }

    // Acepta ISO (con o sin zona horaria) y el formato local dd/MM/aaaa. Se trunca a segundos para que el
    // mismo evento no quede distinto por los milisegundos (la columna es datetime, que redondea).
    private static readonly CultureInfo Argentina = CultureInfo.GetCultureInfo("es-AR");

    public static bool TryParseFecha(string texto, out DateTime fecha)
    {
        if (DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out fecha)
            || DateTime.TryParse(texto, Argentina, DateTimeStyles.AssumeLocal, out fecha))
        {
            fecha = new DateTime(fecha.Ticks - fecha.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Unspecified);
            return true;
        }
        return false;
    }
}

// Escribe en Cliente y EventosCliente lo que dejó TransformadorMapeo. Es idempotente: se puede correr
// cada 15 minutos sin duplicar nada (los clientes se reconocen por la clave; los eventos por cliente + tipo + fecha).
internal class ImportadorDatos
{
    private const int EventosPorLote = 400;       // 3 parámetros por evento: queda lejos del tope de 2100 de SQL Server
    private const string SinDefinir = "Sin definir";

    private readonly AccesoDatos _accesoDatos;
    public ImportadorDatos(AccesoDatos accesoDatos) => _accesoDatos = accesoDatos;

    public record ResultadoImportacion(int ClientesNuevos, int ClientesActualizados, int EventosNuevos, int EventosSinCliente);

    public class ClienteExistente
    {
        public int IdCliente { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? IdExterno { get; set; }
        public int? IdIntegracion { get; set; }
    }

    public async Task<ResultadoImportacion> Importar(int idIntegracion, MapeoIntegracion mapeo,
        TransformadorMapeo.Resultado datos, CancellationToken ct)
    {
        // ---------------------------------------------------------------- clientes
        var existentes = (await _accesoDatos.Leer<ClienteExistente>(
            "SELECT IdCliente, Email, IdExterno, IdIntegracion FROM Cliente", ct: ct)).ToList();

        var porIdExterno = existentes
            .Where(e => e.IdIntegracion == idIntegracion && !string.IsNullOrEmpty(e.IdExterno))
            .GroupBy(e => e.IdExterno!)
            .ToDictionary(g => g.Key, g => g.First());

        // Un cliente que ya estaba cargado a mano (sin integración) y tiene el mismo email se "adopta": no se duplica.
        var porEmail = new Dictionary<string, ClienteExistente>();
        foreach (var e in existentes.Where(e => e.IdIntegracion is null || e.IdIntegracion == idIntegracion))
            porEmail.TryAdd(e.Email.Trim().ToLowerInvariant(), e);

        int nuevos = 0, actualizados = 0;

        foreach (var c in datos.Clientes)
        {
            ClienteExistente? existente = null;
            if (mapeo.ClaveCliente == DefinicionMapeo.ClaveIdExterno)
                porIdExterno.TryGetValue(c.Clave, out existente);
            existente ??= porEmail.GetValueOrDefault(c.Email.ToLowerInvariant());

            var parametros = new
            {
                id = existente?.IdCliente,
                nombre = c.Nombre,
                apellido = c.Apellido,
                email = c.Email,
                telefono = c.Telefono,
                estado = c.Estado,
                plan = c.Plan,
                sexo = c.Sexo,
                sede = c.Sede,
                idExterno = c.IdExterno,
                idIntegracion,
                estadoNuevo = c.Estado ?? "Socio Activo",
                planNuevo = c.Plan ?? SinDefinir,
                sexoNuevo = c.Sexo ?? SinDefinir,
                sedeNuevo = c.Sede ?? SinDefinir
            };

            if (existente is not null)
            {
                // Los datos que la fuente no trae (o no están mapeados) se conservan como estaban.
                await _accesoDatos.Modificar(
                    @"UPDATE Cliente
                         SET Nombre = @nombre, Email = @email,
                             Apellido = CASE WHEN @apellido = '' THEN Apellido ELSE @apellido END,
                             Telefono = COALESCE(@telefono, Telefono),
                             EstadoRegistro = COALESCE(@estado, EstadoRegistro),
                             PlanSocio = COALESCE(@plan, PlanSocio),
                             Sexo = COALESCE(@sexo, Sexo),
                             Sede = COALESCE(@sede, Sede),
                             IdExterno = COALESCE(@idExterno, IdExterno),
                             IdIntegracion = @idIntegracion
                       WHERE IdCliente = @id", parametros, ct: ct);
                actualizados++;
            }
            else
            {
                await _accesoDatos.Escribir(
                    @"INSERT INTO Cliente (Nombre, Apellido, Email, EstadoRegistro, PlanSocio, Sexo, Sede, Telefono, IdExterno, IdIntegracion)
                      VALUES (@nombre, @apellido, @email, @estadoNuevo, @planNuevo, @sexoNuevo, @sedeNuevo, @telefono, @idExterno, @idIntegracion)",
                    parametros, ct: ct);
                nuevos++;
            }
        }

        // ---------------------------------------------------------------- eventos
        // Los eventos se asocian solo a los clientes de esta integración (los que acaban de importarse o adoptarse).
        var vinculados = (await _accesoDatos.Leer<ClienteExistente>(
            "SELECT IdCliente, Email, IdExterno, IdIntegracion FROM Cliente WHERE IdIntegracion = @idIntegracion",
            new { idIntegracion }, ct: ct)).ToList();

        var idPorClave = new Dictionary<string, int>();
        foreach (var v in vinculados)
        {
            var clave = mapeo.ClaveCliente == DefinicionMapeo.ClaveIdExterno ? v.IdExterno : v.Email.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(clave)) idPorClave.TryAdd(clave, v.IdCliente);
        }

        var sinCliente = 0;
        var aInsertar = new HashSet<(int IdCliente, string Evento, DateTime Fecha)>();
        foreach (var e in datos.Eventos)
        {
            if (idPorClave.TryGetValue(e.ClaveCliente, out var idCliente)) aInsertar.Add((idCliente, e.Evento, e.Fecha));
            else sinCliente++;
        }

        var eventosNuevos = 0;
        foreach (var lote in aInsertar.Chunk(EventosPorLote))
            eventosNuevos += await InsertarLoteEventos(lote, ct);

        return new ResultadoImportacion(nuevos, actualizados, eventosNuevos, sinCliente);
    }

    // Inserta solo los eventos que todavía no existen (mismo cliente, tipo y fecha).
    private async Task<int> InsertarLoteEventos((int IdCliente, string Evento, DateTime Fecha)[] lote, CancellationToken ct)
    {
        var sql = new StringBuilder(
            "INSERT INTO EventosCliente (Evento, Fecha, IdCliente) SELECT v.Evento, v.Fecha, v.IdCliente FROM (VALUES ");
        var parametros = new Dictionary<string, object?>();

        for (var i = 0; i < lote.Length; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append($"(@e{i}, @f{i}, @c{i})");
            parametros[$"e{i}"] = lote[i].Evento;
            parametros[$"f{i}"] = lote[i].Fecha;
            parametros[$"c{i}"] = lote[i].IdCliente;
        }

        sql.Append(") AS v(Evento, Fecha, IdCliente) WHERE NOT EXISTS (" +
                   "SELECT 1 FROM EventosCliente x WHERE x.IdCliente = v.IdCliente AND x.Evento = v.Evento AND x.Fecha = v.Fecha)");

        return await _accesoDatos.Escribir(sql.ToString(), parametros, ct: ct);
    }
}

