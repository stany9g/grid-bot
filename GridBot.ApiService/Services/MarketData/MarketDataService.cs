using System.Collections.Concurrent;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Trading;
using GridBot.ApiService.Models.Trading;
using Microsoft.Extensions.Logging;

using AbstractionsCandlestick = GridBot.Abstractions.Models.Market.CandlestickData;

namespace GridBot.ApiService.Services.MarketData;

public sealed class MarketDataService : IMarketDataService
{
    private readonly IMarketDataClient _marketDataClient;
    private readonly IRealtimeDataProvider _realtimeProvider;
    private readonly ILogger<MarketDataService> _logger;
    private readonly ConcurrentDictionary<(int, string, int), (List<CandlestickData>, DateTimeOffset)> _candleCache = new();
    private const int CacheTtlSeconds = 300;
    private const int MaxWsDataAgeSeconds = 60;

    public MarketDataService(
        IMarketDataClient marketDataClient,
        IRealtimeDataProvider realtimeProvider,
        ILogger<MarketDataService> logger)
    {
        _marketDataClient = marketDataClient ?? throw new ArgumentNullException(nameof(marketDataClient));
        _realtimeProvider = realtimeProvider ?? throw new ArgumentNullException(nameof(realtimeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default)
    {
        var marketIdStr = marketId.ToString();

        // Try realtime data first
        if (_realtimeProvider.IsConnected)
        {
            var realtimePrice = _realtimeProvider.GetCurrentPrice(marketIdStr);
            if (realtimePrice.HasValue && realtimePrice.Value > 0)
                return realtimePrice.Value;
        }

        // Fall back to REST API
        return await _marketDataClient.GetCurrentPriceAsync(marketIdStr, ct);
    }

    public async Task<List<CandlestickData>> GetCandlesticksAsync(int marketId, string resolution, int count, CancellationToken ct = default)
    {
        var key = (marketId, resolution, count);
        if (_candleCache.TryGetValue(key, out var cached) && (DateTimeOffset.UtcNow - cached.Item2).TotalSeconds < CacheTtlSeconds)
            return cached.Item1;

        var marketIdStr = marketId.ToString();
        var candles = await _marketDataClient.GetCandlesticksAsync(marketIdStr, resolution, count, ct);
        var result = candles.Select(c => new CandlestickData
        {
            Timestamp = c.Timestamp,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume
        }).ToList();
        _candleCache[key] = (result, DateTimeOffset.UtcNow);
        return result;
    }

    public async Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(int marketId, int depth = 20, CancellationToken ct = default)
    {
        var marketIdStr = marketId.ToString();

        // Try realtime data first
        if (_realtimeProvider.IsConnected)
        {
            var wsOb = _realtimeProvider.GetOrderBook(marketIdStr);
            if (wsOb != null && wsOb.Bids.Count > 0 && (DateTimeOffset.UtcNow - wsOb.Timestamp).TotalSeconds < MaxWsDataAgeSeconds)
                return ConvertAbstractionsOrderBook(marketId, wsOb, depth);
        }

        // Fall back to REST API
        var orderBook = await _marketDataClient.GetOrderBookAsync(marketIdStr, depth, ct);
        return ConvertAbstractionsOrderBook(marketId, orderBook, depth);
    }

    public async Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken ct = default)
    {
        var marketIdStr = marketId.ToString();
        var fundingInfo = await _marketDataClient.GetFundingRateAsync(marketIdStr, ct);
        return fundingInfo?.FundingRate;
    }

    private OrderBookSnapshot ConvertAbstractionsOrderBook(
        int marketId,
        GridBot.Abstractions.Models.OrderBook.OrderBookSnapshot source,
        int depth)
    {
        var bids = source.Bids.Take(depth).Select(b => new PriceLevel { Price = b.Price, Size = b.Quantity }).ToList();
        var asks = source.Asks.Take(depth).Select(a => new PriceLevel { Price = a.Price, Size = a.Quantity }).ToList();
        return new OrderBookSnapshot
        {
            MarketId = marketId,
            Timestamp = source.Timestamp,
            LastPrice = source.MidPrice,
            BestBid = source.BestBidPrice,
            BestAsk = source.BestAskPrice,
            Spread = source.Spread,
            TotalBidDepth = bids.Sum(l => l.Price * l.Size),
            TotalAskDepth = asks.Sum(l => l.Price * l.Size),
            Bids = bids,
            Asks = asks
        };
    }
}
