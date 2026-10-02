using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BLL;

public record ResultadoEnvio(bool Exito, string Mensaje);

// Envío real de mensajes. Hoy: email por SMTP (Mailtrap en desarrollo; para otro proveedor solo cambia la configuración).
// WhatsApp se agrega en el próximo bloque.
//
// Configuración (appsettings.json, sección "Email"; Usuario y Clave van en user-secrets):
//   Host, Puerto, Usuario, Clave, Remitente, NombreRemitente,
//   DestinatarioPrueba  (si tiene valor, TODOS los mails se redirigen a esa casilla),
//   PausaEntreEnviosMs  (espera después de cada envío, para respetar el límite de velocidad del proveedor)
public class ServicioEnvio
{
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

    private ResultadoEnvio Fallo(string motivo)
    {
        _logger.LogWarning("Falló un envío: {Motivo}", motivo);
        return new ResultadoEnvio(false, motivo);
    }
}
