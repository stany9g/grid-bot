using System.Collections.Concurrent;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;

namespace GridBot.Lighter;

/// <summary>
/// Service that processes WebSocket channel events and maintains thread-safe state snapshots.
/// Implements ILighterRealtimeState for read access to latest data.
/// Market subscriptions must be triggered explicitly via SubscribeMarketAsync after market discovery.
/// </summary>
public sealed class LighterRealtimeStateService : ILighterRealtimeState
{
    private readonly ILighterWebSocketClient _wsClient;
    private readonly ILogger<LighterRealtimeStateService> _logger;

    // Thread-safe state storage
    private readonly ConcurrentDictionary<int, OrderBookSnapshot> _orderBooks = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<OrderSnapshot>> _orders = new();
    private readonly ConcurrentDictionary<int, MarketStatsSnapshot> _marketStats = new();
    private volatile AccountSnapshot? _account;
    private long _lastUpdateTimeTicks;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterRealtimeStateService"/> class.
    /// </summary>
    /// <param name="wsClient">WebSocket client for data streaming.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterRealtimeStateService(
        ILighterWebSocketClient wsClient,
        ILogger<LighterRealtimeStateService> logger)
    {
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public OrderBookSnapshot? GetOrderBook(int marketId) =>
        _orderBooks.TryGetValue(marketId, out var book) ? book : null;

    /// <inheritdoc />
    public AccountSnapshot? GetAccount() => _account;

    /// <inheritdoc />
    public IReadOnlyList<OrderSnapshot> GetOrders(int marketId) =>
        _orders.TryGetValue(marketId, out var orders) ? orders : Array.Empty<OrderSnapshot>();

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

        // Connect to WebSocket
        await _wsClient.ConnectAsync(cancellationToken);

        // Subscribe to account data (always needed)
        await _wsClient.SubscribeAccountAsync(cancellationToken);
        await _wsClient.SubscribeOrdersAsync(cancellationToken);
        await _wsClient.SubscribeNotificationsAsync(cancellationToken);

        _logger.LogInformation("WebSocket connected. Starting channel processing...");

        // Start processing channel readers in background
        _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = ProcessAllChannelsAsync(_processingCts.Token);

        _initialized = true;
        _logger.LogInformation("Realtime state service initialized");
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
            ProcessNotificationsAsync(cancellationToken)
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
                _orderBooks[update.MarketId] = update.Snapshot;
                Interlocked.Exchange(ref _lastUpdateTimeTicks, update.Timestamp.UtcTicks);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Order book update for market {MarketId}: bid={BestBid:F2} ask={BestAsk:F2} spread={Spread:F4}%",
                        update.MarketId,
                        update.Snapshot.BestBidPrice,
                        update.Snapshot.BestAskPrice,
                        update.Snapshot.SpreadPercent);
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

    private async Task ProcessAccountUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.AccountUpdates.ReadAllAsync(cancellationToken))
            {
                _account = new AccountSnapshot
                {
                    AccountId = update.AccountId,
                    Collateral = update.Collateral,
                    AvailableBalance = update.AvailableBalance,
                    PortfolioValue = update.PortfolioValue,
                    Positions = update.Positions.ToDictionary(p => p.MarketId),
                    LastUpdated = update.Timestamp
                };
                Interlocked.Exchange(ref _lastUpdateTimeTicks, update.Timestamp.UtcTicks);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Account update: collateral={Collateral:F2} available={Available:F2} positions={PositionCount}",
                        update.Collateral,
                        update.AvailableBalance,
                        update.Positions.Count);
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
                _orders[update.MarketId] = update.Orders;
                Interlocked.Exchange(ref _lastUpdateTimeTicks, update.Timestamp.UtcTicks);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Order update for market {MarketId}: {OrderCount} orders",
                        update.MarketId,
                        update.Orders.Count);
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
                _marketStats[update.MarketId] = new MarketStatsSnapshot
                {
                    MarketId = update.MarketId,
                    IndexPrice = update.IndexPrice,
                    MarkPrice = update.MarkPrice,
                    FundingRate = update.FundingRate,
                    Volume24h = update.Volume24h,
                    LastUpdated = update.Timestamp
                };
                Interlocked.Exchange(ref _lastUpdateTimeTicks, update.Timestamp.UtcTicks);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Market stats update for market {MarketId}: mark={MarkPrice:F2} funding={FundingRate:F6}",
                        update.MarketId,
                        update.MarkPrice,
                        update.FundingRate);
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

                if (update.State == ConnectionState.Failed)
                {
                    _logger.LogError(
                        "WebSocket connection failed after {Attempts} reconnection attempts",
                        update.ReconnectAttempt);
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
}
