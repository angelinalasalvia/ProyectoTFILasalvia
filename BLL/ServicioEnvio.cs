using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BLL;

public record ResultadoEnvio(bool Exito, string Mensaje);

// Envío real de mensajes:
//  - Email por SMTP (Mailtrap en desarrollo; para otro proveedor solo cambia la configuración).
//  - WhatsApp por la API REST de Twilio (sandbox en desarrollo).
//
// Configuración "Email" (appsettings.json; Usuario y Clave van en user-secrets):
//   Host, Puerto, Usuario, Clave, Remitente, NombreRemitente,
//   DestinatarioPrueba  (si tiene valor, TODOS los mails se redirigen a esa casilla),
//   PausaEntreEnviosMs  (espera después de cada envío, para respetar el límite de velocidad del proveedor)
//
// Configuración "Twilio" (AccountSid y AuthToken van en user-secrets):
//   AccountSid, AuthToken, NumeroOrigen (ej: whatsapp:+14155238886),
//   DestinatarioPrueba  (si tiene valor, TODOS los WhatsApp se redirigen a ese número)
//   ContentSid          (opcional, "HX..."). Si tiene valor se envía esa plantilla en lugar del texto libre.
//                       Es obligatorio en la cuenta de prueba (trial) de Twilio, que solo permite las plantillas
//                       que ofrece la pantalla "Try out WhatsApp". Con una cuenta/sender aprobado se puede dejar vacío.
//   ContentVariables    (opcional) JSON con los valores de la plantilla, ej: {"1":"22 de octubre","2":"15:15"}
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


    // ------------------------------------------------------------------ WhatsApp (Twilio)
    // 'telefono' en formato internacional (+5491112345678). Twilio acepta el mensaje (201) y lo entrega después:
    // acá "Exito" significa "Twilio lo aceptó"; la entrega/lectura reales requieren webhooks (fuera del MVP).
    public async Task<ResultadoEnvio> EnviarWhatsApp(string telefono, string mensaje, CancellationToken ct = default)
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
            var variables = _config["Twilio:ContentVariables"];
            if (!string.IsNullOrWhiteSpace(variables)) campos["ContentVariables"] = variables;
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
    }

    // Deja solo "+" y dígitos. Acepta "+54 9 11 1234-5678" y también "5491112345678" (ya con código de país).
    // No adivina el código de país: un número local sin él (ej. 1112345678) se considera inválido.
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
    }

    private ResultadoEnvio Fallo(string motivo)
    {
        _logger.LogWarning("Falló un envío: {Motivo}", motivo);
        return new ResultadoEnvio(false, motivo);
    }
}
