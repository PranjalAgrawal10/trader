using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
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

    public Task SendPlainTextAsync(string to, string subject, string body, CancellationToken ct = default) =>
        SendPlainTextAsync(to, subject, body, Array.Empty<EmailAttachment>(), ct);

    public async Task SendPlainTextAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default)
    {
        if (!_options.IsEnabled)
            ThrowSmtpDisabled();

        using var message = PlainMessage(to, subject, body, attachments);
        await SendCoreAsync(ct, message).ConfigureAwait(false);
    }

    public async Task SendEmailAsync(
        string to,
        string subject,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default)
    {
        if (!_options.IsEnabled)
            ThrowSmtpDisabled();

        var from = ResolveFromAddress();
        var trimmedTo = to.Trim();

        using var message = new MailMessage
        {
            From = from,
            Subject = subject,
            IsBodyHtml = false,
        };
        message.To.Add(trimmedTo);

        AddAlternateViews(message, plainTextBody, htmlBody, embeddedImages);
        AttachFiles(message, attachments);

        await SendCoreAsync(ct, message).ConfigureAwait(false);
    }

    private static void ThrowSmtpDisabled() =>
        throw new InvalidOperationException(
            "Email sending is disabled. Enable SMTP: appsettings/`" + SmtpOptions.SectionName + ":IsEnabled` "
            + "or environment **`" + SmtpOptions.SectionName + "__IsEnabled=true`**, plus host/user/password/from.");

    private MailMessage PlainMessage(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments)
    {
        var from = ResolveFromAddress();
        var message = new MailMessage
        {
            From = from,
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        message.To.Add(to.Trim());

        AttachFiles(message, attachments);
        return message;
    }

    private MailAddress ResolveFromAddress()
    {
        var fromEmail = (_options.FromEmail ?? _options.User)?.Trim()
            ?? throw new InvalidOperationException("SMTP `" + SmtpOptions.SectionName + ":FromEmail` (or User) must be set.");

        _ = _options.Password ?? throw new InvalidOperationException(
            "SMTP `" + SmtpOptions.SectionName + ":Password` must be set.");

        return string.IsNullOrWhiteSpace(_options.FromDisplayName)
            ? new MailAddress(fromEmail)
            : new MailAddress(fromEmail, _options.FromDisplayName);
    }

    private static void AttachFiles(MailMessage message, IReadOnlyList<EmailAttachment> attachments)
    {
        foreach (var a in attachments)
        {
            var stream = new MemoryStream(a.Content, writable: false);
            message.Attachments.Add(new Attachment(stream, a.FileName, a.ContentType));
        }
    }

    private static void AddAlternateViews(
        MailMessage message,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages)
    {
        var plainView = AlternateView.CreateAlternateViewFromString(
            plainTextBody ?? string.Empty,
            Encoding.UTF8,
            MediaTypeNames.Text.Plain);
        plainView.TransferEncoding = TransferEncoding.Base64;

        var htmlView = AlternateView.CreateAlternateViewFromString(
            htmlBody ?? string.Empty,
            Encoding.UTF8,
            MediaTypeNames.Text.Html);
        htmlView.TransferEncoding = TransferEncoding.Base64;

        foreach (var img in embeddedImages)
        {
            var stream = new MemoryStream(img.Content, writable: false);
            var linked = new LinkedResource(stream, new ContentType(img.MimeType))
            {
                ContentId = img.ContentId.Trim(),
                TransferEncoding = TransferEncoding.Base64,
            };
            htmlView.LinkedResources.Add(linked);
        }

        message.AlternateViews.Add(plainView);
        message.AlternateViews.Add(htmlView);
    }

    private async Task SendCoreAsync(CancellationToken ct, MailMessage message)
    {
        var fromEmail = (_options.FromEmail ?? _options.User)!.Trim();
        var password = _options.Password!;
        var user = (_options.User ?? fromEmail).Trim();

        using var client = new SmtpClient(_options.Host.Trim(), _options.Port)
        {
            Credentials = new NetworkCredential(user, password),
            EnableSsl = _options.EnableTls,
        };

        try
        {
            ct.ThrowIfCancellationRequested();
            await client.SendMailAsync(message, ct).ConfigureAwait(false);
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
