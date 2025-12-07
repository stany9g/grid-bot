using System.Globalization;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Realtime;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Hybrid market data service that uses WebSocket data when available,
/// falling back to REST API when WebSocket is unavailable or data is stale.
/// </summary>
public sealed class HybridMarketDataService : IMarketDataService
{
    private readonly ILighterRealtimeState _realtimeState;
    private readonly ILighterQueryClient _restClient;
    private readonly ILogger<HybridMarketDataService> _logger;

    /// <summary>
    /// Maximum age of WebSocket data before falling back to REST.
    /// </summary>
    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridMarketDataService"/> class.
    /// </summary>
    /// <param name="realtimeState">Real-time state from WebSocket.</param>
    /// <param name="restClient">REST API client for fallback.</param>
    /// <param name="logger">Logger instance.</param>
    public HybridMarketDataService(
        ILighterRealtimeState realtimeState,
        ILighterQueryClient restClient,
        ILogger<HybridMarketDataService> logger)
    {
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken cancellationToken = default)
    {
        // Try WebSocket first
        var wsPrice = _realtimeState.GetCurrentPrice(marketId);
        if (wsPrice.HasValue && _realtimeState.IsConnected && IsDataFresh())
        {
            _logger.LogTrace("Using WebSocket price for market {MarketId}: {Price}", marketId, wsPrice.Value);
            return wsPrice.Value;
        }

        // Fall back to REST
        _logger.LogDebug(
            "Falling back to REST for price. WS connected={IsConnected}, has price={HasPrice}, data age={DataAge}",
            _realtimeState.IsConnected,
            wsPrice.HasValue,
            _realtimeState.OldestDataAge);

        return await GetPriceFromRestAsync(marketId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(
        int marketId,
        int depth = 20,
        CancellationToken cancellationToken = default)
    {
        // Try WebSocket first
        var wsBook = _realtimeState.GetOrderBook(marketId);
        if (wsBook != null && _realtimeState.IsConnected && IsDataFresh())
        {
            _logger.LogTrace(
                "Using WebSocket order book for market {MarketId}: bid={BestBid}, ask={BestAsk}",
                marketId,
                wsBook.BestBidPrice,
                wsBook.BestAskPrice);

            return ConvertToOrderBookSnapshot(marketId, wsBook);
        }

        // Fall back to REST
        _logger.LogDebug(
            "Falling back to REST for order book. WS connected={IsConnected}, has book={HasBook}, data age={DataAge}",
            _realtimeState.IsConnected,
            wsBook != null,
            _realtimeState.OldestDataAge);

        return await GetOrderBookFromRestAsync(marketId, depth, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<CandlestickData>> GetCandlesticksAsync(
        int marketId,
        string resolution,
        int count,
        CancellationToken cancellationToken = default)
    {
        // Candlesticks are historical data - always use REST
        try
        {
            var candles = await _restClient.GetCandlesticksAsync(marketId, resolution, count, cancellationToken);

            return candles.Select(c => new CandlestickData
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Timestamp),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume0
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get candlesticks for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken cancellationToken = default)
    {
        // Try WebSocket market stats first
        var wsStats = _realtimeState.GetMarketStats(marketId);
        if (wsStats != null && _realtimeState.IsConnected && IsDataFresh())
        {
            _logger.LogTrace(
                "Using WebSocket funding rate for market {MarketId}: {FundingRate}",
                marketId,
                wsStats.FundingRate);

            return wsStats.FundingRate;
        }

        // Fall back to REST
        _logger.LogDebug(
            "Falling back to REST for funding rate. WS connected={IsConnected}, has stats={HasStats}",
            _realtimeState.IsConnected,
            wsStats != null);

        return await GetFundingRateFromRestAsync(marketId, cancellationToken);
    }

    private bool IsDataFresh()
    {
        var age = _realtimeState.OldestDataAge;
        return age == null || age < StaleThreshold;
    }

    private async Task<decimal> GetPriceFromRestAsync(int marketId, CancellationToken cancellationToken)
    {
        try
        {
            // Get market metadata for last trade price
            var marketData = await _restClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);

            if (marketData.LastTradePrice > 0)
                return marketData.LastTradePrice;

            // Fall back to mid price from best bid/ask
            var orderBook = await _restClient.GetOrderBookOrdersAsync(marketId, limit: 1, cancellationToken);
            var bestBid = orderBook.Bids.FirstOrDefault();
            var bestAsk = orderBook.Asks.FirstOrDefault();

            if (bestBid != null && bestAsk != null)
            {
                var bidPrice = ParseDecimal(bestBid.Price);
                var askPrice = ParseDecimal(bestAsk.Price);
                return (bidPrice + askPrice) / 2m;
            }

            _logger.LogWarning("No price data available for market {MarketId}", marketId);
            return 0m;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current price from REST for market {MarketId}", marketId);
            throw;
        }
    }

    private async Task<OrderBookSnapshot> GetOrderBookFromRestAsync(
        int marketId,
        int depth,
        CancellationToken cancellationToken)
    {
        try
        {
            var orderBookOrders = await _restClient.GetOrderBookOrdersAsync(marketId, limit: depth, cancellationToken);

            var bids = orderBookOrders.Bids
                .GroupBy(b => ParseDecimal(b.Price))
                .Select(g => new PriceLevel
                {
                    Price = g.Key,
                    Size = g.Sum(o => ParseDecimal(o.RemainingBaseAmount))
                })
                .OrderByDescending(p => p.Price)
                .ToList();

            var asks = orderBookOrders.Asks
                .GroupBy(a => ParseDecimal(a.Price))
                .Select(g => new PriceLevel
                {
                    Price = g.Key,
                    Size = g.Sum(o => ParseDecimal(o.RemainingBaseAmount))
                })
                .OrderBy(p => p.Price)
                .ToList();

            var bestBid = bids.FirstOrDefault()?.Price ?? 0m;
            var bestAsk = asks.FirstOrDefault()?.Price ?? 0m;
            var spread = bestAsk > 0 && bestBid > 0 ? bestAsk - bestBid : 0m;

            var marketData = await _restClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);
            var lastPrice = marketData.LastTradePrice;

            if (lastPrice == 0m && bestBid > 0 && bestAsk > 0)
                lastPrice = (bestBid + bestAsk) / 2m;

            return new OrderBookSnapshot
            {
                MarketId = marketId,
                Timestamp = DateTimeOffset.UtcNow,
                LastPrice = lastPrice,
                BestBid = bestBid,
                BestAsk = bestAsk,
                Spread = spread,
                TotalBidDepth = CalculateTotalDepth(bids),
                TotalAskDepth = CalculateTotalDepth(asks),
                Bids = bids,
                Asks = asks
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get order book from REST for market {MarketId}", marketId);
            throw;
        }
    }

    private async Task<decimal?> GetFundingRateFromRestAsync(int marketId, CancellationToken cancellationToken)
    {
        try
        {
            var fundingRates = await _restClient.GetFundingRatesAsync(cancellationToken);

            var rate = fundingRates.FirstOrDefault(f =>
                f.MarketId == marketId &&
                f.Exchange.Equals("lighter", StringComparison.OrdinalIgnoreCase));

            if (rate == null)
            {
                _logger.LogDebug("No funding rate found for market {MarketId}", marketId);
                return null;
            }

            return decimal.TryParse(rate.Rate, NumberStyles.Any, CultureInfo.InvariantCulture, out var fundingRate)
                ? fundingRate
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get funding rate from REST for market {MarketId}", marketId);
            return null;
        }
    }

    private static OrderBookSnapshot ConvertToOrderBookSnapshot(
        int marketId,
        GridBot.Lighter.Models.WebSocket.OrderBookSnapshot wsBook)
    {
        var bids = wsBook.Bids
            .Select(b => new PriceLevel { Price = b.Price, Size = b.Size })
            .ToList();

        var asks = wsBook.Asks
            .Select(a => new PriceLevel { Price = a.Price, Size = a.Size })
            .ToList();

        return new OrderBookSnapshot
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            LastPrice = wsBook.MidPrice,
            BestBid = wsBook.BestBidPrice,
            BestAsk = wsBook.BestAskPrice,
            Spread = wsBook.Spread,
            TotalBidDepth = CalculateTotalDepth(bids),
            TotalAskDepth = CalculateTotalDepth(asks),
            Bids = bids,
            Asks = asks
        };
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static decimal CalculateTotalDepth(List<PriceLevel> levels)
    {
        return levels.Sum(l => l.Price * l.Size);
    }
}
