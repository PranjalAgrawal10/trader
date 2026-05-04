namespace Trader.Application.Exceptions;

/// <summary>Signals a conflicting state (maps to HTTP 409).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message)
        : base(message)
    {
    }
}
