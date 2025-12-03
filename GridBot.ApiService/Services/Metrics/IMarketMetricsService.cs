using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Metrics;

/// <summary>
/// Service for aggregating market data into comprehensive metrics for trading decisions.
/// </summary>
public interface IMarketMetricsService
{
    /// <summary>
    /// Gets aggregated market metrics including indicators, order book analysis, and volume data.
    /// Results may be cached for performance.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated market metrics.</returns>
    Task<MarketMetrics> GetMarketMetricsAsync(int marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a refresh of cached metrics for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RefreshMetricsAsync(int marketId, CancellationToken cancellationToken = default);
}
