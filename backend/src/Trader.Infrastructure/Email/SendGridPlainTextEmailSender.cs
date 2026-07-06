using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Messaging;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Email;

/// <summary>SendGrid v3 mail/send over HTTPS — recommended on hosts that block outbound SMTP.</summary>
public sealed class SendGridPlainTextEmailSender
{
    private readonly HttpClient _http;
    private readonly SmtpOptions _options;
    private readonly ILogger<SendGridPlainTextEmailSender> _logger;

    public SendGridPlainTextEmailSender(
        HttpClient http,
        IOptions<SmtpOptions> options,
        ILogger<SendGridPlainTextEmailSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public Task SendPlainTextAsync(string to, string subject, string body, CancellationToken ct = default) =>
        SendPlainTextAsync(to, subject, body, Array.Empty<EmailAttachment>(), ct);

    public Task SendPlainTextAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default) =>
        SendCoreAsync(to, subject, plainTextBody: body, htmlBody: null, embeddedImages: Array.Empty<EmbeddedEmailImage>(), attachments, ct);

    public Task SendEmailAsync(
        string to,
        string subject,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default) =>
        SendCoreAsync(to, subject, plainTextBody, htmlBody, embeddedImages, attachments, ct);

    private async Task SendCoreAsync(
        string to,
        string subject,
        string? plainTextBody,
        string? htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct)
    {
        var apiKey = _options.SendGridApiKey?.Trim()
            ?? throw new InvalidOperationException("Smtp:SendGridApiKey must be set.");
        var (fromEmail, fromName) = ResolveFromAddress();

        var content = new List<object>();
        if (!string.IsNullOrEmpty(plainTextBody))
            content.Add(new { type = "text/plain", value = plainTextBody });
        if (!string.IsNullOrEmpty(htmlBody))
            content.Add(new { type = "text/html", value = htmlBody });
        if (content.Count == 0)
            content.Add(new { type = "text/plain", value = string.Empty });

        var sgAttachments = BuildAttachments(attachments, embeddedImages);

        var payload = new Dictionary<string, object>
        {
            ["personalizations"] = new[]
            {
                new { to = new[] { new { email = to.Trim() } } },
            },
            ["from"] = string.IsNullOrWhiteSpace(fromName)
                ? new { email = fromEmail }
                : new { email = fromEmail, name = fromName },
            ["subject"] = subject,
            ["content"] = content,
        };
        if (sgAttachments.Count > 0)
            payload["attachments"] = sgAttachments;

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(payload);

        var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError(
                "SendGrid send failed with {StatusCode}: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);
            throw new InvalidOperationException(
                "Unable to send email. Check Smtp__SendGridApiKey and verify the From address in SendGrid.");
        }

        _logger.LogInformation("Email sent to {Recipient} via SendGrid API", to.Trim());
    }

    private static List<object> BuildAttachments(
        IReadOnlyList<EmailAttachment> attachments,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages)
    {
        var list = new List<object>(attachments.Count + embeddedImages.Count);
        foreach (var a in attachments)
        {
            list.Add(new
            {
                content = Convert.ToBase64String(a.Content),
                filename = a.FileName,
                type = a.ContentType,
                disposition = "attachment",
            });
        }

        foreach (var img in embeddedImages)
        {
            list.Add(new
            {
                content = Convert.ToBase64String(img.Content),
                filename = img.ContentId.Trim() + ".png",
                type = img.MimeType,
                disposition = "inline",
                content_id = img.ContentId.Trim(),
            });
        }

        return list;
    }

    private (string FromEmail, string? FromDisplayName) ResolveFromAddress()
    {
        var fromEmail = (_options.FromEmail ?? _options.User)?.Trim()
            ?? throw new InvalidOperationException("SMTP `" + SmtpOptions.SectionName + ":FromEmail` (or User) must be set.");

        return (fromEmail, _options.FromDisplayName?.Trim());
    }
}
