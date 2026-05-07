namespace Trader.Domain.Entities;

/// <summary>One row per user per calendar report day (timezone from automation options) after EOD email was sent.</summary>
public class MlFavoriteEodReportSent
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>Calendar date in the report timezone, <c>yyyy-MM-dd</c>.</summary>
    public string ReportDayYmd { get; set; } = string.Empty;

    public DateTimeOffset SentAtUtc { get; set; }
}
