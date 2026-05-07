namespace Trader.Application.Abstractions.Messaging;

/// <summary>
/// MIME part linked from HTML via <c>src="cid:<see cref="ContentId"/></c> using <see cref="System.Net.Mail.LinkedResource"/>.
/// </summary>
public sealed record EmbeddedEmailImage(string ContentId, string MimeType, byte[] Content);
