using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Capacity;

/// <summary>
/// Service for calculating operational capacity levels.
/// Capacity ranges from 10% (minimum) to 100% (full operation).
/// The bot NEVER goes to 0% - it always maintains minimum monitoring capacity.
/// </summary>
public interface IOperationalCapacityService
{
    /// <summary>
    /// Calculates the current operational capacity based on state and conditions.
    /// </summary>
    /// <param name="state">Current trading state.</param>
    /// <param name="skewDeviation">How far current skew is from acceptable range (percentage points).</param>
    /// <param name="hasApiErrors">Whether there have been recent API errors.</param>
    /// <param name="highVolatility">Whether high volatility is detected.</param>
    /// <param name="lowLiquidity">Whether low liquidity is detected.</param>
    /// <returns>Capacity percentage (10-100).</returns>
    int CalculateCapacity(TradingState state, decimal skewDeviation, bool hasApiErrors, bool highVolatility, bool lowLiquidity);

    /// <summary>
    /// Gets the order size multiplier for the given capacity.
    /// At 100% capacity, multiplier is 1.0. At 50% capacity, multiplier is 0.5.
    /// </summary>
    /// <param name="capacity">Current capacity (10-100).</param>
    /// <returns>Multiplier to apply to order sizes (0.1-1.0).</returns>
    decimal GetOrderSizeMultiplier(int capacity);

    /// <summary>
    /// Gets the spread multiplier for the given capacity.
    /// At lower capacity, spreads are widened for protection.
    /// </summary>
    /// <param name="capacity">Current capacity (10-100).</param>
    /// <returns>Multiplier to apply to spreads (1.0-3.0).</returns>
    decimal GetSpreadMultiplier(int capacity);

    /// <summary>
    /// Gets the adjusted order count for the given capacity.
    /// At lower capacity, fewer grid levels are used.
    /// </summary>
    /// <param name="capacity">Current capacity (10-100).</param>
    /// <param name="normalCount">Normal number of grid levels.</param>
    /// <returns>Adjusted order count (minimum 2).</returns>
    int GetOrderCountMultiplier(int capacity, int normalCount);

    /// <summary>
    /// Determines if the state is a degraded state.
    /// </summary>
    /// <param name="state">Trading state to check.</param>
    /// <returns>True if the state is one of the Degraded_* states.</returns>
    bool IsDegradedState(TradingState state);

    /// <summary>
    /// Gets the recommended TradingState based on current conditions.
    /// </summary>
    /// <param name="isBootstrapMode">Whether we're in bootstrap mode (no position).</param>
    /// <param name="skewCorrectionNeeded">Whether skew correction is needed.</param>
    /// <param name="highVolatility">Whether high volatility is detected.</param>
    /// <param name="lowLiquidity">Whether low liquidity is detected.</param>
    /// <param name="lossApproachingLimit">Whether loss is approaching limits.</param>
    /// <returns>Recommended trading state.</returns>
    TradingState GetRecommendedState(
        bool isBootstrapMode,
        bool skewCorrectionNeeded,
        bool highVolatility,
        bool lowLiquidity,
        bool lossApproachingLimit);
}
