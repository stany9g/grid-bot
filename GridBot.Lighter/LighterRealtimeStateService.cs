using System.Collections.Concurrent;
using System.Net.Http.Json;
using GridBot.Lighter.Models.Api;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter;

/// <summary>
/// Service that processes WebSocket channel events and maintains thread-safe state snapshots.
/// Implements ILighterRealtimeState for read access to latest data.
/// Market subscriptions must be triggered explicitly via SubscribeMarketAsync after market discovery.
/// </summary>
public sealed class LighterRealtimeStateService : ILighterRealtimeState
{
    private readonly ILighterWebSocketClient _wsClient;
    private readonly SignerClient _signerClient;
    private readonly HttpClient _httpClient;
    private readonly LighterOptions _options;
    private readonly ILogger<LighterRealtimeStateService> _logger;

    // Thread-safe state storage
    private readonly ConcurrentDictionary<int, OrderBookSnapshot> _orderBooks = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<OrderSnapshot>> _orders = new();
    private readonly ConcurrentDictionary<int, MarketStatsSnapshot> _marketStats = new();
    private volatile AccountSnapshot? _account;
    private volatile UserStatsUpdateEvent? _userStats;
    private long _lastUpdateTimeTicks;

    // Mutable order book state for delta accumulation (per market)
    // Key: price, Value: size. Size = 0 means remove.
    private readonly ConcurrentDictionary<int, MutableOrderBookState> _mutableOrderBooks = new();
    private readonly object _orderBookLock = new();

    // Mutable order state for delta accumulation (per market)
    // Key: OrderIndex, Value: OrderSnapshot
    // Orders with status "cancelled" or "filled" should be removed.
    private readonly ConcurrentDictionary<int, MutableOrderState> _mutableOrders = new();
    private readonly object _ordersLock = new();

    // Mutable position state for delta accumulation
    // Key: MarketId, Value: PositionSnapshot
    // Position with Size = 0 means closed (remove from state).
    private readonly ConcurrentDictionary<int, PositionSnapshot> _mutablePositions = new();
    private readonly object _positionsLock = new();

    // Cache of recently removed orders with their final status for fill vs cancel detection
    // Key: MarketId, Value: Dictionary of ClientOrderIndex -> (Status, RemovedAt)
    // Orders are kept for 60 seconds after removal.
    private readonly ConcurrentDictionary<int, ConcurrentDictionary<long, (string Status, DateTimeOffset RemovedAt)>> _recentlyRemovedOrders = new();
    private static readonly TimeSpan RemovedOrderCacheTtl = TimeSpan.FromSeconds(60);

    // Health monitoring state
    private long _lastMessageReceivedTicks;
    private readonly List<DateTimeOffset> _disconnectEvents = new();
    private readonly object _disconnectLock = new();
    private volatile bool _wasConnected;

    // Background processing
    private CancellationTokenSource? _processingCts;
    private Task? _processingTask;
    private bool _initialized;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsConnected => _wsClient.IsConnected;

    /// <inheritdoc />
    public TimeSpan? OldestDataAge
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastUpdateTimeTicks);
            if (ticks == 0) return null;
            var lastUpdate = new DateTimeOffset(ticks, TimeSpan.Zero);
            return DateTimeOffset.UtcNow - lastUpdate;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? LastMessageReceived
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastMessageReceivedTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <inheritdoc />
    public TimeSpan? TimeSinceLastMessage
    {
        get
        {
            var lastMsg = LastMessageReceived;
            return lastMsg.HasValue ? DateTimeOffset.UtcNow - lastMsg.Value : null;
        }
    }

    /// <inheritdoc />
    public int DisconnectCount24h
    {
        get
        {
            lock (_disconnectLock)
            {
                var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
                _disconnectEvents.RemoveAll(e => e < cutoff);
                return _disconnectEvents.Count;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<WebSocketHealthChangedEventArgs>? HealthChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterRealtimeStateService"/> class.
    /// </summary>
    /// <param name="wsClient">WebSocket client for data streaming.</param>
    /// <param name="signerClient">Signer client for nonce management.</param>
    /// <param name="httpClient">HTTP client for nonce sync API calls.</param>
    /// <param name="options">Lighter configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterRealtimeStateService(
        ILighterWebSocketClient wsClient,
        SignerClient signerClient,
        HttpClient httpClient,
        IOptions<LighterOptions> options,
        ILogger<LighterRealtimeStateService> logger)
    {
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _signerClient = signerClient ?? throw new ArgumentNullException(nameof(signerClient));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public OrderBookSnapshot? GetOrderBook(int marketId) =>
        _orderBooks.TryGetValue(marketId, out var book) ? book : null;

    /// <inheritdoc />
    public AccountSnapshot? GetAccount() => _account;

    /// <inheritdoc />
    public IReadOnlyList<OrderSnapshot> GetOrders(int marketId)
    {
        if (_orders.TryGetValue(marketId, out var orders))
        {
            _logger.LogInformation(
                "GetOrders: Market {MarketId} returning {Count} orders from state",
                marketId, orders.Count);
            return orders;
        }

        _logger.LogWarning(
            "GetOrders: Market {MarketId} has NO orders in state (key not found). " +
            "Total markets with orders: {MarketCount}",
            marketId, _orders.Count);
        return Array.Empty<OrderSnapshot>();
    }

    /// <inheritdoc />
    public MarketStatsSnapshot? GetMarketStats(int marketId) =>
        _marketStats.TryGetValue(marketId, out var stats) ? stats : null;

    /// <inheritdoc />
    public decimal? GetCurrentPrice(int marketId)
    {
        // Try order book mid price first (most accurate real-time price)
        if (_orderBooks.TryGetValue(marketId, out var book) && book.MidPrice > 0)
            return book.MidPrice;

        // Fall back to mark price from market stats
        if (_marketStats.TryGetValue(marketId, out var stats) && stats.MarkPrice > 0)
            return stats.MarkPrice;

        return null;
    }

    /// <inheritdoc />
    public bool IsMarketDataReady(int marketId)
    {
        var price = GetCurrentPrice(marketId);
        return price.HasValue && price.Value > 0;
    }

    /// <inheritdoc />
    public bool IsOrderBookReady(int marketId)
    {
        if (!_orderBooks.TryGetValue(marketId, out var book))
            return false;

        // Ensure we have actual bid/ask data
        return book.Bids.Count > 0 || book.Asks.Count > 0;
    }

    /// <inheritdoc />
    public async Task WaitForMarketDataAsync(int marketId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var deadline = DateTimeOffset.UtcNow + effectiveTimeout;

        _logger.LogInformation(
            "Waiting for market {MarketId} data (timeout: {Timeout}s)...",
            marketId, effectiveTimeout.TotalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsMarketDataReady(marketId))
            {
                var price = GetCurrentPrice(marketId);
                _logger.LogInformation(
                    "Market {MarketId} data ready. Price: {Price:F2}",
                    marketId, price);
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for market {marketId} data after {effectiveTimeout.TotalSeconds}s. " +
                    $"WebSocket connected: {IsConnected}");
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public async Task WaitForOrderBookAsync(int marketId, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var deadline = DateTimeOffset.UtcNow + effectiveTimeout;

        _logger.LogInformation(
            "Waiting for order book data for market {MarketId} (timeout: {Timeout}s)...",
            marketId, effectiveTimeout.TotalSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsOrderBookReady(marketId))
            {
                var book = GetOrderBook(marketId);
                _logger.LogInformation(
                    "Order book ready for market {MarketId}. Bids: {BidCount}, Asks: {AskCount}, MidPrice: {MidPrice:F2}",
                    marketId, book?.Bids.Count ?? 0, book?.Asks.Count ?? 0, book?.MidPrice ?? 0);
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out waiting for order book data for market {marketId} after {effectiveTimeout.TotalSeconds}s. " +
                    $"WebSocket connected: {IsConnected}");
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    /// <inheritdoc />
    public decimal? GetPositionSize(int marketId)
    {
        var account = _account;
        if (account?.Positions.TryGetValue(marketId, out var position) == true)
            return position.Size;

        return null;
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_initialized)
        {
            _logger.LogWarning("LighterRealtimeStateService already initialized");
            return;
        }

        _logger.LogInformation("Initializing realtime state service");

        // Sync nonce from server if InitialNonce is 0 (default)
        // This must happen before any transactions are signed
        if (_options.InitialNonce == 0)
        {
            await SyncNonceFromServerAsync(cancellationToken);
        }
        else
        {
            _logger.LogInformation(
                "Using configured InitialNonce={Nonce} (skipping server sync)",
                _options.InitialNonce);
        }

        // Connect to WebSocket
        await _wsClient.ConnectAsync(cancellationToken);

        // Subscribe to account data (always needed)
        await _wsClient.SubscribeAccountAsync(cancellationToken);
        await _wsClient.SubscribeOrdersAsync(cancellationToken);
        await _wsClient.SubscribeNotificationsAsync(cancellationToken);
        await _wsClient.SubscribeUserStatsAsync(cancellationToken);

        _logger.LogInformation("WebSocket connected. Starting channel processing...");

        // Start processing channel readers in background
        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessAllChannelsAsync(_processingCts.Token);

        _initialized = true;
        _logger.LogInformation("Realtime state service initialized");
    }

    /// <summary>
    /// Synchronizes the nonce from server before any transactions.
    /// </summary>
    private async Task SyncNonceFromServerAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Syncing nonce from server for account {AccountIndex}, apiKeyIndex {ApiKeyIndex}...",
            _options.AccountIndex, _options.ApiKeyIndex);

        try
        {
            var url = $"nextNonce?account_index={_options.AccountIndex}&api_key_index={_options.ApiKeyIndex}";
            var response = await _httpClient.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Failed to sync nonce from server: {StatusCode} - {Error}. Will rely on retry mechanism.",
                    (int)response.StatusCode, errorContent);
                return;
            }

            var nonceResponse = await response.Content.ReadFromJsonAsync<NextNonce>(
                LighterJsonOptions.Default, cancellationToken);

            if (nonceResponse == null || !nonceResponse.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to parse nonce response: {Message}. Will rely on retry mechanism.",
                    nonceResponse?.Message ?? "null response");
                return;
            }

            // SetNonce sets _currentNonce, but GetNextNonce() does pre-increment (++_currentNonce).
            // So if server says "next nonce is 500", we set _currentNonce = 499,
            // then GetNextNonce() returns ++499 = 500 (the correct value).
            _signerClient.SetNonce(nonceResponse.Nonce - 1);

            _logger.LogInformation(
                "Nonce synced successfully: server nextNonce={ServerNonce}, internal={InternalNonce}",
                nonceResponse.Nonce, nonceResponse.Nonce - 1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error syncing nonce from server. Will rely on retry mechanism for nonce errors.");
        }
    }

    /// <inheritdoc />
    public async Task SubscribeMarketAsync(int marketId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_initialized)
        {
            throw new InvalidOperationException("Service not initialized. Call InitializeAsync first.");
        }

        _logger.LogInformation("Subscribing to market {MarketId} data streams", marketId);

        await _wsClient.SubscribeOrderBookAsync(marketId, cancellationToken);
        await _wsClient.SubscribeMarketStatsAsync(marketId, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _processingCts?.Cancel();

        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // Expected during shutdown
            }
        }

        _processingCts?.Dispose();

        _logger.LogInformation("Realtime state service disposed");
    }

    private async Task ProcessAllChannelsAsync(CancellationToken cancellationToken)
    {
        var tasks = new[]
        {
            ProcessOrderBookUpdatesAsync(cancellationToken),
            ProcessAccountUpdatesAsync(cancellationToken),
            ProcessOrderUpdatesAsync(cancellationToken),
            ProcessMarketStatsUpdatesAsync(cancellationToken),
            ProcessConnectionStateAsync(cancellationToken),
            ProcessNotificationsAsync(cancellationToken),
            ProcessUserStatsUpdatesAsync(cancellationToken)
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Channel processing stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in channel processing");
        }
    }

    private async Task ProcessOrderBookUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.OrderBookUpdates.ReadAllAsync(cancellationToken))
            {
                // CRITICAL FIX: Lighter WebSocket sends full snapshot on first subscribe,
                // then only DELTAS afterward. We must MERGE deltas, not REPLACE.
                // Size = 0 means remove the price level.
                var snapshot = ApplyOrderBookDelta(update.MarketId, update.Snapshot);
                _orderBooks[update.MarketId] = snapshot;
                RecordMessageReceived(update.Timestamp);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Order book update for market {MarketId}: bid={BestBid:F2} ask={BestAsk:F2} spread={Spread:F4}% (bids={BidCount}, asks={AskCount})",
                        update.MarketId,
                        snapshot.BestBidPrice,
                        snapshot.BestAskPrice,
                        snapshot.SpreadPercent,
                        snapshot.Bids.Count,
                        snapshot.Asks.Count);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order book updates");
        }
    }

    /// <summary>
    /// Applies an order book delta to the accumulated state and returns a new snapshot.
    /// Handles both initial snapshots (many levels) and incremental deltas (few levels).
    /// Size = 0 means remove the price level.
    /// </summary>
    private OrderBookSnapshot ApplyOrderBookDelta(int marketId, OrderBookSnapshot delta)
    {
        // Get or create mutable state for this market
        var mutableState = _mutableOrderBooks.GetOrAdd(marketId, _ => new MutableOrderBookState());

        lock (_orderBookLock)
        {
            // Determine if this is a full snapshot or incremental delta
            // Heuristic: If delta has many levels (>50), treat as full snapshot and replace
            var isFullSnapshot = delta.Bids.Count > 500 || delta.Asks.Count > 500;

            if (isFullSnapshot)
            {
                // Full snapshot: replace all state
                mutableState.Bids.Clear();
                mutableState.Asks.Clear();

                foreach (var (price, size) in delta.Bids)
                {
                    if (size > 0)
                        mutableState.Bids[price] = size;
                }

                foreach (var (price, size) in delta.Asks)
                {
                    if (size > 0)
                        mutableState.Asks[price] = size;
                }

                _logger.LogDebug(
                    "Order book full snapshot for market {MarketId}: {BidCount} bids, {AskCount} asks",
                    marketId, mutableState.Bids.Count, mutableState.Asks.Count);
            }
            else
            {
                // Incremental delta: merge changes
                // Size = 0 means remove, Size > 0 means add/update
                foreach (var (price, size) in delta.Bids)
                {
                    if (size <= 0)
                        mutableState.Bids.Remove(price);
                    else
                        mutableState.Bids[price] = size;
                }

                foreach (var (price, size) in delta.Asks)
                {
                    if (size <= 0)
                        mutableState.Asks.Remove(price);
                    else
                        mutableState.Asks[price] = size;
                }
            }

            // Build snapshot from accumulated state
            return BuildSnapshotFromMutableState(mutableState, delta.LastUpdate);
        }
    }

    /// <summary>
    /// Builds an immutable OrderBookSnapshot from mutable accumulated state.
    /// </summary>
    private static OrderBookSnapshot BuildSnapshotFromMutableState(MutableOrderBookState state, DateTimeOffset timestamp)
    {
        // Convert to sorted lists
        // Bids: highest price first (descending)
        var bids = state.Bids
            .OrderByDescending(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

        // Asks: lowest price first (ascending)
        var asks = state.Asks
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))
            .ToList();

        // Calculate metrics - GUARD against empty sides
        var bestBid = bids.Count > 0 ? bids[0] : (0m, 0m);
        var bestAsk = asks.Count > 0 ? asks[0] : (0m, 0m);

        // Calculate mid price - only if BOTH sides have valid data
        decimal midPrice;
        decimal spread;
        decimal spreadPercent;

        if (bestBid.Item1 > 0 && bestAsk.Item1 > 0)
        {
            // Normal case: both sides have data
            midPrice = (bestBid.Item1 + bestAsk.Item1) / 2m;
            spread = bestAsk.Item1 - bestBid.Item1;
            spreadPercent = midPrice > 0 ? spread / midPrice * 100m : 0m;
        }
        else if (bestBid.Item1 > 0)
        {
            // Only bids available - use bid price as reference
            midPrice = bestBid.Item1;
            spread = 0m;
            spreadPercent = 0m;
        }
        else if (bestAsk.Item1 > 0)
        {
            // Only asks available - use ask price as reference
            midPrice = bestAsk.Item1;
            spread = 0m;
            spreadPercent = 0m;
        }
        else
        {
            // No data on either side
            midPrice = 0m;
            spread = 0m;
            spreadPercent = 0m;
        }

        return new OrderBookSnapshot
        {
            BestBidPrice = bestBid.Item1,
            BestAskPrice = bestAsk.Item1,
            BestBidSize = bestBid.Item2,
            BestAskSize = bestAsk.Item2,
            MidPrice = midPrice,
            Spread = spread,
            SpreadPercent = spreadPercent,
            Bids = bids,
            Asks = asks,
            LastUpdate = timestamp
        };
    }

    /// <summary>
    /// Clears the accumulated order book state for a market.
    /// Called on reconnection to force a fresh snapshot.
    /// </summary>
    public void ClearOrderBookState(int marketId)
    {
        if (_mutableOrderBooks.TryRemove(marketId, out _))
        {
            _logger.LogInformation("Cleared order book state for market {MarketId}", marketId);
        }
    }

    /// <summary>
    /// Clears all accumulated order book state for all markets.
    /// Called on disconnect to ensure fresh data on reconnect.
    /// </summary>
    private void ClearAllOrderBookState()
    {
        var count = _mutableOrderBooks.Count;
        _mutableOrderBooks.Clear();
        _orderBooks.Clear();

        if (count > 0)
        {
            _logger.LogDebug("Cleared order book state for {Count} markets", count);
        }
    }

    /// <summary>
    /// Applies an order delta to the accumulated state and returns a new snapshot.
    /// Handles both initial snapshots (many orders) and incremental deltas (few orders).
    /// Status = "cancelled" or "filled" means remove the order.
    /// </summary>
    private IReadOnlyList<OrderSnapshot> ApplyOrderDelta(int marketId, IReadOnlyList<OrderSnapshot> deltaOrders)
    {
        // Get or create mutable state for this market
        var mutableState = _mutableOrders.GetOrAdd(marketId, _ => new MutableOrderState());

        lock (_ordersLock)
        {
            // Determine if this is a full snapshot or incremental delta
            // Heuristic: If delta has many orders (>10), treat as full snapshot
            // Also treat as snapshot if we have no existing state for this market
            var isFullSnapshot = mutableState.Orders.Count == 0 || deltaOrders.Count > 10;

            if (isFullSnapshot)
            {
                // Full snapshot: replace all state with active orders only
                mutableState.Orders.Clear();

                foreach (var order in deltaOrders)
                {
                    if (IsActiveOrder(order))
                    {
                        mutableState.Orders[order.OrderIndex] = order;
                    }
                }

                _logger.LogDebug(
                    "Order full snapshot for market {MarketId}: {ActiveCount} active orders (from {TotalCount} in message)",
                    marketId, mutableState.Orders.Count, deltaOrders.Count);
            }
            else
            {
                // Incremental delta: merge changes
                // Active orders are added/updated, inactive orders are removed
                foreach (var order in deltaOrders)
                {
                    if (IsActiveOrder(order))
                    {
                        mutableState.Orders[order.OrderIndex] = order;
                        _logger.LogDebug(
                            "Order delta ADD/UPDATE market {MarketId}: OrderIndex={OrderIndex}, Status={Status}, Price={Price}",
                            marketId, order.OrderIndex, order.Status, order.Price);
                    }
                    else
                    {
                        if (mutableState.Orders.Remove(order.OrderIndex))
                        {
                            // Track the removed order's final status for fill vs cancel detection
                            // Use ClientOrderIndex as key since that's what GridOrderManager uses for matching
                            if (order.ClientOrderIndex > 0)
                            {
                                var removedCache = _recentlyRemovedOrders.GetOrAdd(marketId, _ => new());
                                removedCache[order.ClientOrderIndex] = (order.Status, DateTimeOffset.UtcNow);
                            }

                            _logger.LogDebug(
                                "Order delta REMOVE market {MarketId}: OrderIndex={OrderIndex}, ClientOrderIndex={ClientOrderIndex}, Status={Status} (filled/cancelled)",
                                marketId, order.OrderIndex, order.ClientOrderIndex, order.Status);
                        }
                    }
                }

                // Periodically clean up expired cache entries
                CleanupRemovedOrdersCache();
            }

            // Return as list
            return mutableState.Orders.Values.ToList();
        }
    }

    /// <summary>
    /// Determines if an order is still active (should be kept in state).
    /// </summary>
    private static bool IsActiveOrder(OrderSnapshot order)
    {
        // Active statuses: "open", "partial"
        // Inactive statuses: "filled", "cancelled", "expired"
        return order.Status is "open" or "partial";
    }

    /// <summary>
    /// Clears the accumulated order state for a market.
    /// Called on reconnection to force a fresh snapshot.
    /// </summary>
    public void ClearOrderState(int marketId)
    {
        if (_mutableOrders.TryRemove(marketId, out _))
        {
            _orders.TryRemove(marketId, out _);
            _logger.LogInformation("Cleared order state for market {MarketId}", marketId);
        }
    }

    /// <summary>
    /// Clears all accumulated order state for all markets.
    /// Called on disconnect to ensure fresh data on reconnect.
    /// </summary>
    private void ClearAllOrderState()
    {
        var count = _mutableOrders.Count;
        _mutableOrders.Clear();
        _orders.Clear();

        if (count > 0)
        {
            _logger.LogDebug("Cleared order state for {Count} markets", count);
        }
    }

    /// <summary>
    /// Applies position delta to the accumulated state and returns the current positions.
    /// Handles both initial snapshots (many positions) and incremental deltas (few/no positions).
    /// Position with Size = 0 means closed (remove from state).
    /// </summary>
    private Dictionary<int, PositionSnapshot> ApplyPositionDelta(IReadOnlyList<PositionSnapshot> deltaPositions)
    {
        lock (_positionsLock)
        {
            // If delta has positions, process them
            if (deltaPositions.Count > 0)
            {
                // Determine if this is a full snapshot or incremental delta
                // Heuristic: If we have no existing positions AND delta has positions, treat as snapshot
                // Also if delta has many positions (>3), treat as full snapshot
                var isFullSnapshot = _mutablePositions.Count == 0 || deltaPositions.Count > 3;

                if (isFullSnapshot)
                {
                    // Full snapshot: replace all positions
                    _mutablePositions.Clear();

                    foreach (var pos in deltaPositions)
                    {
                        if (pos.Size != 0)
                        {
                            _mutablePositions[pos.MarketId] = pos;
                        }
                    }

                    _logger.LogDebug(
                        "Position full snapshot: {Count} active positions",
                        _mutablePositions.Count);
                }
                else
                {
                    // Incremental delta: merge changes
                    foreach (var pos in deltaPositions)
                    {
                        if (pos.Size != 0)
                        {
                            var existed = _mutablePositions.ContainsKey(pos.MarketId);
                            _mutablePositions[pos.MarketId] = pos;
                            _logger.LogDebug(
                                "Position delta {Action} market {MarketId}: Size={Size:F8}, AvgEntry={AvgEntry:F2}",
                                existed ? "UPDATE" : "ADD",
                                pos.MarketId, pos.Size, pos.AvgEntryPrice);
                        }
                        else
                        {
                            // Size = 0 means position closed
                            if (_mutablePositions.TryRemove(pos.MarketId, out var removed))
                            {
                                _logger.LogInformation(
                                    "Position delta CLOSE market {MarketId}: Previous size={PrevSize:F8}",
                                    pos.MarketId, removed.Size);
                            }
                        }
                    }
                }
            }
            // If delta has NO positions, keep existing state (this is the key fix!)
            // An empty delta does NOT mean all positions are closed

            return new Dictionary<int, PositionSnapshot>(_mutablePositions);
        }
    }

    /// <summary>
    /// Clears the accumulated position state for a market.
    /// </summary>
    public void ClearPositionState(int marketId)
    {
        if (_mutablePositions.TryRemove(marketId, out _))
        {
            _logger.LogInformation("Cleared position state for market {MarketId}", marketId);
        }
    }

    /// <summary>
    /// Clears all accumulated position state.
    /// Called on disconnect to ensure fresh data on reconnect.
    /// </summary>
    private void ClearAllPositionState()
    {
        var count = _mutablePositions.Count;
        _mutablePositions.Clear();

        if (count > 0)
        {
            _logger.LogDebug("Cleared position state for {Count} markets", count);
        }
    }

    /// <summary>
    /// Clears all accumulated market stats state.
    /// Called on disconnect to ensure fresh data on reconnect.
    /// </summary>
    private void ClearAllMarketStatsState()
    {
        var count = _marketStats.Count;
        _marketStats.Clear();

        if (count > 0)
        {
            _logger.LogDebug("Cleared market stats state for {Count} markets", count);
        }
    }

    /// <summary>
    /// Clears all accumulated user stats state.
    /// Called on disconnect to ensure fresh data on reconnect.
    /// </summary>
    private void ClearAllUserStatsState()
    {
        _userStats = null;
        _logger.LogDebug("Cleared user stats state");
    }

    /// <inheritdoc />
    public string? GetRemovedOrderStatus(int marketId, long clientOrderIndex)
    {
        if (_recentlyRemovedOrders.TryGetValue(marketId, out var cache) &&
            cache.TryGetValue(clientOrderIndex, out var entry))
        {
            // Only return if not expired
            if (entry.RemovedAt >= DateTimeOffset.UtcNow - RemovedOrderCacheTtl)
            {
                return entry.Status;
            }
        }
        return null;
    }

    /// <summary>
    /// Cleans up expired entries from the removed orders cache.
    /// Called periodically during order delta processing.
    /// </summary>
    private void CleanupRemovedOrdersCache()
    {
        var cutoff = DateTimeOffset.UtcNow - RemovedOrderCacheTtl;

        foreach (var (marketId, cache) in _recentlyRemovedOrders)
        {
            var expiredKeys = cache
                .Where(kvp => kvp.Value.RemovedAt < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in expiredKeys)
            {
                cache.TryRemove(key, out _);
            }

            // Remove empty market caches
            if (cache.IsEmpty)
            {
                _recentlyRemovedOrders.TryRemove(marketId, out _);
            }
        }
    }

    /// <summary>
    /// Clears all removed orders cache state.
    /// Called on disconnect to ensure fresh data on reconnect.
    /// </summary>
    private void ClearAllRemovedOrdersCache()
    {
        var count = _recentlyRemovedOrders.Count;
        _recentlyRemovedOrders.Clear();

        if (count > 0)
        {
            _logger.LogDebug("Cleared removed orders cache for {Count} markets", count);
        }
    }

    private async Task ProcessAccountUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.AccountUpdates.ReadAllAsync(cancellationToken))
            {
                // Use user_stats for collateral and available balance (perps trading balance)
                // Fall back to account update values if user_stats not yet received
                var userStats = _userStats;
                var collateral = userStats?.Collateral ?? update.Collateral;
                var availableBalance = userStats?.AvailableBalance ?? update.AvailableBalance;
                var portfolioValue = userStats?.PortfolioValue ?? update.PortfolioValue;

                // CRITICAL FIX: Apply position delta instead of full replacement
                // Lighter WebSocket sends deltas - if a message has no position data,
                // it doesn't mean positions are closed!
                var positions = ApplyPositionDelta(update.Positions);

                _account = new AccountSnapshot
                {
                    AccountId = update.AccountId,
                    Collateral = collateral,
                    AvailableBalance = availableBalance,
                    PortfolioValue = portfolioValue,
                    Positions = positions,
                    LastUpdated = update.Timestamp
                };
                RecordMessageReceived(update.Timestamp);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Account update: collateral={Collateral:F2} available={Available:F2} positions={PositionCount}",
                        collateral,
                        availableBalance,
                        positions.Count);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing account updates");
        }
    }

    private async Task ProcessOrderUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.OrderUpdates.ReadAllAsync(cancellationToken))
            {
                // CRITICAL FIX: Lighter WebSocket sends full snapshot on first subscribe,
                // then only DELTAS afterward. We must MERGE deltas, not REPLACE.
                // Status = "cancelled" or "filled" means remove the order.
                var previousCount = _orders.TryGetValue(update.MarketId, out var prev) ? prev.Count : 0;

                var orders = ApplyOrderDelta(update.MarketId, update.Orders);
                _orders[update.MarketId] = orders;
                RecordMessageReceived(update.Timestamp);

                var openCount = orders.Count(o => o.Status == "open");
                var partialCount = orders.Count(o => o.Status == "partial");

                // Always log order updates for troubleshooting
                _logger.LogInformation(
                    "ProcessOrderUpdates: Market {MarketId} delta applied. " +
                    "Delta contained {DeltaCount} orders. State: {PrevCount} -> {NewCount} (open={Open}, partial={Partial})",
                    update.MarketId, update.Orders.Count, previousCount, orders.Count, openCount, partialCount);

                // Log if all orders were removed (potential issue or legitimate)
                if (orders.Count == 0 && previousCount > 0)
                {
                    _logger.LogWarning(
                        "ProcessOrderUpdates: Market {MarketId} all orders removed (was {PrevCount}). " +
                        "This could be legitimate (all cancelled/filled) or a WebSocket issue.",
                        update.MarketId, previousCount);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing order updates");
        }
    }

    private async Task ProcessMarketStatsUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.MarketStatsUpdates.ReadAllAsync(cancellationToken))
            {
                // DELTA HANDLING: Merge new values with existing, only updating non-zero values
                // This prevents partial updates from wiping out valid data
                var existing = _marketStats.TryGetValue(update.MarketId, out var prev) ? prev : null;

                _marketStats[update.MarketId] = new MarketStatsSnapshot
                {
                    MarketId = update.MarketId,
                    IndexPrice = update.IndexPrice > 0 ? update.IndexPrice : (existing?.IndexPrice ?? 0),
                    MarkPrice = update.MarkPrice > 0 ? update.MarkPrice : (existing?.MarkPrice ?? 0),
                    FundingRate = update.FundingRate != 0 ? update.FundingRate : (existing?.FundingRate ?? 0),
                    Volume24h = update.Volume24h > 0 ? update.Volume24h : (existing?.Volume24h ?? 0),
                    LastUpdated = update.Timestamp
                };
                RecordMessageReceived(update.Timestamp);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Market stats update for market {MarketId}: mark={MarkPrice:F2} index={IndexPrice:F2} funding={FundingRate:F6}",
                        update.MarketId,
                        _marketStats[update.MarketId].MarkPrice,
                        _marketStats[update.MarketId].IndexPrice,
                        _marketStats[update.MarketId].FundingRate);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing market stats updates");
        }
    }

    private async Task ProcessConnectionStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.ConnectionStateChanges.ReadAllAsync(cancellationToken))
            {
                _logger.LogInformation(
                    "WebSocket connection state: {State}" + (update.Reason != null ? " - {Reason}" : ""),
                    update.State,
                    update.Reason);

                // Track disconnect events for health monitoring
                var isNowConnected = update.State == ConnectionState.Connected;
                var wasConnectedBefore = _wasConnected;
                _wasConnected = isNowConnected;

                // Detect disconnect event (was connected, now not)
                if (wasConnectedBefore && !isNowConnected)
                {
                    lock (_disconnectLock)
                    {
                        _disconnectEvents.Add(DateTimeOffset.UtcNow);
                    }

                    // Clear all accumulated state on disconnect to force fresh snapshots on reconnect
                    ClearAllOrderBookState();
                    ClearAllOrderState();
                    ClearAllPositionState();
                    ClearAllMarketStatsState();
                    ClearAllUserStatsState();
                    ClearAllRemovedOrdersCache();

                    _logger.LogWarning(
                        "WebSocket disconnected. Disconnect count in 24h: {Count}. All WebSocket state cleared.",
                        DisconnectCount24h);

                    // Fire health changed event - disconnected
                    OnHealthChanged(new WebSocketHealthChangedEventArgs
                    {
                        IsHealthy = false,
                        IsConnected = false,
                        DataAge = OldestDataAge,
                        Reason = update.Reason ?? "Connection lost",
                        Timestamp = DateTimeOffset.UtcNow,
                        IsConnectionEvent = true
                    });
                }
                // Detect reconnection (was not connected, now connected)
                else if (!wasConnectedBefore && isNowConnected)
                {
                    _logger.LogInformation(
                        "WebSocket reconnected. Data age: {DataAge}",
                        OldestDataAge?.TotalSeconds.ToString("F1") ?? "N/A");

                    // Fire health changed event - reconnected
                    // Note: Health depends on data age, which will be checked by WebSocketHealthMonitor
                    OnHealthChanged(new WebSocketHealthChangedEventArgs
                    {
                        IsHealthy = true, // Initial optimistic state, monitor will verify
                        IsConnected = true,
                        DataAge = OldestDataAge,
                        Reason = "Connection restored",
                        Timestamp = DateTimeOffset.UtcNow,
                        IsConnectionEvent = true
                    });
                }

                if (update.State == ConnectionState.Failed)
                {
                    _logger.LogError(
                        "WebSocket connection failed after {Attempts} reconnection attempts",
                        update.ReconnectAttempt);

                    OnHealthChanged(new WebSocketHealthChangedEventArgs
                    {
                        IsHealthy = false,
                        IsConnected = false,
                        DataAge = OldestDataAge,
                        Reason = $"Connection failed after {update.ReconnectAttempt} attempts",
                        Timestamp = DateTimeOffset.UtcNow,
                        IsConnectionEvent = true
                    });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing connection state updates");
        }
    }

    private async Task ProcessNotificationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var notification in _wsClient.Notifications.ReadAllAsync(cancellationToken))
            {
                var logLevel = notification.NotificationType.Contains("warning", StringComparison.OrdinalIgnoreCase)
                    ? LogLevel.Warning
                    : LogLevel.Information;

                _logger.Log(
                    logLevel,
                    "Notification [{Type}]: {Message}" + (notification.MarketId.HasValue ? " (market {MarketId})" : ""),
                    notification.NotificationType,
                    notification.Message,
                    notification.MarketId);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing notifications");
        }
    }

    private async Task ProcessUserStatsUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.UserStatsUpdates.ReadAllAsync(cancellationToken))
            {
                // DELTA HANDLING: Merge new values with existing, only updating non-zero values
                // This prevents partial updates from wiping out valid data
                var existing = _userStats;

                var mergedStats = new UserStatsUpdateEvent
                {
                    AccountId = update.AccountId,
                    Collateral = update.Collateral > 0 ? update.Collateral : (existing?.Collateral ?? 0),
                    PortfolioValue = update.PortfolioValue > 0 ? update.PortfolioValue : (existing?.PortfolioValue ?? 0),
                    AvailableBalance = update.AvailableBalance > 0 ? update.AvailableBalance : (existing?.AvailableBalance ?? 0),
                    BuyingPower = update.BuyingPower > 0 ? update.BuyingPower : (existing?.BuyingPower ?? 0),
                    Leverage = update.Leverage > 0 ? update.Leverage : (existing?.Leverage ?? 0),
                    MarginUsage = update.MarginUsage > 0 ? update.MarginUsage : (existing?.MarginUsage ?? 0)
                };

                _userStats = mergedStats;
                RecordMessageReceived(update.Timestamp);

                // Also update the account snapshot with new user stats values
                var existingAccount = _account;
                if (existingAccount != null)
                {
                    _account = existingAccount with
                    {
                        Collateral = mergedStats.Collateral,
                        AvailableBalance = mergedStats.AvailableBalance,
                        PortfolioValue = mergedStats.PortfolioValue,
                        LastUpdated = update.Timestamp
                    };
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "User stats update: collateral={Collateral:F2} available={Available:F2} portfolioValue={PortfolioValue:F2} leverage={Leverage:F2}",
                        mergedStats.Collateral,
                        mergedStats.AvailableBalance,
                        mergedStats.PortfolioValue,
                        mergedStats.Leverage);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user stats updates");
        }
    }

    /// <summary>
    /// Records that a message was received and updates both timestamp fields.
    /// </summary>
    private void RecordMessageReceived(DateTimeOffset timestamp)
    {
        var ticks = timestamp.UtcTicks;
        Interlocked.Exchange(ref _lastUpdateTimeTicks, ticks);
        Interlocked.Exchange(ref _lastMessageReceivedTicks, ticks);
    }

    /// <summary>
    /// Raises the HealthChanged event.
    /// </summary>
    private void OnHealthChanged(WebSocketHealthChangedEventArgs e)
    {
        HealthChanged?.Invoke(this, e);
    }
}

/// <summary>
/// Mutable order book state for accumulating WebSocket deltas.
/// Not thread-safe - must be accessed under lock.
/// </summary>
internal sealed class MutableOrderBookState
{
    /// <summary>
    /// Bid levels: Key = price, Value = size.
    /// </summary>
    public Dictionary<decimal, decimal> Bids { get; } = new();

    /// <summary>
    /// Ask levels: Key = price, Value = size.
    /// </summary>
    public Dictionary<decimal, decimal> Asks { get; } = new();
}

/// <summary>
/// Mutable order state for accumulating WebSocket deltas.
/// Not thread-safe - must be accessed under lock.
/// </summary>
internal sealed class MutableOrderState
{
    /// <summary>
    /// Orders: Key = OrderIndex, Value = OrderSnapshot.
    /// </summary>
    public Dictionary<long, OrderSnapshot> Orders { get; } = new();
}
