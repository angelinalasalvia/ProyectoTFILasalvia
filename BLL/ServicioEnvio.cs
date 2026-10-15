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
    
    public Task<ResultadoEnvio> EnviarCorreoRecuperacion(string email, string tokenLink, CancellationToken ct = default)
    {
        const string asunto = "Recuperá tu contraseña";
        var cuerpo =
            "Hola,\n\n" +
            "Recibimos una solicitud para restablecer tu contraseña. " +
            "Ingresá al siguiente enlace (vale 30 minutos y se puede usar una sola vez):\n\n" +
            $"{tokenLink}\n\n" +
            "Si no fuiste vos, ignorá este mensaje: tu contraseña no va a cambiar.";

        return EnviarEmail(email, asunto, cuerpo, ct);
    }

    // DS CU21: Sistema de Correo -> EnviarInstruccionesAcceso(email, tokenLink)
    public Task<ResultadoEnvio> EnviarInstruccionesAcceso(string email, string tokenLink, CancellationToken ct = default)
    {
        const string asunto = "Tu cuenta fue creada: establecé tu contraseña";
        var cuerpo =
            "Hola,\n\n" +
            "Se creó una cuenta para vos en la plataforma.\n\n" +
            $"Usuario: {email}\n\n" +
            "Para activarla y establecer tu contraseña ingresá al siguiente enlace " +
            "(vale 24 horas y se puede usar una sola vez):\n\n" +
            $"{tokenLink}\n\n" +
            "Si no esperabas este correo, ignoralo.";

        return EnviarEmail(email, asunto, cuerpo, ct);
    }

    private ResultadoEnvio Fallo(string motivo)
    {
        _logger.LogWarning("Falló un envío: {Motivo}", motivo);
        return new ResultadoEnvio(false, motivo);
    }
}