namespace Trader.Application.Abstractions.Messaging;

/// <summary>Binary attachment for SMTP multipart messages.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);
