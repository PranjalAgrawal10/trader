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
}
