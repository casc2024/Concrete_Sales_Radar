using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace ConcreteSalesRadar.Services;

public class EmailSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string? From { get; set; }
    public string FromName { get; set; } = "Concrete Sales Radar";
    public bool UseStartTls { get; set; } = true;
}

public interface IEmailService
{
    /// <summary>True cuando hay credenciales SMTP configuradas.</summary>
    bool EstaConfigurado { get; }

    Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml);
}

public class EmailService : IEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _settings = config.GetSection("Email").Get<EmailSettings>() ?? new EmailSettings();
        _logger = logger;
    }

    public bool EstaConfigurado =>
        !string.IsNullOrWhiteSpace(_settings.Host) &&
        !string.IsNullOrWhiteSpace(_settings.From);

    public async Task EnviarAsync(string destinatario, string asunto, string cuerpoHtml)
    {
        if (!EstaConfigurado)
        {
            // Modo desarrollo: sin SMTP configurado se registra el correo en el log
            // en lugar de enviarlo. El código se muestra en pantalla (ver controlador).
            _logger.LogWarning("SMTP no configurado. Correo NO enviado a {Dest}. Asunto: {Asunto}",
                destinatario, asunto);
            return;
        }

        var mensaje = new MimeMessage();
        mensaje.From.Add(new MailboxAddress(_settings.FromName, _settings.From));
        mensaje.To.Add(MailboxAddress.Parse(destinatario));
        mensaje.Subject = asunto;
        mensaje.Body = new BodyBuilder { HtmlBody = cuerpoHtml }.ToMessageBody();

        using var client = new SmtpClient();
        var opcion = _settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.SslOnConnect;
        await client.ConnectAsync(_settings.Host, _settings.Port, opcion);
        if (!string.IsNullOrWhiteSpace(_settings.User))
            await client.AuthenticateAsync(_settings.User, _settings.Password);
        await client.SendAsync(mensaje);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Correo enviado a {Dest}. Asunto: {Asunto}", destinatario, asunto);
    }
}
