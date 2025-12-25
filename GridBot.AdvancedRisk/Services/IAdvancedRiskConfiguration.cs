namespace GridBot.AdvancedRisk.Services;

/// <summary>
/// Configuration options for advanced risk management.
/// Implemented by GridBot.ApiService to provide actual configuration values.
/// </summary>
public interface IAdvancedRiskConfiguration
{
    // Recovery configuration

    /// <summary>
    /// Duration of Phase 1 recovery in minutes (default 15).
    /// </summary>
    int RecoveryPhase1Minutes { get; }

    /// <summary>
    /// Duration of Phase 2 recovery in minutes (default 30).
    /// </summary>
    int RecoveryPhase2Minutes { get; }

    /// <summary>
    /// Duration of Phase 3 recovery in minutes (default 60).
    /// </summary>
    int RecoveryPhase3Minutes { get; }

    /// <summary>
    /// Position multiplier during Phase 1 recovery (default 0.25).
    /// </summary>
    decimal Phase1PositionMultiplier { get; }

    /// <summary>
    /// Position multiplier during Phase 2 recovery (default 0.50).
    /// </summary>
    decimal Phase2PositionMultiplier { get; }

    /// <summary>
    /// Position multiplier during Phase 3 recovery (default 0.75).
    /// </summary>
    decimal Phase3PositionMultiplier { get; }

    /// <summary>
    /// Spread multiplier during Phase 1 recovery (default 2.0).
    /// </summary>
    decimal Phase1SpreadMultiplier { get; }

    // Flash pump detection

    /// <summary>
    /// Flash pump threshold as percentage (default 0.10 = 10%).
    /// Price increase within window triggers protection.
    /// </summary>
    decimal FlashPumpThreshold { get; }

    /// <summary>
    /// Flash pump detection window in minutes (default 1).
    /// </summary>
    int FlashPumpWindowMinutes { get; }

    /// <summary>
    /// Cooldown period in minutes after flash pump detection (default 10).
    /// </summary>
    int FlashPumpCooldownMinutes { get; }

    // Liquidity monitoring

    /// <summary>
    /// Minimum order book depth in USD for healthy liquidity (default 10000).
    /// </summary>
    decimal MinimumHealthyDepthUsd { get; }

    /// <summary>
    /// Order book depth in USD below which liquidity is critical (default 2000).
    /// </summary>
    decimal CriticalDepthUsd { get; }

    /// <summary>
    /// Percentage of mid price to scan for liquidity (default 0.02 = 2%).
    /// </summary>
    decimal LiquidityScanRangePercent { get; }

    // Webhook notifications

    /// <summary>
    /// Whether webhook notifications are enabled (default false).
    /// </summary>
    bool WebhooksEnabled { get; }

    /// <summary>
    /// Maximum retries for failed webhook calls (default 3).
    /// </summary>
    int WebhookMaxRetries { get; }

    /// <summary>
    /// Timeout for webhook calls in seconds (default 10).
    /// </summary>
    int WebhookTimeoutSeconds { get; }
}
