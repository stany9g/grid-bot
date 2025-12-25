using System.Collections.Concurrent;
using System.Globalization;
using GridBot.ApiService.Models.Trading;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.MarketData;

public sealed class MarketDataService : IMarketDataService
{
    private readonly ILighterQueryClient _queryClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly ILogger<MarketDataService> _logger;
    private readonly ConcurrentDictionary<(int, string, int), (List<CandlestickData>, DateTimeOffset)> _candleCache = new();
    private const int CacheTtlSeconds = 300;
    private const int MaxWsDataAgeSeconds = 60;

    public MarketDataService(ILighterQueryClient queryClient, ILighterRealtimeState realtimeState, ILogger<MarketDataService> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default)
    {
        if (_realtimeState.IsConnected)
        {
            var wsOb = _realtimeState.GetOrderBook(marketId);
            if (wsOb != null && wsOb.MidPrice > 0 && (DateTimeOffset.UtcNow - wsOb.LastUpdate).TotalSeconds < MaxWsDataAgeSeconds)
                return wsOb.MidPrice;
        }
        var data = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: ct);
        return data.LastTradePrice;
    }

    public async Task<List<CandlestickData>> GetCandlesticksAsync(int marketId, string resolution, int count, CancellationToken ct = default)
    {
        var key = (marketId, resolution, count);
        if (_candleCache.TryGetValue(key, out var cached) && (DateTimeOffset.UtcNow - cached.Item2).TotalSeconds < CacheTtlSeconds)
            return cached.Item1;

        var candles = await _queryClient.GetCandlesticksAsync(marketId, resolution, count, ct);
        var result = candles.Select(c => new CandlestickData
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Timestamp),
            Open = c.Open, High = c.High, Low = c.Low, Close = c.Close, Volume = c.Volume0
        }).ToList();
        _candleCache[key] = (result, DateTimeOffset.UtcNow);
        return result;
    }

    public async Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(int marketId, int depth = 20, CancellationToken ct = default)
    {
        if (_realtimeState.IsConnected)
        {
            var wsOb = _realtimeState.GetOrderBook(marketId);
            if (wsOb != null && wsOb.Bids.Count > 0 && (DateTimeOffset.UtcNow - wsOb.LastUpdate).TotalSeconds < MaxWsDataAgeSeconds)
                return ConvertWsOrderBook(marketId, wsOb, depth);
        }

        var orders = await _queryClient.GetOrderBookOrdersAsync(marketId, limit: depth, ct);
        var bids = orders.Bids.GroupBy(b => ParseDecimal(b.Price))
            .Select(g => new PriceLevel { Price = g.Key, Size = g.Sum(o => ParseDecimal(o.RemainingBaseAmount)) })
            .OrderByDescending(p => p.Price).ToList();
        var asks = orders.Asks.GroupBy(a => ParseDecimal(a.Price))
            .Select(g => new PriceLevel { Price = g.Key, Size = g.Sum(o => ParseDecimal(o.RemainingBaseAmount)) })
            .OrderBy(p => p.Price).ToList();

        var bestBid = bids.FirstOrDefault()?.Price ?? 0;
        var bestAsk = asks.FirstOrDefault()?.Price ?? 0;
        var data = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: ct);
        var lastPrice = data.LastTradePrice > 0 ? data.LastTradePrice : (bestBid + bestAsk) / 2;

        return new OrderBookSnapshot
        {
            MarketId = marketId, Timestamp = DateTimeOffset.UtcNow, LastPrice = lastPrice,
            BestBid = bestBid, BestAsk = bestAsk, Spread = bestAsk - bestBid,
            TotalBidDepth = bids.Sum(l => l.Price * l.Size), TotalAskDepth = asks.Sum(l => l.Price * l.Size),
            Bids = bids, Asks = asks
        };
    }

    public async Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken ct = default)
    {
        var rates = await _queryClient.GetFundingRatesAsync(ct);
        var rate = rates.FirstOrDefault(f => f.MarketId == marketId && f.Exchange.Equals("lighter", StringComparison.OrdinalIgnoreCase));
        return rate != null && decimal.TryParse(rate.Rate, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : null;
    }

    private OrderBookSnapshot ConvertWsOrderBook(int marketId, Lighter.Models.WebSocket.OrderBookSnapshot ws, int depth)
    {
        var bids = ws.Bids.Take(depth).Select(b => new PriceLevel { Price = b.Price, Size = b.Size }).ToList();
        var asks = ws.Asks.Take(depth).Select(a => new PriceLevel { Price = a.Price, Size = a.Size }).ToList();
        return new OrderBookSnapshot
        {
            MarketId = marketId, Timestamp = ws.LastUpdate, LastPrice = ws.MidPrice,
            BestBid = ws.BestBidPrice, BestAsk = ws.BestAskPrice, Spread = ws.Spread,
            TotalBidDepth = bids.Sum(l => l.Price * l.Size), TotalAskDepth = asks.Sum(l => l.Price * l.Size),
            Bids = bids, Asks = asks
        };
    }

    private static decimal ParseDecimal(string? v) => decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var r) ? r : 0;
}
