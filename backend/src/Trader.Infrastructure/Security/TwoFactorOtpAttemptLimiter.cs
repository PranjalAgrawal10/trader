using Trader.Application.Abstractions.Security;

namespace Trader.Infrastructure.Security;

/// <summary>
/// No-op implementation: the application no longer rate-limits failed 2FA attempts.
/// The interface is kept so callers in <c>AuthService</c> remain unchanged; reintroduce a
/// concrete bucket implementation here if a brute-force lockout is wanted again.
/// </summary>
public sealed class TwoFactorOtpAttemptLimiter : ITwoFactorOtpAttemptLimiter
{
    public void EnsureNotBlocked(string scope)
    {
    }

    public void RegisterFailure(string scope)
    {
    }

    public void Reset(string scope)
    {
    }
}
