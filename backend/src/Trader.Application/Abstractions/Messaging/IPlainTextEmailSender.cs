namespace Trader.Application.Abstractions.Messaging;

public interface IPlainTextEmailSender
{
    Task SendPlainTextAsync(string to, string subject, string body, CancellationToken ct = default);
}
