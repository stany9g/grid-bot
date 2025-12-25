namespace GridBot.Core.Configuration;

/// <summary>
/// Simple grid trading configuration with ~20 parameters.
/// Intentionally minimal - advanced features are in separate modules.
/// </summary>
public sealed class SimpleGridConfig
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "GridBot";

    // ===== Grid Configuration =====

    /// <summary>
    /// Spacing between grid levels as a percentage of price.
    /// Example: 0.5 means 0.5% spacing between levels.
    /// </summary>
    public decimal GridSpacingPercent { get; set; } = 0.5m;

    /// <summary>
    /// Number of buy orders below current price.
    /// </summary>
    public int BuyLevels { get; set; } = 10;

    /// <summary>
    /// Number of sell orders above current price.
    /// </summary>
    public int SellLevels { get; set; } = 10;

    /// <summary>
    /// Size of each grid order in USDC.
    /// </summary>
    public decimal OrderSizeUsdc { get; set; } = 100m;

    // ===== Risk Configuration =====

    /// <summary>
    /// Maximum allowed daily loss as percentage of account value.
    /// Trading pauses when this limit is reached.
    /// </summary>
    public decimal MaxDailyLossPercent { get; set; } = 5m;

    /// <summary>
    /// Flash crash detection threshold.
    /// If price drops this percentage in 1 minute, trading pauses.
    /// </summary>
    public decimal FlashCrashThresholdPercent { get; set; } = 8m;

    /// <summary>
    /// How long to pause after a risk event (in minutes).
    /// </summary>
    public int PauseCooldownMinutes { get; set; } = 15;

    // ===== Position Limits =====

    /// <summary>
    /// Maximum position size as percentage of account equity.
    /// Prevents over-leveraging.
    /// </summary>
    public decimal MaxPositionPercent { get; set; } = 10m;

    // ===== Exchange Configuration =====

    /// <summary>
    /// Trading market name (for display/logging).
    /// </summary>
    public string Market { get; set; } = "BTC-USDC";

    /// <summary>
    /// Lighter DEX market index.
    /// </summary>
    public int MarketIndex { get; set; } = 4;

    /// <summary>
    /// Position leverage multiplier.
    /// </summary>
    public int Leverage { get; set; } = 3;

    // ===== Timing Configuration =====

    /// <summary>
    /// How often to run the trading loop (in seconds).
    /// </summary>
    public int LoopIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum retries for API calls.
    /// </summary>
    public int MaxApiRetries { get; set; } = 3;

    /// <summary>
    /// API call timeout (in seconds).
    /// </summary>
    public int ApiTimeoutSeconds { get; set; } = 10;

    // ===== Order Management =====

    /// <summary>
    /// Use post-only orders (maker orders that don't cross spread).
    /// Reduces fees but may not fill immediately.
    /// </summary>
    public bool UsePostOnlyOrders { get; set; } = true;

    /// <summary>
    /// Minimum profit per trade in USDC to be considered profitable.
    /// Used for fill tracking.
    /// </summary>
    public decimal MinProfitUsdc { get; set; } = 0.10m;

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <returns>Error message if invalid, null if valid.</returns>
    public string? Validate()
    {
        if (GridSpacingPercent <= 0 || GridSpacingPercent > 10)
            return "GridSpacingPercent must be between 0 and 10";

        if (BuyLevels <= 0 || BuyLevels > 100)
            return "BuyLevels must be between 1 and 100";

        if (SellLevels <= 0 || SellLevels > 100)
            return "SellLevels must be between 1 and 100";

        if (OrderSizeUsdc <= 0)
            return "OrderSizeUsdc must be positive";

        if (MaxDailyLossPercent <= 0 || MaxDailyLossPercent > 100)
            return "MaxDailyLossPercent must be between 0 and 100";

        if (FlashCrashThresholdPercent <= 0 || FlashCrashThresholdPercent > 50)
            return "FlashCrashThresholdPercent must be between 0 and 50";

        if (PauseCooldownMinutes <= 0)
            return "PauseCooldownMinutes must be positive";

        if (MaxPositionPercent <= 0 || MaxPositionPercent > 100)
            return "MaxPositionPercent must be between 0 and 100";

        if (MarketIndex < 0)
            return "MarketIndex must be non-negative";

        if (Leverage <= 0 || Leverage > 50)
            return "Leverage must be between 1 and 50";

        if (LoopIntervalSeconds <= 0)
            return "LoopIntervalSeconds must be positive";

        return null;
    }
}
