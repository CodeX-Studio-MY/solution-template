using System.ComponentModel.DataAnnotations;

namespace Acme.Core.Configuration;

[OptionsSection("Smtp", "Outgoing email from the Notification module (Mailpit locally).")]
public sealed record SmtpOptions
{
    [Required]
    [Setting("SMTP server host.", Example = "mailpit")]
    public string Host { get; init; } = "";

    [Range(1, 65535)]
    [Setting("SMTP server port.")]
    public int Port { get; init; } = 1025;

    [Setting("Use STARTTLS. Off for Mailpit, on for real servers.")]
    public bool UseTls { get; init; }

    [Setting("SMTP user name. Leave empty if the server needs none.")]
    public string? User { get; init; }

    [Secret]
    [Setting("SMTP password, from the secret store.")]
    public string? Password { get; init; }

    [Required, EmailAddress]
    [Setting("The From address of platform emails.", Example = "no-reply@example.com")]
    public string FromAddress { get; init; } = "";

    [Setting("The From name of platform emails.")]
    public string FromName { get; init; } = "Notifications";
}
