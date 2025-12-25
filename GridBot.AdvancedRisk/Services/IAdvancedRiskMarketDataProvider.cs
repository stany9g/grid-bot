namespace GridBot.AdvancedRisk.Services;

/// <summary>
/// Provides market data for advanced risk management.
/// Implemented by GridBot.ApiService to fetch data from Lighter DEX.
/// </summary>
public interface IAdvancedRiskMarketDataProvider
{
    /// <summary>
    /// Gets the current mid price for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Current mid price, or null if unavailable.</returns>
    Task<decimal?> GetMidPriceAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Gets the order book depth within a price range.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="rangePercent">Percentage of mid price to scan (e.g., 0.02 = 2%).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Tuple of (bidDepthUsd, askDepthUsd, spreadBps).</returns>
    Task<(decimal BidDepthUsd, decimal AskDepthUsd, decimal SpreadBps)?> GetOrderBookDepthAsync(
        int marketId,
        decimal rangePercent,
        CancellationToken ct = default);
}
