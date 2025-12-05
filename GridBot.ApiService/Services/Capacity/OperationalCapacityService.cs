using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Capacity;

/// <summary>
/// Calculates operational capacity for the trading bot.
/// CORE PRINCIPLE: Capacity NEVER goes to 0%. Even at minimum (10%), the bot is monitoring.
/// </summary>
public sealed class OperationalCapacityService : IOperationalCapacityService
{
    /// <summary>
    /// Minimum operational capacity. The bot NEVER goes below this.
    /// At 10%, the bot is still monitoring market, managing trailing stops, etc.
    /// </summary>
    private const int MinimumCapacity = 10;

    /// <summary>
    /// Capacity reduction for high volatility conditions.
    /// </summary>
    private const int VolatilityReduction = 25;

    /// <summary>
    /// Capacity reduction for low liquidity conditions.
    /// </summary>
    private const int LiquidityReduction = 25;

    /// <summary>
    /// Capacity reduction for significant skew deviation.
    /// </summary>
    private const int SkewDeviationReduction = 25;

    /// <summary>
    /// Capacity reduction for recent API errors.
    /// </summary>
    private const int ApiErrorReduction = 25;

    /// <inheritdoc />
    public int CalculateCapacity(
        TradingState state,
        decimal skewDeviation,
        bool hasApiErrors,
        bool highVolatility,
        bool lowLiquidity)
    {
        var capacity = 100;

        // State-based reductions (cap at specific levels)
        capacity = state switch
        {
            TradingState.Degraded_ProtectiveMode => Math.Min(capacity, 25),
            TradingState.Degraded_Bootstrap => Math.Min(capacity, 50),
            TradingState.Degraded_SkewCorrection => Math.Min(capacity, 75),
            TradingState.Degraded_HighVolatility => Math.Min(capacity, 50),
            TradingState.Degraded_LowLiquidity => Math.Min(capacity, 50),
            TradingState.Recovering => Math.Min(capacity, 50),
            TradingState.Active => capacity,
            _ => capacity
        };

        // Condition-based reductions (cumulative)
        if (highVolatility && state != TradingState.Degraded_HighVolatility)
        {
            capacity -= VolatilityReduction;
        }

        if (lowLiquidity && state != TradingState.Degraded_LowLiquidity)
        {
            capacity -= LiquidityReduction;
        }

        if (skewDeviation > 20)
        {
            capacity -= SkewDeviationReduction;
        }

        if (hasApiErrors)
        {
            capacity -= ApiErrorReduction;
        }

        // NEVER go below minimum - the bot must always be running
        return Math.Max(capacity, MinimumCapacity);
    }

    /// <inheritdoc />
    public decimal GetOrderSizeMultiplier(int capacity)
    {
        // Linear scaling: 100% capacity = 1.0x, 10% capacity = 0.1x
        return capacity / 100m;
    }

    /// <inheritdoc />
    public decimal GetSpreadMultiplier(int capacity)
    {
        // Inverse relationship: lower capacity = wider spreads for protection
        return capacity switch
        {
            >= 100 => 1.0m,
            >= 75 => 1.25m,
            >= 50 => 1.5m,
            >= 25 => 2.0m,
            _ => 3.0m  // Minimum capacity = widest spreads
        };
    }

    /// <inheritdoc />
    public int GetOrderCountMultiplier(int capacity, int normalCount)
    {
        // Scale order count with capacity
        var scaled = (int)(normalCount * (capacity / 100m));

        // At least 2 orders (1 buy, 1 sell) to maintain market presence
        return Math.Max(scaled, 2);
    }

    /// <inheritdoc />
    public bool IsDegradedState(TradingState state)
    {
        return state switch
        {
            TradingState.Degraded_Bootstrap => true,
            TradingState.Degraded_SkewCorrection => true,
            TradingState.Degraded_HighVolatility => true,
            TradingState.Degraded_LowLiquidity => true,
            TradingState.Degraded_ProtectiveMode => true,
            _ => false
        };
    }

    /// <inheritdoc />
    public TradingState GetRecommendedState(
        bool isBootstrapMode,
        bool skewCorrectionNeeded,
        bool highVolatility,
        bool lowLiquidity,
        bool lossApproachingLimit)
    {
        // Priority order: most severe condition first

        // Loss approaching limit is highest priority
        if (lossApproachingLimit)
        {
            return TradingState.Degraded_ProtectiveMode;
        }

        // Bootstrap mode takes precedence over other degraded states
        if (isBootstrapMode)
        {
            return TradingState.Degraded_Bootstrap;
        }

        // High volatility is more urgent than low liquidity or skew
        if (highVolatility)
        {
            return TradingState.Degraded_HighVolatility;
        }

        // Low liquidity is more urgent than skew correction
        if (lowLiquidity)
        {
            return TradingState.Degraded_LowLiquidity;
        }

        // Skew correction is the least severe degraded state
        if (skewCorrectionNeeded)
        {
            return TradingState.Degraded_SkewCorrection;
        }

        // All conditions normal
        return TradingState.Active;
    }
}
