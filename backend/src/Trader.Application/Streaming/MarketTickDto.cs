using System.Text.Json.Serialization;

namespace Trader.Application.Streaming;

/// <summary>Compact tick pushed to the SPA (Kite LTP mode–friendly).</summary>
public sealed record MarketTickDto(
    [property: JsonPropertyName("i")] uint InstrumentToken,
    [property: JsonPropertyName("p")] decimal LastPrice,
    [property: JsonPropertyName("v")] uint Volume,
    [property: JsonPropertyName("t")] long? UnixTimestampSeconds);
