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

    /// <summary>Decrypts the stored broker access token by broker name, or null if missing.</summary>
    Task<string?> GetBrokerAccessTokenAsync(Guid userId, string brokerName, CancellationToken ct);

    /// <summary>Connected brokers (decryptable token present) for the user.</summary>
    Task<IReadOnlyList<string>> GetConnectedBrokerProvidersAsync(Guid userId, CancellationToken ct);
}

public sealed record BrokerSetupSnapshot(Guid UserId, DateTimeOffset? BrokerConnectedAt, string? BrokerProvider);
