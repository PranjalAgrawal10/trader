namespace Trader.Application.Abstractions.Security;

/// <summary>Generate and verify one-time recovery codes (hashed at rest, encrypted payload in persistence).</summary>
public interface ITwoFactorRecoveryCodesHelper
{
    /// <summary>Produces human-readable codes and an encrypted persistence blob.</summary>
    (IReadOnlyList<string> PlaintextCodes, string ProtectedPayload) IssueNewProtectedPayload();

    /// <summary>
    /// If <paramref name="enteredCode"/> matches a remaining code, returns <c>true</c> with an updated encrypted payload,
    /// or <c>null</c> when the last code was consumed. Returns <c>false</c> when no match or payload is invalid.
    /// </summary>
    bool TryConsumeOne(string? storedProtectedPayload, string enteredCode, out string? updatedProtectedPayloadOrNullWhenCleared);
}
