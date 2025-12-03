using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Monitors market liquidity conditions for risk management.
/// Thread-safe for concurrent access.
/// </summary>
public interface ILiquidityMonitor
{
    /// <summary>
    /// Checks current liquidity conditions for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current liquidity status with recommendations.</returns>
    Task<LiquidityStatus> CheckLiquidityAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the recommended spread multiplier based on current liquidity.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Spread multiplier (1.0 = normal, >1.0 = widen spreads).</returns>
    decimal GetRecommendedSpreadMultiplier(int marketId);

    /// <summary>
    /// Gets whether trading is currently allowed based on liquidity.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>True if liquidity conditions allow trading.</returns>
    bool IsTradingAllowed(int marketId);

    /// <summary>
    /// Gets the recommended position reduction percentage based on funding rate.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <returns>Reduction percentage (0 = no reduction, 25 = reduce by 25%, etc.).</returns>
    decimal GetFundingRateReduction(int marketId);
}
