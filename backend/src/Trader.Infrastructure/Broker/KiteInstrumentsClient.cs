using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Broker;

public sealed partial class KiteInstrumentsClient : IKiteInstrumentsClient
{
    private static readonly TimeZoneInfo IndiaTz = ResolveIndiaTimeZone();

    /// <summary>Kite regenerates instrument masters daily; keep parsed rows warm through the session.</summary>
    private static readonly TimeSpan ExchangeMasterCacheTtl = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;

    public KiteInstrumentsClient(HttpClient http, IMemoryCache cache)
    {
        _http = http;
        _cache = cache;
    }
}
