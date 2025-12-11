# Session 1: WebSocket-First Architecture & Order Book Waiting

## Status: ACTIVE

## COMPLETED: user_stats WebSocket Channel for Perps Balance (2025-12-11)

### Problem
The `account_all` WebSocket channel does NOT include perps collateral/available_balance at root level. Current implementation incorrectly calculated collateral from `assets.3.balance` (USDC spot balance), but this is NOT the perps trading balance.

UI shows:
- USDC/Spot: $7.50 (matches `assets.3.balance`)
- USDC/Perps: $4,955.30 (THIS is needed for trading but NOT in `account_all`)

### Root Cause
Lighter DEX has a **separate `user_stats` channel** that provides perps trading balance:

```json
{
  "channel": "user_stats:{ACCOUNT_ID}",
  "stats": {
    "collateral": "4955.30",
    "portfolio_value": "5123.45",
    "available_balance": "3500.00",
    "buying_power": "8750.00",
    "leverage": "2.50",
    "margin_usage": "0.35",
    "cross_stats": {...},
    "total_stats": {...}
  },
  "type": "update/user_stats"
}
```

### Key Insight: Spot vs Perps Balance
Two separate balance pools on Lighter:
1. **Spot Balance** (`assets.3.balance` in `account_all`) - USDC in spot wallet
2. **Perps Collateral** (`stats.collateral` in `user_stats`) - USDC deposited for perpetual futures trading

### Implementation COMPLETED

| File | Change |
|------|--------|
| `Models/WebSocket/ChannelEvents.cs` | Added `UserStatsUpdateEvent` with Collateral, PortfolioValue, AvailableBalance, BuyingPower, Leverage, MarginUsage |
| `ILighterWebSocketClient.cs` | Added `ChannelReader<UserStatsUpdateEvent> UserStatsUpdates` and `SubscribeUserStatsAsync()` |
| `LighterWebSocketClient.cs` | Added `_userStatsChannel`, subscription method, message routing, and `HandleUserStatsMessageAsync()` |
| `LighterRealtimeStateService.cs` | Added `_userStats` field, subscribes to user_stats in `InitializeAsync()`, added `ProcessUserStatsUpdatesAsync()`, updated `ProcessAccountUpdatesAsync()` to use user_stats values for Collateral/AvailableBalance |

### How It Works Now
1. `LighterRealtimeStateService.InitializeAsync()` subscribes to `user_stats/{ACCOUNT_ID}`
2. `ProcessUserStatsUpdatesAsync()` stores the latest `UserStatsUpdateEvent` in `_userStats`
3. `ProcessAccountUpdatesAsync()` uses `_userStats.Collateral` and `_userStats.AvailableBalance` when creating `AccountSnapshot`
4. The `AccountSnapshot` returned by `GetAccount()` now contains the correct perps collateral ($4,955.30) instead of spot USDC ($7.50)

### Reference Docs
See full documentation: `.claude/doc/lighter-websocket-user-stats-channel.md`

---

## Recent Change: DryRun Mode (2025-12-11)

### Problem
Need to test WebSocket data feeds on mainnet without creating real orders.

### Solution
Added `DryRun` configuration option that disables all write operations while preserving read operations.

### Changes Made

| File | Change |
|------|--------|
| `LighterOptions.cs` | Added `DryRun` property (default: false) |
| `DryRunCommandClient.cs` | New decorator that logs commands instead of executing |
| `LighterServiceCollectionExtensions.cs` | Conditionally wraps command client with decorator |

### Usage

```json
{
  "Lighter": {
    "DryRun": true
  }
}
```

When enabled:
- All order create/modify/cancel operations are logged but NOT executed
- WebSocket data feeds (order book, account, etc.) work normally
- Auth token creation and nonce sync still work (read operations)
- Log messages prefixed with `[DRY RUN]`
- Startup shows warning: "DRY RUN MODE ENABLED"

---

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
│  LighterWebSocketClient                                      │
│  └── Channels: OrderBook, Account, Orders, MarketStats,      │
│                UserStats, Notifications, Connection          │
│                           ▼                                  │
│  LighterRealtimeStateService (BackgroundService)             │
│  ├── ConcurrentDictionary<marketId, snapshot>                │
│  ├── _userStats: UserStatsUpdateEvent (perps balance)        │
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
   └── Subscribes to account/orders/notifications/user_stats
   └── Does NOT subscribe to market data yet
3. MarketResolver.InitializeAsync()
   └── Calls REST GetOrderBooksAsync to get market list
   └── Resolves Symbol (e.g., "BTC") → MarketId (e.g., 0)
4. realtimeState.SubscribeMarketAsync(marketId)
   └── Subscribes to order book & market stats for the market
5. App starts running
   └── First GetOrderBookOrdersAsync call waits for WS data
   └── Account balance shows perps collateral (from user_stats)
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
