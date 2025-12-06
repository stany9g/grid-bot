using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.MarketData;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Configuration;

/// <summary>
/// Provides runtime access to risk configuration parameters.
/// Wraps IOptionsMonitor for hot-reload support.
/// </summary>
public sealed class RiskConfiguration : IRiskConfiguration
{
    private readonly IOptionsMonitor<TradingBotOptions> _optionsMonitor;
    private readonly IMarketResolver _marketResolver;

    /// <summary>
    /// Creates a new RiskConfiguration instance.
    /// </summary>
    public RiskConfiguration(
        IOptionsMonitor<TradingBotOptions> optionsMonitor,
        IMarketResolver marketResolver)
    {
        ArgumentNullException.ThrowIfNull(optionsMonitor);
        ArgumentNullException.ThrowIfNull(marketResolver);
        _optionsMonitor = optionsMonitor;
        _marketResolver = marketResolver;
    }

    /// <inheritdoc />
    public TradingBotOptions Options => _optionsMonitor.CurrentValue;

    /// <inheritdoc />
    public CapitalOptions Capital => Options.Capital;

    /// <inheritdoc />
    public LossLimitOptions LossLimits => Options.LossLimits;

    /// <inheritdoc />
    public GridOptions Grid => Options.Grid;

    /// <inheritdoc />
    public TrendOptions Trend => Options.Trend;

    /// <inheritdoc />
    public MoonBagOptions MoonBag => Options.MoonBag;

    /// <inheritdoc />
    public FlashCrashOptions FlashCrash => Options.FlashCrash;

    /// <inheritdoc />
    public LiquidityOptions Liquidity => Options.Liquidity;

    /// <inheritdoc />
    public int MarketId => _marketResolver.MarketId;

    /// <inheritdoc />
    public string Symbol => _marketResolver.Symbol;

    /// <inheritdoc />
    public int DecisionLoopIntervalMs => Options.DecisionLoopIntervalMs;

    /// <inheritdoc />
    public decimal GetTargetSkewForTrend(TrendState trend)
    {
        return trend switch
        {
            TrendState.StrongBull => 80m,
            TrendState.MildBull => 70m,
            TrendState.Neutral => 50m,
            TrendState.MildBear => 30m,
            TrendState.StrongBear => 20m,
            _ => 50m
        };
    }

    /// <inheritdoc />
    public (decimal Spacing, int OrdersPerSide) CalculateGridParameters(decimal atrPercent)
    {
        // Based on ATR thresholds from risk specification
        var (spacing, orders) = atrPercent switch
        {
            < 0.5m => (0.2m, 10),   // Low volatility - tight grid
            < 1.0m => (0.5m, 8),    // Normal volatility
            < 2.0m => (1.0m, 6),    // Elevated volatility
            < 3.0m => (1.5m, 5),    // High volatility
            _ => (2.0m, 4)          // Extreme volatility - wide grid
        };

        // Apply constraints
        spacing = Math.Clamp(spacing, Grid.MinSpacing, Grid.MaxSpacing);
        orders = Math.Clamp(orders, Grid.MinOrdersPerSide, Grid.MaxOrdersPerSide);

        return (spacing, orders);
    }
}
