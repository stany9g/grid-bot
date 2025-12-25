namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Liquidity health classification.
/// </summary>
public enum LiquidityHealth
{
    /// <summary>
    /// Liquidity is healthy - normal operations.
    /// </summary>
    Healthy = 0,

    /// <summary>
    /// Liquidity is thin - widen spreads.
    /// </summary>
    Thin = 1,

    /// <summary>
    /// Liquidity is critically low - reduce exposure.
    /// </summary>
    Critical = 2,

    /// <summary>
    /// No liquidity data available.
    /// </summary>
    Unknown = 3
}
