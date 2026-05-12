using Trader.Application.Abstractions.Persistence;

namespace Trader.Application.Wallet;

public sealed class WalletService : IWalletService
{
    public const decimal MaxSingleLoad = 99_999_999.99m;
    public const decimal MaxWalletBalance = 999_999_999.99m;

    private readonly IUserRepository _users;

    public WalletService(IUserRepository users)
    {
        _users = users;
    }

    public async Task<WalletBalanceResponse> GetBalanceAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        return new WalletBalanceResponse(user.WalletBalance);
    }

    public async Task<WalletBalanceResponse> LoadMoneyAsync(Guid userId, decimal amount, CancellationToken ct = default)
    {
        var normalized = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (normalized <= 0)
            throw new InvalidOperationException("Amount must be greater than zero.");

        if (normalized > MaxSingleLoad)
            throw new InvalidOperationException($"Amount cannot exceed {MaxSingleLoad:N2} per top-up.");

        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        try
        {
            checked
            {
                var next = user.WalletBalance + normalized;
                if (next > MaxWalletBalance)
                    throw new InvalidOperationException($"Wallet balance cannot exceed {MaxWalletBalance:N2}.");

                user.WalletBalance = next;
            }
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException($"Wallet balance cannot exceed {MaxWalletBalance:N2}.");
        }

        await _users.SaveChangesAsync(ct).ConfigureAwait(false);
        return new WalletBalanceResponse(user.WalletBalance);
    }
}
