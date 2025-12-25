using GridBot.TrendIntelligence.Models;

namespace GridBot.TrendIntelligence.Services;

/// <summary>
/// Abstraction for market data access required by trend intelligence services.
/// Implemented by GridBot.ApiService to provide actual market data.
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Gets historical candlestick data for a market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="interval">Candle interval (e.g., "1h").</param>
    /// <param name="count">Number of candles to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of candlestick data ordered by time ascending.</returns>
    Task<IReadOnlyList<CandlestickData>> GetCandlesticksAsync(int marketId, string interval, int count, CancellationToken ct = default);

    /// <summary>
    /// Gets the current market price.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current price.</returns>
    Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default);
}
