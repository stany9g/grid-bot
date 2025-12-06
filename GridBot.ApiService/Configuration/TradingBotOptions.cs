namespace GridBot.ApiService.Configuration;

/// <summary>
/// Root configuration options for the ALTE trading bot.
/// Bind from "TradingBot" section in appsettings.json.
/// </summary>
public sealed class TradingBotOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "TradingBot";

    /// <summary>
    /// Capital allocation and position sizing options.
    /// </summary>
    public CapitalOptions Capital { get; set; } = new();

    /// <summary>
    /// Loss limit thresholds and circuit breakers.
    /// </summary>
    public LossLimitOptions LossLimits { get; set; } = new();

    /// <summary>
    /// Dynamic grid geometry options.
    /// </summary>
    public GridOptions Grid { get; set; } = new();

    /// <summary>
    /// Trend detection and inventory management options.
    /// </summary>
    public TrendOptions Trend { get; set; } = new();

    /// <summary>
    /// Moon bag protection options.
    /// </summary>
    public MoonBagOptions MoonBag { get; set; } = new();

    /// <summary>
    /// Flash crash detection and protection options.
    /// </summary>
    public FlashCrashOptions FlashCrash { get; set; } = new();

    /// <summary>
    /// Liquidity monitoring options.
    /// </summary>
    public LiquidityOptions Liquidity { get; set; } = new();

    /// <summary>
    /// Decision engine options.
    /// </summary>
    public DecisionEngineOptions DecisionEngine { get; set; } = new();

    /// <summary>
    /// Market symbol to trade (e.g., "BTC", "ETH").
    /// The symbol is resolved to a market ID at startup by matching against available order books.
    /// </summary>
    public string Symbol { get; set; } = "BTC";

    /// <summary>
    /// Main decision loop interval in milliseconds.
    /// </summary>
    public int DecisionLoopIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Whether to automatically start trading on startup.
    /// When true, the bot transitions to Active state immediately after initialization.
    /// When false, the bot starts in Paused state and requires manual activation.
    /// Default is false for safety in production.
    /// </summary>
    public bool AutoStartTrading { get; set; } = false;
}

/// <summary>
/// Capital allocation and position sizing configuration.
/// </summary>
public sealed class CapitalOptions
{
    /// <summary>
    /// Maximum single position size as percentage of portfolio (default 10%).
    /// </summary>
    public decimal MaxPositionSizePercent { get; set; } = 10m;

    /// <summary>
    /// Minimum reserve balance as percentage of total equity (default 20%).
    /// </summary>
    public decimal ReserveBalancePercent { get; set; } = 20m;

    /// <summary>
    /// Maximum leverage per position (default 5x).
    /// </summary>
    public decimal MaxLeverage { get; set; } = 5m;

    /// <summary>
    /// Maximum aggregate leverage across all positions (default 3x).
    /// </summary>
    public decimal MaxAggregateLeverage { get; set; } = 3m;

    /// <summary>
    /// Maximum portfolio allocation per market (default 25%).
    /// </summary>
    public decimal MaxPerMarketPercent { get; set; } = 25m;

    /// <summary>
    /// Maximum total deployed capital (default 80%).
    /// </summary>
    public decimal MaxDeployedCapitalPercent { get; set; } = 80m;

    /// <summary>
    /// Maximum single order size as percentage of portfolio (default 5%).
    /// </summary>
    public decimal MaxOrderSizePercent { get; set; } = 5m;

    /// <summary>
    /// Minimum order size as percentage of portfolio (default 0.1%).
    /// </summary>
    public decimal MinOrderSizePercent { get; set; } = 0.1m;

    /// <summary>
    /// Minimum order size in USD (default $10).
    /// </summary>
    public decimal MinOrderSizeUsd { get; set; } = 10m;
}

/// <summary>
/// Loss limit thresholds and circuit breaker configuration.
/// Uses rolling windows instead of calendar-based periods for 24/7 crypto trading.
/// </summary>
public sealed class LossLimitOptions
{
    /// <summary>
    /// Rolling 24-hour loss limit as percentage (default -12%).
    /// Triggers protective mode if breached.
    /// </summary>
    public decimal Rolling24HourLossPercent { get; set; } = -12m;

    /// <summary>
    /// Rolling 7-day loss limit as percentage (default -20%).
    /// Triggers protective mode with extended recovery.
    /// </summary>
    public decimal Rolling7DayLossPercent { get; set; } = -20m;

    /// <summary>
    /// Rolling 30-day loss limit as percentage (default -30%).
    /// Triggers protective mode requiring manual review.
    /// </summary>
    public decimal Rolling30DayLossPercent { get; set; } = -30m;

    /// <summary>
    /// Maximum drawdown from all-time high as percentage (default -35%).
    /// Only clears when equity recovers to 75% of HWM or manual override.
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; } = -35m;

    /// <summary>
    /// Single trade loss limit as percentage (default -3%).
    /// Triggers alert but not halt.
    /// </summary>
    public decimal SingleTradeLossPercent { get; set; } = -3m;

    /// <summary>
    /// Position size reduction on max drawdown breach (default 75%).
    /// </summary>
    public decimal DrawdownPositionReductionPercent { get; set; } = 75m;

    /// <summary>
    /// Minimum hours to wait before entering recovery for 24h breach (default 4).
    /// </summary>
    public int Rolling24HourRecoveryWaitHours { get; set; } = 4;

    /// <summary>
    /// Minimum hours to wait before entering recovery for 7d breach (default 24).
    /// </summary>
    public int Rolling7DayRecoveryWaitHours { get; set; } = 24;

    /// <summary>
    /// Minimum hours to wait before entering recovery for 30d breach (default 72).
    /// </summary>
    public int Rolling30DayRecoveryWaitHours { get; set; } = 72;

    /// <summary>
    /// Days to retain trade records for rolling calculations (default 35).
    /// Must be at least 30 days + buffer.
    /// </summary>
    public int TradeRecordRetentionDays { get; set; } = 35;

    /// <summary>
    /// Interval in minutes for equity snapshots (default 60).
    /// Used for validation and fallback calculations.
    /// </summary>
    public int EquitySnapshotIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Dynamic grid geometry configuration.
/// </summary>
public sealed class GridOptions
{
    /// <summary>
    /// Minimum grid spacing as percentage (default 0.15%).
    /// </summary>
    public decimal MinSpacing { get; set; } = 0.15m;

    /// <summary>
    /// Maximum grid spacing as percentage (default 3.0%).
    /// </summary>
    public decimal MaxSpacing { get; set; } = 3.0m;

    /// <summary>
    /// Minimum total grid width as percentage (default 5%).
    /// </summary>
    public decimal MinWidth { get; set; } = 5m;

    /// <summary>
    /// Maximum total grid width as percentage (default 30%).
    /// </summary>
    public decimal MaxWidth { get; set; } = 30m;

    /// <summary>
    /// Minimum orders per side of the grid (default 4).
    /// </summary>
    public int MinOrdersPerSide { get; set; } = 4;

    /// <summary>
    /// Maximum orders per side of the grid (default 10).
    /// </summary>
    public int MaxOrdersPerSide { get; set; } = 10;

    /// <summary>
    /// Default orders per side when ATR is unavailable (default 6).
    /// </summary>
    public int DefaultOrdersPerSide { get; set; } = 6;

    /// <summary>
    /// Default grid spacing when ATR is unavailable (default 1.0%).
    /// </summary>
    public decimal DefaultSpacing { get; set; } = 1.0m;
}

/// <summary>
/// Trend detection and inventory management configuration.
/// </summary>
public sealed class TrendOptions
{
    /// <summary>
    /// Fast EMA period for trend detection (default 20).
    /// </summary>
    public int EmaFastPeriod { get; set; } = 20;

    /// <summary>
    /// Slow EMA period for trend detection (default 50).
    /// </summary>
    public int EmaSlowPeriod { get; set; } = 50;

    /// <summary>
    /// Delay before acting on trend state change in minutes (default 15).
    /// </summary>
    public int ConfirmationDelayMinutes { get; set; } = 15;

    /// <summary>
    /// Cooldown period after rapid trend flips in minutes (default 120).
    /// </summary>
    public int TrendFlipCooldownMinutes { get; set; } = 120;

    /// <summary>
    /// ADX threshold for strong trend detection (default 25).
    /// </summary>
    public decimal AdxStrongTrendThreshold { get; set; } = 25m;

    /// <summary>
    /// ADX threshold below which trend is considered neutral (default 20).
    /// </summary>
    public decimal AdxNeutralThreshold { get; set; } = 20m;

    /// <summary>
    /// EMA proximity threshold for neutral detection as percentage (default 1%).
    /// </summary>
    public decimal EmaNeutralProximityPercent { get; set; } = 1m;

    /// <summary>
    /// Rebalance tolerance - no action if skew deviation below this (default 5%).
    /// </summary>
    public decimal RebalanceTolerancePercent { get; set; } = 5m;

    /// <summary>
    /// Maximum rebalance rate per hour as percentage of portfolio (default 10%).
    /// </summary>
    public decimal MaxRebalanceRatePercent { get; set; } = 10m;

    /// <summary>
    /// Emergency rebalance threshold - force rebalance above this deviation (default 30%).
    /// </summary>
    public decimal EmergencyRebalanceThresholdPercent { get; set; } = 30m;

    /// <summary>
    /// Maximum skew before trading halt (default 90%).
    /// </summary>
    public decimal MaxSkewPercent { get; set; } = 90m;
}

/// <summary>
/// Moon bag protection and trailing grid configuration.
/// </summary>
public sealed class MoonBagOptions
{
    /// <summary>
    /// Moon bag reserve as percentage of max position achieved (default 15%).
    /// This portion is protected from automated selling.
    /// </summary>
    public decimal MoonBagPercentage { get; set; } = 0.15m;

    /// <summary>
    /// Price movement to trigger grid shift upward as percentage (default 2%).
    /// Grid shifts up after price increases by this amount above upper bound.
    /// </summary>
    public decimal TrailingGridStep { get; set; } = 0.02m;

    /// <summary>
    /// Maximum trailing distance as percentage below current high (default 20%).
    /// Prevents trailing stop from being too distant from current price.
    /// </summary>
    public decimal MaxTrailDistance { get; set; } = 0.20m;

    /// <summary>
    /// Initial trailing stop distance as percentage below high watermark (default 15%).
    /// Activated when price moves 10% above initial grid.
    /// </summary>
    public decimal InitialTrailingStopPercent { get; set; } = 0.15m;

    /// <summary>
    /// Tightened trailing stop distance at 50%+ profit (default 10%).
    /// </summary>
    public decimal TightenedStopPercent { get; set; } = 0.10m;

    /// <summary>
    /// Aggressive trailing stop distance at 100%+ profit (default 7%).
    /// </summary>
    public decimal AggressiveStopPercent { get; set; } = 0.07m;

    /// <summary>
    /// Emergency trailing stop distance at 200%+ profit (default 5%).
    /// </summary>
    public decimal EmergencyStopPercent { get; set; } = 0.05m;

    /// <summary>
    /// Profit threshold to tighten trailing stop from 15% to 10% (default 50%).
    /// </summary>
    public decimal TightenAtProfitPercent50 { get; set; } = 0.50m;

    /// <summary>
    /// Profit threshold to tighten trailing stop from 10% to 7% (default 100%).
    /// </summary>
    public decimal TightenAtProfitPercent100 { get; set; } = 1.00m;

    /// <summary>
    /// Profit threshold to tighten trailing stop from 7% to 5% (default 200%).
    /// </summary>
    public decimal TightenAtProfitPercent200 { get; set; } = 2.00m;

    /// <summary>
    /// Flash spike threshold - pause grid shift if price increases by this percentage in 5 minutes (default 20%).
    /// </summary>
    public decimal FlashSpikeThreshold { get; set; } = 0.20m;

    /// <summary>
    /// Cooldown period in minutes after flash spike detection before resuming grid shifts (default 10).
    /// </summary>
    public int FlashSpikeCooldownMinutes { get; set; } = 10;

    /// <summary>
    /// Warm-up period in minutes before moon bag protection activates (default 30).
    /// Allows normal grid trading to establish position before protection.
    /// </summary>
    public int WarmUpPeriodMinutes { get; set; } = 30;

    /// <summary>
    /// Minimum moon bag value in USD before protection is enabled (default $50).
    /// </summary>
    public decimal MinimumMoonBagUsd { get; set; } = 50m;

    /// <summary>
    /// Minimum seconds between consecutive grid shifts (default 60).
    /// </summary>
    public int ShiftCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum single grid shift as percentage (default 10%).
    /// Larger shifts are capped and flagged for review.
    /// </summary>
    public decimal MaxShiftPercent { get; set; } = 0.10m;

    /// <summary>
    /// Maximum cumulative grid shift within 1 hour as percentage (default 20%).
    /// Prevents runaway grid shifting in parabolic markets.
    /// </summary>
    public decimal MaxCumulativeShift1h { get; set; } = 0.20m;

    /// <summary>
    /// Whether to enable moon bag protection for short positions (default false).
    /// When enabled, applies inverse moon bag logic for short positions.
    /// </summary>
    public bool EnableShortMoonBag { get; set; } = false;

    /// <summary>
    /// Trailing stop activation threshold - price must move this percentage above initial grid (default 10%).
    /// </summary>
    public decimal TrailingStopActivationThreshold { get; set; } = 0.10m;

    /// <summary>
    /// Number of consecutive price ticks required to confirm trailing stop trigger (default 3).
    /// </summary>
    public int TrailingStopConfirmationTicks { get; set; } = 3;

    /// <summary>
    /// Minimum interval in seconds between trailing stop order updates (default 30).
    /// </summary>
    public int TrailingStopUpdateIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// High watermark update threshold - only update if price increased by this percentage (default 0.5%).
    /// </summary>
    public decimal HighWatermarkUpdateThreshold { get; set; } = 0.005m;
}

/// <summary>
/// Flash crash detection and protection configuration.
/// </summary>
public sealed class FlashCrashOptions
{
    /// <summary>
    /// 1-minute price drop threshold for buying pause (default -3%).
    /// </summary>
    public decimal OneMinuteDropPercent { get; set; } = -3m;

    /// <summary>
    /// 5-minute price drop threshold for all order pause (default -5%).
    /// </summary>
    public decimal FiveMinuteDropPercent { get; set; } = -5m;

    /// <summary>
    /// 15-minute price drop threshold for position reduction (default -10%).
    /// </summary>
    public decimal FifteenMinuteDropPercent { get; set; } = -10m;

    /// <summary>
    /// 1-hour price drop threshold for full halt (default -15%).
    /// </summary>
    public decimal OneHourDropPercent { get; set; } = -15m;

    /// <summary>
    /// Pause duration after 1-minute drop in minutes (default 5).
    /// </summary>
    public int OneMinutePauseDurationMinutes { get; set; } = 5;

    /// <summary>
    /// Pause duration after 5-minute drop in minutes (default 15).
    /// </summary>
    public int FiveMinutePauseDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Pause duration after 15-minute drop in minutes (default 60).
    /// </summary>
    public int FifteenMinutePauseDurationMinutes { get; set; } = 60;

    /// <summary>
    /// Pause duration after 1-hour drop in minutes (default 240).
    /// </summary>
    public int OneHourPauseDurationMinutes { get; set; } = 240;

    /// <summary>
    /// Maximum flash crash events in 24 hours before extended halt (default 2).
    /// </summary>
    public int MaxEventsIn24Hours { get; set; } = 2;

    /// <summary>
    /// Recovery stabilization period in minutes (default 10).
    /// </summary>
    public int RecoveryStabilizationMinutes { get; set; } = 10;

    /// <summary>
    /// Recovery capacity increment per period as percentage (default 25%).
    /// </summary>
    public decimal RecoveryCapacityIncrementPercent { get; set; } = 25m;
}

/// <summary>
/// Liquidity monitoring configuration.
/// </summary>
public sealed class LiquidityOptions
{
    /// <summary>
    /// Minimum order book depth in USD (default $50,000).
    /// </summary>
    public decimal MinBookDepthUsd { get; set; } = 50_000m;

    /// <summary>
    /// Critical order book depth threshold in USD (default $25,000).
    /// </summary>
    public decimal CriticalBookDepthUsd { get; set; } = 25_000m;

    /// <summary>
    /// Volume warning threshold as percentage of 7d average (default 50%).
    /// </summary>
    public decimal VolumeWarningPercent { get; set; } = 50m;

    /// <summary>
    /// Critical volume threshold as percentage of 7d average (default 25%).
    /// </summary>
    public decimal VolumeCriticalPercent { get; set; } = 25m;

    /// <summary>
    /// Maximum funding rate before position reduction as percentage (default 0.1%).
    /// </summary>
    public decimal MaxFundingRatePercent { get; set; } = 0.1m;

    /// <summary>
    /// Critical funding rate threshold as percentage (default 0.3%).
    /// </summary>
    public decimal CriticalFundingRatePercent { get; set; } = 0.3m;

    /// <summary>
    /// Maximum bid-ask spread before pausing as percentage (default 0.5%).
    /// </summary>
    public decimal MaxBidAskSpreadPercent { get; set; } = 0.5m;

    /// <summary>
    /// Order book depth imbalance threshold for warning (default 3.0).
    /// </summary>
    public decimal DepthImbalanceWarningRatio { get; set; } = 3.0m;

    /// <summary>
    /// Hours of low volume before trading halt (default 4).
    /// </summary>
    public int LowVolumeHaltHours { get; set; } = 4;
}

/// <summary>
/// Decision engine configuration for orchestrating the trading loop.
/// </summary>
public sealed class DecisionEngineOptions
{
    /// <summary>
    /// Timeout for parallel data collection in milliseconds (default 2000ms).
    /// </summary>
    public int DataCollectionTimeoutMs { get; set; } = 2000;

    /// <summary>
    /// Maximum consecutive timeouts before pausing trading (default 5).
    /// </summary>
    public int MaxConsecutiveTimeouts { get; set; } = 5;

    /// <summary>
    /// Critical timeout threshold requiring halt state (default 10).
    /// </summary>
    public int CriticalTimeoutThreshold { get; set; } = 10;

    /// <summary>
    /// Maximum age for cached data in milliseconds (default 30000ms).
    /// </summary>
    public int CacheValidityMs { get; set; } = 30000;

    /// <summary>
    /// Recovery Phase 1 duration in milliseconds (default 15 minutes).
    /// </summary>
    public int RecoveryPhase1DurationMs { get; set; } = 900000;

    /// <summary>
    /// Recovery Phase 2 duration in milliseconds (default 15 minutes).
    /// </summary>
    public int RecoveryPhase2DurationMs { get; set; } = 900000;

    /// <summary>
    /// Recovery Phase 3 duration in milliseconds (default 30 minutes).
    /// </summary>
    public int RecoveryPhase3DurationMs { get; set; } = 1800000;

    /// <summary>
    /// Stability window for recovery phase advancement in milliseconds (default 15 minutes).
    /// </summary>
    public int RecoveryStabilityWindowMs { get; set; } = 900000;

    /// <summary>
    /// Maximum volatility threshold for recovery phase advancement (default 2%).
    /// </summary>
    public decimal RecoveryVolatilityThreshold { get; set; } = 0.02m;

    /// <summary>
    /// Maximum loss threshold in recovery phase before reset (default 0.5%).
    /// </summary>
    public decimal RecoveryLossThreshold { get; set; } = 0.005m;

    /// <summary>
    /// Minimum order book depth for recovery phase advancement in USD (default $100,000).
    /// </summary>
    public decimal RecoveryDepthMinimum { get; set; } = 100000m;

    /// <summary>
    /// Minimum position multiplier floor after all reductions (default 10%).
    /// </summary>
    public decimal PositionMultiplierFloor { get; set; } = 0.10m;

    /// <summary>
    /// Maximum spread multiplier ceiling after all additions (default 3.0x).
    /// </summary>
    public decimal SpreadMultiplierCeiling { get; set; } = 3.0m;
}
