namespace Trader.Application.Configuration;

/// <summary>
/// Outbound email: Gmail SMTP (dev) or SendGrid HTTP API (recommended on DigitalOcean App Platform).
/// Enable SMTP via <c>Smtp__IsEnabled=true</c>, or set <c>Smtp__SendGridApiKey</c> (HTTPS — no outbound SMTP ports).
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public bool IsEnabled { get; set; }

    /// <summary>SendGrid v3 API key. When set, email is sent over HTTPS (works when SMTP ports are blocked).</summary>
    public string? SendGridApiKey { get; set; }

    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    /// <summary>Usually true on port 587 (STARTTLS).</summary>
    public bool EnableTls { get; set; } = true;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string? FromEmail { get; set; }

    public string? FromDisplayName { get; set; }

    public bool UsesSendGridApi => !string.IsNullOrWhiteSpace(SendGridApiKey);

    public bool HasOutboundProvider => UsesSendGridApi || IsEnabled;
}
