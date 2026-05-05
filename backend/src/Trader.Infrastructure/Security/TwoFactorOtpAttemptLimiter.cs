using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Security;
using Trader.Application.Configuration;

namespace Trader.Infrastructure.Security;

public sealed class TwoFactorOtpAttemptLimiter : ITwoFactorOtpAttemptLimiter
{
    private sealed class Bucket
    {
        public readonly object Sync = new();
        public int FailureCount;
        public DateTimeOffset? BlockedUntilUtc;
    }

    private readonly ConcurrentDictionary<string, Bucket> _scopes = new();
    private readonly int _maxFailures;
    private readonly TimeSpan _lockoutDuration;

    public TwoFactorOtpAttemptLimiter(IOptions<AuthOptions> auth)
    {
        var max = auth.Value.MaxFailedTotpAttemptsPerScope;
        _maxFailures = Math.Clamp(max < 1 ? 5 : max, 1, 50);

        var minutes = auth.Value.TotpAttemptLockoutMinutes;
        minutes = Math.Clamp(minutes < 1 ? 15 : minutes, 1, 1440);
        _lockoutDuration = TimeSpan.FromMinutes(minutes);
    }

    public void EnsureNotBlocked(string scope)
    {
        if (!_scopes.TryGetValue(scope, out var bucket))
            return;

        lock (bucket.Sync)
        {
            if (!IsBlockedUtc(bucket.BlockedUntilUtc))
                return;
        }

        throw new InvalidOperationException(
            $"Too many failed attempts. Wait about {(int)Math.Ceiling(_lockoutDuration.TotalMinutes)} minute(s), then try again.");
    }

    public void RegisterFailure(string scope)
    {
        var bucket = _scopes.GetOrAdd(scope, static _ => new Bucket());
        lock (bucket.Sync)
        {
            if (IsBlockedUtc(bucket.BlockedUntilUtc))
                return;

            bucket.FailureCount++;
            if (bucket.FailureCount >= _maxFailures)
            {
                bucket.BlockedUntilUtc = DateTimeOffset.UtcNow.Add(_lockoutDuration);
                bucket.FailureCount = 0;
            }
        }
    }

    public void Reset(string scope) => _scopes.TryRemove(scope, out _);

    private bool IsBlockedUtc(DateTimeOffset? blockedUntilUtc) =>
        blockedUntilUtc is { } until && DateTimeOffset.UtcNow < until;
}
