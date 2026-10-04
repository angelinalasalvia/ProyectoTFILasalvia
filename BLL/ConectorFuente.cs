using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using BE;
using Microsoft.Data.SqlClient;

namespace BLL;

public record ResultadoConexion(bool Exito, string Mensaje);
public record ResultadoSincronizacion(bool Exito, string Mensaje, int Registros);

// Tipo normalizado: "texto", "fecha", "numero" u "otro". El nombre va calificado: "Origen.Campo".
internal record CampoFuente(string Nombre, string Tipo);

internal class FuenteException : Exception
{
    public FuenteException(string mensaje) : base(mensaje) { }
}

// Conexión real contra las tres clases de fuente que soporta el MVP:
//  - Base de Datos: SQL Server (usuario y contraseña). Origen = tabla.
//  - API Personalizada: dos endpoints REST que responden JSON (clientes y eventos). Origen = "Clientes" / "Eventos".
//    La clave se manda como Bearer y como X-API-Key.
//  - CRM: Salesforce con OAuth 2.0 "client credentials". Origen = objeto (Contact, Task, Event).
internal static partial class ConectorFuente
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string VersionSalesforce = "v60.0";
    private const int MaxValoresDistintos = 100;
    private const int MaxFilasPorOrigen = 50_000;

    public const string OrigenApiClientes = "Clientes";
    public const string OrigenApiEventos = "Eventos";
    private static readonly string[] ObjetosCrm = { "Contact", "Task", "Event" };

    // ---------------------------------------------------------------- probar conexión
    public static async Task<ResultadoConexion> Probar(string tipo, CredencialesIntegracion c, CancellationToken ct)
    {
        try
        {
            switch (tipo)
            {
                case Integracion.TipoBD:
                    await using (var cn = new SqlConnection(CadenaConexion(c)))
                    {
                        await cn.OpenAsync(ct);
                    }
                    break;

                case Integracion.TipoAPI:
                    using (await LlamarApi(c.Url, c.Secreto, ct)) { }
                    if (string.IsNullOrWhiteSpace(c.UrlEventos))
                        throw new FuenteException("Falta el endpoint de eventos de la API.");
                    using (await LlamarApi(c.UrlEventos, c.Secreto, ct)) { }
                    break;

                case Integracion.TipoCRM:
                    await ObtenerTokenCrm(c, ct);
                    break;

                default:
                    throw new FuenteException("Tipo de integración desconocido.");
            }

            return new ResultadoConexion(true, "Conexión exitosa.");
        }
        catch (Exception ex)
        {
            return new ResultadoConexion(false, Describir(ex));
        }
    }

    // ---------------------------------------------------------------- esquema de campos ("Origen.Campo")
    public static async Task<List<CampoFuente>> ObtenerEsquema(string tipo, CredencialesIntegracion c, CancellationToken ct)
    {
        return tipo switch
        {
            Integracion.TipoBD => await EsquemaBD(c, ct),
            Integracion.TipoAPI => await EsquemaApi(c, ct),
            Integracion.TipoCRM => await EsquemaCrm(c, ct),
            _ => throw new FuenteException("Tipo de integración desconocido.")
        };
    }

    // ---------------------------------------------------------------- valores distintos de un campo
    // Se usan para traducir los valores de la fuente (ej. "check-in") a los de la plataforma (ej. "Visita al gimnasio").
    public static async Task<List<string>> ObtenerValoresDistintos(string tipo, CredencialesIntegracion c,
        string campoExterno, CancellationToken ct)
    {
        var origen = MapeoIntegracion.Origen(campoExterno);
        var campo = MapeoIntegracion.Campo(campoExterno);

        return tipo switch
        {
            Integracion.TipoBD => await ValoresBD(c, origen, campo, ct),
            Integracion.TipoAPI => await ValoresApi(c, origen, campo, ct),
            Integracion.TipoCRM => await ValoresCrm(c, origen, campo, ct),
            _ => throw new FuenteException("Tipo de integración desconocido.")
        };
    }

    // ---------------------------------------------------------------- lectura de filas de un origen
    // Devuelve una fila por registro, con los valores como texto y las claves "Origen.Campo" (las mismas del mapeo).
    // Las fechas salen en formato ISO. Si el origen tiene más de MaxFilasPorOrigen filas se corta con un error.
    public static async Task<List<Dictionary<string, string?>>> LeerFilas(string tipo, CredencialesIntegracion c,
        string origen, IReadOnlyCollection<string> camposCalificados, CancellationToken ct)
    {
        var campos = camposCalificados.Distinct().ToList();

        return tipo switch
        {
            Integracion.TipoBD => await LeerFilasBD(c, origen, campos, ct),
            Integracion.TipoAPI => await LeerFilasDeApi(c, origen, campos, ct),
            Integracion.TipoCRM => await LeerFilasCrm(c, origen, campos, ct),
            _ => throw new FuenteException("Tipo de integración desconocido.")
        };
    }

    // ================================================================ Base de datos (SQL Server)
    private static string CadenaConexion(CredencialesIntegracion c) =>
        new SqlConnectionStringBuilder
        {
            DataSource = c.Url,
            InitialCatalog = c.BaseDatos ?? string.Empty,
            UserID = c.Usuario,
            Password = c.Secreto,
            TrustServerCertificate = true,
            ConnectTimeout = 8
        }.ConnectionString;

    private static string Escapar(string s) => s.Replace("]", "]]");

    private static async Task<List<CampoFuente>> EsquemaBD(CredencialesIntegracion c, CancellationToken ct)
    {
        var campos = new List<CampoFuente>();

        await using var cn = new SqlConnection(CadenaConexion(c));
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand(
            @"SELECT col.TABLE_NAME + '.' + col.COLUMN_NAME, col.DATA_TYPE
              FROM INFORMATION_SCHEMA.COLUMNS col
              JOIN INFORMATION_SCHEMA.TABLES t
                ON t.TABLE_SCHEMA = col.TABLE_SCHEMA AND t.TABLE_NAME = col.TABLE_NAME
              WHERE t.TABLE_TYPE = 'BASE TABLE'
              ORDER BY col.TABLE_NAME, col.ORDINAL_POSITION", cn);

        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            campos.Add(new CampoFuente(lector.GetString(0), TipoSql(lector.GetString(1))));

        return campos;
    }

    private static string TipoSql(string tipo) => tipo.ToLowerInvariant() switch
    {
        "char" or "varchar" or "nchar" or "nvarchar" or "text" or "ntext" => "texto",
        "date" or "datetime" or "datetime2" or "smalldatetime" or "datetimeoffset" => "fecha",
        "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "numeric" or "float" or "real" or "money" or "smallmoney" => "numero",
        _ => "otro"
    };

    // El nombre de la tabla/columna sale del mapeo guardado: se verifica que exista de verdad antes de armar la consulta.
    private static async Task<List<string>> ValoresBD(CredencialesIntegracion c, string tabla, string columna, CancellationToken ct)
    {
        await using var cn = new SqlConnection(CadenaConexion(c));
        await cn.OpenAsync(ct);

        await using var buscar = new SqlCommand(
            "SELECT TOP 1 TABLE_SCHEMA FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @t AND COLUMN_NAME = @c", cn);
        buscar.Parameters.AddWithValue("@t", tabla);
        buscar.Parameters.AddWithValue("@c", columna);
        var esquemaSql = (string?)await buscar.ExecuteScalarAsync(ct)
            ?? throw new FuenteException($"El campo {tabla}.{columna} ya no existe en la fuente.");

        await using var cmd = new SqlCommand(
            $"SELECT DISTINCT TOP ({MaxValoresDistintos}) CAST([{Escapar(columna)}] AS nvarchar(200)) " +
            $"FROM [{Escapar(esquemaSql)}].[{Escapar(tabla)}] WHERE [{Escapar(columna)}] IS NOT NULL ORDER BY 1", cn);

        var valores = new List<string>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
            valores.Add(lector.GetString(0));
        return valores;
    }

    private static async Task<List<Dictionary<string, string?>>> LeerFilasBD(CredencialesIntegracion c, string tabla,
        List<string> camposCalificados, CancellationToken ct)
    {
        await using var cn = new SqlConnection(CadenaConexion(c));
        await cn.OpenAsync(ct);

        // El nombre de la tabla y de las columnas sale del mapeo guardado: se verifica que existan antes de armar la consulta.
        await using var buscar = new SqlCommand(
            "SELECT TOP 1 TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @t AND TABLE_TYPE = 'BASE TABLE'", cn);
        buscar.Parameters.AddWithValue("@t", tabla);
        var esquemaSql = (string?)await buscar.ExecuteScalarAsync(ct)
            ?? throw new FuenteException($"La tabla {tabla} ya no existe en la fuente.");

        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cols = new SqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t", cn))
        {
            cols.Parameters.AddWithValue("@s", esquemaSql);
            cols.Parameters.AddWithValue("@t", tabla);
            await using var lectorCols = await cols.ExecuteReaderAsync(ct);
            while (await lectorCols.ReadAsync(ct))
                existentes.Add(lectorCols.GetString(0));
        }

        var columnas = camposCalificados.Select(MapeoIntegracion.Campo).ToList();
        var faltante = columnas.FirstOrDefault(col => !existentes.Contains(col));
        if (faltante is not null)
            throw new FuenteException($"El campo {tabla}.{faltante} ya no existe en la fuente.");

        await using var cmd = new SqlCommand(
            $"SELECT TOP ({MaxFilasPorOrigen + 1}) {string.Join(", ", columnas.Select(col => $"[{Escapar(col)}]"))} " +
            $"FROM [{Escapar(esquemaSql)}].[{Escapar(tabla)}]", cn);

        var filas = new List<Dictionary<string, string?>>();
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        while (await lector.ReadAsync(ct))
        {
            if (filas.Count >= MaxFilasPorOrigen)
                throw new FuenteException($"El origen {tabla} tiene más de {MaxFilasPorOrigen:N0} filas: es demasiado grande para importar.");

            var fila = new Dictionary<string, string?>();
            for (var i = 0; i < camposCalificados.Count; i++)
                fila[camposCalificados[i]] = lector.IsDBNull(i) ? null : Formatear(lector.GetValue(i));
            filas.Add(fila);
        }
        return filas;
    }

    // Fechas en ISO (sin milisegundos) y números con punto, para que el resto del proceso no dependa de la cultura.
    private static string? Formatear(object valor) => valor switch
    {
        DateTime d => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        _ => Convert.ToString(valor, CultureInfo.InvariantCulture)
    };

    // ================================================================ API REST
    // Devuelve la respuesta ya validada; quien la llama debe liberarla (using).
    private static async Task<HttpResponseMessage> LlamarApi(string url, string secreto, CancellationToken ct)
    {
        ValidarUrlHttp(url);

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(secreto))
        {
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + secreto);
            req.Headers.TryAddWithoutValidation("X-API-Key", secreto);
        }

        var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.IsSuccessStatusCode) return resp;

        var codigo = (int)resp.StatusCode;
        resp.Dispose();
        throw new FuenteException(codigo is 401 or 403
            ? $"La API rechazó la clave ({codigo})."
            : $"La API respondió con un error ({codigo}).");
    }

    private static string UrlDeOrigenApi(CredencialesIntegracion c, string origen)
    {
        if (origen == OrigenApiEventos)
            return string.IsNullOrWhiteSpace(c.UrlEventos)
                ? throw new FuenteException("Falta el endpoint de eventos de la API.")
                : c.UrlEventos;

        return c.Url;
    }

    // Filas del endpoint como texto: un diccionario "propiedad -> valor" por elemento.
    private static async Task<List<Dictionary<string, string?>>> LeerFilasApi(CredencialesIntegracion c, string origen, CancellationToken ct)
    {
        using var resp = await LlamarApi(UrlDeOrigenApi(c, origen), c.Secreto, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var filas = new List<Dictionary<string, string?>>();
        foreach (var elemento in ElementosDeLaRespuesta(doc.RootElement))
        {
            if (elemento.ValueKind != JsonValueKind.Object) continue;

            var fila = new Dictionary<string, string?>();
            foreach (var p in elemento.EnumerateObject())
            {
                fila[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    _ => p.Value.ToString()
                };
            }
            filas.Add(fila);
        }
        return filas;
    }

    // Igual que LeerFilasApi, pero con las claves "Origen.Campo" del mapeo.
    private static async Task<List<Dictionary<string, string?>>> LeerFilasDeApi(CredencialesIntegracion c, string origen,
        List<string> camposCalificados, CancellationToken ct)
    {
        var filas = await LeerFilasApi(c, origen, ct);
        if (filas.Count > MaxFilasPorOrigen)
            throw new FuenteException($"El origen {origen} tiene más de {MaxFilasPorOrigen:N0} filas: es demasiado grande para importar.");

        return filas
            .Select(f => camposCalificados.ToDictionary(k => k, k => f.GetValueOrDefault(MapeoIntegracion.Campo(k))))
            .ToList();
    }

    // Los elementos están en un arreglo en la raíz, o en un arreglo dentro de una propiedad ("data", "items"...).
    private static List<JsonElement> ElementosDeLaRespuesta(JsonElement raiz)
    {
        if (raiz.ValueKind == JsonValueKind.Array) return raiz.EnumerateArray().ToList();

        if (raiz.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in raiz.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array)
                    return p.Value.EnumerateArray().ToList();
            return new List<JsonElement> { raiz };
        }
        return new List<JsonElement>();
    }

    private static async Task<List<CampoFuente>> EsquemaApi(CredencialesIntegracion c, CancellationToken ct)
    {
        var campos = await EsquemaDeEndpoint(c.Url, c.Secreto, OrigenApiClientes, ct);
        if (!string.IsNullOrWhiteSpace(c.UrlEventos))
            campos.AddRange(await EsquemaDeEndpoint(c.UrlEventos, c.Secreto, OrigenApiEventos, ct));
        return campos;
    }

    private static async Task<List<CampoFuente>> EsquemaDeEndpoint(string url, string secreto, string origen, CancellationToken ct)
    {
        using var resp = await LlamarApi(url, secreto, ct);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

        var muestra = ElementosDeLaRespuesta(doc.RootElement).FirstOrDefault(e => e.ValueKind == JsonValueKind.Object);
        if (muestra.ValueKind != JsonValueKind.Object) return new List<CampoFuente>();

        return muestra.EnumerateObject()
            .Select(p => new CampoFuente($"{origen}.{p.Name}", TipoJson(p.Value)))
            .ToList();
    }

    private static string TipoJson(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.String => DateTime.TryParse(valor.GetString(), out _) ? "fecha" : "texto",
        JsonValueKind.Number => "numero",
        _ => "otro"
    };

    private static async Task<List<string>> ValoresApi(CredencialesIntegracion c, string origen, string campo, CancellationToken ct)
    {
        var filas = await LeerFilasApi(c, origen, ct);
        return filas
            .Select(f => f.GetValueOrDefault(campo))
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct()
            .OrderBy(v => v)
            .Take(MaxValoresDistintos)
            .ToList();
    }

    // ================================================================ CRM (Salesforce)
    private static async Task<(string Token, string InstanceUrl)> ObtenerTokenCrm(CredencialesIntegracion c, CancellationToken ct)
    {
        ValidarUrlHttp(c.Url);
        var baseUrl = c.Url.TrimEnd('/');

        using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/services/oauth2/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = c.Usuario,
                ["client_secret"] = c.Secreto
            })
        };

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new FuenteException($"El CRM rechazó las credenciales ({(int)resp.StatusCode}).");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new FuenteException("El CRM no devolvió un token de acceso.");
        var instancia = doc.RootElement.TryGetProperty("instance_url", out var i) ? i.GetString() ?? baseUrl : baseUrl;

        return (token, instancia);
    }

    private static async Task<List<CampoFuente>> EsquemaCrm(CredencialesIntegracion c, CancellationToken ct)
    {
        var (token, instancia) = await ObtenerTokenCrm(c, ct);
        var campos = new List<CampoFuente>();

        foreach (var objeto in ObjetosCrm)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                $"{instancia.TrimEnd('/')}/services/data/{VersionSalesforce}/sobjects/{objeto}/describe");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var resp = await Http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                // Contact es imprescindible; Task o Event pueden no estar habilitados en la organización.
                if (objeto == "Contact")
                    throw new FuenteException($"El CRM no devolvió el esquema de contactos ({(int)resp.StatusCode}).");
                continue;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            foreach (var f in doc.RootElement.GetProperty("fields").EnumerateArray())
            {
                campos.Add(new CampoFuente(
                    $"{objeto}.{f.GetProperty("name").GetString()}",
                    TipoSalesforce(f.GetProperty("type").GetString() ?? string.Empty)));
            }
        }
        return campos;
    }

    private static string TipoSalesforce(string tipo) => tipo.ToLowerInvariant() switch
    {
        "string" or "email" or "phone" or "picklist" or "textarea" or "url" or "id" or "reference" => "texto",
        "date" or "datetime" => "fecha",
        "int" or "double" or "currency" or "percent" => "numero",
        _ => "otro"
    };

    private static async Task<JsonDocument> GetCrm(string urlAbsoluta, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, urlAbsoluta);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new FuenteException($"El CRM no pudo resolver la consulta ({(int)resp.StatusCode}).");

        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    private static Task<JsonDocument> ConsultarCrm(string instancia, string token, string soql, CancellationToken ct) =>
        GetCrm($"{instancia.TrimEnd('/')}/services/data/{VersionSalesforce}/query?q={Uri.EscapeDataString(soql)}", token, ct);

    // Recorre todas las páginas de la consulta (Salesforce devuelve "nextRecordsUrl" mientras queden registros).
    private static async Task<List<Dictionary<string, string?>>> LeerFilasCrm(CredencialesIntegracion c, string objeto,
        List<string> camposCalificados, CancellationToken ct)
    {
        var (token, instancia) = await ObtenerTokenCrm(c, ct);
        var columnas = camposCalificados.Select(MapeoIntegracion.Campo).Select(NombreSeguro).ToList();
        var soql = $"SELECT {string.Join(", ", columnas)} FROM {NombreSeguro(objeto)}";

        var filas = new List<Dictionary<string, string?>>();
        var url = $"{instancia.TrimEnd('/')}/services/data/{VersionSalesforce}/query?q={Uri.EscapeDataString(soql)}";

        while (url is not null)
        {
            using var doc = await GetCrm(url, token, ct);

            foreach (var registro in doc.RootElement.GetProperty("records").EnumerateArray())
            {
                if (filas.Count >= MaxFilasPorOrigen)
                    throw new FuenteException($"El objeto {objeto} tiene más de {MaxFilasPorOrigen:N0} registros: es demasiado grande para importar.");

                var fila = new Dictionary<string, string?>();
                for (var i = 0; i < camposCalificados.Count; i++)
                {
                    fila[camposCalificados[i]] =
                        registro.TryGetProperty(columnas[i], out var v) && v.ValueKind != JsonValueKind.Null
                            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                            : null;
                }
                filas.Add(fila);
            }

            var terminado = doc.RootElement.TryGetProperty("done", out var d) && d.GetBoolean();
            url = !terminado && doc.RootElement.TryGetProperty("nextRecordsUrl", out var sig) && sig.GetString() is { } relativa
                ? instancia.TrimEnd('/') + relativa
                : null;
        }
        return filas;
    }

    private static async Task<List<string>> ValoresCrm(CredencialesIntegracion c, string objeto, string campo, CancellationToken ct)
    {
        var (token, instancia) = await ObtenerTokenCrm(c, ct);
        var soql = $"SELECT {NombreSeguro(campo)} FROM {NombreSeguro(objeto)} WHERE {NombreSeguro(campo)} != null LIMIT 200";

        using var doc = await ConsultarCrm(instancia, token, soql, ct);
        return doc.RootElement.GetProperty("records").EnumerateArray()
            .Select(r => r.TryGetProperty(campo, out var v) && v.ValueKind != JsonValueKind.Null
                ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                : null)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!)
            .Distinct()
            .OrderBy(v => v)
            .Take(MaxValoresDistintos)
            .ToList();
    }

    // Los nombres de objeto/campo salen del mapeo guardado y van dentro de una consulta SOQL: solo letras, números y "_".
    private static string NombreSeguro(string nombre) =>
        NombreValido().IsMatch(nombre) ? nombre : throw new FuenteException("Nombre de campo u objeto no válido.");

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex NombreValido();

    // ================================================================ utilidades
    private static void ValidarUrlHttp(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new FuenteException("La URL no es válida (debe empezar con http:// o https://).");
    }

    internal static string Describir(Exception ex) => ex switch
    {
        FuenteException => ex.Message,
        SqlException { Number: 18456 } => "Usuario o contraseña incorrectos.",
        SqlException s => $"No se pudo conectar a la base de datos: {s.Message}",
        TaskCanceledException => "Se agotó el tiempo de espera de la conexión.",
        HttpRequestException => "No se pudo contactar al servidor. Revisá la URL.",
        JsonException => "La respuesta del servidor no tiene el formato esperado.",
        _ => $"No se pudo establecer la conexión: {ex.Message}"
    };
}
