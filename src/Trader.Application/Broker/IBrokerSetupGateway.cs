namespace Trader.Application.Broker;

/// <summary>
/// Persistence port for broker onboarding (DIP: broker use cases do not depend on IUserRepository).
/// </summary>
public interface IBrokerSetupGateway
{
    Task<BrokerSetupSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken ct);
    Task CompleteBrokerSetupAsync(Guid userId, CancellationToken ct);
    Task PersistKiteSessionAsync(Guid userId, BrokerKitePersistRequest session, CancellationToken ct);
    Task DisconnectBrokerAsync(Guid userId, CancellationToken ct);

    /// <summary>Decrypts the stored Kite access token, or null if missing.</summary>
    Task<string?> GetKiteAccessTokenAsync(Guid userId, CancellationToken ct);
}

public sealed record BrokerSetupSnapshot(Guid UserId, DateTimeOffset? BrokerConnectedAt, string? BrokerProvider);
