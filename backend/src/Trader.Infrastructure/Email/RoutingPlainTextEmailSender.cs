using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Email;

/// <summary>Routes outbound email to SMTP, or Development console logging when SMTP is off.</summary>
public sealed class RoutingPlainTextEmailSender : IPlainTextEmailSender
{
    private readonly SmtpOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly SmtpPlainTextEmailSender _smtp;
    private readonly ILogger<RoutingPlainTextEmailSender> _logger;

    public RoutingPlainTextEmailSender(
        IOptions<SmtpOptions> options,
        IHostEnvironment environment,
        SmtpPlainTextEmailSender smtp,
        ILogger<RoutingPlainTextEmailSender> logger)
    {
        _options = options.Value;
        _environment = environment;
        _smtp = smtp;
        _logger = logger;
    }

    public Task SendPlainTextAsync(string to, string subject, string body, CancellationToken ct = default) =>
        SendPlainTextAsync(to, subject, body, Array.Empty<EmailAttachment>(), ct);

    public Task SendPlainTextAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default)
    {
        if (TryLogDevelopmentOnly(to, subject, body))
            return Task.CompletedTask;

        return _smtp.SendPlainTextAsync(to, subject, body, attachments, ct);
    }

    public Task SendEmailAsync(
        string to,
        string subject,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default)
    {
        if (TryLogDevelopmentOnly(to, subject, plainTextBody))
            return Task.CompletedTask;

        return _smtp.SendEmailAsync(to, subject, plainTextBody, htmlBody, embeddedImages, attachments, ct);
    }

    private bool TryLogDevelopmentOnly(string to, string subject, string body)
    {
        if (_options.HasOutboundProvider || !_environment.IsDevelopment())
            return false;

        _logger.LogWarning(
            "Development email (SMTP not enabled) — not sent over the network.\nTo: {Recipient}\nSubject: {Subject}\n{Body}",
            to.Trim(),
            subject,
            body);
        return true;
    }
}
