# WebSocket Client Implementation Plan

**Date:** 2025-12-07
**Status:** Ready for Implementation
**Objective:** Replace REST polling with WebSocket streaming using System.Threading.Channels

---

## Executive Summary

This plan implements a high-performance WebSocket client for Lighter DEX that:
1. Uses `System.Threading.Channels` for lock-free, backpressure-aware event processing
2. Replaces REST polling for order book, account, and order data
3. Integrates seamlessly with existing `TradingDecisionEngine`
4. Reduces API weight consumption by ~12,000/min (freeing budget for order operations)

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         WebSocket Layer                                  │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │              LighterWebSocketClient                              │   │
│  │  • Connection management (connect, reconnect, ping/pong)        │   │
│  │  • Auth token refresh                                            │   │
│  │  • Message routing to channels                                   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    ▼               ▼               ▼
        ┌───────────────┐  ┌───────────────┐  ┌───────────────┐
        │ OrderBook     │  │ Account       │  │ Orders        │
        │ Channel       │  │ Channel       │  │ Channel       │
        │ (Bounded)     │  │ (Bounded)     │  │ (Bounded)     │
        └───────┬───────┘  └───────┬───────┘  └───────┬───────┘
                │                  │                  │
                ▼                  ▼                  ▼
        ┌───────────────┐  ┌───────────────┐  ┌───────────────┐
        │ OrderBook     │  │ Account       │  │ Orders        │
        │ Processor     │  │ Processor     │  │ Processor     │
        │ (Background)  │  │ (Background)  │  │ (Background)  │
        └───────┬───────┘  └───────┬───────┘  └───────┬───────┘
                │                  │                  │
                └──────────────────┼──────────────────┘
                                   ▼
                    ┌─────────────────────────────┐
                    │   ILighterRealtimeState     │
                    │   (Thread-safe snapshot)    │
                    │   • Latest order book       │
                    │   • Latest account/position │
                    │   • Latest orders           │
                    │   • Connection status       │
                    └─────────────────────────────┘
                                   │
                                   ▼
                    ┌─────────────────────────────┐
                    │   TradingDecisionEngine     │
                    │   (Reads from state OR      │
                    │    subscribes to channels)  │
                    └─────────────────────────────┘
```

---

## Phase 1: Core Infrastructure (GridBot.Lighter)

### 1.1 WebSocket Options Configuration

**File:** `GridBot.Lighter/WebSocketOptions.cs`

```csharp
public sealed class WebSocketOptions
{
    public const string SectionName = "LighterWebSocket";

    /// <summary>
    /// WebSocket URL. Auto-derived from ApiUrl if not set.
    /// Mainnet: wss://mainnet.zklighter.elliot.ai/stream
    /// Testnet: wss://testnet.zklighter.elliot.ai/stream
    /// </summary>
    public string? WebSocketUrl { get; set; }

    /// <summary>
    /// Initial reconnection delay in milliseconds.
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 1000;

    /// <summary>
    /// Maximum reconnection delay in milliseconds.
    /// </summary>
    public int MaxReconnectDelayMs { get; set; } = 60000;

    /// <summary>
    /// Auth token refresh interval in seconds (should be less than token validity).
    /// </summary>
    public int AuthTokenRefreshSeconds { get; set; } = 480; // 8 minutes

    /// <summary>
    /// Channel capacity for bounded channels.
    /// </summary>
    public int ChannelCapacity { get; set; } = 100;

    /// <summary>
    /// Behavior when channel is full. DropOldest recommended for trading.
    /// </summary>
    public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.DropOldest;

    /// <summary>
    /// Maximum reconnection attempts before giving up.
    /// </summary>
    public int MaxReconnectAttempts { get; set; } = 100;

    /// <summary>
    /// Ping timeout in seconds. Connection considered dead if no pong received.
    /// </summary>
    public int PingTimeoutSeconds { get; set; } = 30;
}
```

### 1.2 WebSocket Message Models

**File:** `GridBot.Lighter/Models/WebSocket/WebSocketMessages.cs`

```csharp
namespace GridBot.Lighter.Models.WebSocket;

// Base message for all WebSocket messages
public abstract record WebSocketMessage
{
    public required string Type { get; init; }
    public string? Channel { get; init; }
}

// Subscription messages (client -> server)
public sealed record SubscribeMessage : WebSocketMessage
{
    public string? Auth { get; init; }
}

public sealed record UnsubscribeMessage : WebSocketMessage;

public sealed record PongMessage : WebSocketMessage;

// Server messages
public sealed record PingMessage : WebSocketMessage;

public sealed record SubscribedMessage : WebSocketMessage
{
    public long Offset { get; init; }
}

public sealed record ErrorMessage : WebSocketMessage
{
    public int Code { get; init; }
    public string? Message { get; init; }
}

// Order Book messages
public sealed record OrderBookMessage : WebSocketMessage
{
    public required OrderBookData OrderBook { get; init; }
    public long Offset { get; init; }
}

public sealed record OrderBookData
{
    public int Code { get; init; }
    public List<OrderBookLevel> Asks { get; init; } = [];
    public List<OrderBookLevel> Bids { get; init; } = [];
    public long Offset { get; init; }
    public long Nonce { get; init; }
    public long Timestamp { get; init; }
}

public sealed record OrderBookLevel
{
    public required string Price { get; init; }
    public required string Size { get; init; }
}

// Account messages
public sealed record AccountAllMessage : WebSocketMessage
{
    public required AccountData Account { get; init; }
}

public sealed record AccountData
{
    public long AccountId { get; init; }
    public List<PositionData> Positions { get; init; } = [];
    public List<TradeData> Trades { get; init; } = [];
}

public sealed record PositionData
{
    public int MarketId { get; init; }
    public required string Position { get; init; }
    public required string AvgEntryPrice { get; init; }
    public required string LiquidationPrice { get; init; }
    public required string UnrealizedPnl { get; init; }
    public required string MarginMode { get; init; }
}

// Order messages
public sealed record OrdersMessage : WebSocketMessage
{
    public Dictionary<string, List<OrderData>> Orders { get; init; } = [];
}

public sealed record OrderData
{
    public long OrderIndex { get; init; }
    public int MarketIndex { get; init; }
    public required string Price { get; init; }
    public required string Size { get; init; }
    public required string FilledSize { get; init; }
    public required string Status { get; init; }
    public required string TimeInForce { get; init; }
    public required string Side { get; init; }
    public string? TriggerPrice { get; init; }
    public long CreatedAt { get; init; }
}

// Market Stats messages
public sealed record MarketStatsMessage : WebSocketMessage
{
    public required MarketStatsData MarketStats { get; init; }
}

public sealed record MarketStatsData
{
    public int MarketIndex { get; init; }
    public required string IndexPrice { get; init; }
    public required string MarkPrice { get; init; }
    public required string OpenInterest { get; init; }
    public required string FundingRate { get; init; }
    public long NextFundingTime { get; init; }
    public required string Volume24h { get; init; }
    public required string High24h { get; init; }
    public required string Low24h { get; init; }
}

// User Stats messages
public sealed record UserStatsMessage : WebSocketMessage
{
    public required UserStatsData UserStats { get; init; }
}

public sealed record UserStatsData
{
    public required string Collateral { get; init; }
    public required string PortfolioValue { get; init; }
    public required string Leverage { get; init; }
    public required string AvailableBalance { get; init; }
    public required string MarginUsage { get; init; }
    public required string BuyingPower { get; init; }
}

// Notification messages
public sealed record NotificationMessage : WebSocketMessage
{
    public required NotificationData Notification { get; init; }
}

public sealed record NotificationData
{
    public required string Type { get; init; }
    public required string Message { get; init; }
    public int? MarketIndex { get; init; }
    public long Timestamp { get; init; }
}
```

### 1.3 Channel Event Types

**File:** `GridBot.Lighter/Models/WebSocket/ChannelEvents.cs`

```csharp
namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Events pushed through channels for consumers.
/// Uses discriminated union pattern for type-safe handling.
/// </summary>
public abstract record ChannelEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

// Order Book Events
public sealed record OrderBookUpdateEvent : ChannelEvent
{
    public required int MarketId { get; init; }
    public required OrderBookSnapshot Snapshot { get; init; }
    public long Offset { get; init; }
}

public sealed record OrderBookSnapshot
{
    public required decimal BestBidPrice { get; init; }
    public required decimal BestAskPrice { get; init; }
    public required decimal BestBidSize { get; init; }
    public required decimal BestAskSize { get; init; }
    public required decimal MidPrice { get; init; }
    public required decimal Spread { get; init; }
    public required decimal SpreadPercent { get; init; }
    public required IReadOnlyList<(decimal Price, decimal Size)> Bids { get; init; }
    public required IReadOnlyList<(decimal Price, decimal Size)> Asks { get; init; }
}

// Account Events
public sealed record AccountUpdateEvent : ChannelEvent
{
    public required long AccountId { get; init; }
    public required decimal Collateral { get; init; }
    public required decimal AvailableBalance { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required IReadOnlyList<PositionSnapshot> Positions { get; init; }
}

public sealed record PositionSnapshot
{
    public required int MarketId { get; init; }
    public required decimal Size { get; init; }
    public required decimal AvgEntryPrice { get; init; }
    public required decimal UnrealizedPnl { get; init; }
    public required decimal LiquidationPrice { get; init; }
    public required bool IsCross { get; init; }
}

// Order Events
public sealed record OrderUpdateEvent : ChannelEvent
{
    public required int MarketId { get; init; }
    public required IReadOnlyList<OrderSnapshot> Orders { get; init; }
}

public sealed record OrderSnapshot
{
    public required long OrderIndex { get; init; }
    public required decimal Price { get; init; }
    public required decimal Size { get; init; }
    public required decimal FilledSize { get; init; }
    public required string Status { get; init; }
    public required bool IsBuy { get; init; }
}

// Market Stats Events
public sealed record MarketStatsUpdateEvent : ChannelEvent
{
    public required int MarketId { get; init; }
    public required decimal IndexPrice { get; init; }
    public required decimal MarkPrice { get; init; }
    public required decimal FundingRate { get; init; }
    public required decimal Volume24h { get; init; }
}

// Connection Events
public sealed record ConnectionStateEvent : ChannelEvent
{
    public required ConnectionState State { get; init; }
    public string? Reason { get; init; }
    public int ReconnectAttempt { get; init; }
}

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed
}

// Notification Events
public sealed record NotificationEvent : ChannelEvent
{
    public required string NotificationType { get; init; }
    public required string Message { get; init; }
    public int? MarketId { get; init; }
}
```

### 1.4 Core WebSocket Client Interface

**File:** `GridBot.Lighter/ILighterWebSocketClient.cs`

```csharp
namespace GridBot.Lighter;

/// <summary>
/// WebSocket client for real-time Lighter DEX data streaming.
/// Uses System.Threading.Channels for high-performance event distribution.
/// </summary>
public interface ILighterWebSocketClient : IAsyncDisposable
{
    /// <summary>
    /// Current connection state.
    /// </summary>
    ConnectionState ConnectionState { get; }

    /// <summary>
    /// Whether the client is connected and subscriptions are active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Channel reader for order book updates.
    /// </summary>
    ChannelReader<OrderBookUpdateEvent> OrderBookUpdates { get; }

    /// <summary>
    /// Channel reader for account updates.
    /// </summary>
    ChannelReader<AccountUpdateEvent> AccountUpdates { get; }

    /// <summary>
    /// Channel reader for order updates.
    /// </summary>
    ChannelReader<OrderUpdateEvent> OrderUpdates { get; }

    /// <summary>
    /// Channel reader for market stats updates.
    /// </summary>
    ChannelReader<MarketStatsUpdateEvent> MarketStatsUpdates { get; }

    /// <summary>
    /// Channel reader for connection state changes.
    /// </summary>
    ChannelReader<ConnectionStateEvent> ConnectionStateChanges { get; }

    /// <summary>
    /// Channel reader for notifications (liquidation warnings, etc).
    /// </summary>
    ChannelReader<NotificationEvent> Notifications { get; }

    /// <summary>
    /// Connects to WebSocket and starts receiving messages.
    /// </summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to order book updates for a specific market.
    /// </summary>
    Task SubscribeOrderBookAsync(int marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to account updates (requires auth).
    /// </summary>
    Task SubscribeAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to order updates (requires auth).
    /// </summary>
    Task SubscribeOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to market stats updates.
    /// </summary>
    Task SubscribeMarketStatsAsync(int marketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to notifications (requires auth).
    /// </summary>
    Task SubscribeNotificationsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribes from a channel.
    /// </summary>
    Task UnsubscribeAsync(string channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gracefully disconnects from WebSocket.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
```

### 1.5 WebSocket Client Implementation

**File:** `GridBot.Lighter/LighterWebSocketClient.cs`

Key implementation details:
- Uses `ClientWebSocket` for connection management
- Creates bounded channels with configurable capacity
- Implements ping/pong handling with timeout detection
- Handles reconnection with exponential backoff + jitter
- Refreshes auth tokens before expiry
- Routes messages to appropriate channels based on type
- Thread-safe state management

```csharp
public sealed class LighterWebSocketClient : ILighterWebSocketClient
{
    // Dependencies
    private readonly ISignerClient _signerClient;
    private readonly ILogger<LighterWebSocketClient> _logger;
    private readonly WebSocketOptions _options;
    private readonly LighterOptions _lighterOptions;

    // Connection state
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private Task? _pingTask;
    private volatile ConnectionState _connectionState = ConnectionState.Disconnected;
    private int _reconnectAttempts;
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    // Auth management
    private string? _currentAuthToken;
    private DateTimeOffset _authTokenExpiry;
    private readonly SemaphoreSlim _authLock = new(1, 1);

    // Subscription tracking
    private readonly ConcurrentDictionary<string, bool> _subscriptions = new();

    // Channels - using bounded channels with DropOldest for trading
    private readonly Channel<OrderBookUpdateEvent> _orderBookChannel;
    private readonly Channel<AccountUpdateEvent> _accountChannel;
    private readonly Channel<OrderUpdateEvent> _ordersChannel;
    private readonly Channel<MarketStatsUpdateEvent> _marketStatsChannel;
    private readonly Channel<ConnectionStateEvent> _connectionChannel;
    private readonly Channel<NotificationEvent> _notificationChannel;

    // Channel readers (exposed via interface)
    public ChannelReader<OrderBookUpdateEvent> OrderBookUpdates => _orderBookChannel.Reader;
    public ChannelReader<AccountUpdateEvent> AccountUpdates => _accountChannel.Reader;
    public ChannelReader<OrderUpdateEvent> OrderUpdates => _ordersChannel.Reader;
    public ChannelReader<MarketStatsUpdateEvent> MarketStatsUpdates => _marketStatsChannel.Reader;
    public ChannelReader<ConnectionStateEvent> ConnectionStateChanges => _connectionChannel.Reader;
    public ChannelReader<NotificationEvent> Notifications => _notificationChannel.Reader;

    public ConnectionState ConnectionState => _connectionState;
    public bool IsConnected => _connectionState == ConnectionState.Connected;

    public LighterWebSocketClient(
        ISignerClient signerClient,
        IOptions<WebSocketOptions> options,
        IOptions<LighterOptions> lighterOptions,
        ILogger<LighterWebSocketClient> logger)
    {
        _signerClient = signerClient;
        _options = options.Value;
        _lighterOptions = lighterOptions.Value;
        _logger = logger;

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
    }

    // ... implementation continues (see detailed methods below)
}
```

### 1.6 Key Implementation Methods

**Connection Management:**
```csharp
public async Task ConnectAsync(CancellationToken cancellationToken = default)
{
    await _connectLock.WaitAsync(cancellationToken);
    try
    {
        if (_connectionState == ConnectionState.Connected)
            return;

        await SetConnectionStateAsync(ConnectionState.Connecting);

        _webSocket?.Dispose();
        _webSocket = new ClientWebSocket();

        var wsUrl = GetWebSocketUrl();
        await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);

        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), _receiveCts.Token);
        _pingTask = Task.Run(() => PingLoopAsync(_receiveCts.Token), _receiveCts.Token);

        await SetConnectionStateAsync(ConnectionState.Connected);
        _reconnectAttempts = 0;

        // Resubscribe to all previous subscriptions
        await ResubscribeAllAsync(cancellationToken);
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
```

**Receive Loop with Message Routing:**
```csharp
private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
{
    var buffer = new ArraySegment<byte>(new byte[8192]);
    using var ms = new MemoryStream();

    while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
    {
        try
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;

            do
            {
                result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
                ms.Write(buffer.Array!, buffer.Offset, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await HandleDisconnectAsync("Server closed connection");
                break;
            }

            var messageJson = Encoding.UTF8.GetString(ms.ToArray());
            await RouteMessageAsync(messageJson, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            break;
        }
        catch (WebSocketException ex)
        {
            _logger.LogWarning(ex, "WebSocket error in receive loop");
            await HandleDisconnectAsync(ex.Message);
            break;
        }
    }
}
```

**Message Routing:**
```csharp
private async ValueTask RouteMessageAsync(string json, CancellationToken ct)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "ping":
                await SendPongAsync(ct);
                break;

            case "subscribed/order_book":
            case "update/order_book":
                var orderBookMsg = JsonSerializer.Deserialize<OrderBookMessage>(json, _jsonOptions);
                if (orderBookMsg != null)
                    await WriteToChannelAsync(_orderBookChannel, ParseOrderBookEvent(orderBookMsg), ct);
                break;

            case "subscribed/account_all":
            case "update/account_all":
                var accountMsg = JsonSerializer.Deserialize<AccountAllMessage>(json, _jsonOptions);
                if (accountMsg != null)
                    await WriteToChannelAsync(_accountChannel, ParseAccountEvent(accountMsg), ct);
                break;

            case "subscribed/account_all_orders":
            case "update/account_all_orders":
                var ordersMsg = JsonSerializer.Deserialize<OrdersMessage>(json, _jsonOptions);
                if (ordersMsg != null)
                    await WriteToChannelAsync(_ordersChannel, ParseOrdersEvent(ordersMsg), ct);
                break;

            case "update/market_stats":
                var statsMsg = JsonSerializer.Deserialize<MarketStatsMessage>(json, _jsonOptions);
                if (statsMsg != null)
                    await WriteToChannelAsync(_marketStatsChannel, ParseMarketStatsEvent(statsMsg), ct);
                break;

            case "update/notification":
                var notifMsg = JsonSerializer.Deserialize<NotificationMessage>(json, _jsonOptions);
                if (notifMsg != null)
                    await WriteToChannelAsync(_notificationChannel, ParseNotificationEvent(notifMsg), ct);
                break;

            case "error":
                var errorMsg = JsonSerializer.Deserialize<ErrorMessage>(json, _jsonOptions);
                _logger.LogError("WebSocket error: {Code} - {Message}", errorMsg?.Code, errorMsg?.Message);
                break;

            default:
                _logger.LogDebug("Unknown message type: {Type}", type);
                break;
        }
    }
    catch (JsonException ex)
    {
        _logger.LogWarning(ex, "Failed to parse WebSocket message: {Json}", json[..Math.Min(200, json.Length)]);
    }
}

private static ValueTask WriteToChannelAsync<T>(Channel<T> channel, T item, CancellationToken ct)
{
    // TryWrite is non-blocking; if channel is full, DropOldest kicks in
    if (!channel.Writer.TryWrite(item))
    {
        // Channel is full and couldn't drop - very rare with DropOldest
        return channel.Writer.WriteAsync(item, ct);
    }
    return ValueTask.CompletedTask;
}
```

**Reconnection with Exponential Backoff:**
```csharp
private async Task HandleDisconnectAsync(string reason)
{
    if (_connectionState == ConnectionState.Reconnecting)
        return;

    await SetConnectionStateAsync(ConnectionState.Reconnecting, reason);

    while (_reconnectAttempts < _options.MaxReconnectAttempts)
    {
        _reconnectAttempts++;
        var delay = CalculateBackoff(_reconnectAttempts);

        _logger.LogInformation(
            "Reconnecting in {Delay}ms (attempt {Attempt}/{Max})",
            delay.TotalMilliseconds, _reconnectAttempts, _options.MaxReconnectAttempts);

        await Task.Delay(delay);

        try
        {
            await ConnectAsync();
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
        _options.MaxReconnectDelayMs,
        _options.ReconnectDelayMs * Math.Pow(2, attempt - 1));
    var jitter = Random.Shared.Next(0, 500);
    return TimeSpan.FromMilliseconds(exponentialDelay + jitter);
}
```

---

## Phase 2: Real-time State Service (GridBot.ApiService)

### 2.1 Real-time State Interface

**File:** `GridBot.ApiService/Services/Realtime/ILighterRealtimeState.cs`

```csharp
namespace GridBot.ApiService.Services.Realtime;

/// <summary>
/// Thread-safe snapshot access to real-time WebSocket data.
/// Replaces REST polling for decision engine.
/// </summary>
public interface ILighterRealtimeState
{
    /// <summary>
    /// Gets the latest order book snapshot for a market.
    /// Returns null if no data received yet.
    /// </summary>
    OrderBookSnapshot? GetOrderBook(int marketId);

    /// <summary>
    /// Gets the latest account snapshot.
    /// Returns null if no data received yet.
    /// </summary>
    AccountSnapshot? GetAccount();

    /// <summary>
    /// Gets the latest orders for a market.
    /// Returns empty list if no data received yet.
    /// </summary>
    IReadOnlyList<OrderSnapshot> GetOrders(int marketId);

    /// <summary>
    /// Gets the latest market stats.
    /// Returns null if no data received yet.
    /// </summary>
    MarketStatsSnapshot? GetMarketStats(int marketId);

    /// <summary>
    /// Gets the current price for a market.
    /// Returns null if no data received yet.
    /// </summary>
    decimal? GetCurrentPrice(int marketId);

    /// <summary>
    /// Gets the position size for a market.
    /// Returns null if no data received yet.
    /// </summary>
    decimal? GetPositionSize(int marketId);

    /// <summary>
    /// Whether WebSocket is connected and providing data.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Age of the oldest data snapshot.
    /// Used to determine if we should fall back to REST.
    /// </summary>
    TimeSpan? OldestDataAge { get; }

    /// <summary>
    /// Subscribes to data updates for specific market.
    /// </summary>
    Task SubscribeMarketAsync(int marketId, CancellationToken ct = default);
}

public sealed record AccountSnapshot
{
    public required long AccountId { get; init; }
    public required decimal Collateral { get; init; }
    public required decimal AvailableBalance { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required IReadOnlyDictionary<int, PositionSnapshot> Positions { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }
}

public sealed record MarketStatsSnapshot
{
    public required int MarketId { get; init; }
    public required decimal IndexPrice { get; init; }
    public required decimal MarkPrice { get; init; }
    public required decimal FundingRate { get; init; }
    public required decimal Volume24h { get; init; }
    public required DateTimeOffset LastUpdated { get; init; }
}
```

### 2.2 Real-time State Service Implementation

**File:** `GridBot.ApiService/Services/Realtime/LighterRealtimeStateService.cs`

```csharp
public sealed class LighterRealtimeStateService : BackgroundService, ILighterRealtimeState
{
    private readonly ILighterWebSocketClient _wsClient;
    private readonly ILogger<LighterRealtimeStateService> _logger;

    // Thread-safe state storage using Interlocked
    private readonly ConcurrentDictionary<int, OrderBookSnapshot> _orderBooks = new();
    private readonly ConcurrentDictionary<int, IReadOnlyList<OrderSnapshot>> _orders = new();
    private readonly ConcurrentDictionary<int, MarketStatsSnapshot> _marketStats = new();
    private volatile AccountSnapshot? _account;
    private volatile DateTimeOffset _lastUpdateTime;

    public bool IsConnected => _wsClient.IsConnected;
    public TimeSpan? OldestDataAge => _lastUpdateTime == default
        ? null
        : DateTimeOffset.UtcNow - _lastUpdateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

    private async Task ProcessOrderBookUpdatesAsync(CancellationToken ct)
    {
        await foreach (var update in _wsClient.OrderBookUpdates.ReadAllAsync(ct))
        {
            _orderBooks[update.MarketId] = update.Snapshot;
            _lastUpdateTime = update.Timestamp;
            _logger.LogTrace(
                "Order book update for market {MarketId}: bid={Bid} ask={Ask}",
                update.MarketId, update.Snapshot.BestBidPrice, update.Snapshot.BestAskPrice);
        }
    }

    private async Task ProcessAccountUpdatesAsync(CancellationToken ct)
    {
        await foreach (var update in _wsClient.AccountUpdates.ReadAllAsync(ct))
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
            _lastUpdateTime = update.Timestamp;
        }
    }

    // ... other processors follow same pattern

    public OrderBookSnapshot? GetOrderBook(int marketId) =>
        _orderBooks.TryGetValue(marketId, out var book) ? book : null;

    public AccountSnapshot? GetAccount() => _account;

    public decimal? GetCurrentPrice(int marketId)
    {
        if (_orderBooks.TryGetValue(marketId, out var book))
            return book.MidPrice;
        if (_marketStats.TryGetValue(marketId, out var stats))
            return stats.MarkPrice;
        return null;
    }

    public decimal? GetPositionSize(int marketId)
    {
        var account = _account;
        if (account?.Positions.TryGetValue(marketId, out var pos) == true)
            return pos.Size;
        return null;
    }
}
```

---

## Phase 3: Integration with Decision Engine

### 3.1 Hybrid Data Provider

**File:** `GridBot.ApiService/Services/MarketData/HybridMarketDataService.cs`

Creates a service that:
1. Tries WebSocket data first (via `ILighterRealtimeState`)
2. Falls back to REST if WebSocket data is stale or unavailable
3. Transparent to consumers - same interface as before

```csharp
public sealed class HybridMarketDataService : IMarketDataService
{
    private readonly ILighterRealtimeState _realtimeState;
    private readonly ILighterQueryClient _restClient;
    private readonly ILogger<HybridMarketDataService> _logger;

    private static readonly TimeSpan StaleThreshold = TimeSpan.FromSeconds(30);

    public async Task<decimal> GetCurrentPriceAsync(int marketId, CancellationToken ct = default)
    {
        // Try WebSocket first
        var wsPrice = _realtimeState.GetCurrentPrice(marketId);
        if (wsPrice.HasValue && _realtimeState.IsConnected)
        {
            return wsPrice.Value;
        }

        // Fall back to REST
        _logger.LogDebug("Falling back to REST for price (WS: {Connected})", _realtimeState.IsConnected);
        var orderBook = await _restClient.GetOrderBookOrdersAsync(marketId, limit: 1, ct);
        // ... extract price from REST response
    }

    public async Task<OrderBookSnapshot> GetOrderBookSnapshotAsync(int marketId, int depth, CancellationToken ct)
    {
        // Try WebSocket first
        var wsBook = _realtimeState.GetOrderBook(marketId);
        if (wsBook != null && _realtimeState.IsConnected && _realtimeState.OldestDataAge < StaleThreshold)
        {
            return wsBook;
        }

        // Fall back to REST
        _logger.LogDebug("Falling back to REST for order book (WS: {Connected}, Age: {Age})",
            _realtimeState.IsConnected, _realtimeState.OldestDataAge);
        return await FetchOrderBookFromRestAsync(marketId, depth, ct);
    }

    // Candlesticks always use REST (historical data)
    public Task<List<CandlestickData>> GetCandlesticksAsync(
        int marketId, string resolution, int count, CancellationToken ct)
    {
        return FetchCandlesticksFromRestAsync(marketId, resolution, count, ct);
    }
}
```

### 3.2 Update Decision Engine

Modify `TradingDecisionEngine.CollectMarketDataAsync` to use `HybridMarketDataService`:

```csharp
// Before (REST polling):
currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, timeoutCts.Token);

// After (WebSocket with REST fallback):
// No changes needed - HybridMarketDataService handles it transparently
// Just need to inject the hybrid service instead
```

---

## Phase 4: Service Registration

### 4.1 Update LighterServiceCollectionExtensions

**File:** `GridBot.Lighter/LighterServiceCollectionExtensions.cs`

```csharp
public static IServiceCollection AddLighterWebSocket(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddOptions<WebSocketOptions>()
        .Bind(configuration.GetSection(WebSocketOptions.SectionName));

    // Register WebSocket client as singleton
    services.AddSingleton<ILighterWebSocketClient, LighterWebSocketClient>();

    return services;
}
```

### 4.2 Update ApiService Registration

**File:** `GridBot.ApiService/Program.cs`

```csharp
// Add WebSocket client
builder.Services.AddLighterWebSocket(builder.Configuration);

// Add real-time state service (hosted service)
builder.Services.AddSingleton<ILighterRealtimeState, LighterRealtimeStateService>();
builder.Services.AddHostedService(sp => (LighterRealtimeStateService)sp.GetRequiredService<ILighterRealtimeState>());

// Register hybrid market data service
builder.Services.AddSingleton<IMarketDataService, HybridMarketDataService>();
```

---

## Phase 5: Testing Strategy

### 5.1 Unit Tests

- **Channel behavior tests**: Verify bounded channel drops oldest when full
- **Message parsing tests**: Each WebSocket message type
- **Reconnection tests**: Verify exponential backoff calculation
- **Auth token refresh tests**: Verify refresh before expiry

### 5.2 Integration Tests

- **Connection lifecycle**: Connect, subscribe, receive, disconnect
- **Fallback behavior**: Verify REST fallback when WebSocket disconnected
- **State consistency**: Verify state updates are atomic

---

## Implementation Order

1. **Phase 1.1-1.2**: Options and message models (2 files)
2. **Phase 1.3**: Channel event types (1 file)
3. **Phase 1.4-1.6**: WebSocket client interface and implementation (2 files)
4. **Phase 2.1-2.2**: Real-time state service (2 files)
5. **Phase 3.1**: Hybrid market data service (1 file)
6. **Phase 4**: Service registration updates (2 files)
7. **Phase 5**: Testing

---

## Configuration Example

**appsettings.json:**
```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "PrivateKey": "...",
    "ChainId": 304,
    "AccountIndex": 123
  },
  "LighterWebSocket": {
    "ReconnectDelayMs": 1000,
    "MaxReconnectDelayMs": 60000,
    "AuthTokenRefreshSeconds": 480,
    "ChannelCapacity": 100,
    "MaxReconnectAttempts": 100,
    "PingTimeoutSeconds": 30
  }
}
```

---

## Files to Create

| # | File | Project | Description |
|---|------|---------|-------------|
| 1 | `WebSocketOptions.cs` | GridBot.Lighter | Configuration options |
| 2 | `Models/WebSocket/WebSocketMessages.cs` | GridBot.Lighter | Server message models |
| 3 | `Models/WebSocket/ChannelEvents.cs` | GridBot.Lighter | Channel event types |
| 4 | `ILighterWebSocketClient.cs` | GridBot.Lighter | Interface |
| 5 | `LighterWebSocketClient.cs` | GridBot.Lighter | Implementation |
| 6 | `Services/Realtime/ILighterRealtimeState.cs` | GridBot.ApiService | State interface |
| 7 | `Services/Realtime/LighterRealtimeStateService.cs` | GridBot.ApiService | State processor |
| 8 | `Services/MarketData/HybridMarketDataService.cs` | GridBot.ApiService | Hybrid data provider |

---

## Files to Modify

| # | File | Changes |
|---|------|---------|
| 1 | `LighterServiceCollectionExtensions.cs` | Add `AddLighterWebSocket` method |
| 2 | `Program.cs` (ApiService) | Register WebSocket services |
| 3 | `ISignerClient.cs` | Expose `CreateAuthTokenAsync` (if not already) |

---

## Risk Mitigation

1. **WebSocket unavailable**: Hybrid service falls back to REST automatically
2. **Rate limit exhaustion**: WebSocket eliminates most REST calls
3. **Stale data**: Age tracking prevents using old data for decisions
4. **Memory leak**: Bounded channels with DropOldest prevent unbounded growth
5. **Connection instability**: Exponential backoff with jitter prevents thundering herd

---

## Expected Outcomes

| Metric | Before | After |
|--------|--------|-------|
| REST weight/min | ~12,000 | ~300 (candlesticks only) |
| Data latency | 5 seconds (poll interval) | <50ms (push) |
| API 429 errors | Common | Rare |
| Rate limit headroom | 50% | 99%+ |
