using Trader.Domain.Enums;

namespace Trader.Application.Broker;

public sealed record OpeningAtmUnderlyingDto(string Key, string Label);

/// <summary>Opening ATM prefs (09:15 IST live MIS + ± GTT / optional trail) for a selectable index underlying.</summary>
public sealed record NiftyOpenAutoTradeSettingsDto(
    bool Enabled,
    string Underlying,
    string OptionSide,
    int MaxLots,
    /// <summary>Preferred expiry (<c>yyyy-MM-dd</c>); null means nearest future at fire/preview time.</summary>
    string? Expiry,
    decimal StopLossPercent,
    decimal TargetPercent,
    bool StopLossEnabled,
    bool TargetEnabled,
    bool TrailEnabled,
    decimal TrailPoints,
    DateOnly? LastSessionDateIst,
    IReadOnlyList<string> AvailableExpiries,
    IReadOnlyList<OpeningAtmUnderlyingDto> AvailableUnderlyings,
    NiftyOpenAutoTradeRunDto? LastRun);

public sealed class NiftyOpenAutoTradeSettingsPutDto
{
    public bool Enabled { get; set; }

    /// <summary>Underlying key: nifty, banknifty, finnifty, midcpnifty, sensex, bankex.</summary>
    public string? Underlying { get; set; }

    /// <summary><c>CE</c> or <c>PE</c> (aliases: call/put).</summary>
    public string? OptionSide { get; set; }

    /// <summary>Lot cap (clamped 1–AbsoluteMaxLots). Entry buys max affordable lots up to this.</summary>
    public int MaxLots { get; set; } = 10;

    /// <summary>
    /// Preferred option expiry as <c>yyyy-MM-dd</c>. Empty/null clears to nearest-future auto.
    /// </summary>
    public string? Expiry { get; set; }

    /// <summary>−ve GTT stop-loss percent of entry premium (e.g. 5 = 5%).</summary>
    public decimal StopLossPercent { get; set; } = 5m;

    /// <summary>+ve GTT target percent of entry premium (e.g. 5 = 5%).</summary>
    public decimal TargetPercent { get; set; } = 5m;

    public bool StopLossEnabled { get; set; } = true;

    public bool TargetEnabled { get; set; } = true;

    /// <summary>
    /// When true, place a single-leg SL GTT and trail it via PUT /gtt/triggers/:id (Kite Connect has no native TSL).
    /// Distance is <see cref="TrailPoints"/> percent below the running premium peak.
    /// </summary>
    public bool TrailEnabled { get; set; }

    /// <summary>Trail distance as percent of premium peak (e.g. 5 = keep SL 5% below peak).</summary>
    public decimal TrailPoints { get; set; } = 5m;
}

public sealed record NiftyOpenAutoTradeRunDto(
    Guid Id,
    DateOnly SessionDateIst,
    string Status,
    string OptionSide,
    string? Exchange,
    string? Tradingsymbol,
    decimal? Strike,
    string? Expiry,
    int Lots,
    int Quantity,
    decimal? OptionLtp,
    decimal? SpotLtp,
    decimal? AvailableBalanceInr,
    string? OrderId,
    string? GttTriggerId,
    string? Message,
    DateTimeOffset CreatedAtUtc);

public sealed record NiftyOpenAutoTradePreviewDto(
    bool CanTrade,
    string? Reason,
    decimal? SpotLtp,
    decimal? AvailableBalanceInr,
    string Underlying,
    string OptionSide,
    string? Exchange,
    string? Tradingsymbol,
    decimal? Strike,
    string? Expiry,
    int Lots,
    int Quantity,
    decimal? OptionLtp,
    decimal EstimatedPremiumInr,
    int MaxLots,
    decimal? StopLossPrice,
    decimal? TargetPrice,
    bool TrailEnabled,
    decimal? TrailPoints);

public static class NiftyOpenAutoTradeOptionSideParser
{
    public static string Normalize(string? raw)
    {
        var s = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return s switch
        {
            "PE" or "PUT" or "P" => "PE",
            _ => "CE",
        };
    }

    public static NiftyOpenAutoTradeOptionSide ToEnum(string? raw) =>
        Normalize(raw) == "PE" ? NiftyOpenAutoTradeOptionSide.Pe : NiftyOpenAutoTradeOptionSide.Ce;

    public static string FromEnum(NiftyOpenAutoTradeOptionSide side) =>
        side == NiftyOpenAutoTradeOptionSide.Pe ? "PE" : "CE";
}
