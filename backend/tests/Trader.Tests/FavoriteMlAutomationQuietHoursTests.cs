using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class FavoriteMlAutomationQuietHoursTests
{
    private static FavoriteMlAutomationOptions DefaultOpts(bool enabled = true) =>
        new()
        {
            QuietHoursEnabled = enabled,
            QuietHoursStartLocalHour = 23,
            QuietHoursStartLocalMinute = 20,
            QuietHoursEndLocalHour = 5,
            QuietHoursEndLocalMinute = 0,
            ReportTimeZoneId = "Asia/Kolkata",
        };

    /** IST 2025-06-01 23:19 = UTC 2025-06-01 17:49 — before nightly stop. */
    [Fact]
    public void IST_2319_NotPaused()
    {
        var utc = new DateTime(2025, 6, 1, 17, 49, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-01 23:20 = UTC 2025-06-01 17:50 — stop inclusive. */
    [Fact]
    public void IST_2320_Paused()
    {
        var utc = new DateTime(2025, 6, 1, 17, 50, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-02 04:59 = UTC 2025-06-01 23:29 — overnight pause. */
    [Fact]
    public void IST_0459StillPaused()
    {
        var utc = new DateTime(2025, 6, 1, 23, 29, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-02 05:00 = UTC 2025-06-01 23:30 — resume exclusive. */
    [Fact]
    public void IST_0500_NotPaused()
    {
        var utc = new DateTime(2025, 6, 1, 23, 30, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** Midday IST 2025-06-02 14:00 = UTC 2025-06-02 08:30. */
    [Fact]
    public void IST_afternoon_NotPaused()
    {
        var utc = new DateTime(2025, 6, 2, 8, 30, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    [Fact]
    public void QuietHoursDisabled_NeverPausedEvenAtNight()
    {
        var utc = new DateTime(2025, 6, 1, 17, 50, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(enabled: false), utc));
    }
}
