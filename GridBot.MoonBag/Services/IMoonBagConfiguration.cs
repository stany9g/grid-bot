namespace GridBot.MoonBag.Services;

/// <summary>
/// Configuration options for moon bag protection and trailing grid.
/// Implemented by GridBot.ApiService to provide actual configuration values.
/// </summary>
public interface IMoonBagConfiguration
{
    /// <summary>
    /// Moon bag reserve as percentage of max position achieved (default 15%).
    /// This portion is protected from automated selling.
    /// </summary>
    decimal MoonBagPercentage { get; }

    /// <summary>
    /// Price movement to trigger grid shift upward as percentage (default 2%).
    /// </summary>
    decimal TrailingGridStep { get; }

    /// <summary>
    /// Maximum trailing distance as percentage below current high (default 20%).
    /// </summary>
    decimal MaxTrailDistance { get; }

    /// <summary>
    /// Initial trailing stop distance as percentage below high watermark (default 15%).
    /// </summary>
    decimal InitialTrailingStopPercent { get; }

    /// <summary>
    /// Tightened trailing stop distance at 50%+ profit (default 10%).
    /// </summary>
    decimal TightenedStopPercent { get; }

    /// <summary>
    /// Aggressive trailing stop distance at 100%+ profit (default 7%).
    /// </summary>
    decimal AggressiveStopPercent { get; }

    /// <summary>
    /// Emergency trailing stop distance at 200%+ profit (default 5%).
    /// </summary>
    decimal EmergencyStopPercent { get; }

    /// <summary>
    /// Profit threshold to tighten trailing stop from 15% to 10% (default 50%).
    /// </summary>
    decimal TightenAtProfitPercent50 { get; }

    /// <summary>
    /// Flash spike threshold - pause grid shift if price increases by this percentage in 5 minutes (default 20%).
    /// </summary>
    decimal FlashSpikeThreshold { get; }

    /// <summary>
    /// Cooldown period in minutes after flash spike detection (default 10).
    /// </summary>
    int FlashSpikeCooldownMinutes { get; }

    /// <summary>
    /// Warm-up period in minutes before moon bag protection activates (default 30).
    /// </summary>
    int WarmUpPeriodMinutes { get; }

    /// <summary>
    /// Minimum moon bag value in USD before protection is enabled (default $50).
    /// </summary>
    decimal MinimumMoonBagUsd { get; }

    /// <summary>
    /// Minimum seconds between consecutive grid shifts (default 60).
    /// </summary>
    int ShiftCooldownSeconds { get; }

    /// <summary>
    /// Maximum single grid shift as percentage (default 10%).
    /// </summary>
    decimal MaxShiftPercent { get; }

    /// <summary>
    /// Maximum cumulative grid shift within 1 hour as percentage (default 20%).
    /// </summary>
    decimal MaxCumulativeShift1h { get; }

    /// <summary>
    /// Whether to enable moon bag protection for short positions (default false).
    /// </summary>
    bool EnableShortMoonBag { get; }

    /// <summary>
    /// Trailing stop activation threshold - price must move this percentage above initial grid (default 10%).
    /// </summary>
    decimal TrailingStopActivationThreshold { get; }

    /// <summary>
    /// Number of consecutive price ticks required to confirm trailing stop trigger (default 3).
    /// </summary>
    int TrailingStopConfirmationTicks { get; }

    /// <summary>
    /// Minimum interval in seconds between trailing stop order updates (default 30).
    /// </summary>
    int TrailingStopUpdateIntervalSeconds { get; }

    /// <summary>
    /// High watermark update threshold - only update if price increased by this percentage (default 0.5%).
    /// </summary>
    decimal HighWatermarkUpdateThreshold { get; }

    /// <summary>
    /// Whether automatic moon bag release is enabled (default true).
    /// </summary>
    bool AutoReleaseEnabled { get; }

    /// <summary>
    /// Hours of confirmed StrongBear trend before auto-release (default 4).
    /// </summary>
    int AutoReleaseConfirmationHours { get; }

    /// <summary>
    /// Unrealized loss percentage threshold for immediate auto-release (default -20%).
    /// </summary>
    decimal AutoReleaseUnrealizedLossPercent { get; }

    /// <summary>
    /// Whether operator can override and disable auto-release for specific markets (default true).
    /// </summary>
    bool AllowOperatorOverride { get; }
}
