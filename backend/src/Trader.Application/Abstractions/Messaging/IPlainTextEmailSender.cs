namespace Trader.Application.Abstractions.Messaging;

public interface IPlainTextEmailSender
{
    Task SendPlainTextAsync(string to, string subject, string body, CancellationToken ct = default);

    Task SendPlainTextAsync(
        string to,
        string subject,
        string body,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default);

    /// <summary>
    /// multipart/alternative: plain + HTML (UTF-8).
    /// Inline images: add <c>&lt;img src="cid:<paramref name="embeddedImages"/>.ContentId"&gt;</c> matching <see cref="EmbeddedEmailImage"/>.
    /// </summary>
    Task SendEmailAsync(
        string to,
        string subject,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmbeddedEmailImage> embeddedImages,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default);
}
