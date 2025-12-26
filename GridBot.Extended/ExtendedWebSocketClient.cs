using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GridBot.Extended.Models;
using GridBot.Extended.Models.Api;
using GridBot.Extended.Models.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended;

/// <summary>
/// WebSocket client for real-time Extended DEX data streaming.
/// Thread-safe implementation using bounded channels for backpressure handling.
/// Implements automatic reconnection with exponential backoff.
/// </summary>
public sealed class ExtendedWebSocketClient : IExtendedWebSocketClient
{
    private readonly ExtendedOptions _options;
    private readonly WebSocketOptions _wsOptions;
    private readonly ILogger<ExtendedWebSocketClient> _logger;

    // Connection state
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Task? _heartbeatTask;
    private volatile ConnectionState _connectionState = ConnectionState.Disconnected;
    private int _reconnectAttempts;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    // Subscription tracking
    private readonly ConcurrentDictionary<string, bool> _subscriptions = new();

    // Message timing
    private DateTimeOffset _lastMessageTime;
    private readonly ConcurrentQueue<DateTimeOffset> _disconnectTimes = new();

    // Channels - bounded with DropOldest for trading data (stale data is useless)
    private readonly Channel<OrderBookUpdateEvent> _orderBookChannel;
    private readonly Channel<AccountUpdateEvent> _accountChannel;
    private readonly Channel<OrderUpdateEvent> _ordersChannel;
    private readonly Channel<TradeUpdateEvent> _tradesChannel;
    private readonly Channel<BalanceUpdateEvent> _balancesChannel;
    private readonly Channel<PositionUpdateEvent> _positionsChannel;
    private readonly Channel<ConnectionStateEvent> _connectionChannel;

    private bool _disposed;

    /// <inheritdoc />
    public ChannelReader<OrderBookUpdateEvent> OrderBookUpdates => _orderBookChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<AccountUpdateEvent> AccountUpdates => _accountChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<OrderUpdateEvent> OrderUpdates => _ordersChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<TradeUpdateEvent> TradeUpdates => _tradesChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<BalanceUpdateEvent> BalanceUpdates => _balancesChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<PositionUpdateEvent> PositionUpdates => _positionsChannel.Reader;

    /// <inheritdoc />
    public ChannelReader<ConnectionStateEvent> ConnectionStateChanges => _connectionChannel.Reader;

    /// <inheritdoc />
    public ConnectionState ConnectionState => _connectionState;

    /// <inheritdoc />
    public bool IsConnected => _connectionState == ConnectionState.Connected;

    /// <inheritdoc />
    public TimeSpan? TimeSinceLastMessage =>
        _lastMessageTime == default ? null : DateTimeOffset.UtcNow - _lastMessageTime;

    /// <inheritdoc />
    public int DisconnectCount24h
    {
        get
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            while (_disconnectTimes.TryPeek(out var oldest) && oldest < cutoff)
            {
                _disconnectTimes.TryDequeue(out _);
            }
            return _disconnectTimes.Count;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedWebSocketClient"/> class.
    /// </summary>
    /// <param name="options">Extended options.</param>
    /// <param name="wsOptions">WebSocket options.</param>
    /// <param name="logger">Logger instance.</param>
    public ExtendedWebSocketClient(
        IOptions<ExtendedOptions> options,
        IOptions<WebSocketOptions> wsOptions,
        ILogger<ExtendedWebSocketClient> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _wsOptions = wsOptions?.Value ?? throw new ArgumentNullException(nameof(wsOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Create bounded channels with configured capacity
        var channelOptions = new BoundedChannelOptions(_wsOptions.ChannelCapacity)
        {
            FullMode = _wsOptions.FullMode,
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        };

        _orderBookChannel = Channel.CreateBounded<OrderBookUpdateEvent>(channelOptions);
        _accountChannel = Channel.CreateBounded<AccountUpdateEvent>(channelOptions);
        _ordersChannel = Channel.CreateBounded<OrderUpdateEvent>(channelOptions);
        _tradesChannel = Channel.CreateBounded<TradeUpdateEvent>(channelOptions);
        _balancesChannel = Channel.CreateBounded<BalanceUpdateEvent>(channelOptions);
        _positionsChannel = Channel.CreateBounded<PositionUpdateEvent>(channelOptions);
        _connectionChannel = Channel.CreateBounded<ConnectionStateEvent>(channelOptions);
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectLock.WaitAsync(ct);
        try
        {
            if (_connectionState == ConnectionState.Connected)
            {
                _logger.LogDebug("Already connected to WebSocket");
                return;
            }

            await SetConnectionStateAsync(ConnectionState.Connecting, null);

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader(ExtendedConstants.ApiKeyHeader, _options.ApiKey);
            _webSocket.Options.SetRequestHeader("User-Agent", _options.UserAgent);

            var wsUrl = GetWebSocketUrl();
            _logger.LogInformation("Connecting to WebSocket at {Url}", wsUrl);

            await _webSocket.ConnectAsync(new Uri(wsUrl), ct);

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
            _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(_receiveCts.Token), _receiveCts.Token);

            await SetConnectionStateAsync(ConnectionState.Connected, null);
            _reconnectAttempts = 0;
            _lastMessageTime = DateTimeOffset.UtcNow;

            _logger.LogInformation("WebSocket connected successfully");

            // Resubscribe to all previous subscriptions
            await ResubscribeAllAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to WebSocket");
            await SetConnectionStateAsync(ConnectionState.Disconnected, ex.Message);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync(CancellationToken ct = default)
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
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during WebSocket close");
            }
        }

        await SetConnectionStateAsync(ConnectionState.Disconnected, "Client initiated disconnect");
    }

    /// <inheritdoc />
    public async Task SubscribeOrderBookAsync(string market, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        var channel = $"/orderbooks/{market}";
        await SubscribeAsync(channel, ct);
    }

    /// <inheritdoc />
    public async Task SubscribeAccountAsync(CancellationToken ct = default)
    {
        var channel = "/account";
        await SubscribeAsync(channel, ct);
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot unsubscribe when not connected");
            return;
        }

        var message = new { type = "unsubscribe", channel };
        await SendMessageAsync(message, ct);
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

        // Complete all channels
        _orderBookChannel.Writer.TryComplete();
        _accountChannel.Writer.TryComplete();
        _ordersChannel.Writer.TryComplete();
        _tradesChannel.Writer.TryComplete();
        _balancesChannel.Writer.TryComplete();
        _positionsChannel.Writer.TryComplete();
        _connectionChannel.Writer.TryComplete();

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

        if (_heartbeatTask != null)
        {
            try
            {
                await _heartbeatTask.WaitAsync(TimeSpan.FromSeconds(5));
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

        _logger.LogInformation("WebSocket client disposed");
    }

    private string GetWebSocketUrl()
    {
        if (!string.IsNullOrEmpty(_wsOptions.WebSocketUrl))
            return _wsOptions.WebSocketUrl;

        // Derive WebSocket URL based on environment
        return _options.IsTestnet
            ? "wss://starknet.sepolia.extended.exchange/stream.extended.exchange/v1"
            : "wss://api.starknet.extended.exchange/stream.extended.exchange/v1";
    }

    private async Task SubscribeAsync(string channel, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot subscribe when not connected. Channel: {Channel}", channel);
            throw new InvalidOperationException("WebSocket is not connected");
        }

        var message = new { type = "subscribe", channel };
        await SendMessageAsync(message, ct);
        _subscriptions[channel] = true;

        _logger.LogDebug("Subscribed to {Channel}", channel);
    }

    private async Task ResubscribeAllAsync(CancellationToken ct)
    {
        var subscriptions = _subscriptions.Keys.ToArray();
        foreach (var channel in subscriptions)
        {
            try
            {
                await SubscribeAsync(channel, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resubscribe to {Channel}", channel);
            }
        }
    }

    private async Task SendMessageAsync(object message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, ExtendedJsonOptions.Default);
        _logger.LogDebug("WS sending: {Json}", json.Length > 200 ? json[..200] + "..." : json);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _sendLock.WaitAsync(ct);
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
                ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();

        while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Server closed WebSocket connection");
                    await HandleDisconnectAsync("Server closed connection", ct);
                    break;
                }

                _lastMessageTime = DateTimeOffset.UtcNow;
                var messageJson = Encoding.UTF8.GetString(ms.ToArray());
                await RouteMessageAsync(messageJson, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket error in receive loop");
                await HandleDisconnectAsync(ex.Message, ct);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in receive loop");
                await HandleDisconnectAsync(ex.Message, ct);
                break;
            }
        }
    }

    private async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_wsOptions.HeartbeatIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);

                if (_connectionState != ConnectionState.Connected)
                    continue;

                // Check for data staleness
                var timeSinceLastMessage = TimeSinceLastMessage;
                if (timeSinceLastMessage.HasValue &&
                    timeSinceLastMessage.Value.TotalSeconds > _wsOptions.DataStalenessThresholdSeconds)
                {
                    _logger.LogWarning(
                        "Data staleness detected: {Seconds}s since last message (threshold: {Threshold}s)",
                        timeSinceLastMessage.Value.TotalSeconds,
                        _wsOptions.DataStalenessThresholdSeconds);
                }

                // Send ping
                await SendMessageAsync(new { type = "ping" }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in heartbeat loop");
            }
        }
    }

    private async ValueTask RouteMessageAsync(string json, CancellationToken ct)
    {
        _logger.LogTrace("WS received: {Json}", json.Length > 200 ? json[..200] + "..." : json);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.TryGetProperty("type", out var typeElement)
                ? typeElement.GetString()
                : null;

            var updateType = root.TryGetProperty("updateType", out var updateTypeElement)
                ? updateTypeElement.GetString()
                : null;

            switch (type)
            {
                case "pong":
                    _logger.LogTrace("Received pong");
                    break;

                case "subscribed":
                    HandleSubscribedMessage(root);
                    break;

                case "error":
                    HandleErrorMessage(root);
                    break;

                default:
                    // Route based on content
                    if (root.TryGetProperty("bids", out _) || root.TryGetProperty("asks", out _))
                    {
                        await HandleOrderBookMessageAsync(json, ct);
                    }
                    else if (!string.IsNullOrEmpty(updateType))
                    {
                        await HandleAccountUpdateMessageAsync(json, updateType, ct);
                    }
                    else
                    {
                        _logger.LogDebug("Unknown message type: {Type}", type);
                    }
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse WebSocket message");
        }
    }

    private void HandleSubscribedMessage(JsonElement root)
    {
        var channel = root.TryGetProperty("channel", out var ch) ? ch.GetString() : "unknown";
        _logger.LogDebug("Subscribed to {Channel}", channel);
    }

    private void HandleErrorMessage(JsonElement root)
    {
        var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
        var message = root.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";
        _logger.LogError("WebSocket error: Code={Code}, Message={Message}", code, message);
    }

    private async Task HandleOrderBookMessageAsync(string json, CancellationToken ct)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<OrderBookMessage>(json, ExtendedJsonOptions.Default);
            if (msg == null) return;

            var snapshot = ParseOrderBookSnapshot(msg);

            var evt = new OrderBookUpdateEvent
            {
                MarketId = msg.Market ?? "unknown",
                Snapshot = snapshot,
                Sequence = msg.Sequence
            };

            WriteToChannel(_orderBookChannel, evt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle order book message");
        }
    }

    private async Task HandleAccountUpdateMessageAsync(string json, string updateType, CancellationToken ct)
    {
        try
        {
            var msg = JsonSerializer.Deserialize<AccountUpdateMessage>(json, ExtendedJsonOptions.Default);
            if (msg == null) return;

            switch (updateType)
            {
                case "order" when msg.Order != null:
                    HandleOrderUpdate(msg.Order);
                    break;

                case "trade" when msg.Trade != null:
                    HandleTradeUpdate(msg.Trade);
                    break;

                case "balance" when msg.Balance != null:
                    HandleBalanceUpdate(msg.Balance);
                    break;

                case "position" when msg.Position != null:
                    HandlePositionUpdate(msg.Position);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to handle account update message");
        }
    }

    private void HandleOrderUpdate(OrderUpdateData order)
    {
        var state = order.Status switch
        {
            "open" or "partial" => OrderState.Active,
            "filled" => OrderState.Filled,
            "cancelled" => OrderState.Cancelled,
            "rejected" => OrderState.Rejected,
            _ => OrderState.Unknown
        };

        var evt = new OrderUpdateEvent
        {
            MarketId = order.Market,
            OrderId = order.Id,
            ClientOrderId = order.ClientOrderId,
            IsBuy = order.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase),
            Price = ParseDecimal(order.Price),
            Size = ParseDecimal(order.Qty),
            FilledSize = ParseDecimal(order.FilledQty),
            Status = order.Status,
            State = state,
            RejectReason = order.RejectReason,
            ReduceOnly = order.ReduceOnly
        };

        WriteToChannel(_ordersChannel, evt);
    }

    private void HandleTradeUpdate(TradeUpdateData trade)
    {
        var evt = new TradeUpdateEvent
        {
            TradeId = trade.Id,
            OrderId = trade.OrderId,
            MarketId = trade.Market,
            IsBuy = trade.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase),
            Price = ParseDecimal(trade.Price),
            Quantity = ParseDecimal(trade.Qty),
            Fee = ParseDecimal(trade.Fee),
            FeeAsset = trade.FeeAsset,
            IsMaker = trade.IsMaker
        };

        WriteToChannel(_tradesChannel, evt);
    }

    private void HandleBalanceUpdate(BalanceUpdateData balance)
    {
        var evt = new BalanceUpdateEvent
        {
            Asset = balance.Asset,
            Total = ParseDecimal(balance.Total),
            Available = ParseDecimal(balance.Available),
            Locked = ParseDecimal(balance.Locked)
        };

        WriteToChannel(_balancesChannel, evt);
    }

    private void HandlePositionUpdate(PositionUpdateData position)
    {
        var size = ParseDecimal(position.Size);
        if (position.Side.Equals("SHORT", StringComparison.OrdinalIgnoreCase))
        {
            size = -Math.Abs(size);
        }

        var evt = new PositionUpdateEvent
        {
            MarketId = position.Market,
            Size = size,
            EntryPrice = ParseDecimal(position.EntryPrice),
            MarkPrice = ParseDecimal(position.MarkPrice),
            UnrealizedPnl = ParseDecimal(position.UnrealizedPnl),
            LiquidationPrice = string.IsNullOrEmpty(position.LiquidationPrice)
                ? null
                : ParseDecimal(position.LiquidationPrice),
            Leverage = position.Leverage
        };

        WriteToChannel(_positionsChannel, evt);
    }

    private OrderBookSnapshot ParseOrderBookSnapshot(OrderBookMessage msg)
    {
        var bids = (msg.Bids ?? [])
            .Select(b => (ParseDecimal(b.Price), ParseDecimal(b.Size)))
            .Where(x => x.Item2 > 0)
            .OrderByDescending(x => x.Item1)
            .ToList();

        var asks = (msg.Asks ?? [])
            .Select(a => (ParseDecimal(a.Price), ParseDecimal(a.Size)))
            .Where(x => x.Item2 > 0)
            .OrderBy(x => x.Item1)
            .ToList();

        var bestBid = bids.Count > 0 ? bids[0] : (0m, 0m);
        var bestAsk = asks.Count > 0 ? asks[0] : (0m, 0m);

        decimal midPrice;
        decimal spread;
        decimal spreadPercent;

        if (bestBid.Item1 > 0 && bestAsk.Item1 > 0)
        {
            midPrice = (bestBid.Item1 + bestAsk.Item1) / 2m;
            spread = bestAsk.Item1 - bestBid.Item1;
            spreadPercent = midPrice > 0 ? spread / midPrice * 100m : 0m;
        }
        else
        {
            midPrice = bestBid.Item1 > 0 ? bestBid.Item1 : bestAsk.Item1;
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
            Asks = asks
        };
    }

    private async Task HandleDisconnectAsync(string reason, CancellationToken ct)
    {
        if (_connectionState is ConnectionState.Reconnecting or ConnectionState.Failed)
            return;

        _disconnectTimes.Enqueue(DateTimeOffset.UtcNow);
        await SetConnectionStateAsync(ConnectionState.Reconnecting, reason);

        while (_reconnectAttempts < _wsOptions.MaxReconnectAttempts && !ct.IsCancellationRequested)
        {
            _reconnectAttempts++;
            var delay = CalculateBackoff(_reconnectAttempts);

            _logger.LogInformation(
                "Reconnecting in {Delay}ms (attempt {Attempt})",
                delay.TotalMilliseconds, _reconnectAttempts);

            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await ConnectAsync(ct);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", _reconnectAttempts);
            }
        }

        await SetConnectionStateAsync(ConnectionState.Failed, "Max reconnection attempts exceeded");
    }

    private TimeSpan CalculateBackoff(int attempt)
    {
        var exponentialDelay = Math.Min(
            _wsOptions.MaxReconnectDelayMs,
            _wsOptions.ReconnectDelayMs * Math.Pow(2, attempt - 1));
        var jitter = Random.Shared.Next(0, 500);
        return TimeSpan.FromMilliseconds(exponentialDelay + jitter);
    }

    private async Task SetConnectionStateAsync(ConnectionState state, string? reason)
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

    private static void WriteToChannel<T>(Channel<T> channel, T item)
    {
        channel.Writer.TryWrite(item);
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return 0m;

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result)
            ? result
            : 0m;
    }
}
