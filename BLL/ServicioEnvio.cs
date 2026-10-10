using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BLL;

public record ResultadoEnvio(bool Exito, string Mensaje);

public class ServicioEnvio
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly IConfiguration _config;
    private readonly ILogger<ServicioEnvio> _logger;

    public ServicioEnvio(IConfiguration config, ILogger<ServicioEnvio> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<ResultadoEnvio> EnviarEmail(string destinatario, string asunto, string cuerpo,
        CancellationToken ct = default)
    {
        var host = _config["Email:Host"];
        var usuario = _config["Email:Usuario"];
        var clave = _config["Email:Clave"];

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(clave))
            return Fallo("El envío de email no está configurado (faltan Host, Usuario o Clave).");

        if (string.IsNullOrWhiteSpace(destinatario))
            return Fallo("El cliente no tiene email.");

        var puerto = int.TryParse(_config["Email:Puerto"], out var p) ? p : 2525;
        var remitente = _config["Email:Remitente"] ?? "retencion@tfi-gym.com";
        var nombreRemitente = _config["Email:NombreRemitente"] ?? "Retención";

        // Modo prueba: nada llega al cliente real.
        var destinatarioOriginal = destinatario;
        var destinatarioPrueba = _config["Email:DestinatarioPrueba"];
        if (!string.IsNullOrWhiteSpace(destinatarioPrueba))
        {
            destinatario = destinatarioPrueba;
            cuerpo += $"\n\n[Modo prueba] Destinatario original: {destinatarioOriginal}";
        }

        try
        {
            using var mensaje = new MailMessage
            {
                From = new MailAddress(remitente, nombreRemitente),
                Subject = asunto,
                Body = cuerpo,
                IsBodyHtml = false
            };
            mensaje.To.Add(destinatario);

            // EnableSsl = true usa STARTTLS en los puertos 587 / 2525.
            using var smtp = new SmtpClient(host, puerto)
            {
                Credentials = new NetworkCredential(usuario, clave),
                EnableSsl = true,
                Timeout = 15000
            };
            await smtp.SendMailAsync(mensaje, ct);

            if (int.TryParse(_config["Email:PausaEntreEnviosMs"], out var pausa) && pausa > 0)
                await Task.Delay(pausa, ct);

            return new ResultadoEnvio(true, "Email enviado.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Fallo($"No se pudo enviar el email: {ex.Message}");
        }
    }


    // ------------------------------------------------------------------ Telegram (Bot API)
    // Sin plantillas ni aprobaciones: acepta cualquier texto. Configuración "Telegram":
    //   BotToken            (va en user-secrets; lo entrega @BotFather al crear el bot)
    //   DestinatarioPrueba  (chat_id numérico; si tiene valor, TODOS los mensajes se redirigen a ese chat)
    // Un bot no puede escribirle a alguien por su teléfono: la persona tiene que haberle escrito antes al bot.
    // 'chatIdCliente' es el chat_id de ese cliente (null mientras no esté guardado en la base).
    public async Task<ResultadoEnvio> EnviarTelegram(string? chatIdCliente, string mensaje, CancellationToken ct = default)
    {
        var token = _config["Telegram:BotToken"];
        if (string.IsNullOrWhiteSpace(token))
            return Fallo("El envío de Telegram no está configurado (falta BotToken).");

        var chatPrueba = _config["Telegram:DestinatarioPrueba"];
        var modoPrueba = !string.IsNullOrWhiteSpace(chatPrueba);
        var chatId = modoPrueba ? chatPrueba!.Trim() : chatIdCliente?.Trim();
        if (string.IsNullOrWhiteSpace(chatId))
            return Fallo("El cliente no tiene Telegram vinculado (falta su chat_id).");

        if (mensaje.Length > 4096) mensaje = mensaje[..4096]; // límite de Telegram

        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.telegram.org/bot{token}/sendMessage")
        {
            Content = new StringContent(JsonSerializer.Serialize(new { chat_id = chatId, text = mensaje }),
                Encoding.UTF8, "application/json")
        };

        try
        {
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            return resp.IsSuccessStatusCode
                ? new ResultadoEnvio(true, "Telegram enviado.")
                : Fallo($"Telegram rechazó el mensaje: {ExtraerErrorTelegram(json)}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Fallo($"No se pudo contactar a Telegram: {ex.GetType().Name}");
        }
    }

    private static string ExtraerErrorTelegram(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var d = doc.RootElement.TryGetProperty("description", out var x) ? x.GetString() : null;
            if (!string.IsNullOrWhiteSpace(d)) return d;
        }
        catch (JsonException) { }

        return json.Length > 200 ? json[..200] : json;
    }
    /*
    public Task<ResultadoEnvio> EnviarWhatsApp(string telefono, string mensaje, CancellationToken ct = default) =>
        string.Equals(_config["WhatsApp:Proveedor"], "Twilio", StringComparison.OrdinalIgnoreCase)
            ? EnviarWhatsAppTwilio(telefono, mensaje, ct)
            : EnviarWhatsAppMeta(telefono, mensaje, ct);

    private async Task<ResultadoEnvio> EnviarWhatsAppMeta(string telefono, string mensaje, CancellationToken ct)
    {
        var token = _config["Meta:AccessToken"];
        var phoneId = _config["Meta:PhoneNumberId"];
        var version = _config["Meta:ApiVersion"] ?? "v21.0";

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(phoneId))
            return Fallo("El envío de WhatsApp (Meta) no está configurado (faltan AccessToken o PhoneNumberId).");

        var telefonoPrueba = _config["Meta:DestinatarioPrueba"];
        var modoPrueba = !string.IsNullOrWhiteSpace(telefonoPrueba);
        var destino = NormalizarTelefono(modoPrueba ? telefonoPrueba! : telefono);
        if (destino is null)
            return Fallo("El cliente no tiene un teléfono válido (formato internacional, ej: +5491112345678).");

        if (modoPrueba) mensaje += $"\n\n[Modo prueba] Destinatario original: {telefono}";

        var nombrePlantilla = _config["Meta:PlantillaNombre"];
        var usaPlantilla = !string.IsNullOrWhiteSpace(nombrePlantilla);

        object payload;
        if (usaPlantilla)
        {
            var template = new Dictionary<string, object>
            {
                ["name"] = nombrePlantilla!.Trim(),
                ["language"] = new { code = _config["Meta:PlantillaIdioma"] ?? "es_AR" }
            };
            if (bool.TryParse(_config["Meta:PlantillaUsaTexto"], out var usaTexto) && usaTexto)
            {
                template["components"] = new[]
                {
                    new
                    {
                        type = "body",
                        parameters = new[] { new { type = "text", text = AplanarParaPlantilla(mensaje) } }
                    }
                };
            }

            payload = new Dictionary<string, object>
            {
                ["messaging_product"] = "whatsapp",
                ["to"] = destino.TrimStart('+'),
                ["type"] = "template",
                ["template"] = template
            };
        }
        else
        {
            payload = new
            {
                messaging_product = "whatsapp",
                to = destino.TrimStart('+'),
                type = "text",
                text = new { body = mensaje }
            };
        }
    
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{version}/{phoneId}/messages")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            return resp.IsSuccessStatusCode
                ? new ResultadoEnvio(true, usaPlantilla ? "WhatsApp enviado (con plantilla)." : "WhatsApp enviado.")
                : Fallo($"Meta rechazó el mensaje: {ExtraerErrorMeta(json)}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Fallo($"No se pudo contactar a Meta: {ex.Message}");
        }
    }*/
    /*
    private static string ExtraerErrorMeta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                var mensaje = e.TryGetProperty("message", out var m) ? m.GetString() : null;
                var codigo = e.TryGetProperty("code", out var c) ? c.ToString() : null;
                var detalle = e.TryGetProperty("error_data", out var d) && d.TryGetProperty("details", out var dt) ? dt.GetString() : null;
                if (!string.IsNullOrWhiteSpace(mensaje))
                    return $"{mensaje}{(detalle is null ? "" : " - " + detalle)}{(codigo is null ? "" : $" (código {codigo})")}";
            }
        }
        catch (JsonException) { }

        return json.Length > 300 ? json[..300] : json;
    }*/
    /*
    private async Task<ResultadoEnvio> EnviarWhatsAppTwilio(string telefono, string mensaje, CancellationToken ct)
    {
        var sid = _config["Twilio:AccountSid"];
        var token = _config["Twilio:AuthToken"];
        var origen = _config["Twilio:NumeroOrigen"];

        if (string.IsNullOrWhiteSpace(sid) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(origen))
            return Fallo("El envío de WhatsApp no está configurado (faltan AccountSid, AuthToken o NumeroOrigen).");

        // Modo prueba: nada llega al cliente real.
        var telefonoPrueba = _config["Twilio:DestinatarioPrueba"];
        var modoPrueba = !string.IsNullOrWhiteSpace(telefonoPrueba);
        var destino = NormalizarTelefono(modoPrueba ? telefonoPrueba! : telefono);
        if (destino is null)
            return Fallo("El cliente no tiene un teléfono válido (formato internacional, ej: +5491112345678).");

        if (modoPrueba) mensaje += $"\n\n[Modo prueba] Destinatario original: {telefono}";

        var campos = new Dictionary<string, string>
        {
            ["From"] = origen.StartsWith("whatsapp:") ? origen : "whatsapp:" + origen,
            ["To"] = "whatsapp:" + destino
        };

        // Con ContentSid se envía una plantilla (el texto lo define la plantilla, no 'mensaje'); sin él, texto libre.
        var contentSid = _config["Twilio:ContentSid"];
        var usaPlantilla = !string.IsNullOrWhiteSpace(contentSid);
        if (usaPlantilla)
        {
            campos["ContentSid"] = contentSid!.Trim();

            // Si hay ContentVariables fijas en la configuración se usan tal cual (plantilla con valores fijos).
            // Si no, el texto de la campaña viaja en la variable {{1}}: sirve una plantilla genérica cuyo cuerpo sea "{{1}}".
            var variables = _config["Twilio:ContentVariables"];
            campos["ContentVariables"] = !string.IsNullOrWhiteSpace(variables)
                ? variables
                : JsonSerializer.Serialize(new Dictionary<string, string> { ["1"] = AplanarParaPlantilla(mensaje) });
        }
        else
        {
            campos["Body"] = mensaje;
        }

        using var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(campos)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));

        _logger.LogInformation("Twilio WhatsApp -> campos enviados: {Campos}", string.Join(", ", campos.Keys));

        try
        {
            using var resp = await Http.SendAsync(req, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            return resp.IsSuccessStatusCode
                ? new ResultadoEnvio(true, usaPlantilla ? "WhatsApp enviado (con plantilla)." : "WhatsApp enviado.")
                : Fallo($"Twilio rechazó el mensaje: {ExtraerErrorTwilio(json)}");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Fallo($"No se pudo contactar a Twilio: {ex.Message}");
        }
    }*/

    /*
    private static string AplanarParaPlantilla(string texto) =>
        System.Text.RegularExpressions.Regex.Replace(texto.Replace("\r", " ").Replace("\n", " ").Replace("\t", " "), " {2,}", " ").Trim();

    
    private static string? NormalizarTelefono(string? telefono)
    {
        if (string.IsNullOrWhiteSpace(telefono)) return null;

        var digitos = new string(telefono.Where(char.IsDigit).ToArray());
        if (digitos.Length is < 11 or > 15) return null;

        return "+" + digitos;
    }

    private static string ExtraerErrorTwilio(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var mensaje = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
            var codigo = doc.RootElement.TryGetProperty("code", out var c) ? c.ToString() : null;
            if (!string.IsNullOrWhiteSpace(mensaje)) return codigo is null ? mensaje : $"{mensaje} (código {codigo})";
        }
        catch (JsonException) { }

        return json.Length > 200 ? json[..200] : json;
    }*/

    private ResultadoEnvio Fallo(string motivo)
    {
        _logger.LogWarning("Falló un envío: {Motivo}", motivo);
        return new ResultadoEnvio(false, motivo);
    }
}