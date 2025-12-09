using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.Lighter;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Realtime;

/// <summary>
/// Background service that processes WebSocket channel events and maintains thread-safe state snapshots.
/// Implements ILighterRealtimeState for read access to latest data.
/// </summary>
public sealed class LighterRealtimeStateService : BackgroundService, ILighterRealtimeState
{
    private readonly ILighterWebSocketClient _wsClient;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ILogger<LighterRealtimeStateService> _logger;

    // Thread-safe state storage
    private readonly ConcurrentDictionary<int, OrderBookSnapshot> _orderBooks = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<OrderSnapshot>> _orders = new();
    private readonly ConcurrentDictionary<int, MarketStatsSnapshot> _marketStats = new();
    private volatile AccountSnapshot? _account;
    private long _lastUpdateTimeTicks;

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
    /// <param name="riskConfig">Risk configuration for market ID.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterRealtimeStateService(
        ILighterWebSocketClient wsClient,
        IRiskConfiguration riskConfig,
        ILogger<LighterRealtimeStateService> logger)
    {
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _riskConfig = riskConfig ?? throw new ArgumentNullException(nameof(riskConfig));
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
    public decimal? GetPositionSize(int marketId)
    {
        var account = _account;
        if (account?.Positions.TryGetValue(marketId, out var position) == true)
            return position.Size;

        return null;
    }

    /// <inheritdoc />
    public async Task SubscribeMarketAsync(int marketId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Subscribing to market {MarketId} data streams", marketId);

        await _wsClient.SubscribeOrderBookAsync(marketId, cancellationToken);
        await _wsClient.SubscribeMarketStatsAsync(marketId, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting realtime state service");

        try
        {
            // Connect to WebSocket
            await _wsClient.ConnectAsync(stoppingToken);

            // Subscribe to account data
            await _wsClient.SubscribeAccountAsync(stoppingToken);
            await _wsClient.SubscribeOrdersAsync(stoppingToken);
            await _wsClient.SubscribeNotificationsAsync(stoppingToken);

            // Subscribe to market data for the configured trading market
            var marketId = _riskConfig.MarketId;
            _logger.LogInformation(
                "Auto-subscribing to market data for configured trading market {MarketId} ({Symbol})",
                marketId, _riskConfig.Symbol);
            await SubscribeMarketAsync(marketId, stoppingToken);

            // Start processing channel readers in parallel
            var tasks = new[]
            {
                ProcessOrderBookUpdatesAsync(stoppingToken),
                ProcessAccountUpdatesAsync(stoppingToken),
                ProcessOrderUpdatesAsync(stoppingToken),
                ProcessMarketStatsUpdatesAsync(stoppingToken),
                ProcessConnectionStateAsync(stoppingToken),
                ProcessNotificationsAsync(stoppingToken)
            };

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Realtime state service stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in realtime state service");
            throw;
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

                _logger.LogTrace(
                    "Order book update for market {MarketId}: bid={BestBid:F2} ask={BestAsk:F2} spread={Spread:F4}%",
                    update.MarketId,
                    update.Snapshot.BestBidPrice,
                    update.Snapshot.BestAskPrice,
                    update.Snapshot.SpreadPercent);
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

                _logger.LogTrace(
                    "Account update: collateral={Collateral:F2} available={Available:F2} positions={PositionCount}",
                    update.Collateral,
                    update.AvailableBalance,
                    update.Positions.Count);
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

                _logger.LogTrace(
                    "Order update for market {MarketId}: {OrderCount} orders",
                    update.MarketId,
                    update.Orders.Count);
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

                _logger.LogTrace(
                    "Market stats update for market {MarketId}: mark={MarkPrice:F2} funding={FundingRate:F6}",
                    update.MarketId,
                    update.MarkPrice,
                    update.FundingRate);
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
