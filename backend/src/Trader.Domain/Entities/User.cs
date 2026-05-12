using Trader.Domain.Enums;

namespace Trader.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Trader.Domain.Enums.UserRole.Trader;
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When non-null, the user verified their email (registration flow).</summary>
    public DateTimeOffset? EmailVerifiedAtUtc { get; set; }

    /// <summary>SHA-256 (64 hex, upper) over the raw verification token; nullable when not pending.</summary>
    public string? EmailVerificationTokenHash { get; set; }

    public DateTimeOffset? EmailVerificationExpiresAtUtc { get; set; }

    /// <summary>SHA-256 hex over raw password-reset token; nullable when not pending.</summary>
    public string? PasswordResetTokenHash { get; set; }

    public DateTimeOffset? PasswordResetExpiresAtUtc { get; set; }

    /// <summary>Saved Kite instruments page chart candle interval (e.g. <c>5m</c>).</summary>
    public string? KiteInstrumentsChartInterval { get; set; }

    /// <summary>Saved chart range preset (e.g. <c>auto</c>, <c>last1d</c>).</summary>
    public string? KiteInstrumentsChartRangePreset { get; set; }

    /// <summary>Saved chart style: <c>line</c> or <c>bar</c>.</summary>
    public string? KiteInstrumentsChartGraphType { get; set; }

    /// <summary>JSON object: instrument token → visible bar count when chart zoom is applied (same keys as Kite token strings).</summary>
    public string? KiteInstrumentsChartZoomJson { get; set; }

    /// <summary>JSON object: instrument token → chart interval override (<c>1m</c>…<c>1d</c>); missing key uses <see cref="KiteInstrumentsChartInterval"/>.</summary>
    public string? KiteInstrumentsChartIntervalByInstrumentTokenJson { get; set; }

    /// <summary>JSON array of chart interval codes (<c>1m</c>…<c>1w</c>) selected for multi-interval trend analysis on favorites / locked grids.</summary>
    public string? KiteInstrumentsTrendAnalysisIntervalsJson { get; set; }

    /// <summary>Stored balance for the in-app wallet (simulated top-ups; no payment integration yet).</summary>
    public decimal WalletBalance { get; set; }

    /// <summary>UI-only demo flag: no live orders; EOD summary uses hypothetical allocation from automation rows.</summary>
    public bool DemoAutoTradeEnabled { get; set; }

    /// <summary>Demo allocation preset id (SPA/server constant: default <c>equal_split</c>). Does not execute trades.</summary>
    public string DemoAutoTradeStrategy { get; set; } = "equal_split";

    /// <summary>When true, server background job may run ML predictions for <see cref="KiteFavoriteInstruments"/> (requires global automation + Kite session).</summary>
    public bool FavoriteMlAutomationEnabled { get; set; }

    /// <summary>
    /// <strong>m</strong>: when set (e.g. <c>1m</c>, <c>5m</c>), favorite automation uses this candle interval for every favorite (normalized) as model input.
    /// When null, falls back to <c>FavoriteMlAutomation:PredictionIntervalOverride</c> when non-empty, else saved chart interval / per-token overrides.
    /// </summary>
    public string? FavoriteMlAutomationInterval { get; set; }

    /// <summary>
    /// <strong>N</strong>-minute run cadence: when set (always a multiple of 60 from the API = whole minutes), at least that many
    /// seconds must elapse after the previous automated <strong>new</strong> pass <strong>started</strong> before the next pass (pending resolution still runs every global cycle).
    /// When set, automation does <strong>not</strong> wait on the <strong>m</strong>-minute model bar phase (intrabar delay is skipped); passes still validate merged <strong>m</strong>-candles.
    /// When null, only the host <c>FavoriteMlAutomation:PollInterval*</c> applies and optional intrabar delay may gate passes.
    /// </summary>
    public int? FavoriteMlAutomationPollIntervalSeconds { get; set; }

    /// <summary>UTC timestamp of the last automated new-prediction pass start (for per-user poll throttling).</summary>
    public DateTimeOffset? FavoriteMlAutomationLastNewPassUtc { get; set; }

    /// <summary>
    /// When set, defers new automation predictions on the current ref bar until at least this many seconds after bar open
    /// (intrabar). When null, <c>FavoriteMlAutomation:MinSecondsAfterBarOpenForAutomation</c> applies. Ignored when <c>FavoriteMlAutomationPollIntervalSeconds</c> (N cadence) is set.
    /// </summary>
    public int? FavoriteMlAutomationMinSecondsAfterBarOpen { get; set; }

    /// <summary>Authenticator (TOTP) enabled for this account.</summary>
    public bool TwoFactorEnabled { get; set; }

    /// <summary>Authenticator or email sign-in OTP when <see cref="TwoFactorEnabled"/>.</summary>
    public SecondFactorMethod SecondFactorMethod { get; set; }

    /// <summary>Data-protection encrypted TOTP secret (Base64 payload) when <see cref="TwoFactorEnabled"/> and method is authenticator.</summary>
    public string? TotpSecretProtected { get; set; }

    /// <summary>Pending enrollment: encrypted secret until the user confirms the first code.</summary>
    public string? TotpPendingSecretProtected { get; set; }

    /// <summary>Data-protection encrypted JSON of hashed one-time recovery codes when <see cref="TwoFactorEnabled"/>.</summary>
    public string? TotpRecoveryCodesProtected { get; set; }

    public ICollection<BrokerAccount> BrokerAccounts { get; set; } = new List<BrokerAccount>();

    public ICollection<Strategy> Strategies { get; set; } = new List<Strategy>();
    public ICollection<Bot> Bots { get; set; } = new List<Bot>();
    public ICollection<KiteFavoriteInstrument> KiteFavoriteInstruments { get; set; } = new List<KiteFavoriteInstrument>();

    public ICollection<KiteTradingLockInstrument> KiteTradingLockInstruments { get; set; } = new List<KiteTradingLockInstrument>();
    public ICollection<MlPriceDirectionPrediction> MlPriceDirectionPredictions { get; set; } = new List<MlPriceDirectionPrediction>();

    public ICollection<MlLightGbmTripleBarrierPrediction> MlLightGbmTripleBarrierPredictions { get; set; } =
        new List<MlLightGbmTripleBarrierPrediction>();

    public ICollection<MlFavoriteEodReportSent> MlFavoriteEodReportsSent { get; set; } = new List<MlFavoriteEodReportSent>();

    public ICollection<DemoPaperPosition> DemoPaperPositions { get; set; } = new List<DemoPaperPosition>();

    public ICollection<DemoPaperBuyLeg> DemoPaperBuyLegs { get; set; } = new List<DemoPaperBuyLeg>();

    public ICollection<DemoPaperTradeLog> DemoPaperTradeLogs { get; set; } = new List<DemoPaperTradeLog>();
}