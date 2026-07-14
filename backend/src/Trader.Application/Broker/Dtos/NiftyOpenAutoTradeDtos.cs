using Trader.Domain.Enums;

namespace Trader.Application.Broker;

public sealed record NiftyOpenAutoTradeSettingsDto(
    bool Enabled,
    string OptionSide,
    int MaxLots,
    DateOnly? LastSessionDateIst,
    NiftyOpenAutoTradeRunDto? LastRun);

public sealed class NiftyOpenAutoTradeSettingsPutDto
{
    public bool Enabled { get; set; }

    /// <summary><c>CE</c> or <c>PE</c> (aliases: call/put).</summary>
    public string? OptionSide { get; set; }

    /// <summary>Hard cap on lots (clamped 1–10; default 5).</summary>
    public int MaxLots { get; set; } = 5;
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
    int MaxLots);

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
