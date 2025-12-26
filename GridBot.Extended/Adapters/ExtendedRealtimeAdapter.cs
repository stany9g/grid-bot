using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using GridBot.Abstractions.Communication;
using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Models.Orders;
using GridBot.Extended.Models.WebSocket;
using Microsoft.Extensions.Logging;
using AbstractionOrderBookSnapshot = GridBot.Abstractions.Models.OrderBook.OrderBookSnapshot;
using PriceLevel = GridBot.Abstractions.Models.OrderBook.PriceLevel;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Provides real-time data from Extended WebSocket streams.
/// Implements <see cref="IRealtimeDataProvider"/>.
/// </summary>
internal sealed class ExtendedRealtimeAdapter : IRealtimeDataProvider
{
    private readonly IExtendedWebSocketClient _wsClient;
    private readonly IExtendedHttpClient _httpClient;
    private readonly ExtendedOrderAdapter _orderAdapter;
    private readonly ILogger<ExtendedRealtimeAdapter> _logger;

    private readonly ConcurrentDictionary<string, AbstractionOrderBookSnapshot> _orderBooks = new();
    // Thread-safe: ConcurrentDictionary<MarketId, ConcurrentDictionary<OrderId, OrderInfo>>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, OrderInfo>> _orders = new();
    private readonly ConcurrentDictionary<string, PositionInfo> _positions = new();
    private volatile AccountInfo? _account;
    private DateTimeOffset _lastDataReceived = DateTimeOffset.MinValue;
    private CancellationTokenSource? _channelProcessorCts;
    private Task? _channelProcessorTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedRealtimeAdapter"/> class.
    /// </summary>
    public ExtendedRealtimeAdapter(
        IExtendedWebSocketClient wsClient,
        IExtendedHttpClient httpClient,
        ExtendedOrderAdapter orderAdapter,
        ILogger<ExtendedRealtimeAdapter> logger)
    {
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _orderAdapter = orderAdapter ?? throw new ArgumentNullException(nameof(orderAdapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsConnected => _wsClient.IsConnected;

    /// <summary>
    /// Gets the time since last data was received.
    /// </summary>
    public TimeSpan? DataAge => _lastDataReceived == DateTimeOffset.MinValue
        ? null
        : DateTimeOffset.UtcNow - _lastDataReceived;

    /// <summary>
    /// Gets whether data is considered stale (> 5 seconds).
    /// </summary>
    public bool IsDataStale => DataAge?.TotalSeconds > 5;

    /// <inheritdoc />
    public AbstractionOrderBookSnapshot? GetOrderBook(string marketId)
    {
        return _orderBooks.TryGetValue(marketId, out var snapshot) ? snapshot : null;
    }

    /// <inheritdoc />
    public AccountInfo? GetAccount() => _account;

    /// <inheritdoc />
    public IReadOnlyList<OrderInfo> GetOrders(string marketId)
    {
        return _orders.TryGetValue(marketId, out var marketOrders)
            ? marketOrders.Values.ToList()
            : [];
    }

    /// <inheritdoc />
    public decimal? GetCurrentPrice(string marketId)
    {
        var orderBook = GetOrderBook(marketId);
        if (orderBook == null)
            return null;

        if (orderBook.BestBidPrice > 0 && orderBook.BestAskPrice > 0)
            return (orderBook.BestBidPrice + orderBook.BestAskPrice) / 2m;

        return orderBook.BestBidPrice > 0 ? orderBook.BestBidPrice : orderBook.BestAskPrice;
    }

    /// <inheritdoc />
    public PositionInfo? GetPosition(string marketId)
    {
        return _positions.TryGetValue(marketId, out var position) ? position : null;
    }

    /// <inheritdoc />
    public bool IsMarketDataReady(string marketId)
    {
        return _orderBooks.ContainsKey(marketId);
    }

    /// <inheritdoc />
    public async Task SubscribeMarketAsync(string marketId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketId);

        await _wsClient.SubscribeOrderBookAsync(marketId, ct);
        _logger.LogInformation("Subscribed to market data: {Market}", marketId);
    }

    /// <inheritdoc />
    public async Task WaitForMarketDataAsync(string marketId, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!IsMarketDataReady(marketId))
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException($"Timeout waiting for market data: {marketId}");
            }

            ct.ThrowIfCancellationRequested();
            await Task.Delay(100, ct);
        }
    }

    /// <summary>
    /// Starts processing WebSocket channel messages.
    /// </summary>
    public void StartProcessing()
    {
        _channelProcessorCts = new CancellationTokenSource();
        _channelProcessorTask = Task.Run(() => ProcessChannelsAsync(_channelProcessorCts.Token));
    }

    /// <summary>
    /// Stops processing WebSocket channel messages.
    /// </summary>
    public async Task StopProcessingAsync()
    {
        if (_channelProcessorCts != null)
        {
            await _channelProcessorCts.CancelAsync();
            if (_channelProcessorTask != null)
            {
                try
                {
                    await _channelProcessorTask;
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }
            _channelProcessorCts.Dispose();
            _channelProcessorCts = null;
        }
    }

    private async Task ProcessChannelsAsync(CancellationToken ct)
    {
        var tasks = new List<Task>
        {
            ProcessOrderBookUpdatesAsync(ct),
            ProcessOrderUpdatesAsync(ct),
            ProcessPositionUpdatesAsync(ct),
            ProcessBalanceUpdatesAsync(ct)
        };

        await Task.WhenAll(tasks);
    }

    private async Task ProcessOrderBookUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var update in _wsClient.OrderBookUpdates.ReadAllAsync(ct))
            {
                HandleOrderBookUpdate(update);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected
        }
    }

    private async Task ProcessOrderUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var update in _wsClient.OrderUpdates.ReadAllAsync(ct))
            {
                HandleOrderUpdate(update);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected
        }
    }

    private async Task ProcessPositionUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var update in _wsClient.PositionUpdates.ReadAllAsync(ct))
            {
                HandlePositionUpdate(update);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected
        }
    }

    private async Task ProcessBalanceUpdatesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var update in _wsClient.BalanceUpdates.ReadAllAsync(ct))
            {
                HandleBalanceUpdate(update);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Expected
        }
    }

    /// <summary>
    /// Reconciles local state with REST API.
    /// Call this after WebSocket reconnection.
    /// </summary>
    public async Task ReconcileStateAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Reconciling realtime state with REST API...");

        try
        {
            // Fetch current orders
            var orders = await _httpClient.GetOrdersAsync(null, ct);
            _orders.Clear();

            foreach (var order in orders.Where(o => o.Status is "open" or "partial"))
            {
                var orderInfo = MapOrder(order);
                var marketOrders = _orders.GetOrAdd(order.Market, _ => new ConcurrentDictionary<string, OrderInfo>());
                marketOrders[orderInfo.OrderId] = orderInfo;
            }

            // Fetch current positions
            var positions = await _httpClient.GetPositionsAsync(ct);
            _positions.Clear();

            foreach (var pos in positions)
            {
                if (decimal.TryParse(pos.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var size) && size != 0)
                {
                    _positions[pos.Market] = MapPosition(pos);
                }
            }

            _logger.LogInformation(
                "Reconciliation complete: {OrderCount} orders, {PositionCount} positions",
                _orders.Sum(x => x.Value.Count),
                _positions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reconcile state");
            throw;
        }
    }

    private void HandleOrderBookUpdate(OrderBookUpdateEvent e)
    {
        _lastDataReceived = DateTimeOffset.UtcNow;

        // Convert from local OrderBookSnapshot to abstraction OrderBookSnapshot
        var bids = e.Snapshot.Bids.Select(b => new PriceLevel { Price = b.Price, Quantity = b.Size }).ToList();
        var asks = e.Snapshot.Asks.Select(a => new PriceLevel { Price = a.Price, Quantity = a.Size }).ToList();

        var snapshot = new AbstractionOrderBookSnapshot
        {
            MarketId = e.MarketId,
            BestBidPrice = e.Snapshot.BestBidPrice,
            BestAskPrice = e.Snapshot.BestAskPrice,
            Spread = e.Snapshot.Spread,
            Timestamp = e.Timestamp,
            Bids = bids,
            Asks = asks
        };

        _orderBooks[e.MarketId] = snapshot;
    }

    private void HandleOrderUpdate(OrderUpdateEvent e)
    {
        _lastDataReceived = DateTimeOffset.UtcNow;

        // Update order adapter state machine
        if (e.Status is "open" or "partial")
        {
            _orderAdapter.ConfirmOrder(e.ClientOrderId ?? e.OrderId, e.OrderId);
        }
        else if (e.Status == "rejected")
        {
            _orderAdapter.RejectOrder(e.ClientOrderId ?? e.OrderId, e.RejectReason ?? "Rejected");
        }
        else if (e.Status is "cancelled" or "canceled")
        {
            // Confirm cancellation for async-aware cancel tracking
            _orderAdapter.ConfirmCancellation(e.ClientOrderId ?? e.OrderId);
        }

        // Update local order cache
        UpdateOrderCache(e);
    }

    private void HandlePositionUpdate(PositionUpdateEvent e)
    {
        _lastDataReceived = DateTimeOffset.UtcNow;

        if (e.Size == 0)
        {
            _positions.TryRemove(e.MarketId, out _);
        }
        else
        {
            _positions[e.MarketId] = new PositionInfo
            {
                MarketId = e.MarketId,
                Size = e.Size,
                EntryPrice = e.EntryPrice,
                MarkPrice = e.MarkPrice,
                UnrealizedPnl = e.UnrealizedPnl,
                Leverage = e.Leverage,
                LiquidationPrice = e.LiquidationPrice
            };
        }
    }

    private void HandleBalanceUpdate(BalanceUpdateEvent e)
    {
        _lastDataReceived = DateTimeOffset.UtcNow;

        // Update account info with new balance
        if (_account != null)
        {
            _account = _account with
            {
                Collateral = e.Total,
                AvailableBalance = e.Available,
                PortfolioValue = e.Total + _account.TotalUnrealizedPnl,
                LastUpdated = DateTimeOffset.UtcNow
            };
        }
    }

    private void UpdateOrderCache(OrderUpdateEvent e)
    {
        var marketOrders = _orders.GetOrAdd(e.MarketId, _ => new ConcurrentDictionary<string, OrderInfo>());

        if (e.Status is "filled" or "cancelled" or "rejected")
        {
            // Remove from active orders (thread-safe)
            marketOrders.TryRemove(e.OrderId, out _);
        }
        else
        {
            // Update or add order (thread-safe)
            marketOrders[e.OrderId] = new OrderInfo
            {
                OrderId = e.OrderId,
                ClientOrderId = e.ClientOrderId ?? e.OrderId,
                MarketId = e.MarketId,
                Side = e.IsBuy ? OrderSide.Buy : OrderSide.Sell,
                Type = OrderType.Limit,
                Price = e.Price,
                Size = e.Size,
                RemainingSize = e.RemainingSize,
                Status = MapOrderStatus(e.Status),
                CreatedAt = e.Timestamp
            };
        }
    }

    private static OrderInfo MapOrder(Models.Api.OrderResponse order)
    {
        var price = decimal.TryParse(order.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0m;
        var qty = decimal.TryParse(order.Qty, NumberStyles.Any, CultureInfo.InvariantCulture, out var q) ? q : 0m;
        var filled = decimal.TryParse(order.FilledQty, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0m;

        return new OrderInfo
        {
            OrderId = order.Id,
            ClientOrderId = order.ClientOrderId ?? order.Id,
            MarketId = order.Market,
            Side = order.Side == "BUY" ? OrderSide.Buy : OrderSide.Sell,
            Type = order.Type == "market" ? OrderType.Market : OrderType.Limit,
            Price = price,
            Size = qty,
            RemainingSize = qty - filled,
            Status = MapOrderStatus(order.Status),
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(order.CreatedAt)
        };
    }

    private static PositionInfo MapPosition(Models.Api.PositionResponse pos)
    {
        var size = decimal.TryParse(pos.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0m;
        var entry = decimal.TryParse(pos.EntryPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var e) ? e : 0m;
        var mark = decimal.TryParse(pos.MarkPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var m) ? m : 0m;
        var pnl = decimal.TryParse(pos.UnrealizedPnl, NumberStyles.Any, CultureInfo.InvariantCulture, out var pnlVal) ? pnlVal : 0m;
        var liq = decimal.TryParse(pos.LiquidationPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : (decimal?)null;
        var realizedPnl = decimal.TryParse(pos.RealizedPnl, NumberStyles.Any, CultureInfo.InvariantCulture, out var rpnl) ? rpnl : 0m;

        var marginMode = pos.MarginMode?.ToLowerInvariant() switch
        {
            "isolated" => MarginMode.Isolated,
            _ => MarginMode.Cross
        };

        return new PositionInfo
        {
            MarketId = pos.Market,
            Size = size,
            EntryPrice = entry,
            MarkPrice = mark,
            UnrealizedPnl = pnl,
            RealizedPnl = realizedPnl,
            Leverage = pos.Leverage,
            LiquidationPrice = liq,
            MarginMode = marginMode
        };
    }

    private static OrderStatus MapOrderStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "open" => OrderStatus.Open,
            "partial" => OrderStatus.PartiallyFilled,
            "filled" => OrderStatus.Filled,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            "rejected" => OrderStatus.Rejected,
            _ => OrderStatus.Unknown
        };
    }
}
