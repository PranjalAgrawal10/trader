using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Email;

public sealed class SmtpPlainTextEmailSender : IPlainTextEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpPlainTextEmailSender> _logger;

    public SmtpPlainTextEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpPlainTextEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendPlainTextAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        if (!_options.IsEnabled)
        {
            throw new InvalidOperationException(
                "Email sending is disabled. Set `" + SmtpOptions.SectionName + ":IsEnabled` and SMTP credentials.");
        }

        var fromEmail = (_options.FromEmail ?? _options.User)?.Trim()
            ?? throw new InvalidOperationException("SMTP `" + SmtpOptions.SectionName + ":FromEmail` (or User) must be set.");
        var password = _options.Password
            ?? throw new InvalidOperationException("SMTP `" + SmtpOptions.SectionName + ":Password` must be set.");

        var from = string.IsNullOrWhiteSpace(_options.FromDisplayName)
            ? new MailAddress(fromEmail)
            : new MailAddress(fromEmail, _options.FromDisplayName);

        using var message = new MailMessage
        {
            From = from,
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        message.To.Add(to.Trim());

        var user = (_options.User ?? fromEmail).Trim();
        using var client = new SmtpClient(_options.Host.Trim(), _options.Port)
        {
            Credentials = new NetworkCredential(user, password),
            EnableSsl = _options.EnableTls,
        };

        try
        {
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(message);
        }
        catch (SmtpException ex)
        {
            _logger.LogError(ex, "SMTP send failed");
            throw new InvalidOperationException("Unable to send email. Check SMTP configuration and credentials.", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email send failed");
            throw new InvalidOperationException("Unable to send email.", ex);
        }
    }
}
