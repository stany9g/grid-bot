using GridBot.Abstractions.Models.Market;
using GridBot.Abstractions.Models.OrderBook;

namespace GridBot.Abstractions.Trading;

/// <summary>
/// Client for market data retrieval (prices, order books, candles).
/// </summary>
public interface IMarketDataClient
{
    /// <summary>
    /// Gets the current price for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current price.</returns>
    Task<decimal> GetCurrentPriceAsync(string marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the current order book for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="depth">Number of price levels to retrieve per side.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The order book snapshot.</returns>
    Task<OrderBookSnapshot> GetOrderBookAsync(string marketId, int depth = 20, CancellationToken ct = default);

    /// <summary>
    /// Gets historical candlestick data for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="resolution">The candle resolution (e.g., "1h", "4h", "1d").</param>
    /// <param name="count">Number of candles to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of candlestick data ordered by time ascending.</returns>
    Task<IReadOnlyList<CandlestickData>> GetCandlesticksAsync(string marketId, string resolution, int count, CancellationToken ct = default);

    /// <summary>
    /// Gets all available markets.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of market information.</returns>
    Task<IReadOnlyList<MarketInfo>> GetMarketsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the current funding rate for a perpetual market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The funding rate info, or null if not a perpetual market.</returns>
    Task<FundingRateInfo?> GetFundingRateAsync(string marketId, CancellationToken ct = default);
}
