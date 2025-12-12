using System.Collections.Concurrent;
using System.Globalization;
using GridBot.ApiService.Models.Trading;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// Implementation of market data service using Lighter DEX API.
/// Thread-safe singleton service.
/// Uses WebSocket data when available, falls back to REST.
/// </summary>
public sealed class MarketDataService : IMarketDataService
{
    private readonly ILighterQueryClient _queryClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly ILogger<MarketDataService> _logger;

    /// <summary>
    /// Cache for candlestick data to reduce REST API calls.
    /// Key: (marketId, resolution, count), Value: (data, cachedAt)
    /// </summary>
    private readonly ConcurrentDictionary<(int MarketId, string Resolution, int Count), (List<CandlestickData> Data, DateTimeOffset CachedAt)> _candlestickCache = new();

    /// <summary>
    /// Candlestick cache TTL in seconds. Default 300 (5 minutes).
    /// </summary>
    private const int CandlestickCacheTtlSeconds = 300;

    /// <summary>
    /// Maximum acceptable age for WebSocket data in seconds.
    /// Data older than this is considered stale and REST fallback is used.
    /// Note: In illiquid markets, updates are naturally infrequent, so this is generous.
    /// </summary>
    private const int MaxWebSocketDataAgeSeconds = 60;

    /// <summary>
    /// Initializes a new instance of the <see cref="MarketDataService"/> class.
    /// </summary>
    /// <param name="queryClient">Lighter query client.</param>
    /// <param name="realtimeState">WebSocket real-time state for order book and price data.</param>
    /// <param name="logger">Logger instance.</param>
    public MarketDataService(
        ILighterQueryClient queryClient,
        ILighterRealtimeState realtimeState,
        ILogger<MarketDataService> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Checks if WebSocket is connected and has received data.
    /// </summary>
    /// <returns>True if WebSocket is connected and ready.</returns>
    private bool IsWebSocketConnected()
    {
        return _realtimeState.IsConnected && _realtimeState.OldestDataAge.HasValue;
    }

    /// <summary>
    /// Checks if a specific timestamp is fresh enough to be used for trading decisions.
    /// </summary>
    /// <param name="dataTimestamp">The timestamp of the specific data.</param>
    /// <returns>True if the data is fresh, false if stale.</returns>
    private bool IsDataFresh(DateTimeOffset dataTimestamp)
    {
        var age = DateTimeOffset.UtcNow - dataTimestamp;
        return age.TotalSeconds < MaxWebSocketDataAgeSeconds;
    }

    /// <inheritdoc />
    public async Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Try WebSocket first - check connection and get order book for timestamp validation
            if (IsWebSocketConnected())
            {
                var wsOrderBook = _realtimeState.GetOrderBook(marketId);
                if (wsOrderBook != null && wsOrderBook.MidPrice > 0)
                {
                    // Check the order book's own timestamp, not global data age
                    if (IsDataFresh(wsOrderBook.LastUpdate))
                    {
                        _logger.LogDebug("Using WebSocket price for market {MarketId}: {Price}", marketId, wsOrderBook.MidPrice);
                        return wsOrderBook.MidPrice;
                    }

                    var age = DateTimeOffset.UtcNow - wsOrderBook.LastUpdate;
                    _logger.LogWarning(
                        "WebSocket order book is stale ({Age:F1}s old), falling back to REST for market {MarketId}",
                        age.TotalSeconds,
                        marketId);
                }
                else
                {
                    // Try market stats mark price as fallback
                    var wsStats = _realtimeState.GetMarketStats(marketId);
                    if (wsStats != null && wsStats.MarkPrice > 0 && IsDataFresh(wsStats.LastUpdated))
                    {
                        _logger.LogDebug("Using WebSocket mark price for market {MarketId}: {Price}", marketId, wsStats.MarkPrice);
                        return wsStats.MarkPrice;
                    }

                    _logger.LogDebug("WebSocket price not available for market {MarketId}, falling back to REST", marketId);
                }
            }
            else
            {
                _logger.LogDebug("WebSocket not connected for market {MarketId}, using REST", marketId);
            }

            // Fall back to REST - get market metadata for last trade price
            var marketData = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);

            // Use last trade price if available (now a decimal in the API response)
            if (marketData.LastTradePrice > 0)
            {
                return marketData.LastTradePrice;
            }

            // Fall back to mid price from best bid/ask
            var orderBook = await _queryClient.GetOrderBookOrdersAsync(marketId, limit: 1, cancellationToken);
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
            _logger.LogError(ex, "Failed to get current price for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<CandlestickData>> GetCandlesticksAsync(
        int marketId,
        string resolution,
        int count,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = (marketId, resolution, count);

            // Check cache first
            if (_candlestickCache.TryGetValue(cacheKey, out var cached))
            {
                var age = DateTimeOffset.UtcNow - cached.CachedAt;
                if (age.TotalSeconds < CandlestickCacheTtlSeconds)
                {
                    _logger.LogDebug(
                        "Cache HIT for candlesticks: market {MarketId}, resolution {Resolution}, count {Count}, age {AgeSeconds:F1}s",
                        marketId, resolution, count, age.TotalSeconds);
                    return cached.Data;
                }

                _logger.LogDebug(
                    "Cache EXPIRED for candlesticks: market {MarketId}, resolution {Resolution}, count {Count}, age {AgeSeconds:F1}s",
                    marketId, resolution, count, age.TotalSeconds);
            }
            else
            {
                _logger.LogDebug(
                    "Cache MISS for candlesticks: market {MarketId}, resolution {Resolution}, count {Count}",
                    marketId, resolution, count);
            }

            // Fetch from REST API
            var candles = await _queryClient.GetCandlesticksAsync(marketId, resolution, count, cancellationToken);

            var result = candles.Select(c => new CandlestickData
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Timestamp),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume0
            }).ToList();

            // Update cache
            _candlestickCache[cacheKey] = (result, DateTimeOffset.UtcNow);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get candlesticks for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(
        int marketId,
        int depth = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try WebSocket order book first - check connection and data freshness
            if (IsWebSocketConnected())
            {
                var wsOrderBook = _realtimeState.GetOrderBook(marketId);
                if (wsOrderBook != null && wsOrderBook.Bids.Count > 0)
                {
                    // Check the order book's own timestamp, not global data age
                    if (IsDataFresh(wsOrderBook.LastUpdate))
                    {
                        _logger.LogDebug("Using WebSocket order book for market {MarketId}", marketId);
                        return ConvertWebSocketOrderBook(marketId, wsOrderBook, depth);
                    }

                    var age = DateTimeOffset.UtcNow - wsOrderBook.LastUpdate;
                    _logger.LogWarning(
                        "WebSocket order book is stale ({Age:F1}s old), falling back to REST for market {MarketId}",
                        age.TotalSeconds,
                        marketId);
                }
                else
                {
                    _logger.LogDebug("WebSocket order book not available for market {MarketId}, falling back to REST", marketId);
                }
            }
            else
            {
                _logger.LogDebug("WebSocket not connected for market {MarketId}, using REST for order book", marketId);
            }

            // Fall back to REST API
            // Get order book orders (bids and asks)
            var orderBookOrders = await _queryClient.GetOrderBookOrdersAsync(marketId, limit: depth, cancellationToken);

            // Convert OrderBookOrder to aggregated PriceLevel (group by price, sum sizes)
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

            // Get market metadata for last trade price
            var marketData = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);
            var lastPrice = marketData.LastTradePrice;

            if (lastPrice == 0m && bestBid > 0 && bestAsk > 0)
            {
                lastPrice = (bestBid + bestAsk) / 2m;
            }

            return new OrderBookSnapshot
            {
                MarketId = marketId,
                Timestamp = DateTimeOffset.UtcNow, // Use current time since orderBookOrders doesn't have timestamp
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
            _logger.LogError(ex, "Failed to get order book snapshot for market {MarketId}", marketId);
            throw;
        }
    }

    /// <summary>
    /// Converts WebSocket order book snapshot to API order book snapshot model.
    /// </summary>
    private OrderBookSnapshot ConvertWebSocketOrderBook(
        int marketId,
        Lighter.Models.WebSocket.OrderBookSnapshot wsOrderBook,
        int depth)
    {
        // Convert WebSocket bids/asks to API PriceLevel
        var bids = wsOrderBook.Bids
            .Take(depth)
            .Select(b => new PriceLevel { Price = b.Price, Size = b.Size })
            .ToList();

        var asks = wsOrderBook.Asks
            .Take(depth)
            .Select(a => new PriceLevel { Price = a.Price, Size = a.Size })
            .ToList();

        // For LastPrice, use MidPrice from WebSocket or fall back to market stats MarkPrice
        var lastPrice = wsOrderBook.MidPrice;
        if (lastPrice == 0m)
        {
            var marketStats = _realtimeState.GetMarketStats(marketId);
            if (marketStats != null && marketStats.MarkPrice > 0)
            {
                lastPrice = marketStats.MarkPrice;
            }
        }

        return new OrderBookSnapshot
        {
            MarketId = marketId,
            Timestamp = wsOrderBook.LastUpdate,
            LastPrice = lastPrice,
            BestBid = wsOrderBook.BestBidPrice,
            BestAsk = wsOrderBook.BestAskPrice,
            Spread = wsOrderBook.Spread,
            TotalBidDepth = CalculateTotalDepth(bids),
            TotalAskDepth = CalculateTotalDepth(asks),
            Bids = bids,
            Asks = asks
        };
    }

    /// <inheritdoc />
    public async Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken cancellationToken = default)
    {
        try
        {
            var fundingRates = await _queryClient.GetFundingRatesAsync(cancellationToken);

            // Find the Lighter exchange funding rate for this market
            var rate = fundingRates.FirstOrDefault(f =>
                f.MarketId == marketId &&
                f.Exchange.Equals("lighter", StringComparison.OrdinalIgnoreCase));

            if (rate == null)
            {
                _logger.LogDebug("No funding rate found for market {MarketId}", marketId);
                return null;
            }

            if (decimal.TryParse(rate.Rate, NumberStyles.Any, CultureInfo.InvariantCulture, out var fundingRate))
            {
                return fundingRate;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get funding rate for market {MarketId}", marketId);
            return null;
        }
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
