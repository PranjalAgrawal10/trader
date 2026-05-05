namespace Trader.Application.Exceptions;

/// <summary>Too many requests (maps to HTTP 429).</summary>
public sealed class RateLimitExceededException : Exception
{
    public RateLimitExceededException(string message)
        : base(message)
    {
    }
}
