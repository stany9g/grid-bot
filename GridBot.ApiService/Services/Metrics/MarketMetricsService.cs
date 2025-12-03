using System.Collections.Concurrent;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.OrderBook;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Metrics;

/// <summary>
/// Implementation of market metrics aggregation service.
/// Thread-safe with built-in caching.
/// </summary>
public sealed class MarketMetricsService : IMarketMetricsService
{
    private readonly IMarketDataService _marketDataService;
    private readonly IIndicatorService _indicatorService;
    private readonly IOrderBookAnalyzer _orderBookAnalyzer;
    private readonly ILogger<MarketMetricsService> _logger;

    private readonly ConcurrentDictionary<int, CachedMetrics> _cache = new();
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of candlesticks to fetch for indicator calculations.
    /// 100 candles should be sufficient for EMA50, MACD(26), ATR(14), ADX(14).
    /// </summary>
    private const int CandlestickCount = 100;

    /// <summary>
    /// Candlestick resolution for indicator calculations.
    /// </summary>
    private const string CandlestickResolution = "1h";

    /// <summary>
    /// Initializes a new instance of the <see cref="MarketMetricsService"/> class.
    /// </summary>
    /// <param name="marketDataService">Market data service.</param>
    /// <param name="indicatorService">Indicator calculation service.</param>
    /// <param name="orderBookAnalyzer">Order book analyzer.</param>
    /// <param name="logger">Logger instance.</param>
    public MarketMetricsService(
        IMarketDataService marketDataService,
        IIndicatorService indicatorService,
        IOrderBookAnalyzer orderBookAnalyzer,
        ILogger<MarketMetricsService> logger)
    {
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _indicatorService = indicatorService ?? throw new ArgumentNullException(nameof(indicatorService));
        _orderBookAnalyzer = orderBookAnalyzer ?? throw new ArgumentNullException(nameof(orderBookAnalyzer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MarketMetrics> GetMarketMetricsAsync(int marketId, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cache.TryGetValue(marketId, out var cached) && !cached.IsExpired(_cacheDuration))
        {
            _logger.LogDebug("Returning cached metrics for market {MarketId}", marketId);
            return cached.Metrics.Clone();
        }

        return await FetchAndCacheMetricsAsync(marketId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RefreshMetricsAsync(int marketId, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(marketId, out _);
        await FetchAndCacheMetricsAsync(marketId, cancellationToken);
    }

    private async Task<MarketMetrics> FetchAndCacheMetricsAsync(int marketId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching fresh metrics for market {MarketId}", marketId);

        var metrics = new MarketMetrics
        {
            MarketId = marketId,
            LastUpdated = DateTimeOffset.UtcNow
        };

        try
        {
            // Fetch data in parallel where possible
            var candlesTask = _marketDataService.GetCandlesticksAsync(
                marketId, CandlestickResolution, CandlestickCount, cancellationToken);
            var orderBookTask = _marketDataService.GetOrderBookSnapshotAsync(marketId, 20, cancellationToken);
            var fundingTask = _marketDataService.GetFundingRateAsync(marketId, cancellationToken);

            await Task.WhenAll(candlesTask, orderBookTask, fundingTask);

            var candles = await candlesTask;
            var orderBook = await orderBookTask;
            var fundingRate = await fundingTask;

            // Set current price from order book
            metrics.CurrentPrice = orderBook.LastPrice;

            // Calculate technical indicators if we have enough data
            if (candles.Count >= 50)
            {
                var closePrices = candles.Select(c => c.Close).ToList();

                // ATR
                metrics.Atr14 = _indicatorService.CalculateAtr(candles, 14);

                // EMAs
                metrics.Ema20 = _indicatorService.CalculateEma(closePrices, 20);
                metrics.Ema50 = _indicatorService.CalculateEma(closePrices, 50);

                // MACD
                var macd = _indicatorService.CalculateMacd(closePrices, 12, 26, 9);
                metrics.MacdLine = macd.MacdLine;
                metrics.MacdSignal = macd.SignalLine;
                metrics.MacdHistogram = macd.Histogram;

                // ADX
                metrics.Adx = _indicatorService.CalculateAdx(candles, 14);
            }
            else
            {
                _logger.LogWarning(
                    "Insufficient candle data for market {MarketId}: got {Count}, need at least 50",
                    marketId, candles.Count);
            }

            // Calculate volume metrics from candles
            if (candles.Count > 0)
            {
                // 24h volume from most recent candles (24 hourly candles)
                var recent24h = candles.TakeLast(24).ToList();
                metrics.Volume24h = recent24h.Sum(c => c.Volume * c.Close);

                // 7-day average (168 hourly candles or however many we have)
                var recent7d = candles.TakeLast(168).ToList();
                if (recent7d.Count > 0)
                {
                    var totalVolume = recent7d.Sum(c => c.Volume * c.Close);
                    var daysOfData = recent7d.Count / 24m;
                    // Only calculate average if we have at least 1 day of data
                    metrics.Volume7dAvg = daysOfData >= 1 ? totalVolume / daysOfData : 0m;
                }
            }

            // Order book analysis
            var analysis = _orderBookAnalyzer.AnalyzeOrderBook(orderBook);
            metrics.OrderBookBidDepth = analysis.TotalBidDepth;
            metrics.OrderBookAskDepth = analysis.TotalAskDepth;
            metrics.BidAskSpread = analysis.SpreadPercent;

            // Funding rate
            metrics.FundingRate = fundingRate;

            // Cache the metrics
            _cache[marketId] = new CachedMetrics(metrics);

            return metrics.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch market metrics for market {MarketId}", marketId);
            throw;
        }
    }

    private sealed class CachedMetrics
    {
        public MarketMetrics Metrics { get; }
        public DateTimeOffset CachedAt { get; }

        public CachedMetrics(MarketMetrics metrics)
        {
            Metrics = metrics;
            CachedAt = DateTimeOffset.UtcNow;
        }

        public bool IsExpired(TimeSpan duration)
        {
            return DateTimeOffset.UtcNow - CachedAt > duration;
        }
    }
}
