using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services.Trend;

/// <summary>
/// Service for detecting market trends using technical indicators.
/// </summary>
public interface ITrendDetector
{
    /// <summary>
    /// Analyzes the current market trend using EMA, MACD, and ADX indicators.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Trend analysis result.</returns>
    Task<TrendAnalysis> AnalyzeTrendAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Determines the trend state based on indicator values.
    /// </summary>
    /// <param name="ema20">Fast EMA (20-period) value.</param>
    /// <param name="ema50">Slow EMA (50-period) value.</param>
    /// <param name="macd">MACD indicator result.</param>
    /// <param name="adx">ADX trend strength value (0-100).</param>
    /// <returns>Determined trend state.</returns>
    TrendState DetermineTrendState(decimal ema20, decimal ema50, MacdResult macd, decimal adx);

    /// <summary>
    /// Determines whether a trend state change should be confirmed or delayed.
    /// </summary>
    /// <param name="newState">Proposed new trend state.</param>
    /// <param name="currentState">Current confirmed trend state.</param>
    /// <param name="lastTrendChange">When the trend last changed.</param>
    /// <returns>True if the new state requires confirmation delay.</returns>
    bool ShouldConfirmTrend(TrendState newState, TrendState currentState, DateTimeOffset? lastTrendChange);
}
