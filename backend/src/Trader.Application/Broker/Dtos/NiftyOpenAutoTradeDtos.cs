using Trader.Domain.Enums;

namespace Trader.Application.Broker;

/// <summary>NIFTY Opening ATM prefs (09:15 IST live MIS + ± GTT).</summary>
public sealed record NiftyOpenAutoTradeSettingsDto(
    bool Enabled,
    string OptionSide,
    int MaxLots,
    /// <summary>Preferred expiry (<c>yyyy-MM-dd</c>); null means nearest future at fire/preview time.</summary>
    string? Expiry,
    decimal StopLossPoints,
    decimal TargetPoints,
    bool StopLossEnabled,
    bool TargetEnabled,
    DateOnly? LastSessionDateIst,
    IReadOnlyList<string> AvailableExpiries,
    NiftyOpenAutoTradeRunDto? LastRun);

public sealed class NiftyOpenAutoTradeSettingsPutDto
{
    public bool Enabled { get; set; }

    /// <summary><c>CE</c> or <c>PE</c> (aliases: call/put).</summary>
    public string? OptionSide { get; set; }

    /// <summary>Lot cap (clamped 1–AbsoluteMaxLots). Entry buys max affordable lots up to this.</summary>
    public int MaxLots { get; set; } = 10;

    /// <summary>
    /// Preferred NIFTY option expiry as <c>yyyy-MM-dd</c>. Empty/null clears to nearest-future auto.
    /// </summary>
    public string? Expiry { get; set; }

    /// <summary>−ve GTT stop-loss points below entry premium.</summary>
    public decimal StopLossPoints { get; set; } = 5m;

    /// <summary>+ve GTT target points above entry premium.</summary>
    public decimal TargetPoints { get; set; } = 5m;

    public bool StopLossEnabled { get; set; } = true;

    public bool TargetEnabled { get; set; } = true;
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
    decimal? TargetPrice);

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
