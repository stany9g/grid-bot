using GridBot.Core.Models;

namespace GridBot.Core.Services.Adaptive;

/// <summary>
/// Service for calculating auto-tuned parameter suggestions.
/// Uses ATR and equity to suggest optimal grid parameters.
/// </summary>
public interface IAdaptiveParameterService
{
    /// <summary>
    /// Calculates parameter suggestions based on current market conditions.
    /// Uses 14-period ATR on 1-hour candles with EMA smoothing.
    /// </summary>
    /// <param name="marketId">Market ID for candlestick data.</param>
    /// <param name="equity">Current account equity in USDC.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Calculated suggestions with reasoning.</returns>
    Task<AdaptiveSuggestions> CalculateSuggestionsAsync(
        int marketId,
        decimal equity,
        CancellationToken ct = default);
}
