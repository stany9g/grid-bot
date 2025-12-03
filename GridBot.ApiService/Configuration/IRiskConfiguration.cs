using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Configuration;

/// <summary>
/// Provides runtime access to risk configuration parameters.
/// </summary>
public interface IRiskConfiguration
{
    /// <summary>
    /// Gets the current trading bot options.
    /// </summary>
    TradingBotOptions Options { get; }

    /// <summary>
    /// Gets the capital allocation options.
    /// </summary>
    CapitalOptions Capital { get; }

    /// <summary>
    /// Gets the loss limit options.
    /// </summary>
    LossLimitOptions LossLimits { get; }

    /// <summary>
    /// Gets the grid geometry options.
    /// </summary>
    GridOptions Grid { get; }

    /// <summary>
    /// Gets the trend detection options.
    /// </summary>
    TrendOptions Trend { get; }

    /// <summary>
    /// Gets the moon bag options.
    /// </summary>
    MoonBagOptions MoonBag { get; }

    /// <summary>
    /// Gets the flash crash options.
    /// </summary>
    FlashCrashOptions FlashCrash { get; }

    /// <summary>
    /// Gets the liquidity monitoring options.
    /// </summary>
    LiquidityOptions Liquidity { get; }

    /// <summary>
    /// Gets the target market ID.
    /// </summary>
    int MarketId { get; }

    /// <summary>
    /// Gets the decision loop interval in milliseconds.
    /// </summary>
    int DecisionLoopIntervalMs { get; }

    /// <summary>
    /// Gets the target inventory skew for a given trend state.
    /// </summary>
    decimal GetTargetSkewForTrend(TrendState trend);

    /// <summary>
    /// Calculates grid spacing based on ATR percentage.
    /// </summary>
    /// <param name="atrPercent">ATR as percentage of price.</param>
    /// <returns>Grid spacing percentage and orders per side.</returns>
    (decimal Spacing, int OrdersPerSide) CalculateGridParameters(decimal atrPercent);
}
