using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
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

        var message = BuildPlainMessage(to, subject, body, attachments);
        await SendCoreAsync(message, ct).ConfigureAwait(false);
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

        var message = BuildMultipartMessage(to, subject, plainTextBody, htmlBody, embeddedImages, attachments);
        await SendCoreAsync(message, ct).ConfigureAwait(false);
    }

    private static void ThrowSmtpDisabled() =>
        throw new InvalidOperationException(
            "Email sending is disabled. Enable SMTP: appsettings/`" + SmtpOptions.SectionName + ":IsEnabled` "
            + "or environment **`" + SmtpOptions.SectionName + "__IsEnabled=true`**, plus host/user/password/from.");

    private MimeMessage BuildPlainMessage(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments)
    {
        var message = CreateBaseMessage(to, subject);
        if (attachments.Count == 0)
        {
            message.Body = new TextPart("plain")
            {
                Text = body,
                ContentTransferEncoding = ContentEncoding.Base64,
            };
            return message;
        }

        var builder = new BodyBuilder { TextBody = body };
        AddAttachments(builder, attachments);
        message.Body = builder.ToMessageBody();
        return message;
    }

    private MimeMessage BuildMultipartMessage(
        string to,
        string subject,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages,
        IReadOnlyList<EmailAttachment> attachments)
    {
        var message = CreateBaseMessage(to, subject);
        var builder = new BodyBuilder
        {
            TextBody = plainTextBody ?? string.Empty,
            HtmlBody = htmlBody ?? string.Empty,
        };

        foreach (var img in embeddedImages)
        {
            var linked = builder.LinkedResources.Add("image", img.Content, ContentType.Parse(img.MimeType));
            linked.ContentId = img.ContentId.Trim();
            linked.ContentDisposition = new ContentDisposition(ContentDisposition.Inline);
        }

        AddAttachments(builder, attachments);
        message.Body = builder.ToMessageBody();
        return message;
    }

    private MimeMessage CreateBaseMessage(string to, string subject)
    {
        var (fromEmail, fromName) = ResolveFromAddress();
        var message = new MimeMessage
        {
            Subject = subject,
        };
        message.From.Add(string.IsNullOrWhiteSpace(fromName)
            ? MailboxAddress.Parse(fromEmail)
            : new MailboxAddress(fromName, fromEmail));
        message.To.Add(MailboxAddress.Parse(to.Trim()));
        return message;
    }

    private (string FromEmail, string? FromDisplayName) ResolveFromAddress()
    {
        var fromEmail = (_options.FromEmail ?? _options.User)?.Trim()
            ?? throw new InvalidOperationException("SMTP `" + SmtpOptions.SectionName + ":FromEmail` (or User) must be set.");

        _ = ResolvePassword() ?? throw new InvalidOperationException(
            "SMTP `" + SmtpOptions.SectionName + ":Password` must be set.");

        return (fromEmail, _options.FromDisplayName?.Trim());
    }

    private string? ResolvePassword()
    {
        var raw = _options.Password?.Trim();
        if (string.IsNullOrEmpty(raw))
            return null;

        // Gmail app passwords are often pasted with spaces (e.g. "abcd efgh ijkl mnop").
        return raw.Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static void AddAttachments(BodyBuilder builder, IReadOnlyList<EmailAttachment> attachments)
    {
        foreach (var a in attachments)
            builder.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
    }

    private async Task SendCoreAsync(MimeMessage message, CancellationToken ct)
    {
        var fromEmail = (_options.FromEmail ?? _options.User)!.Trim();
        var password = ResolvePassword()!;
        var user = (_options.User ?? fromEmail).Trim();
        var host = _options.Host.Trim();
        var port = _options.Port;
        var secureSocketOptions = ResolveSecureSocketOptions(port, _options.EnableTls);

        using var client = new SmtpClient();
        try
        {
            ct.ThrowIfCancellationRequested();
            await client.ConnectAsync(host, port, secureSocketOptions, ct).ConfigureAwait(false);
            await client.AuthenticateAsync(user, password, ct).ConfigureAwait(false);
            await client.SendAsync(message, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
            _logger.LogInformation("Email sent to {Recipient} via {Host}:{Port}", message.To.Mailboxes.FirstOrDefault()?.Address, host, port);
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "SMTP authentication failed for user {SmtpUser} on {Host}:{Port}", user, host, port);
            throw new InvalidOperationException(
                "Unable to send email. SMTP authentication failed — use a Gmail App password (not your login password) in Smtp__Password.",
                ex);
        }
        catch (SmtpCommandException ex)
        {
            _logger.LogError(ex, "SMTP command failed: {StatusCode} {Message}", ex.StatusCode, ex.Message);
            throw new InvalidOperationException("Unable to send email. Check SMTP configuration and credentials.", ex);
        }
        catch (SmtpProtocolException ex)
        {
            _logger.LogError(ex, "SMTP protocol error");
            throw new InvalidOperationException("Unable to send email. Check SMTP host, port, and TLS settings.", ex);
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

    private static SecureSocketOptions ResolveSecureSocketOptions(int port, bool enableTls) =>
        port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => enableTls ? SecureSocketOptions.StartTlsWhenAvailable : SecureSocketOptions.None,
        };
}
