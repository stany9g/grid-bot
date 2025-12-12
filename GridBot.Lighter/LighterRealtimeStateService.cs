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
                // Use user_stats for collateral and available balance (perps trading balance)
                // Fall back to account update values if user_stats not yet received
                var userStats = _userStats;
                var collateral = userStats?.Collateral ?? update.Collateral;
                var availableBalance = userStats?.AvailableBalance ?? update.AvailableBalance;
                var portfolioValue = userStats?.PortfolioValue ?? update.PortfolioValue;

                _account = new AccountSnapshot
                {
                    AccountId = update.AccountId,
                    Collateral = collateral,
                    AvailableBalance = availableBalance,
                    PortfolioValue = portfolioValue,
                    Positions = update.Positions.ToDictionary(p => p.MarketId),
                    LastUpdated = update.Timestamp
                };
                Interlocked.Exchange(ref _lastUpdateTimeTicks, update.Timestamp.UtcTicks);

                if (_logger.IsEnabled(LogLevel.Trace))
                {
                    _logger.LogTrace(
                        "Account update: collateral={Collateral:F2} available={Available:F2} positions={PositionCount}",
                        collateral,
                        availableBalance,
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

    private async Task ProcessUserStatsUpdatesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in _wsClient.UserStatsUpdates.ReadAllAsync(cancellationToken))
            {
                _userStats = update;
                Interlocked.Exchange(ref _lastUpdateTimeTicks, update.Timestamp.UtcTicks);

                // Also update the account snapshot with new user stats values
                var existingAccount = _account;
                if (existingAccount != null)
                {
                    _account = existingAccount with
                    {
                        Collateral = update.Collateral,
                        AvailableBalance = update.AvailableBalance,
                        PortfolioValue = update.PortfolioValue,
                        LastUpdated = update.Timestamp
                    };
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "User stats update: collateral={Collateral:F2} available={Available:F2} portfolioValue={PortfolioValue:F2}",
                        update.Collateral,
                        update.AvailableBalance,
                        update.PortfolioValue);
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
}
