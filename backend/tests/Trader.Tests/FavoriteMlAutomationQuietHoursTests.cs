using Trader.Application.Configuration;
using Trader.Application.Prediction;

namespace Trader.Tests;

public sealed class FavoriteMlAutomationQuietHoursTests
{
    private static FavoriteMlAutomationOptions DefaultOpts(bool quietHours = true, bool weekends = true) =>
        new()
        {
            QuietHoursEnabled = quietHours,
            PauseAutomationOnWeekends = weekends,
            QuietHoursStartLocalHour = 23,
            QuietHoursStartLocalMinute = 25,
            QuietHoursEndLocalHour = 8,
            QuietHoursEndLocalMinute = 0,
            ReportTimeZoneId = "Asia/Kolkata",
        };

    /** IST 2025-06-02 (Mon) 23:24 = UTC 2025-06-02 17:54 — before nightly stop. */
    [Fact]
    public void IST_2324_Monday_NotPaused()
    {
        var utc = new DateTime(2025, 6, 2, 17, 54, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-02 (Mon) 23:25 = UTC 2025-06-02 17:55 — stop inclusive. */
    [Fact]
    public void IST_2325_Monday_Paused()
    {
        var utc = new DateTime(2025, 6, 2, 17, 55, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-03 (Tue) 04:59 = UTC 2025-06-02 23:29 — overnight pause. */
    [Fact]
    public void IST_0459_TuesdayStillPaused()
    {
        var utc = new DateTime(2025, 6, 2, 23, 29, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-03 (Tue) 08:00 = UTC 2025-06-03 02:30 — resume exclusive. */
    [Fact]
    public void IST_0800_Tuesday_NotPaused()
    {
        var utc = new DateTime(2025, 6, 3, 2, 30, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-02 (Mon) 07:59 = UTC 2025-06-02 02:29 — still before 08:00 resume. */
    [Fact]
    public void IST_0759_Monday_Paused()
    {
        var utc = new DateTime(2025, 6, 2, 2, 29, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** Midday IST 2025-06-02 (Mon) 14:00 = UTC 2025-06-02 08:30. */
    [Fact]
    public void IST_afternoon_Monday_NotPaused()
    {
        var utc = new DateTime(2025, 6, 2, 8, 30, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** IST 2025-06-07 (Sat) 14:00 = UTC 2025-06-07 08:30 — weekend all-day pause. */
    [Fact]
    public void IST_Saturday_afternoon_Paused()
    {
        var utc = new DateTime(2025, 6, 7, 8, 30, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(), utc));
    }

    /** Weekend pause off: Saturday midday still active if not in nightly window. */
    [Fact]
    public void Saturday_WeekendPauseDisabled_NotPausedAtNoon()
    {
        var utc = new DateTime(2025, 6, 7, 8, 30, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(weekends: false), utc));
    }

    [Fact]
    public void QuietHoursDisabled_WeekendsOn_StillPausedSaturday()
    {
        var utc = new DateTime(2025, 6, 7, 8, 30, 0, DateTimeKind.Utc);
        Assert.True(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(quietHours: false, weekends: true), utc));
    }

    [Fact]
    public void QuietHoursDisabled_WeekendsOff_NotPausedSaturday()
    {
        var utc = new DateTime(2025, 6, 7, 8, 30, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(quietHours: false, weekends: false), utc));
    }

    [Fact]
    public void QuietHoursDisabled_NeverPausedEvenAtNight_ExceptWeekends()
    {
        var utc = new DateTime(2025, 6, 2, 17, 55, 0, DateTimeKind.Utc);
        Assert.False(FavoriteMlAutomationQuietHours.IsAutomationPaused(DefaultOpts(quietHours: false, weekends: false), utc));
    }
}
