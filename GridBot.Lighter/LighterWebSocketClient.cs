using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Lighter;

/// <summary>
/// WebSocket client for real-time Lighter DEX data streaming.
/// Thread-safe implementation using bounded channels for backpressure handling.
/// </summary>
public sealed class LighterWebSocketClient : ILighterWebSocketClient
{
    private readonly SignerClient _signerClient;
    private readonly ILogger<LighterWebSocketClient> _logger;
    private readonly WebSocketOptions _options;
    private readonly LighterOptions _lighterOptions;

    // Connection state
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Task? _authRefreshTask;
    private volatile ConnectionState _connectionState = ConnectionState.Disconnected;
    private int _reconnectAttempts;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Auth management
    private string? _currentAuthToken;
    private DateTimeOffset _authTokenExpiry;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    // Subscription tracking
    private readonly ConcurrentDictionary<string, bool> _subscriptions = new();

    // Transaction response tracking
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _pendingRequests = new();

    // Channels - bounded with DropOldest for trading data (stale data is useless)
    private readonly Channel<OrderBookUpdateEvent> _orderBookChannel;
    private readonly Channel<AccountUpdateEvent> _accountChannel;
    private readonly Channel<OrderUpdateEvent> _ordersChannel;
    private readonly Channel<MarketStatsUpdateEvent> _marketStatsChannel;
    private readonly Channel<ConnectionStateEvent> _connectionChannel;
    private readonly Channel<NotificationEvent> _notificationChannel;
    private readonly Channel<UserStatsUpdateEvent> _userStatsChannel;

    private bool _disposed;

    /// <inheritdoc />
    public ChannelReader<OrderBookUpdateEvent> OrderBookUpdates => _orderBookChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<AccountUpdateEvent> AccountUpdates => _accountChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<OrderUpdateEvent> OrderUpdates => _ordersChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<MarketStatsUpdateEvent> MarketStatsUpdates => _marketStatsChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<ConnectionStateEvent> ConnectionStateChanges => _connectionChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<NotificationEvent> Notifications => _notificationChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<UserStatsUpdateEvent> UserStatsUpdates => _userStatsChannel.Reader;

    /// <inheritdoc />
    public ConnectionState ConnectionState => _connectionState;

    /// <inheritdoc />
    public bool IsConnected => _connectionState == ConnectionState.Connected;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterWebSocketClient"/> class.
    /// </summary>
    /// <param name="signerClient">Signer client for auth token generation.</param>
    /// <param name="options">WebSocket options.</param>
    /// <param name="lighterOptions">Lighter API options.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterWebSocketClient(
        SignerClient signerClient,
        IOptions<WebSocketOptions> options,
        IOptions<LighterOptions> lighterOptions,
        ILogger<LighterWebSocketClient> logger)
    {
        _signerClient = signerClient ?? throw new ArgumentNullException(nameof(signerClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _lighterOptions = lighterOptions?.Value ?? throw new ArgumentNullException(nameof(lighterOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create bounded channels with configured capacity
        var channelOptions = new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = _options.FullMode,
            SingleReader = false,  // Multiple consumers allowed
            SingleWriter = true,   // Only WebSocket receive loop writes
            AllowSynchronousContinuations = false
        };

        _orderBookChannel = Channel.CreateBounded<OrderBookUpdateEvent>(channelOptions);
        _accountChannel = Channel.CreateBounded<AccountUpdateEvent>(channelOptions);
        _ordersChannel = Channel.CreateBounded<OrderUpdateEvent>(channelOptions);
        _marketStatsChannel = Channel.CreateBounded<MarketStatsUpdateEvent>(channelOptions);
        _connectionChannel = Channel.CreateBounded<ConnectionStateEvent>(channelOptions);
        _notificationChannel = Channel.CreateBounded<NotificationEvent>(channelOptions);
        _userStatsChannel = Channel.CreateBounded<UserStatsUpdateEvent>(channelOptions);
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_connectionState == ConnectionState.Connected)
            {
                _logger.LogDebug("Already connected to WebSocket");
                return;
            }

            await SetConnectionStateAsync(ConnectionState.Connecting, null, cancellationToken);

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();

            var wsUrl = GetWebSocketUrl();
            _logger.LogInformation("Connecting to WebSocket at {Url}", wsUrl);

            await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
            _authRefreshTask = Task.Run(() => AuthRefreshLoopAsync(_receiveCts.Token), _receiveCts.Token);

            await SetConnectionStateAsync(ConnectionState.Connected, null, cancellationToken);
            _reconnectAttempts = 0;

            _logger.LogInformation("WebSocket connected successfully");

            // Resubscribe to all previous subscriptions
            await ResubscribeAllAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to WebSocket");
            await SetConnectionStateAsync(ConnectionState.Disconnected, ex.Message, cancellationToken);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _logger.LogInformation("Disconnecting from WebSocket");

        _receiveCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnect",
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during WebSocket close");
            }
        }

        await SetConnectionStateAsync(ConnectionState.Disconnected, "Client initiated disconnect", cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeOrderBookAsync(int marketId, CancellationToken cancellationToken = default)
    {
        var channel = $"order_book/{marketId}";
        await SubscribeAsync(channel, requiresAuth: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeAccountAsync(CancellationToken cancellationToken = default)
    {
        var channel = $"account_all/{_lighterOptions.AccountIndex}";
        await SubscribeAsync(channel, requiresAuth: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeOrdersAsync(CancellationToken cancellationToken = default)
    {
        var channel = $"account_all_orders/{_lighterOptions.AccountIndex}";
        await SubscribeAsync(channel, requiresAuth: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeMarketStatsAsync(int marketId, CancellationToken cancellationToken = default)
    {
        var channel = $"market_stats/{marketId}";
        await SubscribeAsync(channel, requiresAuth: false, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var channel = $"notification/{_lighterOptions.AccountIndex}";
        await SubscribeAsync(channel, requiresAuth: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SubscribeUserStatsAsync(CancellationToken cancellationToken = default)
    {
        var channel = $"user_stats/{_lighterOptions.AccountIndex}";
        await SubscribeAsync(channel, requiresAuth: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot unsubscribe when not connected");
            return;
        }

        var message = new { type = "unsubscribe", channel };
        await SendMessageAsync(message, cancellationToken);
        _subscriptions.TryRemove(channel, out _);

        _logger.LogDebug("Unsubscribed from {Channel}", channel);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        _receiveCts?.Cancel();

        // CRITICAL: Cancel all pending requests before disposal to prevent caller hangs
        CancelAllPendingRequests(new ObjectDisposedException(nameof(LighterWebSocketClient)));

        // Complete all channels
        _orderBookChannel.Writer.TryComplete();
        _accountChannel.Writer.TryComplete();
        _ordersChannel.Writer.TryComplete();
        _marketStatsChannel.Writer.TryComplete();
        _connectionChannel.Writer.TryComplete();
        _notificationChannel.Writer.TryComplete();
        _userStatsChannel.Writer.TryComplete();

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // Expected during shutdown
            }
        }

        if (_authRefreshTask != null)
        {
            try
            {
                await _authRefreshTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
                // Expected during shutdown
            }
        }

        _webSocket?.Dispose();
        _receiveCts?.Dispose();
        _connectLock.Dispose();
        _sendLock.Dispose();
        _authLock.Dispose();

        _logger.LogInformation("WebSocket client disposed");
    }

    private string GetWebSocketUrl()
    {
        if (!string.IsNullOrEmpty(_options.WebSocketUrl))
            return _options.WebSocketUrl;

        // Derive from API URL: https://mainnet.zklighter.elliot.ai -> wss://mainnet.zklighter.elliot.ai/stream
        var apiUrl = _lighterOptions.ApiUrl;
        var wsUrl = apiUrl.Replace("https://", "wss://").Replace("http://", "ws://");
        if (!wsUrl.EndsWith("/stream", StringComparison.OrdinalIgnoreCase))
            wsUrl = wsUrl.TrimEnd('/') + "/stream";

        return wsUrl;
    }

    private async Task SubscribeAsync(string channel, bool requiresAuth, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot subscribe when not connected. Channel: {Channel}", channel);
            throw new InvalidOperationException("WebSocket is not connected");
        }

        object message;
        if (requiresAuth)
        {
            var authToken = await GetOrRefreshAuthTokenAsync(cancellationToken);
            message = new { type = "subscribe", channel, auth = authToken };
        }
        else
        {
            message = new { type = "subscribe", channel };
        }

        await SendMessageAsync(message, cancellationToken);
        _subscriptions[channel] = requiresAuth;

        _logger.LogDebug("Subscribed to {Channel} (auth={RequiresAuth})", channel, requiresAuth);
    }

    private async Task<string> GetOrRefreshAuthTokenAsync(CancellationToken cancellationToken)
    {
        await _authLock.WaitAsync(cancellationToken);
        try
        {
            // Refresh if expired or expiring soon (within 60 seconds)
            if (_currentAuthToken == null || DateTimeOffset.UtcNow > _authTokenExpiry.AddSeconds(-60))
            {
                var (token, error) = await _signerClient.CreateAuthTokenAsync(600);
                if (error != null)
                    throw new InvalidOperationException($"Failed to create auth token: {error}");

                _currentAuthToken = token!;
                _authTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(600);

                _logger.LogDebug("Auth token refreshed, expires at {Expiry}", _authTokenExpiry);
            }

            return _currentAuthToken;
        }
        finally
        {
            _authLock.Release();
        }
    }

    private async Task AuthRefreshLoopAsync(CancellationToken cancellationToken)
    {
        var refreshInterval = TimeSpan.FromSeconds(_options.AuthTokenRefreshSeconds);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(refreshInterval, cancellationToken);

                if (_connectionState == ConnectionState.Connected)
                {
                    _ = await GetOrRefreshAuthTokenAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in auth refresh loop");
            }
        }
    }

    private async Task ResubscribeAllAsync(CancellationToken cancellationToken)
    {
        var subscriptions = _subscriptions.ToArray();
        foreach (var (channel, requiresAuth) in subscriptions)
        {
            try
            {
                await SubscribeAsync(channel, requiresAuth, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resubscribe to {Channel}", channel);
            }
        }
    }

    private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, LighterJsonOptions.Default);
        _logger.LogDebug("WS sending: {Json}", json.Length > 200 ? json[..200] + "..." : json);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException(
                    $"Cannot send message: WebSocket state is {_webSocket?.State ?? WebSocketState.None}");
            }

            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Server closed WebSocket connection");
                    await HandleDisconnectAsync("Server closed connection", cancellationToken);
                    break;
                }

                var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                _logger.LogTrace("WS raw message received, length={Length}", messageJson.Length);
                await RouteMessageAsync(messageJson, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket error in receive loop");
                await HandleDisconnectAsync(ex.Message, cancellationToken);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in receive loop");
                await HandleDisconnectAsync(ex.Message, cancellationToken);
                break;
            }
        }
    }

    private async ValueTask RouteMessageAsync(string json, CancellationToken cancellationToken)
    {
        _logger.LogDebug("WS received: {Json}", TruncateJson(json));

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Some messages don't have a type field - handle them by content
            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            switch (type)
            {
                case "ping":
                    await SendPongAsync(cancellationToken);
                    break;

                case "subscribed":
                    HandleSubscribedMessage(root);
                    break;

                case "error":
                    HandleErrorMessage(root);
                    break;

                case "update" when root.TryGetProperty("channel", out var channelProp):
                    var channel = channelProp.GetString() ?? "";
                    await RouteUpdateMessageAsync(channel, json, cancellationToken);
                    break;

                case "jsonapi/sendtx":
                    HandleSendTxResponse(json);
                    break;

                case "jsonapi/sendtxbatch":
                    HandleSendTxBatchResponse(json);
                    break;

                default:
                    // Try to route based on data presence
                    if (root.TryGetProperty("order_book", out _))
                    {
                        await HandleOrderBookMessageAsync(json, cancellationToken);
                    }
                    else if (root.TryGetProperty("account", out _))
                    {
                        await HandleAccountMessageAsync(json, cancellationToken);
                    }
                    else if (root.TryGetProperty("orders", out _))
                    {
                        await HandleOrdersMessageAsync(json, cancellationToken);
                    }
                    else if (root.TryGetProperty("market_stats", out _))
                    {
                        await HandleMarketStatsMessageAsync(json, cancellationToken);
                    }
                    else if (root.TryGetProperty("notifs", out _))
                    {
                        await HandleNotificationMessageAsync(json, cancellationToken);
                    }
                    else if (root.TryGetProperty("stats", out _))
                    {
                        await HandleUserStatsMessageAsync(json, cancellationToken);
                    }
                    else
                    {
                        _logger.LogDebug("Unknown message type: {Type}, json: {Json}", type, TruncateJson(json));
                    }
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse WebSocket message: {Json}", TruncateJson(json));
        }
    }

    private async Task RouteUpdateMessageAsync(string channel, string json, CancellationToken cancellationToken)
    {
        // Server may use colon or slash separator (e.g., "order_book:1" or "order_book/1")
        if (channel.StartsWith("order_book/", StringComparison.OrdinalIgnoreCase) ||
            channel.StartsWith("order_book:", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOrderBookMessageAsync(json, cancellationToken);
        }
        else if (channel.StartsWith("account_all/", StringComparison.OrdinalIgnoreCase) ||
                 channel.StartsWith("account_all:", StringComparison.OrdinalIgnoreCase))
        {
            await HandleAccountMessageAsync(json, cancellationToken);
        }
        else if (channel.StartsWith("account_all_orders/", StringComparison.OrdinalIgnoreCase) ||
                 channel.StartsWith("account_all_orders:", StringComparison.OrdinalIgnoreCase))
        {
            await HandleOrdersMessageAsync(json, cancellationToken);
        }
        else if (channel.StartsWith("market_stats/", StringComparison.OrdinalIgnoreCase) ||
                 channel.StartsWith("market_stats:", StringComparison.OrdinalIgnoreCase))
        {
            await HandleMarketStatsMessageAsync(json, cancellationToken);
        }
        else if (channel.StartsWith("notification/", StringComparison.OrdinalIgnoreCase) ||
                 channel.StartsWith("notification:", StringComparison.OrdinalIgnoreCase))
        {
            await HandleNotificationMessageAsync(json, cancellationToken);
        }
        else if (channel.StartsWith("user_stats/", StringComparison.OrdinalIgnoreCase) ||
                 channel.StartsWith("user_stats:", StringComparison.OrdinalIgnoreCase))
        {
            await HandleUserStatsMessageAsync(json, cancellationToken);
        }
        else
        {
            _logger.LogDebug("Unknown channel update: {Channel}", channel);
        }
    }

    private async Task SendPongAsync(CancellationToken cancellationToken)
    {
        await SendMessageAsync(new { type = "pong" }, cancellationToken);
        _logger.LogTrace("Sent pong response");
    }

    private void HandleSubscribedMessage(JsonElement root)
    {
        var channel = root.TryGetProperty("channel", out var ch) ? ch.GetString() : "unknown";
        var offset = root.TryGetProperty("offset", out var off) ? off.GetInt64() : 0;
        _logger.LogDebug("Subscribed to {Channel} at offset {Offset}", channel, offset);
    }

    private void HandleErrorMessage(JsonElement root)
    {
        var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
        var message = root.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
        _logger.LogError("WebSocket error: Code={Code}, Message={Message}", code, message);
    }

    private async Task HandleOrderBookMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<OrderBookMessage>(json, LighterJsonOptions.Default);
            if (msg?.OrderBook == null)
            {
                _logger.LogWarning("Order book message deserialized but OrderBook is null. Channel: {Channel}", msg?.Channel);
                return;
            }

            var marketId = ExtractMarketIdFromChannel(msg.Channel, "order_book/");
            var snapshot = ParseOrderBookSnapshot(msg.OrderBook);

            var evt = new OrderBookUpdateEvent
            {
                MarketId = marketId,
                Snapshot = snapshot,
                Offset = msg.Offset
            };

            WriteToChannel(_orderBookChannel, evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle order book message");
        }
    }

    private async Task HandleAccountMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<AccountAllMessage>(json, LighterJsonOptions.Default);
            if (msg == null) return;

            // Calculate collateral from USDC asset balance (asset_id 3)
            decimal collateral = 0m;
            decimal availableBalance = 0m;
            if (msg.Assets?.TryGetValue("3", out var usdcAsset) == true)
            {
                collateral = ParseDecimal(usdcAsset.Balance);
                availableBalance = collateral - ParseDecimal(usdcAsset.LockedBalance);
            }

            // Parse positions from dictionary
            var positions = new List<PositionSnapshot>();
            if (msg.Positions != null)
            {
                foreach (var (_, posData) in msg.Positions)
                {
                    // Include sign in position size for direction
                    var posSize = ParseDecimal(posData.Position) * posData.Sign;
                    positions.Add(new PositionSnapshot
                    {
                        MarketId = posData.MarketId,
                        Size = posSize,
                        AvgEntryPrice = ParseDecimal(posData.AvgEntryPrice),
                        UnrealizedPnl = ParseDecimal(posData.UnrealizedPnl),
                        LiquidationPrice = ParseDecimal(posData.LiquidationPrice),
                        IsCross = posData.MarginMode == 0 // 0 = cross, 1 = isolated
                    });
                }
            }

                

            var evt = new AccountUpdateEvent
            {
                AccountId = msg.Account,
                Collateral = collateral,
                AvailableBalance = availableBalance,
                PortfolioValue = collateral, // Portfolio value = collateral for now
                Positions = positions
            };

            WriteToChannel(_accountChannel, evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle account message: {Json}", json[..Math.Min(200, json.Length)]);
        }
    }

    private async Task HandleOrdersMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<OrdersMessage>(json, LighterJsonOptions.Default);
            if (msg?.Orders == null) return;

            foreach (var (marketIdStr, orders) in msg.Orders)
            {
                if (!int.TryParse(marketIdStr, out var marketId)) continue;

                var evt = new OrderUpdateEvent
                {
                    MarketId = marketId,
                    Orders = orders.Select(o => new OrderSnapshot
                    {
                        OrderIndex = o.OrderIndex,
                        Price = ParseDecimal(o.Price),
                        Size = ParseDecimal(o.InitialBaseAmount),
                        FilledSize = ParseDecimal(o.FilledBaseAmount),
                        Status = o.Status,
                        IsBuy = !o.IsAsk // IsAsk=true means sell, so IsBuy = !IsAsk
                    }).ToList()
                };

                WriteToChannel(_ordersChannel, evt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle orders message: {Json}", json[..Math.Min(200, json.Length)]);
        }
    }

    private async Task HandleMarketStatsMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<MarketStatsMessage>(json, LighterJsonOptions.Default);
            if (msg?.MarketStats == null) return;

            var evt = new MarketStatsUpdateEvent
            {
                MarketId = msg.MarketStats.MarketId,
                IndexPrice = ParseDecimal(msg.MarketStats.IndexPrice),
                MarkPrice = ParseDecimal(msg.MarketStats.MarkPrice),
                FundingRate = ParseDecimal(msg.MarketStats.CurrentFundingRate),
                Volume24h = msg.MarketStats.DailyQuoteTokenVolume
            };

            WriteToChannel(_marketStatsChannel, evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle market stats message");
        }
    }

    private async Task HandleNotificationMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<NotificationMessage>(json, LighterJsonOptions.Default);
            if (msg?.Notifs == null || msg.Notifs.Count == 0) return;

            foreach (var notif in msg.Notifs)
            {
                // Build message from notification content based on kind
                var message = BuildNotificationMessage(notif);

                var evt = new NotificationEvent
                {
                    NotificationType = notif.Kind,
                    Message = message,
                    MarketId = notif.Content?.MarketIndex
                };

                WriteToChannel(_notificationChannel, evt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle notification message");
        }
    }

    private async Task HandleUserStatsMessageAsync(string json, CancellationToken cancellationToken)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<UserStatsMessage>(json, LighterJsonOptions.Default);
            if (msg?.Stats == null) return;

            var stats = msg.Stats;
            var evt = new UserStatsUpdateEvent
            {
                AccountId = _lighterOptions.AccountIndex,
                Collateral = ParseDecimal(stats.Collateral),
                PortfolioValue = ParseDecimal(stats.PortfolioValue),
                AvailableBalance = ParseDecimal(stats.AvailableBalance),
                BuyingPower = ParseDecimal(stats.BuyingPower),
                Leverage = ParseDecimal(stats.Leverage),
                MarginUsage = ParseDecimal(stats.MarginUsage)
            };

            WriteToChannel(_userStatsChannel, evt);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "User stats update: collateral={Collateral:F2} available={Available:F2} buyingPower={BuyingPower:F2}",
                    evt.Collateral,
                    evt.AvailableBalance,
                    evt.BuyingPower);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle user stats message: {Json}", json[..Math.Min(200, json.Length)]);
        }
    }

    private static string BuildNotificationMessage(NotificationItem notif)
    {
        if (notif.Content == null)
            return $"Notification: {notif.Kind}";

        return notif.Kind switch
        {
            "liquidation" => $"Liquidation: {notif.Content.Size} at {notif.Content.Price}",
            "deleverage" => $"Deleverage at settlement price {notif.Content.SettlementPrice}",
            "announcement" => notif.Content.ContentText,
            _ => $"{notif.Kind}: {notif.Content.ContentText}"
        };
    }

    private OrderBookSnapshot ParseOrderBookSnapshot(OrderBookData data)
    {
        var bids = data.Bids
            .Select(b => (ParseDecimal(b.Price), ParseDecimal(b.Size)))
            .OrderByDescending(x => x.Item1)
            .ToList();

        var asks = data.Asks
            .Select(a => (ParseDecimal(a.Price), ParseDecimal(a.Size)))
            .OrderBy(x => x.Item1)
            .ToList();

        var bestBid = bids.FirstOrDefault();
        var bestAsk = asks.FirstOrDefault();
        var midPrice = (bestBid.Item1 + bestAsk.Item1) / 2m;
        var spread = bestAsk.Item1 - bestBid.Item1;
        var spreadPercent = midPrice > 0 ? spread / midPrice * 100m : 0m;

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
            Asks = asks
        };
    }

    private static int ExtractMarketIdFromChannel(string? channel, string prefix)
    {
        if (string.IsNullOrEmpty(channel)) return 0;

        // Server may use colon or slash separator (e.g., "order_book:1" or "order_book/1")
        var prefixWithColon = prefix.Replace("/", ":");
        var suffix = channel
            .Replace(prefix, "", StringComparison.OrdinalIgnoreCase)
            .Replace(prefixWithColon, "", StringComparison.OrdinalIgnoreCase);

        return int.TryParse(suffix, out var id) ? id : 0;
    }

    private static void WriteToChannel<T>(Channel<T> channel, T item)
    {
        // TryWrite is non-blocking; DropOldest mode handles overflow
        if (!channel.Writer.TryWrite(item))
        {
            // This should rarely happen with DropOldest mode
            // Log if we want to track channel pressure
        }
    }

    private async Task HandleDisconnectAsync(string reason, CancellationToken cancellationToken)
    {
        if (_connectionState == ConnectionState.Reconnecting || _connectionState == ConnectionState.Failed)
            return;

        // CRITICAL: Cancel all pending requests immediately to prevent 30-second stalls
        CancelAllPendingRequests(new InvalidOperationException($"WebSocket disconnected: {reason}"));

        await SetConnectionStateAsync(ConnectionState.Reconnecting, reason, cancellationToken);

        while (_reconnectAttempts < _options.MaxReconnectAttempts && !cancellationToken.IsCancellationRequested)
        {
            _reconnectAttempts++;
            var delay = CalculateBackoff(_reconnectAttempts);

            _logger.LogInformation(
                "Reconnecting in {Delay}ms (attempt {Attempt}/{Max})",
                delay.TotalMilliseconds, _reconnectAttempts, _options.MaxReconnectAttempts);

            try
            {
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                // Release and reacquire lock to allow ConnectAsync to work
                await ConnectAsync(cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", _reconnectAttempts);
            }
        }

        await SetConnectionStateAsync(
            ConnectionState.Failed,
            "Max reconnection attempts exceeded",
            cancellationToken);
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var exponentialDelay = Math.Min(
            _options.MaxReconnectDelayMs,
            _options.ReconnectDelayMs * Math.Pow(2, attempt - 1));
        var jitter = Random.Shared.Next(0, 500);
        return TimeSpan.FromMilliseconds(exponentialDelay + jitter);
    }

    private async Task SetConnectionStateAsync(ConnectionState state, string? reason, CancellationToken cancellationToken)
    {
        _connectionState = state;

        var evt = new ConnectionStateEvent
        {
            State = state,
            Reason = reason,
            ReconnectAttempt = _reconnectAttempts
        };

        WriteToChannel(_connectionChannel, evt);

        _logger.LogInformation(
            "Connection state changed to {State}" + (reason != null ? ": {Reason}" : ""),
            state, reason);
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }

    private static string TruncateJson(string json)
    {
        return json.Length > 200 ? json[..200] + "..." : json;
    }

    /// <summary>
    /// Cancels all pending transaction requests with the specified exception.
    /// This is called during disconnect or dispose to prevent callers from hanging.
    /// </summary>
    /// <param name="exception">The exception to set on all pending requests.</param>
    private void CancelAllPendingRequests(Exception exception)
    {
        var pendingIds = _pendingRequests.Keys.ToArray();
        foreach (var id in pendingIds)
        {
            if (_pendingRequests.TryRemove(id, out var tcs))
            {
                tcs.TrySetException(exception);
            }
        }

        if (pendingIds.Length > 0)
        {
            _logger.LogWarning("Canceled {Count} pending transaction requests due to: {Reason}",
                pendingIds.Length, exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<SendTxWsResponse> SendTransactionAsync(
        int txType,
        string txInfo,
        bool? priceProtection = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected");

        var requestId = $"tx_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingRequests.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException($"Duplicate request ID: {requestId}");
        }

        try
        {
            var data = new Dictionary<string, object>
            {
                ["id"] = requestId,
                ["tx_type"] = txType,
                ["tx_info"] = txInfo
            };

            if (priceProtection.HasValue)
                data["price_protection"] = priceProtection.Value;

            var message = new { type = "jsonapi/sendtx", data };
            await SendMessageAsync(message, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.TransactionTimeoutMs);

            try
            {
                var result = await tcs.Task.WaitAsync(cts.Token);
                return (SendTxWsResponse)result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred (not caller cancellation)
                _logger.LogError(
                    "Transaction {RequestId} timed out after {Timeout}ms - STATUS UNKNOWN. " +
                    "Verify transaction state before retrying.",
                    requestId, _options.TransactionTimeoutMs);
                throw new TransactionStatusUnknownException(
                    requestId,
                    $"Transaction timed out after {_options.TransactionTimeoutMs}ms. Status unknown - verify before retrying.");
            }
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    /// <inheritdoc />
    public async Task<SendTxBatchWsResponse> SendTransactionBatchAsync(
        int[] txTypes,
        string[] txInfos,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket?.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket is not connected");

        if (txTypes.Length != txInfos.Length)
            throw new ArgumentException("txTypes and txInfos must have the same length");

        if (txTypes.Length > 50)
            throw new ArgumentException("Maximum 50 transactions per batch");

        var requestId = $"txbatch_{Guid.NewGuid():N}";
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_pendingRequests.TryAdd(requestId, tcs))
        {
            throw new InvalidOperationException($"Duplicate request ID: {requestId}");
        }

        try
        {
            // IMPORTANT: tx_types and tx_infos must be JSON stringified strings, not arrays
            var message = new
            {
                type = "jsonapi/sendtxbatch",
                data = new
                {
                    id = requestId,
                    tx_types = JsonSerializer.Serialize(txTypes),
                    tx_infos = JsonSerializer.Serialize(txInfos)
                }
            };

            await SendMessageAsync(message, cancellationToken);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.BatchTransactionTimeoutMs);

            try
            {
                var result = await tcs.Task.WaitAsync(cts.Token);
                return (SendTxBatchWsResponse)result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout occurred (not caller cancellation)
                _logger.LogError(
                    "Batch transaction {RequestId} timed out after {Timeout}ms - STATUS UNKNOWN. " +
                    "Verify transaction state before retrying.",
                    requestId, _options.BatchTransactionTimeoutMs);
                throw new TransactionStatusUnknownException(
                    requestId,
                    $"Batch transaction timed out after {_options.BatchTransactionTimeoutMs}ms. Status unknown - verify before retrying.");
            }
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    private void HandleSendTxResponse(string json)
    {
        try
        {
            var response = JsonSerializer.Deserialize<SendTxWsResponse>(json, LighterJsonOptions.Default);
            if (response == null) return;

            if (_pendingRequests.TryGetValue(response.Id, out var tcs))
            {
                tcs.TrySetResult(response);
            }
            else
            {
                _logger.LogWarning("Received SendTx response for unknown request ID: {Id}", response.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle SendTx response");
        }
    }

    private void HandleSendTxBatchResponse(string json)
    {
        try
        {
            var response = JsonSerializer.Deserialize<SendTxBatchWsResponse>(json, LighterJsonOptions.Default);
            if (response == null) return;

            if (_pendingRequests.TryGetValue(response.Id, out var tcs))
            {
                tcs.TrySetResult(response);
            }
            else
            {
                _logger.LogWarning("Received SendTxBatch response for unknown request ID: {Id}", response.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle SendTxBatch response");
        }
    }
}
