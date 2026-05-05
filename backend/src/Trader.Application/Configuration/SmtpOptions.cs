namespace Trader.Application.Configuration;

/// <summary>Outbound SMTP (e.g. Gmail with an app password). Disabled by default; enable via env <c>Smtp__IsEnabled=true</c>.</summary>
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
}
