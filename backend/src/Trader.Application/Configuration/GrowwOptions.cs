namespace Trader.Application.Configuration;

public sealed class GrowwOptions
{
    public const string SectionName = "Groww";

    public string ApiBaseUrl { get; set; } = "https://api.groww.in/v1/";
}
