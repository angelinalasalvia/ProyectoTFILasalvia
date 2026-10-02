using System.Net.Http.Headers;
using System.Text.Json;
using BE;
using Microsoft.Data.SqlClient;

namespace BLL;

public record ResultadoConexion(bool Exito, string Mensaje);
public record ResultadoSincronizacion(bool Exito, string Mensaje, int Registros);

// Tipo normalizado: "texto", "fecha", "numero" u "otro".
internal record CampoFuente(string Nombre, string Tipo);

internal class FuenteException : Exception
{
    public FuenteException(string mensaje) : base(mensaje) { }
}

// Conexión real contra las tres clases de fuente que soporta el MVP:
//  - Base de Datos: SQL Server (usuario y contraseña).
//  - API Personalizada: endpoint REST que responde JSON (GET, con la clave como Bearer y X-API-Key).
//  - CRM: Salesforce con OAuth 2.0 "client credentials" (Client ID + Client Secret).
internal static class ConectorFuente
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const string VersionSalesforce = "v60.0";

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
                    using (await LlamarApi(c, ct)) { }
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

    // ---------------------------------------------------------------- esquema de campos
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

    // Cantidad de registros de la fuente que alimentan el mapeo (el dato "Registros mapeados" del CU14).
    public static async Task<int> ContarRegistros(string tipo, CredencialesIntegracion c,
        IReadOnlyCollection<string> camposMapeados, CancellationToken ct)
    {
        switch (tipo)
        {
            case Integracion.TipoBD:
                return await ContarBD(c, camposMapeados, ct);

            case Integracion.TipoAPI:
                {
                    using var resp = await LlamarApi(c, ct);
                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    return ContarElementos(doc.RootElement);
                }

            case Integracion.TipoCRM:
                {
                    var (token, instancia) = await ObtenerTokenCrm(c, ct);
                    using var req = new HttpRequestMessage(HttpMethod.Get,
                        $"{instancia.TrimEnd('/')}/services/data/{VersionSalesforce}/query?q=SELECT+COUNT()+FROM+Contact");
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    using var resp = await Http.SendAsync(req, ct);
                    if (!resp.IsSuccessStatusCode)
                        throw new FuenteException($"El CRM no pudo contar los contactos ({(int)resp.StatusCode}).");

                    using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
                    return doc.RootElement.GetProperty("totalSize").GetInt32();
                }

            default:
                throw new FuenteException("Tipo de integración desconocido.");
        }
    }

    // Cuenta las filas de la tabla que tiene más campos mapeados.
    private static async Task<int> ContarBD(CredencialesIntegracion c, IReadOnlyCollection<string> camposMapeados, CancellationToken ct)
    {
        var tabla = camposMapeados
            .Where(f => f.Contains('.'))
            .GroupBy(f => f[..f.IndexOf('.')])
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault()
            ?? throw new FuenteException("La integración no tiene campos mapeados.");

        await using var cn = new SqlConnection(CadenaConexion(c));
        await cn.OpenAsync(ct);

        // El nombre de la tabla sale del mapeo guardado: se verifica que exista de verdad antes de armar la consulta.
        await using var buscar = new SqlCommand(
            "SELECT TOP 1 TABLE_SCHEMA FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @t AND TABLE_TYPE = 'BASE TABLE'", cn);
        buscar.Parameters.AddWithValue("@t", tabla);
        var esquemaSql = (string?)await buscar.ExecuteScalarAsync(ct)
            ?? throw new FuenteException($"La tabla {tabla} ya no existe en la fuente.");

        static string Escapar(string s) => s.Replace("]", "]]");
        await using var contar = new SqlCommand($"SELECT COUNT(*) FROM [{Escapar(esquemaSql)}].[{Escapar(tabla)}]", cn);
        return Convert.ToInt32(await contar.ExecuteScalarAsync(ct));
    }

    private static int ContarElementos(JsonElement raiz)
    {
        if (raiz.ValueKind == JsonValueKind.Array) return raiz.GetArrayLength();

        if (raiz.ValueKind == JsonValueKind.Object)
            foreach (var p in raiz.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Array)
                    return p.Value.GetArrayLength();

        return 1;
    }

    // ---------------------------------------------------------------- Base de datos (SQL Server)
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

    // ---------------------------------------------------------------- API REST
    // Devuelve la respuesta ya validada; quien la llama debe liberarla (using).
    private static async Task<HttpResponseMessage> LlamarApi(CredencialesIntegracion c, CancellationToken ct)
    {
        ValidarUrlHttp(c.Url);

        using var req = new HttpRequestMessage(HttpMethod.Get, c.Url);
        if (!string.IsNullOrEmpty(c.Secreto))
        {
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + c.Secreto);
            req.Headers.TryAddWithoutValidation("X-API-Key", c.Secreto);
        }

        var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.IsSuccessStatusCode) return resp;

        var codigo = (int)resp.StatusCode;
        resp.Dispose();
        throw new FuenteException(codigo is 401 or 403
            ? $"La API rechazó la clave ({codigo})."
            : $"La API respondió con un error ({codigo}).");
    }

    private static async Task<List<CampoFuente>> EsquemaApi(CredencialesIntegracion c, CancellationToken ct)
    {
        using var resp = await LlamarApi(c, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var muestra = BuscarObjetoMuestra(doc.RootElement);
        if (muestra is null) return new List<CampoFuente>();

        return muestra.Value.EnumerateObject()
            .Select(p => new CampoFuente(p.Name, TipoJson(p.Value)))
            .ToList();
    }

    // Toma el primer objeto de la respuesta: un arreglo en la raíz, o un arreglo dentro de una propiedad ("data", "items"...).
    private static JsonElement? BuscarObjetoMuestra(JsonElement raiz)
    {
        if (raiz.ValueKind == JsonValueKind.Array)
            return raiz.GetArrayLength() > 0 && raiz[0].ValueKind == JsonValueKind.Object ? raiz[0] : null;

        if (raiz.ValueKind != JsonValueKind.Object) return null;

        foreach (var p in raiz.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.Array && p.Value.GetArrayLength() > 0
                && p.Value[0].ValueKind == JsonValueKind.Object)
                return p.Value[0];
        }
        return raiz;
    }

    private static string TipoJson(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.String => DateTime.TryParse(valor.GetString(), out _) ? "fecha" : "texto",
        JsonValueKind.Number => "numero",
        _ => "otro"
    };

    // ---------------------------------------------------------------- CRM (Salesforce)
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

        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"{instancia.TrimEnd('/')}/services/data/{VersionSalesforce}/sobjects/Contact/describe");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var resp = await Http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            throw new FuenteException($"El CRM no devolvió el esquema de contactos ({(int)resp.StatusCode}).");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("fields").EnumerateArray()
            .Select(f => new CampoFuente(
                f.GetProperty("name").GetString() ?? string.Empty,
                TipoSalesforce(f.GetProperty("type").GetString() ?? string.Empty)))
            .ToList();
    }

    private static string TipoSalesforce(string tipo) => tipo.ToLowerInvariant() switch
    {
        "string" or "email" or "phone" or "picklist" or "textarea" or "url" or "id" or "reference" => "texto",
        "date" or "datetime" => "fecha",
        "int" or "double" or "currency" or "percent" => "numero",
        _ => "otro"
    };

    // ---------------------------------------------------------------- utilidades
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
