# Lighter DEX WebSocket API Research

**Date:** 2025-12-07
**Status:** Research Complete
**Purpose:** Replace REST polling with WebSocket streaming for real-time data

---

## Executive Summary

Lighter DEX provides a comprehensive WebSocket API for real-time streaming data. The API supports both public channels (order book, trades, market stats) and private channels (account updates, orders, positions) that require authentication. The existing `SignerClient.CreateAuthTokenAsync()` method can generate the required auth tokens.

---

## 1. WebSocket Connection URLs

| Environment | URL |
|-------------|-----|
| **Mainnet** | `wss://mainnet.zklighter.elliot.ai/stream` |
| **Testnet** | `wss://testnet.zklighter.elliot.ai/stream` |

### Connection Example (wscat)
```bash
wscat -c 'wss://mainnet.zklighter.elliot.ai/stream'
```

---

## 2. Subscription Message Format

All subscriptions follow a consistent JSON structure:

### Public Channel Subscription
```json
{
  "type": "subscribe",
  "channel": "order_book/0"
}
```

### Private Channel Subscription (Requires Auth Token)
```json
{
  "type": "subscribe",
  "channel": "account_all/123",
  "auth": "YOUR_AUTH_TOKEN_HERE"
}
```

### Unsubscribe
```json
{
  "type": "unsubscribe",
  "channel": "order_book/0"
}
```

---

## 3. Available Channels

### 3.1 Public Channels (No Authentication Required)

| Channel | Format | Description |
|---------|--------|-------------|
| **Order Book** | `order_book/{MARKET_INDEX}` | Real-time bid/ask updates for a specific market |
| **Market Stats** | `market_stats/{MARKET_INDEX}` or `market_stats/all` | Index price, mark price, open interest, funding rates, volumes |
| **Trade** | `trade/{MARKET_INDEX}` | Live trade executions |
| **Height** | `height` | Blockchain height updates |

### 3.2 Private Channels (Require Authentication)

| Channel | Format | Description |
|---------|--------|-------------|
| **Account All** | `account_all/{ACCOUNT_ID}` | Comprehensive account data across all markets |
| **Account Market** | `account_market/{MARKET_ID}/{ACCOUNT_ID}` | Single market orders, position, trades |
| **User Stats** | `user_stats/{ACCOUNT_ID}` | Collateral, portfolio value, leverage, margins, buying power |
| **Account Tx** | `account_tx/{ACCOUNT_ID}` | Transaction history |
| **Account All Orders** | `account_all_orders/{ACCOUNT_ID}` | Orders across all markets |
| **Account Orders** | `account_orders/{MARKET_INDEX}/{ACCOUNT_ID}` | Orders for specific market |
| **Account All Trades** | `account_all_trades/{ACCOUNT_ID}` | Complete trading history |
| **Account All Positions** | `account_all_positions/{ACCOUNT_ID}` | All positions and pool shares |
| **Pool Data** | `pool_data/{ACCOUNT_ID}` | Pool activity (trades, orders, positions) |
| **Pool Info** | `pool_info/{ACCOUNT_ID}` | Pool status, fees, APY, share pricing |
| **Notification** | `notification/{ACCOUNT_ID}` | Liquidations, deleverage alerts, announcements |

---

## 4. Message Formats by Channel

### 4.1 Order Book Channel

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "order_book/0"
}
```

**Initial Snapshot Response:**
```json
{
  "type": "subscribed/order_book",
  "channel": "order_book:0",
  "offset": 41692864,
  "order_book": {
    "code": 0,
    "asks": [
      { "price": "3327.46", "size": "29.0915" },
      { "price": "3328.00", "size": "15.5000" }
    ],
    "bids": [
      { "price": "3326.80", "size": "10.2898" },
      { "price": "3326.00", "size": "25.0000" }
    ],
    "offset": 41692864,
    "nonce": 12345678,
    "timestamp": 1701936000000
  }
}
```

**Update Message:**
```json
{
  "type": "update/order_book",
  "channel": "order_book:0",
  "offset": 41692865,
  "order_book": {
    "code": 0,
    "asks": [
      { "price": "3327.50", "size": "30.0000" }
    ],
    "bids": [
      { "price": "3326.90", "size": "12.0000" }
    ],
    "offset": 41692865
  }
}
```

### 4.2 Market Stats Channel

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "market_stats/0"
}
```

**Response:**
```json
{
  "type": "update/market_stats",
  "channel": "market_stats:0",
  "market_stats": {
    "market_index": 0,
    "index_price": "3327.00",
    "mark_price": "3327.15",
    "open_interest": "1500000.00",
    "funding_rate": "0.0001",
    "next_funding_time": 1701936000,
    "volume_24h": "50000000.00",
    "high_24h": "3400.00",
    "low_24h": "3200.00"
  }
}
```

### 4.3 Trade Channel

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "trade/0"
}
```

**Response:**
```json
{
  "type": "update/trade",
  "channel": "trade:0",
  "trade": {
    "trade_id": 123456789,
    "market_index": 0,
    "price": "3327.00",
    "size": "1.5000",
    "side": "buy",
    "maker_account_id": 100,
    "taker_account_id": 200,
    "maker_fee": "0.003",
    "taker_fee": "0.030",
    "timestamp": 1701936000000
  }
}
```

### 4.4 Account All Channel (Private)

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "account_all/123",
  "auth": "AUTH_TOKEN_HERE"
}
```

**Response:**
```json
{
  "type": "subscribed/account_all",
  "channel": "account_all:123",
  "account": {
    "account_id": 123,
    "positions": [
      {
        "market_id": 0,
        "position": "5.0000",
        "avg_entry_price": "3300.00",
        "liquidation_price": "2800.00",
        "unrealized_pnl": "135.00",
        "margin_mode": "cross"
      }
    ],
    "trades": [...],
    "funding_histories": [...],
    "pool_shares": [...]
  }
}
```

### 4.5 User Stats Channel (Private)

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "user_stats/123",
  "auth": "AUTH_TOKEN_HERE"
}
```

**Response:**
```json
{
  "type": "update/user_stats",
  "channel": "user_stats:123",
  "user_stats": {
    "collateral": "10000.00",
    "portfolio_value": "10135.00",
    "leverage": "1.52",
    "available_balance": "5000.00",
    "margin_usage": "0.45",
    "buying_power": "25000.00",
    "cross_stats": {
      "margin": "5000.00",
      "unrealized_pnl": "135.00"
    },
    "total_stats": {
      "total_margin": "5000.00",
      "total_unrealized_pnl": "135.00"
    }
  }
}
```

### 4.6 Account All Orders Channel (Private)

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "account_all_orders/123",
  "auth": "AUTH_TOKEN_HERE"
}
```

**Response:**
```json
{
  "type": "update/account_all_orders",
  "channel": "account_all_orders:123",
  "orders": {
    "0": [
      {
        "order_index": 789,
        "market_index": 0,
        "price": "3320.00",
        "size": "1.0000",
        "filled_size": "0.0000",
        "status": "open",
        "time_in_force": "GTC",
        "side": "buy",
        "trigger_price": null,
        "created_at": 1701936000000
      }
    ]
  }
}
```

### 4.7 Notification Channel (Private)

**Subscription:**
```json
{
  "type": "subscribe",
  "channel": "notification/123",
  "auth": "AUTH_TOKEN_HERE"
}
```

**Response:**
```json
{
  "type": "update/notification",
  "channel": "notification:123",
  "notification": {
    "type": "liquidation_warning",
    "message": "Position approaching liquidation",
    "market_index": 0,
    "timestamp": 1701936000000
  }
}
```

---

## 5. Authentication Requirements

### 5.1 Generating Auth Tokens

Auth tokens are generated using the `SignerClient` which already exists in the codebase:

```csharp
// C# - Using existing SignerClient
var (authToken, error) = await _signerClient.CreateAuthTokenAsync(validitySeconds: 600);
if (error != null)
{
    throw new Exception($"Failed to create auth token: {error}");
}
```

**Implementation Details (from SignerClient.cs:333-344):**
- Default validity: 600 seconds (10 minutes)
- Token is signed using the account's API private key
- Uses `NativeMethods.CreateAuthToken(deadline, ApiKeyIndex, AccountIndex)`

### 5.2 Token Expiration

- Default expiry: 10 minutes
- Recommended: Generate new token before expiry
- Strategy: Refresh token every 8 minutes (80% of validity)

### 5.3 Using Auth Token in Subscription

```json
{
  "type": "subscribe",
  "channel": "account_all/123",
  "auth": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
}
```

---

## 6. Heartbeat/Ping-Pong Requirements

### 6.1 Server-Initiated Ping

The server sends periodic ping messages:
```json
{
  "type": "ping"
}
```

### 6.2 Client Pong Response

Client must respond with pong to keep connection alive:
```json
{
  "type": "pong"
}
```

### 6.3 Recommended Handling

```csharp
private async Task HandleMessage(string message)
{
    var json = JsonDocument.Parse(message);
    var type = json.RootElement.GetProperty("type").GetString();

    if (type == "ping")
    {
        await SendAsync("{\"type\": \"pong\"}");
        return;
    }

    // Handle other message types...
}
```

---

## 7. Rate Limits for WebSocket

| Limit Type | Value |
|-----------|-------|
| Maximum connections per IP | 100 |
| Subscriptions per connection | 100 |
| Total subscriptions per IP | 1,000 |
| Messages per minute | 200 |
| Inflight messages maximum | 50 |
| Unique accounts maximum | 10 |

**Note:** Exceeding message limits may result in disconnection.

---

## 8. Transaction Operations via WebSocket

WebSocket can also be used to submit transactions (alternative to REST):

### 8.1 Send Single Transaction

```json
{
  "type": "jsonapi/sendtx",
  "data": {
    "tx_type": 3,
    "tx_info": "SIGNED_TX_INFO_FROM_SIGNER"
  }
}
```

### 8.2 Send Batch Transactions (up to 50)

```json
{
  "type": "jsonapi/sendtxbatch",
  "data": {
    "tx_types": "[3, 3, 4]",
    "tx_infos": "[TX_INFO_1, TX_INFO_2, TX_INFO_3]"
  }
}
```

**Transaction Types (from SignerClient):**
- 3 = Create Order
- 4 = Cancel Order
- 5 = Modify Order
- etc.

---

## 9. Reconnection Best Practices

### 9.1 Recommended Strategy

1. **Exponential Backoff:**
   - Initial delay: 1 second
   - Max delay: 60 seconds
   - Formula: `min(60, 2^retryCount)` seconds

2. **Jitter:**
   - Add random 0-500ms to avoid thundering herd

3. **State Recovery:**
   - Re-subscribe to all channels after reconnect
   - Refresh auth token if expired
   - Request full snapshot on reconnect

### 9.2 Implementation Pattern

```csharp
public class LighterWebSocketClient : IAsyncDisposable
{
    private readonly List<string> _subscriptions = new();
    private int _retryCount = 0;
    private const int MaxRetries = 10;

    public async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _retryCount < MaxRetries)
        {
            try
            {
                await ConnectAsync(ct);
                _retryCount = 0; // Reset on successful connection
                await ProcessMessagesAsync(ct);
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket disconnected, reconnecting...");
                var delay = CalculateBackoff(_retryCount++);
                await Task.Delay(delay, ct);
            }
        }
    }

    private TimeSpan CalculateBackoff(int retryCount)
    {
        var seconds = Math.Min(60, Math.Pow(2, retryCount));
        var jitter = Random.Shared.Next(0, 500);
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitter);
    }

    private async Task OnConnectedAsync()
    {
        // Refresh auth token
        var (authToken, error) = await _signerClient.CreateAuthTokenAsync();

        // Re-subscribe to all channels
        foreach (var channel in _subscriptions)
        {
            await SubscribeAsync(channel, authToken);
        }
    }
}
```

---

## 10. Comparison: REST Polling vs WebSocket

| Aspect | REST Polling | WebSocket |
|--------|-------------|-----------|
| **Latency** | 200-500ms per request | Near real-time (~10ms) |
| **Rate Limit Impact** | High (300 weight per call) | Low (separate limits) |
| **Data Freshness** | Only as fresh as poll interval | Instant updates |
| **Connection Overhead** | New TCP connection per request | Single persistent connection |
| **Server Load** | Higher | Lower |
| **Bandwidth** | Higher (full responses) | Lower (incremental updates) |

### Recommended Migration Strategy

1. **Replace order book polling** with `order_book/{MARKET_INDEX}` subscription
2. **Replace account polling** with `account_all/{ACCOUNT_ID}` subscription
3. **Replace order status polling** with `account_all_orders/{ACCOUNT_ID}` subscription
4. **Keep REST** for:
   - Transaction submission (unless using WebSocket sendtx)
   - Initial state loading
   - Fallback when WebSocket disconnects

---

## 11. Implementation Checklist for GridBot

### Required Components

- [ ] `ILighterWebSocketClient` interface
- [ ] `LighterWebSocketClient` implementation
- [ ] Auth token refresh mechanism
- [ ] Ping/pong handler
- [ ] Reconnection logic with exponential backoff
- [ ] Message deserialization for each channel type
- [ ] Event handlers for update propagation

### Suggested Channel Subscriptions for Trading Bot

1. **Essential:**
   - `order_book/{MARKET_INDEX}` - For grid placement decisions
   - `account_all/{ACCOUNT_ID}` - For position/balance monitoring
   - `account_all_orders/{ACCOUNT_ID}` - For order state tracking

2. **Recommended:**
   - `market_stats/{MARKET_INDEX}` - For volatility/ATR calculations
   - `notification/{ACCOUNT_ID}` - For liquidation warnings

3. **Optional:**
   - `trade/{MARKET_INDEX}` - For trade flow analysis

---

## 12. References

- [WebSocket Reference Documentation](https://apidocs.lighter.xyz/docs/websocket-reference)
- [Rate Limits Documentation](https://apidocs.lighter.xyz/docs/rate-limits)
- [Python SDK WebSocket Example](https://github.com/elliottech/lighter-python)
- [Get Started for Programmers](https://apidocs.lighter.xyz/docs/get-started-for-programmers-1)

---

## 13. Open Questions / Uncertainties

1. **Heartbeat Interval:** Server ping frequency not explicitly documented. Recommend implementing pong response handler but monitoring for timeout patterns.

2. **Order Book Deltas vs Full:** Documentation unclear if updates are deltas or full replacements. Python SDK processes them as full order book state per update.

3. **Reconnection Grace Period:** Not documented how long subscriptions remain "reserved" after disconnect.

4. **Auth Token in URL vs Message:** Documentation shows auth in message body, but some systems support auth in query string. Recommend using message body approach as documented.
