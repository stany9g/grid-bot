# Session 1: WebSocket-First Architecture & Order Book Waiting

## Status: ACTIVE

## Recent Change: Order Book Data Waiting Mechanism (2025-12-11)

### Problem
`WsLighterQueryClient.GetOrderBookOrdersAsync` was throwing `LighterApiException` immediately when WebSocket order book data wasn't available yet (e.g., just after connection, before first update arrived).

Error: `Order book data not available for market 1 - not subscribed or no data received yet`

### Solution
Added a waiting mechanism that polls for order book data availability before returning, instead of failing immediately.

### Changes Made

| File | Change |
|------|--------|
| `ILighterRealtimeState.cs` | Added `IsOrderBookReady(int marketId)` and `WaitForOrderBookAsync(int marketId, TimeSpan?, CancellationToken)` methods |
| `LighterRealtimeStateService.cs` | Implemented the two new methods with polling and timeout logic |
| `WsLighterQueryClient.cs` | Updated `GetOrderBookOrdersAsync` to wait up to 10s for order book data if not ready |

### New API

```csharp
// Check if order book data is available
bool IsOrderBookReady(int marketId);

// Wait for order book data (default 30s timeout, uses 10s in query client)
Task WaitForOrderBookAsync(int marketId, TimeSpan? timeout = null, CancellationToken ct = default);
```

### Behavior
1. `GetOrderBookOrdersAsync` checks if order book is ready via `IsOrderBookReady(marketId)`
2. If not ready, waits up to 10 seconds for data via `WaitForOrderBookAsync`
3. If still no data after timeout, throws `TimeoutException` (clearer than before)
4. If data arrives, returns the order book as normal

---

## Architecture Summary

```
┌─────────────────────────────────────────────────────────────┐
│                     GridBot.Lighter                          │
├─────────────────────────────────────────────────────────────┤
│  LighterWebSocketClient (existing)                           │
│  └── Channels: OrderBook, Account, Orders, MarketStats       │
│                           ▼                                  │
│  LighterRealtimeStateService (BackgroundService)             │
│  ├── ConcurrentDictionary<marketId, snapshot>                │
│  ├── Implements ILighterRealtimeState                        │
│  ├── Started automatically as HostedService                  │
│  ├── IsOrderBookReady() - checks if order book data exists   │
│  ├── WaitForOrderBookAsync() - polls until data arrives      │
│  └── NO auto-subscribe - waits for SubscribeMarketAsync      │
│                           ▼                                  │
│  WsLighterQueryClient (ILighterQueryClient)                  │
│  ├── Reads from ILighterRealtimeState for real-time data     │
│  ├── GetOrderBookOrdersAsync - waits for data if not ready   │
│  ├── Uses HTTP for GetOrderBooksAsync (market list)          │
│  └── Throws NotSupportedException for historical data        │
└─────────────────────────────────────────────────────────────┘
```

## Startup Flow

```
Program.cs startup:
1. Services configured (AddLighterClient)
2. LighterRealtimeStateService starts as HostedService
   └── Connects WebSocket
   └── Subscribes to account/orders/notifications
   └── Does NOT subscribe to market data yet
3. MarketResolver.InitializeAsync()
   └── Calls REST GetOrderBooksAsync to get market list
   └── Resolves Symbol (e.g., "BTC") → MarketId (e.g., 0)
4. realtimeState.SubscribeMarketAsync(marketId)
   └── Subscribes to order book & market stats for the market
5. App starts running
   └── First GetOrderBookOrdersAsync call waits for WS data
```

## Configuration

Market is discovered from Symbol at startup (no DefaultMarketId):
```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "AccountIndex": 123,
    "ApiKeyIndex": 0,
    "PrivateKey": "...",
    "ChainId": 304,
    "InitialNonce": 0
  },
  "TradingBot": {
    "Symbol": "BTC"
  }
}
```
