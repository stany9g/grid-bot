namespace GridBot.TrendIntelligence.Models;

/// <summary>
/// Result of MACD (Moving Average Convergence Divergence) calculation.
/// </summary>
public sealed class MacdResult
{
    /// <summary>
    /// MACD line value (Fast EMA - Slow EMA).
    /// </summary>
    public decimal MacdLine { get; init; }

    /// <summary>
    /// Signal line value (EMA of MACD line).
    /// </summary>
    public decimal SignalLine { get; init; }

    /// <summary>
    /// Histogram value (MACD line - Signal line).
    /// Positive = bullish momentum, Negative = bearish momentum.
    /// </summary>
    public decimal Histogram { get; init; }
}
