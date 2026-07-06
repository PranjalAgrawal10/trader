using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Email;

/// <summary>Routes outbound email to SendGrid HTTPS, SMTP, or Development console logging.</summary>
public sealed class RoutingPlainTextEmailSender : IPlainTextEmailSender
{
    private readonly SmtpOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly SmtpPlainTextEmailSender _smtp;
    private readonly SendGridPlainTextEmailSender _sendGrid;
    private readonly ILogger<RoutingPlainTextEmailSender> _logger;

    public RoutingPlainTextEmailSender(
        IOptions<SmtpOptions> options,
        IHostEnvironment environment,
        SmtpPlainTextEmailSender smtp,
        SendGridPlainTextEmailSender sendGrid,
        ILogger<RoutingPlainTextEmailSender> logger)
    {
        _options = options.Value;
        _environment = environment;
        _smtp = smtp;
        _sendGrid = sendGrid;
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

        return _options.UsesSendGridApi
            ? _sendGrid.SendPlainTextAsync(to, subject, body, attachments, ct)
            : _smtp.SendPlainTextAsync(to, subject, body, attachments, ct);
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

        return _options.UsesSendGridApi
            ? _sendGrid.SendEmailAsync(to, subject, plainTextBody, htmlBody, embeddedImages, attachments, ct)
            : _smtp.SendEmailAsync(to, subject, plainTextBody, htmlBody, embeddedImages, attachments, ct);
    }

    private bool TryLogDevelopmentOnly(string to, string subject, string body)
    {
        if (_options.HasOutboundProvider || !_environment.IsDevelopment())
            return false;

        _logger.LogWarning(
            "Development email (no Smtp/SendGrid configured) — not sent over the network.\nTo: {Recipient}\nSubject: {Subject}\n{Body}",
            to.Trim(),
            subject,
            body);
        return true;
    }
}
