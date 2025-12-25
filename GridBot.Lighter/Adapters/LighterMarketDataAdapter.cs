using GridBot.Abstractions.Models.Market;
using GridBot.Abstractions.Trading;
using Microsoft.Extensions.Logging;
using AbstractionsOrderBookSnapshot = GridBot.Abstractions.Models.OrderBook.OrderBookSnapshot;
using AbstractionsPriceLevel = GridBot.Abstractions.Models.OrderBook.PriceLevel;
using LighterOrderBookSnapshot = GridBot.Lighter.Models.WebSocket.OrderBookSnapshot;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts <see cref="ILighterQueryClient"/> to the <see cref="IMarketDataClient"/> interface.
/// Maps Lighter market data models to abstraction models.
/// </summary>
internal sealed class LighterMarketDataAdapter : IMarketDataClient
{
    private readonly ILighterQueryClient _queryClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly LighterMarketMapper _marketMapper;
    private readonly ILogger<LighterMarketDataAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterMarketDataAdapter"/> class.
    /// </summary>
    /// <param name="queryClient">The Lighter query client.</param>
    /// <param name="realtimeState">The realtime state service.</param>
    /// <param name="marketMapper">The market ID mapper.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterMarketDataAdapter(
        ILighterQueryClient queryClient,
        ILighterRealtimeState realtimeState,
        LighterMarketMapper marketMapper,
        ILogger<LighterMarketDataAdapter> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<decimal> GetCurrentPriceAsync(string marketId, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);

        // Prefer realtime data
        var price = _realtimeState.GetCurrentPrice(lighterId);
        if (price.HasValue && price.Value > 0)
        {
            return Task.FromResult(price.Value);
        }

        // If no realtime data, try order book
        var orderBook = _realtimeState.GetOrderBook(lighterId);
        if (orderBook != null && orderBook.MidPrice > 0)
        {
            return Task.FromResult(orderBook.MidPrice);
        }

        _logger.LogWarning("No price data available for market {MarketId}", marketId);
        return Task.FromResult(0m);
    }

    /// <inheritdoc />
    public async Task<AbstractionsOrderBookSnapshot> GetOrderBookAsync(string marketId, int depth = 20, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);

        // Prefer realtime data
        var realtimeBook = _realtimeState.GetOrderBook(lighterId);
        if (realtimeBook != null && _realtimeState.IsConnected)
        {
            return MapOrderBook(marketId, realtimeBook, depth);
        }

        // Fall back to REST API
        _logger.LogDebug("Fetching order book from REST API for market {MarketId}", marketId);
        var orders = await _queryClient.GetOrderBookOrdersAsync(lighterId, depth, ct);

        return MapOrderBookOrders(marketId, orders);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CandlestickData>> GetCandlesticksAsync(string marketId, string resolution, int count, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);

        _logger.LogDebug(
            "Fetching {Count} candles for market {MarketId} with resolution {Resolution}",
            count, marketId, resolution);

        var candles = await _queryClient.GetCandlesticksAsync(lighterId, resolution, count, ct);

        return candles
            .Select(c => new CandlestickData
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(c.Timestamp),
                Open = c.Open,
                High = c.High,
                Low = c.Low,
                Close = c.Close,
                Volume = c.Volume0 // Base asset volume
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MarketInfo>> GetMarketsAsync(CancellationToken ct = default)
    {
        var orderBooks = await _queryClient.GetOrderBooksAsync(ct);

        return orderBooks
            .Select(MapMarketInfo)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<FundingRateInfo?> GetFundingRateAsync(string marketId, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);

        // First check realtime market stats
        var stats = _realtimeState.GetMarketStats(lighterId);
        if (stats != null && stats.FundingRate != 0)
        {
            return new FundingRateInfo
            {
                MarketId = marketId,
                FundingRate = stats.FundingRate,
                NextFundingTime = GetNextFundingTime(),
                FundingIntervalHours = 8
            };
        }

        // Fall back to REST API
        var fundingRates = await _queryClient.GetFundingRatesAsync(ct);

        var lighterRate = fundingRates.FirstOrDefault(r =>
            r.MarketId == lighterId &&
            r.Exchange.Equals("lighter", StringComparison.OrdinalIgnoreCase));

        if (lighterRate == null)
            return null;

        return new FundingRateInfo
        {
            MarketId = marketId,
            FundingRate = decimal.TryParse(lighterRate.Rate, out var rate) ? rate : 0,
            NextFundingTime = GetNextFundingTime(),
            FundingIntervalHours = 8
        };
    }

    private AbstractionsOrderBookSnapshot MapOrderBook(string marketId, LighterOrderBookSnapshot lighterSnapshot, int depth)
    {
        var bids = lighterSnapshot.Bids
            .Take(depth)
            .Select(b => new AbstractionsPriceLevel { Price = b.Item1, Quantity = b.Item2 })
            .ToList();

        var asks = lighterSnapshot.Asks
            .Take(depth)
            .Select(a => new AbstractionsPriceLevel { Price = a.Item1, Quantity = a.Item2 })
            .ToList();

        return new AbstractionsOrderBookSnapshot
        {
            MarketId = marketId,
            Bids = bids,
            Asks = asks,
            BestBidPrice = lighterSnapshot.BestBidPrice,
            BestAskPrice = lighterSnapshot.BestAskPrice,
            Spread = lighterSnapshot.Spread,
            Timestamp = lighterSnapshot.LastUpdate
        };
    }

    private static AbstractionsOrderBookSnapshot MapOrderBookOrders(string marketId, Models.Api.OrderBookOrdersResponse orders)
    {
        var bids = orders.Bids
            .Select(b => new AbstractionsPriceLevel
            {
                Price = decimal.TryParse(b.Price, out var p) ? p : 0,
                Quantity = decimal.TryParse(b.RemainingBaseAmount, out var s) ? s : 0
            })
            .OrderByDescending(p => p.Price)
            .ToList();

        var asks = orders.Asks
            .Select(a => new AbstractionsPriceLevel
            {
                Price = decimal.TryParse(a.Price, out var p) ? p : 0,
                Quantity = decimal.TryParse(a.RemainingBaseAmount, out var s) ? s : 0
            })
            .OrderBy(p => p.Price)
            .ToList();

        var bestBid = bids.FirstOrDefault()?.Price ?? 0;
        var bestAsk = asks.FirstOrDefault()?.Price ?? 0;

        return new AbstractionsOrderBookSnapshot
        {
            MarketId = marketId,
            Bids = bids,
            Asks = asks,
            BestBidPrice = bestBid,
            BestAskPrice = bestAsk,
            Spread = bestAsk > 0 && bestBid > 0 ? bestAsk - bestBid : 0,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static MarketInfo MapMarketInfo(Models.Api.OrderBook orderBook)
    {
        // Parse base and quote from symbol (e.g., "BTC-USDC" -> "BTC", "USDC")
        var parts = orderBook.Symbol.Split('-');
        var baseAsset = parts.Length > 0 ? parts[0] : orderBook.Symbol;
        var quoteAsset = parts.Length > 1 ? parts[1] : "USDC";

        // Calculate tick size and step size from decimals
        var tickSize = (decimal)Math.Pow(10, -orderBook.PriceDecimals);
        var stepSize = (decimal)Math.Pow(10, -orderBook.SizeDecimals);

        return new MarketInfo
        {
            MarketId = orderBook.MarketId.ToString(),
            Symbol = orderBook.Symbol,
            BaseAsset = baseAsset,
            QuoteAsset = quoteAsset,
            IsPerpetual = true, // Lighter is perpetuals only
            MinOrderSize = decimal.TryParse(orderBook.MinBaseAmount, out var minBase) ? minBase : 0,
            TickSize = tickSize,
            StepSize = stepSize,
            MaxLeverage = int.TryParse(orderBook.MaxLeverage, out var maxLev) ? maxLev : 20,
            IsActive = orderBook.Status == "active",
            MakerFeeRate = decimal.TryParse(orderBook.MakerFee, out var makerFee) ? makerFee : 0,
            TakerFeeRate = decimal.TryParse(orderBook.TakerFee, out var takerFee) ? takerFee : 0
        };
    }

    private static DateTimeOffset GetNextFundingTime()
    {
        // Lighter uses 8-hour funding intervals at 00:00, 08:00, 16:00 UTC
        var now = DateTimeOffset.UtcNow;
        var hour = now.Hour;

        int nextFundingHour;
        if (hour < 8)
            nextFundingHour = 8;
        else if (hour < 16)
            nextFundingHour = 16;
        else
            nextFundingHour = 24; // Next day 00:00

        var nextFunding = new DateTimeOffset(
            now.Year, now.Month, now.Day,
            nextFundingHour % 24, 0, 0,
            TimeSpan.Zero);

        if (nextFundingHour == 24)
            nextFunding = nextFunding.AddDays(1);

        return nextFunding;
    }
}
