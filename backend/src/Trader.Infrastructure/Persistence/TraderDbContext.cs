using Microsoft.EntityFrameworkCore;
using Trader.Domain.Entities;
using Trader.Domain.Enums;

namespace Trader.Infrastructure.Persistence;

public sealed class TraderDbContext : DbContext
{
    public TraderDbContext(DbContextOptions<TraderDbContext> options)
        : base(options)
    {
    }

    public DbSet<EmailOtpChallenge> EmailOtpChallenges => Set<EmailOtpChallenge>();

    public DbSet<User> Users => Set<User>();
    public DbSet<BrokerAccount> BrokerAccounts => Set<BrokerAccount>();
    public DbSet<HistoricalCandle> HistoricalCandles => Set<HistoricalCandle>();
    public DbSet<KiteFavoriteInstrument> KiteFavoriteInstruments => Set<KiteFavoriteInstrument>();
    public DbSet<KiteTradingLockInstrument> KiteTradingLockInstruments => Set<KiteTradingLockInstrument>();
    public DbSet<MlPriceDirectionPrediction> MlPriceDirectionPredictions => Set<MlPriceDirectionPrediction>();
    public DbSet<MlLightGbmTripleBarrierPrediction> MlLightGbmTripleBarrierPredictions => Set<MlLightGbmTripleBarrierPrediction>();
    public DbSet<MlFavoriteEodReportSent> MlFavoriteEodReportsSent => Set<MlFavoriteEodReportSent>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<Bot> Bots => Set<Bot>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<TradingOrder> TradingOrders => Set<TradingOrder>();
    public DbSet<DemoPaperPosition> DemoPaperPositions => Set<DemoPaperPosition>();
    public DbSet<DemoPaperBuyLeg> DemoPaperBuyLegs => Set<DemoPaperBuyLeg>();
    public DbSet<DemoPaperTradeLog> DemoPaperTradeLogs => Set<DemoPaperTradeLog>();
    public DbSet<UserLoginAudit> UserLoginAudits => Set<UserLoginAudit>();
    public DbSet<NiftyOpenAutoTradeRun> NiftyOpenAutoTradeRuns => Set<NiftyOpenAutoTradeRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailOtpChallenge>(e =>
        {
            e.Property(x => x.NormalizedEmail).HasMaxLength(320);
            e.Property(x => x.OtpHash).HasMaxLength(200);
            e.HasIndex(x => new { x.NormalizedEmail, x.Purpose, x.IsConsumed });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(64);
            e.Property(x => x.KiteInstrumentsChartInterval).HasMaxLength(16);
            e.Property(x => x.KiteInstrumentsChartRangePreset).HasMaxLength(32);
            e.Property(x => x.KiteInstrumentsChartGraphType).HasMaxLength(16);
            e.Property(x => x.KiteInstrumentsChartZoomJson);
            e.Property(x => x.KiteInstrumentsChartIntervalByInstrumentTokenJson);
            e.Property(x => x.KiteInstrumentsTrendAnalysisIntervalsJson);
            e.Property(x => x.ScalperInterval).HasMaxLength(16);
            e.Property(x => x.ScalperRangePreset).HasMaxLength(32);
            e.Property(x => x.ScalperGraphType).HasMaxLength(16);
            e.Property(x => x.ScalperShowVolume).HasDefaultValue(true);
            e.Property(x => x.ScalperSafeModeEnabled).HasDefaultValue(false);
            e.Property(x => x.ScalperSafeStopLossPoints).HasPrecision(18, 4);
            e.Property(x => x.ScalperSafeTriggerPoints).HasPrecision(18, 4);
            e.Property(x => x.ScalperGttEnabled).HasDefaultValue(true);
            e.Property(x => x.ScalperGttLossEnabled).HasDefaultValue(true);
            e.Property(x => x.ScalperGttProfitEnabled).HasDefaultValue(true);
            e.Property(x => x.WalletBalance).HasPrecision(18, 2).HasDefaultValue(0);
            e.Property(x => x.DemoAutoTradeEnabled).HasDefaultValue(false);
            e.Property(x => x.DemoAutoTradeStrategy).HasMaxLength(32).HasDefaultValue("equal_split");
            e.Property(x => x.NiftyOpenAutoTradeEnabled).HasDefaultValue(false);
            e.Property(x => x.NiftyOpenAutoTradeOptionSide).HasConversion<byte>().HasDefaultValue(NiftyOpenAutoTradeOptionSide.Ce);
            e.Property(x => x.NiftyOpenAutoTradeMaxLots).HasDefaultValue(5);
            e.Property(x => x.FavoriteMlAutomationEnabled).HasDefaultValue(false);
            e.Property(x => x.FavoriteMlAutomationInterval).HasMaxLength(16);
            e.Property(x => x.FavoriteMlAutomationLastNewPassUtc).HasColumnType("datetime(6)");
            e.Property(x => x.FavoriteMlAutomationMinSecondsAfterBarOpen);
            e.Property(x => x.TotpSecretProtected);
            e.Property(x => x.TotpPendingSecretProtected);
            e.Property(x => x.TotpRecoveryCodesProtected);
            e.Property(x => x.SecondFactorMethod).HasConversion<byte>();
            e.Property(x => x.EmailVerificationTokenHash).HasMaxLength(64);
            e.Property(x => x.PasswordResetTokenHash).HasMaxLength(64);
            e.HasIndex(x => x.EmailVerificationTokenHash).IsUnique();
            e.HasIndex(x => x.PasswordResetTokenHash).IsUnique();
        });

        modelBuilder.Entity<BrokerAccount>(e =>
        {
            e.Property(x => x.BrokerName).HasMaxLength(64);
            e.Property(x => x.ApiKey).HasMaxLength(64);
            e.Property(x => x.ExternalUserId).HasMaxLength(64);
            e.Property(x => x.AccessTokenProtected);
            e.Property(x => x.RefreshTokenProtected);
            e.Property(x => x.TokenExpiresAt).HasColumnType("datetime(6)");
            e.Property(x => x.ConnectedAt).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.BrokerName }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.BrokerAccounts).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HistoricalCandle>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.Timeframe).HasMaxLength(16);
            e.Property(x => x.TimestampUtc).HasColumnType("datetime(6)");
            e.Property(x => x.Open).HasPrecision(28, 8);
            e.Property(x => x.High).HasPrecision(28, 8);
            e.Property(x => x.Low).HasPrecision(28, 8);
            e.Property(x => x.Close).HasPrecision(28, 8);
            e.HasIndex(x => new { x.InstrumentToken, x.Timeframe, x.TimestampUtc }).IsUnique();
            e.HasIndex(x => new { x.InstrumentToken, x.TimestampUtc });
        });

        modelBuilder.Entity<KiteFavoriteInstrument>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.Tradingsymbol).HasMaxLength(128);
            e.Property(x => x.Exchange).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.InstrumentType).HasMaxLength(32);
            e.Property(x => x.Segment).HasMaxLength(32);
            e.Property(x => x.Expiry).HasMaxLength(32);
            e.Property(x => x.Strike).HasPrecision(28, 8);
            e.HasIndex(x => new { x.UserId, x.InstrumentToken }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.KiteFavoriteInstruments).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KiteTradingLockInstrument>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.Tradingsymbol).HasMaxLength(128);
            e.Property(x => x.Exchange).HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.InstrumentType).HasMaxLength(32);
            e.Property(x => x.Segment).HasMaxLength(32);
            e.Property(x => x.Expiry).HasMaxLength(32);
            e.Property(x => x.Strike).HasPrecision(28, 8);
            e.HasIndex(x => new { x.UserId, x.InstrumentToken }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.KiteTradingLockInstruments).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DemoPaperPosition>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.UpdatedAtUtc).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.InstrumentToken }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.DemoPaperPositions).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DemoPaperBuyLeg>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.BoughtAtUtc).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.InstrumentToken, x.BoughtAtUtc });
            e.HasOne(x => x.User).WithMany(u => u.DemoPaperBuyLegs).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DemoPaperTradeLog>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.Tradingsymbol).HasMaxLength(128);
            e.Property(x => x.Exchange).HasMaxLength(16);
            e.Property(x => x.Side).HasMaxLength(8);
            e.Property(x => x.LastPrice).HasPrecision(28, 8);
            e.Property(x => x.CashFlowInr).HasPrecision(18, 2);
            e.Property(x => x.WalletBalanceAfter).HasPrecision(18, 2);
            e.Property(x => x.ExecutedAtUtc).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.ExecutedAtUtc });
            e.HasOne(x => x.User).WithMany(u => u.DemoPaperTradeLogs).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserLoginAudit>(e =>
        {
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.Property(x => x.ForwardedFor).HasMaxLength(1024);
            e.Property(x => x.UserAgent).HasMaxLength(512);
            e.Property(x => x.IpInfoJson).HasColumnType("longtext");
            e.Property(x => x.LoggedInAtUtc).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.LoggedInAtUtc });
            e.HasOne(x => x.User).WithMany(u => u.LoginAudits).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MlPriceDirectionPrediction>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.Interval).HasMaxLength(16);
            e.Property(x => x.PredictedAtUtc).HasColumnType("datetime(6)");
            e.Property(x => x.RefBarTimeUtc).HasColumnType("datetime(6)");
            e.Property(x => x.RefClose).HasPrecision(28, 8);
            e.Property(x => x.Direction).HasMaxLength(16);
            e.Property(x => x.ModelId).HasMaxLength(128);
            e.Property(x => x.EngineModelId).HasMaxLength(128);
            e.Property(x => x.Detail).HasColumnType("longtext");
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.NextBarTimeUtc).HasColumnType("datetime(6)");
            e.Property(x => x.NextOpen).HasPrecision(28, 8);
            e.Property(x => x.NextClose).HasPrecision(28, 8);
            e.Property(x => x.LabelThresholdFractionApplied).HasPrecision(28, 10);
            e.Property(x => x.CensorReason).HasMaxLength(32);
            e.Property(x => x.NextBarTimeUtcN3).HasColumnType("datetime(6)");
            e.Property(x => x.NextCloseN3).HasPrecision(28, 8);
            e.Property(x => x.NextBarTimeUtcN5).HasColumnType("datetime(6)");
            e.Property(x => x.NextCloseN5).HasPrecision(28, 8);
            e.HasIndex(x => new { x.UserId, x.InstrumentToken, x.Interval, x.PredictedAtUtc });
            e.HasOne(x => x.User).WithMany(u => u.MlPriceDirectionPredictions).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MlLightGbmTripleBarrierPrediction>(e =>
        {
            e.Property(x => x.InstrumentToken).HasMaxLength(64);
            e.Property(x => x.Interval).HasMaxLength(16);
            e.Property(x => x.PredictedAtUtc).HasColumnType("datetime(6)");
            e.Property(x => x.RefBarTimeUtc).HasColumnType("datetime(6)");
            e.Property(x => x.RefClose).HasPrecision(28, 8);
            e.Property(x => x.Direction).HasMaxLength(16);
            e.Property(x => x.ModelId).HasMaxLength(128);
            e.Property(x => x.EngineModelId).HasMaxLength(128);
            e.Property(x => x.Detail).HasColumnType("longtext");
            e.Property(x => x.Outcome).HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.NextBarTimeUtc).HasColumnType("datetime(6)");
            e.Property(x => x.NextOpen).HasPrecision(28, 8);
            e.Property(x => x.NextClose).HasPrecision(28, 8);
            e.Property(x => x.LabelThresholdFractionApplied).HasPrecision(28, 10);
            e.Property(x => x.CensorReason).HasMaxLength(32);
            e.Property(x => x.NextBarTimeUtcN3).HasColumnType("datetime(6)");
            e.Property(x => x.NextCloseN3).HasPrecision(28, 8);
            e.Property(x => x.NextBarTimeUtcN5).HasColumnType("datetime(6)");
            e.Property(x => x.NextCloseN5).HasPrecision(28, 8);
            e.HasIndex(x => new { x.UserId, x.InstrumentToken, x.Interval, x.PredictedAtUtc });
            e.HasOne(x => x.User).WithMany(u => u.MlLightGbmTripleBarrierPredictions).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MlFavoriteEodReportSent>(e =>
        {
            e.Property(x => x.ReportDayYmd).HasMaxLength(10);
            e.Property(x => x.SentAtUtc).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.ReportDayYmd }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.MlFavoriteEodReportsSent).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Strategy>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasOne(x => x.User).WithMany(u => u.Strategies).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bot>(e =>
        {
            e.HasOne(x => x.User).WithMany(u => u.Bots).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Strategy).WithMany(s => s.Bots).HasForeignKey(x => x.StrategyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Trade>(e =>
        {
            e.Property(x => x.Symbol).HasMaxLength(64);
            e.Property(x => x.Quantity).HasPrecision(28, 8);
            e.Property(x => x.Price).HasPrecision(28, 8);
            e.Property(x => x.RealizedPnl).HasPrecision(28, 8);
            e.HasOne(x => x.Bot).WithMany(b => b.Trades).HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TradingOrder>(e =>
        {
            e.Property(x => x.ExternalId).HasMaxLength(128);
            e.HasOne(x => x.Bot).WithMany(b => b.Orders).HasForeignKey(x => x.BotId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NiftyOpenAutoTradeRun>(e =>
        {
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.OptionSide).HasMaxLength(8);
            e.Property(x => x.Exchange).HasMaxLength(16);
            e.Property(x => x.Tradingsymbol).HasMaxLength(64);
            e.Property(x => x.Expiry).HasMaxLength(32);
            e.Property(x => x.Strike).HasPrecision(18, 4);
            e.Property(x => x.OptionLtp).HasPrecision(18, 4);
            e.Property(x => x.SpotLtp).HasPrecision(18, 4);
            e.Property(x => x.AvailableBalanceInr).HasPrecision(18, 2);
            e.Property(x => x.OrderId).HasMaxLength(64);
            e.Property(x => x.GttTriggerId).HasMaxLength(64);
            e.Property(x => x.TrailPeakPrice).HasPrecision(18, 4);
            e.Property(x => x.TrailStopPrice).HasPrecision(18, 4);
            e.Property(x => x.TrailPoints).HasPrecision(18, 4);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime(6)");
            e.HasIndex(x => new { x.UserId, x.SessionDateIst });
            e.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            e.HasIndex(x => x.TrailActive);
            e.HasOne(x => x.User).WithMany(u => u.NiftyOpenAutoTradeRuns).HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
