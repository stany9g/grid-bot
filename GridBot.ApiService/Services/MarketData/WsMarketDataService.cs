using System.Net.Http.Json;
using GridBot.ApiService.Models.Trading;
using GridBot.Lighter;
using GridBot.Lighter.Models.Api;
using Microsoft.Extensions.Logging;
using WsOrderBookSnapshot = GridBot.Lighter.Models.WebSocket.OrderBookSnapshot;

namespace GridBot.ApiService.Services.MarketData;

/// <summary>
/// WebSocket-first market data service.
/// Real-time data from WebSocket, historical candlesticks from REST.
/// </summary>
public sealed class WsMarketDataService : IMarketDataService
{
    private readonly ILighterRealtimeState _realtimeState;
    private readonly HttpClient _httpClient;
    private readonly ILogger<WsMarketDataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WsMarketDataService"/> class.
    /// </summary>
    /// <param name="realtimeState">Real-time state from WebSocket.</param>
    /// <param name="httpClientFactory">HTTP client factory for REST calls.</param>
    /// <param name="logger">Logger instance.</param>
    public WsMarketDataService(
        ILighterRealtimeState realtimeState,
        IHttpClientFactory httpClientFactory,
        ILogger<WsMarketDataService> logger)
    {
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _httpClient = httpClientFactory?.CreateClient("LighterCommandClient")
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken cancellationToken = default)
    {
        var price = _realtimeState.GetCurrentPrice(marketId);

        if (price.HasValue)
        {
            _logger.LogTrace("WebSocket price for market {MarketId}: {Price}", marketId, price.Value);
            return Task.FromResult(price.Value);
        }

        _logger.LogWarning(
            "No price data available for market {MarketId}. WS connected={IsConnected}",
            marketId,
            _realtimeState.IsConnected);

        return Task.FromResult(0m);
    }

    /// <inheritdoc />
    public Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(
        int marketId,
        int depth = 20,
        CancellationToken cancellationToken = default)
    {
        var wsBook = _realtimeState.GetOrderBook(marketId);

        if (wsBook != null)
        {
            _logger.LogTrace(
                "WebSocket order book for market {MarketId}: bid={BestBid}, ask={BestAsk}",
                marketId,
                wsBook.BestBidPrice,
                wsBook.BestAskPrice);

            return Task.FromResult(ConvertToOrderBookSnapshot(marketId, wsBook));
        }

        _logger.LogWarning(
            "No order book data available for market {MarketId}. WS connected={IsConnected}",
            marketId,
            _realtimeState.IsConnected);

        // Return empty snapshot
        return Task.FromResult(new OrderBookSnapshot
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            LastPrice = 0m,
            BestBid = 0m,
            BestAsk = 0m,
            Spread = 0m,
            TotalBidDepth = 0m,
            TotalAskDepth = 0m,
            Bids = [],
            Asks = []
        });
    }

    /// <inheritdoc />
    public async Task<List<CandlestickData>> GetCandlesticksAsync(
        int marketId,
        string resolution,
        int count,
        CancellationToken cancellationToken = default)
    {
        // Candlesticks are historical data - must use REST
        try
        {
            var endTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var periodMs = resolution switch
            {
                "1m" => 60_000L,
                "5m" => 300_000L,
                "15m" => 900_000L,
                "1h" => 3_600_000L,
                "4h" => 14_400_000L,
                "1d" => 86_400_000L,
                _ => 3_600_000L
            };
            var startTimestamp = endTimestamp - (periodMs * count);

            var queryParams = $"?market_id={marketId}&resolution={resolution}&start_timestamp={startTimestamp}&end_timestamp={endTimestamp}&count_back={count}";

            var response = await _httpClient.GetAsync($"candlesticks{queryParams}", cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<CandlesticksResponse>(LighterJsonOptions.Default, cancellationToken);

            if (result == null || !result.IsSuccess)
            {
                _logger.LogWarning("Failed to get candlesticks for market {MarketId}: {Message}",
                    marketId, result?.Message ?? "null response");
                return [];
            }

            return result.Candlesticks.Select(c => new CandlestickData
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
            return [];
        }
    }

    /// <inheritdoc />
    public Task<decimal?> GetFundingRateAsync(int marketId, CancellationToken cancellationToken = default)
    {
        var wsStats = _realtimeState.GetMarketStats(marketId);

        if (wsStats != null)
        {
            _logger.LogTrace(
                "WebSocket funding rate for market {MarketId}: {FundingRate}",
                marketId,
                wsStats.FundingRate);

            return Task.FromResult<decimal?>(wsStats.FundingRate);
        }

        _logger.LogDebug(
            "No funding rate data available for market {MarketId}. WS connected={IsConnected}",
            marketId,
            _realtimeState.IsConnected);

        return Task.FromResult<decimal?>(null);
    }

    private static OrderBookSnapshot ConvertToOrderBookSnapshot(int marketId, WsOrderBookSnapshot wsBook)
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
            TotalBidDepth = bids.Sum(l => l.Price * l.Size),
            TotalAskDepth = asks.Sum(l => l.Price * l.Size),
            Bids = bids,
            Asks = asks
        };
    }
}
