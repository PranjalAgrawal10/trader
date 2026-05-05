namespace Trader.Application.Abstractions.Security;

/// <summary>Limits repeated failed TOTP / recovery verification per scope (e.g. login ticket hash, user id).</summary>
public interface ITwoFactorOtpAttemptLimiter
{
    void EnsureNotBlocked(string scope);

    /// <summary>Register a failed attempt; may block further attempts for this scope.</summary>
    void RegisterFailure(string scope);

    void Reset(string scope);
}
