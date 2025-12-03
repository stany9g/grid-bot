using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Service for fetching and processing market data from Lighter DEX.
/// </summary>
public interface IMarketDataService
{
    /// <summary>
    /// Gets the current price for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current market price.</returns>
    Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets candlestick data for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="resolution">Candle resolution (1m, 5m, 15m, 1h, 4h, 1d).</param>
    /// <param name="count">Number of candles to retrieve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of candlestick data sorted by timestamp ascending.</returns>
    Task<List<CandlestickData>> GetCandlesticksAsync(
        int marketId,
        string resolution,
        int count,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a snapshot of the current order book.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="depth">Number of price levels to include on each side.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Order book snapshot.</returns>
    Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(
        int marketId,
        int depth = 20,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current funding rate for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Funding rate as decimal, or null if not available.</returns>
    Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken cancellationToken = default);
}
