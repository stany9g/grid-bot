namespace GridBot.TrendIntelligence.Services;

/// <summary>
/// Configuration options for trend detection and inventory management.
/// </summary>
public interface ITrendConfiguration
{
    /// <summary>
    /// Fast EMA period for trend detection (default 20).
    /// </summary>
    int EmaFastPeriod { get; }

    /// <summary>
    /// Slow EMA period for trend detection (default 50).
    /// </summary>
    int EmaSlowPeriod { get; }

    /// <summary>
    /// Delay before acting on trend state change in minutes (default 15).
    /// </summary>
    int ConfirmationDelayMinutes { get; }

    /// <summary>
    /// Cooldown period after rapid trend flips in minutes (default 120).
    /// </summary>
    int TrendFlipCooldownMinutes { get; }

    /// <summary>
    /// ADX threshold for strong trend detection (default 25).
    /// </summary>
    decimal AdxStrongTrendThreshold { get; }

    /// <summary>
    /// ADX threshold below which trend is considered neutral (default 20).
    /// </summary>
    decimal AdxNeutralThreshold { get; }

    /// <summary>
    /// EMA proximity threshold for neutral detection as percentage (default 1%).
    /// </summary>
    decimal EmaNeutralProximityPercent { get; }

    /// <summary>
    /// Rebalance tolerance - no action if skew deviation below this (default 5%).
    /// </summary>
    decimal RebalanceTolerancePercent { get; }

    /// <summary>
    /// Maximum rebalance rate per hour as percentage of portfolio (default 10%).
    /// </summary>
    decimal MaxRebalanceRatePercent { get; }

    /// <summary>
    /// Minimum interval between rebalance attempts in minutes (default 15).
    /// </summary>
    int MinRebalanceIntervalMinutes { get; }

    /// <summary>
    /// Emergency rebalance threshold - force rebalance above this deviation (default 30%).
    /// </summary>
    decimal EmergencyRebalanceThresholdPercent { get; }
}
