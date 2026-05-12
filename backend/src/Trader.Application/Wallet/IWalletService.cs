namespace Trader.Application.Wallet;

public interface IWalletService
{
    Task<WalletBalanceResponse> GetBalanceAsync(Guid userId, CancellationToken ct = default);

    Task<WalletBalanceResponse> LoadMoneyAsync(Guid userId, decimal amount, CancellationToken ct = default);
}
