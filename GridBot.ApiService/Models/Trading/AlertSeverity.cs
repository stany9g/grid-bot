namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Severity levels for trading alerts and risk events.
/// </summary>
public enum AlertSeverity
{
    /// <summary>
    /// Critical severity - requires immediate auto-halt.
    /// Examples: Loss limit hit, flash crash, API auth failure.
    /// </summary>
    Critical,

    /// <summary>
    /// High severity - requires response within 5 minutes.
    /// Examples: Liquidity warning, leverage change failed.
    /// </summary>
    High,

    /// <summary>
    /// Medium severity - requires response within 1 hour.
    /// Examples: Trend state change, large rebalance needed.
    /// </summary>
    Medium,

    /// <summary>
    /// Low severity - daily digest notification.
    /// Examples: Grid adjustments, order fills, performance metrics.
    /// </summary>
    Low
}
