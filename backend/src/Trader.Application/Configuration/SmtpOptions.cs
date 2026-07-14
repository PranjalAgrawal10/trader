namespace Trader.Application.Configuration;

/// <summary>
/// Outbound email via SMTP (e.g. Gmail with an App password).
/// Enable with <c>Smtp__IsEnabled=true</c> plus host/user/password/from.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public bool IsEnabled { get; set; }

    public string Host { get; set; } = "smtp.gmail.com";

    public int Port { get; set; } = 587;

    /// <summary>Usually true on port 587 (STARTTLS).</summary>
    public bool EnableTls { get; set; } = true;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string? FromEmail { get; set; }

    public string? FromDisplayName { get; set; }

    public bool HasOutboundProvider => IsEnabled;
}
