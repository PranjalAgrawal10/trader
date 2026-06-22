using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Trader.Application.Abstractions.Persistence;
using Trader.Application.Configuration;
using Trader.Application.Wallet;
using Trader.Domain.Entities;


namespace Trader.Application.Broker;

public sealed partial class BrokerService
{
    public async Task<IReadOnlyList<DemoPaperPositionListItemDto>> GetDemoPaperPositionsAsync(
        Guid userId,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var positions = await _demoPaperPositions.ListByUserAsync(userId, ct).ConfigureAwait(false);
        if (positions.Count == 0)
            return Array.Empty<DemoPaperPositionListItemDto>();

        var locks = await _kiteTradingLocks.ListByUserAsync(userId, ct).ConfigureAwait(false);
        var byTok = locks.ToDictionary(x => x.InstrumentToken, StringComparer.Ordinal);
        var legs = await _demoPaperBuyLegs.ListOpenByUserAsync(userId, ct).ConfigureAwait(false);
        var openBuysByToken = legs
            .GroupBy(l => l.InstrumentToken, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g =>
                    (IReadOnlyList<DemoPaperOpenBuyMarkerDto>)g.OrderBy(l => l.BoughtAtUtc)
                        .Select(l => new DemoPaperOpenBuyMarkerDto(l.BoughtAtUtc, l.ContractsRemaining))
                        .ToList(),
                StringComparer.Ordinal);

        var positionTokens = positions.Select(x => x.InstrumentToken).Distinct(StringComparer.Ordinal).ToArray();
        var lastBuyByToken = await _demoPaperTradeLogs
            .GetLatestBuyLastPriceByInstrumentTokensAsync(userId, positionTokens, ct)
            .ConfigureAwait(false);

        var list = new List<DemoPaperPositionListItemDto>(positions.Count);
        foreach (var p in positions)
        {
            byTok.TryGetValue(p.InstrumentToken, out var lk);
            openBuysByToken.TryGetValue(p.InstrumentToken, out var openBuys);
            decimal? lastBuyPrice = null;
            if (p.OpenContracts > 0 && lastBuyByToken.TryGetValue(p.InstrumentToken, out var lastBuyPx))
                lastBuyPrice = lastBuyPx;
            list.Add(
                new DemoPaperPositionListItemDto(
                    p.InstrumentToken,
                    lk?.Tradingsymbol ?? p.InstrumentToken,
                    lk?.Exchange ?? "—",
                    lk?.LotSize,
                    p.OpenContracts,
                    openBuys ?? Array.Empty<DemoPaperOpenBuyMarkerDto>(),
                    lastBuyPrice));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DemoPaperTradeHistoryRowDto>> GetDemoPaperTradeHistoryAsync(
        Guid userId,
        int? take = null,
        CancellationToken ct = default)
    {
        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);
        var n = Math.Clamp(take ?? 500, 1, 2000);
        var rows = await _demoPaperTradeLogs.ListRecentByUserAsync(userId, n, ct).ConfigureAwait(false);
        var list = new List<DemoPaperTradeHistoryRowDto>(rows.Count);
        foreach (var r in rows)
        {
            list.Add(
                new DemoPaperTradeHistoryRowDto(
                    r.Id,
                    r.ExecutedAtUtc,
                    r.InstrumentToken,
                    r.Tradingsymbol,
                    r.Exchange,
                    r.Side,
                    r.Contracts,
                    r.LastPrice,
                    r.LotSize,
                    r.CashFlowInr,
                    r.WalletBalanceAfter,
                    r.OpenContractsAfter));
        }

        return list;
    }

    /// <inheritdoc />
    public async Task<DemoPaperTradeResultDto> ExecuteDemoPaperTradeAsync(
        Guid userId,
        DemoPaperTradeRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var token = (request.InstrumentToken ?? string.Empty).Trim();
        if (token.Length == 0 || !token.All(char.IsAsciiDigit))
            throw new InvalidOperationException("A numeric instrument token is required.");

        var side = (request.Side ?? string.Empty).Trim().ToLowerInvariant();
        if (side is not ("buy" or "sell"))
            throw new InvalidOperationException("Side must be buy or sell.");

        if (request.Contracts < 1 || request.Contracts > 1_000_000)
            throw new InvalidOperationException("Lots must be between 1 and 1,000,000.");

        await RequireUserExistsAsync(userId, ct).ConfigureAwait(false);

        var lockRow = await _kiteTradingLocks.FindAsync(userId, token, ct).ConfigureAwait(false);
        if (lockRow is null)
            throw new InvalidOperationException("Instrument must be in Locked for trading to paper trade.");

        var (apiKey, accessToken) = await RequireKiteInstrumentSessionAsync(userId, ct).ConfigureAwait(false);
        var instrumentRowFetch = await _kiteInstruments
            .FetchInstrumentRowByTokenAsync(lockRow.Exchange, token, apiKey, accessToken, ct)
            .ConfigureAwait(false);

        int lotMult;
        if (instrumentRowFetch.Success
            && instrumentRowFetch.Items.Count == 1
            && instrumentRowFetch.Items[0].LotSize is int kiteLot
            && kiteLot >= 1)
        {
            lotMult = kiteLot;
            if (lockRow.LotSize != lotMult)
                lockRow.LotSize = lotMult;
        }
        else if (lockRow.LotSize is int lockedLot && lockedLot >= 1)
        {
            lotMult = lockedLot;
        }
        else
        {
            var hint = instrumentRowFetch.ErrorMessage ?? "instrument row was not returned.";
            throw new InvalidOperationException(
                $"Could not resolve exchange lot size ({hint}). Remove the lock and add the symbol again from search.");
        }

        var lots = request.Contracts;

        var quote = await GetKiteInstrumentLiveQuoteAsync(userId, lockRow.Exchange, lockRow.Tradingsymbol, ct)
            .ConfigureAwait(false);

        var ltp = quote.LastPrice;
        if (ltp <= 0)
            throw new InvalidOperationException("Could not read a positive last price for this instrument.");

        var legCash = decimal.Round(
            decimal.Round(ltp * lotMult, 2, MidpointRounding.AwayFromZero) * lots,
            2,
            MidpointRounding.AwayFromZero);

        var user = await _users.GetByIdAsync(userId, ct).ConfigureAwait(false);
        if (user is null)
            throw new InvalidOperationException("User not found.");

        DemoPaperPosition? pos = await _demoPaperPositions.FindByUserAndTokenAsync(userId, token, ct)
            .ConfigureAwait(false);

        decimal cashFlow;
        int openAfter;

        if (side == "buy")
        {
            if (user.WalletBalance < legCash)
                throw new InvalidOperationException("Insufficient wallet balance for this paper buy.");

            user.WalletBalance = decimal.Round(user.WalletBalance - legCash, 2, MidpointRounding.AwayFromZero);
            cashFlow = -legCash;

            if (pos is null)
            {
                pos = new DemoPaperPosition
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstrumentToken = token,
                    OpenContracts = 0,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                _demoPaperPositions.Add(pos);
            }

            pos.OpenContracts += lots;
            pos.UpdatedAtUtc = DateTimeOffset.UtcNow;
            openAfter = pos.OpenContracts;

            _demoPaperBuyLegs.Add(
                new DemoPaperBuyLeg
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    InstrumentToken = token,
                    ContractsRemaining = lots,
                    BoughtAtUtc = DateTimeOffset.UtcNow,
                });
        }
        else
        {
            if (pos is null || pos.OpenContracts < lots)
                throw new InvalidOperationException("Not enough open lots to sell.");

            await _demoPaperBuyLegs.ApplyFifoSellAsync(userId, token, lots, ct).ConfigureAwait(false);

            pos.OpenContracts -= lots;
            pos.UpdatedAtUtc = DateTimeOffset.UtcNow;
            openAfter = pos.OpenContracts;

            var nextBal = user.WalletBalance + legCash;
            if (nextBal > WalletService.MaxWalletBalance)
                throw new InvalidOperationException($"Wallet balance cannot exceed {WalletService.MaxWalletBalance:N2}.");

            user.WalletBalance = decimal.Round(nextBal, 2, MidpointRounding.AwayFromZero);
            cashFlow = legCash;
        }

        var executedAt = DateTimeOffset.UtcNow;
        _demoPaperTradeLogs.Add(
            new DemoPaperTradeLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                InstrumentToken = token,
                Tradingsymbol = lockRow.Tradingsymbol,
                Exchange = lockRow.Exchange,
                Side = side,
                Contracts = lots,
                LastPrice = ltp,
                LotSize = lotMult,
                CashFlowInr = cashFlow,
                WalletBalanceAfter = user.WalletBalance,
                OpenContractsAfter = openAfter,
                ExecutedAtUtc = executedAt,
            });

        await _users.SaveChangesAsync(ct).ConfigureAwait(false);

        return new DemoPaperTradeResultDto(
            token,
            lockRow.Tradingsymbol,
            lockRow.Exchange,
            side,
            lots,
            ltp,
            lotMult,
            cashFlow,
            user.WalletBalance,
            openAfter);
    }
}
