using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Models.Orders;
using Microsoft.Extensions.Logging;
using AbstractionsOrderBookSnapshot = GridBot.Abstractions.Models.OrderBook.OrderBookSnapshot;
using AbstractionsPriceLevel = GridBot.Abstractions.Models.OrderBook.PriceLevel;
using LighterOrderBookSnapshot = GridBot.Lighter.Models.WebSocket.OrderBookSnapshot;
using LighterOrderSnapshot = GridBot.Lighter.Models.WebSocket.OrderSnapshot;
using LighterPositionSnapshot = GridBot.Lighter.Models.WebSocket.PositionSnapshot;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts <see cref="ILighterRealtimeState"/> to the <see cref="IRealtimeDataProvider"/> interface.
/// Maps Lighter-specific snapshots to abstraction models.
/// </summary>
internal sealed class LighterRealtimeAdapter : IRealtimeDataProvider
{
    private readonly ILighterRealtimeState _realtimeState;
    private readonly ILighterWebSocketClient _wsClient;
    private readonly LighterMarketMapper _marketMapper;
    private readonly ILogger<LighterRealtimeAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterRealtimeAdapter"/> class.
    /// </summary>
    /// <param name="realtimeState">The Lighter realtime state service.</param>
    /// <param name="wsClient">The WebSocket client for subscriptions.</param>
    /// <param name="marketMapper">The market ID mapper.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterRealtimeAdapter(
        ILighterRealtimeState realtimeState,
        ILighterWebSocketClient wsClient,
        LighterMarketMapper marketMapper,
        ILogger<LighterRealtimeAdapter> logger)
    {
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsConnected => _realtimeState.IsConnected;

    /// <inheritdoc />
    public AbstractionsOrderBookSnapshot? GetOrderBook(string marketId)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);
        var lighterSnapshot = _realtimeState.GetOrderBook(lighterId);

        if (lighterSnapshot == null)
            return null;

        return MapOrderBook(marketId, lighterSnapshot);
    }

    /// <inheritdoc />
    public AccountInfo? GetAccount()
    {
        var lighterSnapshot = _realtimeState.GetAccount();

        if (lighterSnapshot == null)
            return null;

        return MapAccount(lighterSnapshot);
    }

    /// <inheritdoc />
    public IReadOnlyList<OrderInfo> GetOrders(string marketId)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);
        var lighterOrders = _realtimeState.GetOrders(lighterId);

        return lighterOrders
            .Select(o => MapOrder(marketId, o))
            .ToList();
    }

    /// <inheritdoc />
    public decimal? GetCurrentPrice(string marketId)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);
        return _realtimeState.GetCurrentPrice(lighterId);
    }

    /// <inheritdoc />
    public PositionInfo? GetPosition(string marketId)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);
        var account = _realtimeState.GetAccount();

        if (account?.Positions.TryGetValue(lighterId, out var position) != true || position == null)
            return null;

        return MapPosition(marketId, position);
    }

    /// <inheritdoc />
    public bool IsMarketDataReady(string marketId)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);
        return _realtimeState.IsMarketDataReady(lighterId);
    }

    /// <inheritdoc />
    public async Task SubscribeMarketAsync(string marketId, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);

        _logger.LogInformation("Subscribing to market {MarketId} (Lighter ID: {LighterId})", marketId, lighterId);

        await _realtimeState.SubscribeMarketAsync(lighterId, ct);
    }

    /// <inheritdoc />
    public async Task WaitForMarketDataAsync(string marketId, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);
        await _realtimeState.WaitForMarketDataAsync(lighterId, timeout, ct);
    }

    private AbstractionsOrderBookSnapshot MapOrderBook(string marketId, LighterOrderBookSnapshot lighterSnapshot)
    {
        var bids = lighterSnapshot.Bids
            .Select(b => new AbstractionsPriceLevel { Price = b.Item1, Quantity = b.Item2 })
            .ToList();

        var asks = lighterSnapshot.Asks
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

    private AccountInfo MapAccount(AccountSnapshot lighterSnapshot)
    {
        var positions = lighterSnapshot.Positions
            .ToDictionary(
                kvp => _marketMapper.FromLighterId(kvp.Key),
                kvp => MapPosition(_marketMapper.FromLighterId(kvp.Key), kvp.Value));

        return new AccountInfo
        {
            AccountId = lighterSnapshot.AccountId.ToString(),
            Collateral = lighterSnapshot.Collateral,
            AvailableBalance = lighterSnapshot.AvailableBalance,
            PortfolioValue = lighterSnapshot.PortfolioValue,
            Positions = positions,
            LastUpdated = lighterSnapshot.LastUpdated
        };
    }

    private static PositionInfo MapPosition(string marketId, LighterPositionSnapshot lighterPosition)
    {
        return new PositionInfo
        {
            MarketId = marketId,
            Size = lighterPosition.Size,
            EntryPrice = lighterPosition.AvgEntryPrice,
            MarkPrice = 0m, // Not available in snapshot, use market stats
            UnrealizedPnl = lighterPosition.UnrealizedPnl,
            RealizedPnl = 0m, // Not available in snapshot
            Leverage = 1, // Default, not available in snapshot
            LiquidationPrice = lighterPosition.LiquidationPrice,
            MarginMode = lighterPosition.IsCross ? MarginMode.Cross : MarginMode.Isolated
        };
    }

    private static OrderInfo MapOrder(string marketId, LighterOrderSnapshot lighterOrder)
    {
        return new OrderInfo
        {
            OrderId = lighterOrder.OrderIndex.ToString(),
            ClientOrderId = lighterOrder.ClientOrderIndex.ToString(),
            MarketId = marketId,
            Side = lighterOrder.IsBuy ? OrderSide.Buy : OrderSide.Sell,
            Type = Abstractions.Models.Enums.OrderType.Limit, // Snapshot doesn't have type info
            Price = lighterOrder.Price,
            Size = lighterOrder.Size,
            RemainingSize = lighterOrder.Size - lighterOrder.FilledSize,
            TimeInForce = Abstractions.Models.Enums.TimeInForce.GoodTillCancel, // Default
            ReduceOnly = false, // Not available in snapshot
            CreatedAt = DateTimeOffset.UtcNow, // Not available in snapshot
            UpdatedAt = null,
            TriggerPrice = null // Not available in snapshot
        };
    }

    private static Abstractions.Models.Enums.OrderType MapOrderType(string lighterType)
    {
        return lighterType.ToLowerInvariant() switch
        {
            "limit" => Abstractions.Models.Enums.OrderType.Limit,
            "market" => Abstractions.Models.Enums.OrderType.Market,
            "stop_loss" or "stoploss" => Abstractions.Models.Enums.OrderType.StopLoss,
            "stop_loss_limit" or "stoplosslimit" => Abstractions.Models.Enums.OrderType.StopLossLimit,
            "take_profit" or "takeprofit" => Abstractions.Models.Enums.OrderType.TakeProfit,
            "take_profit_limit" or "takeprofitlimit" => Abstractions.Models.Enums.OrderType.TakeProfitLimit,
            _ => Abstractions.Models.Enums.OrderType.Limit
        };
    }

    private static Abstractions.Models.Enums.TimeInForce MapTimeInForce(string lighterTif)
    {
        return lighterTif.ToLowerInvariant() switch
        {
            "ioc" or "immediate_or_cancel" => Abstractions.Models.Enums.TimeInForce.ImmediateOrCancel,
            "gtc" or "good_till_cancel" or "gtt" or "good_till_time" => Abstractions.Models.Enums.TimeInForce.GoodTillCancel,
            "post_only" or "postonly" => Abstractions.Models.Enums.TimeInForce.PostOnly,
            "fok" or "fill_or_kill" => Abstractions.Models.Enums.TimeInForce.FillOrKill,
            _ => Abstractions.Models.Enums.TimeInForce.GoodTillCancel
        };
    }
}
