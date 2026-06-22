using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Trader.Application.Broker;

namespace Trader.Infrastructure.Broker;

public sealed partial class KiteInstrumentsClient : IKiteInstrumentsClient
{
    private static readonly TimeZoneInfo IndiaTz = ResolveIndiaTimeZone();

    private readonly HttpClient _http;

    public KiteInstrumentsClient(HttpClient http) => _http = http;
}
