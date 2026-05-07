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

    /// <summary>multipart/alternative: <paramref name="plainTextBody"/> and <paramref name="htmlBody"/> (UTF-8) plus attachments (e.g. CSV).</summary>
    Task SendEmailAsync(
        string to,
        string subject,
        string plainTextBody,
        string htmlBody,
        IReadOnlyList<EmailAttachment> attachments,
        CancellationToken ct = default);
}
