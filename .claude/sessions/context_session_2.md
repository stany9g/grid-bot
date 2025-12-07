# Session 2: Degraded_Bootstrap State Analysis

## Problem Reported
Grid bot creates 12 orders but immediately enters `Degraded_Bootstrap` state with capacity dropping from 50% to 25%, and subsequent decision cycles show `Orders +0/-0`.

## Log Evidence
```
[10:35:00 WRN] PRECISION LOSS: Amount 0.0001683647406772203615074483 has 18.7897% precision loss for market 1
[10:35:00 WRN] MIN ORDER SIZE BUMP for market 1: 17 -> 20 (minBaseAmount=0.00020, scaled=20)
[LighterCommandClient] POST sendTx: ... "IsAsk":1 ... (ALL 12 orders are SELL orders)
[10:35:07 INF] Grid initialized for market 1: 12 orders, spacing=0.42%, width=5.00%
[10:35:10 INF] Decision cycle complete for market 1. State: Degraded_Bootstrap, Capacity: 50%
[10:35:25 INF] Decision cycle complete for market 1. State: Degraded_Bootstrap, Capacity: 25%
```

## Root Cause Analysis

### Issue 1: Grid Places SELL Orders With No Position (PRIMARY)

**The Problem:**
- The bot has NO POSITION (position = 0)
- Grid initializes and places 12 SELL orders (`IsAsk":1`)
- You cannot sell what you don't have!

**Code Flow:**
1. `TradingDecisionEngine.ExecuteDecisionCycleAsync` collects data
2. Position is fetched: `account.Positions?.FirstOrDefault(p => p.MarketId == marketId)` → null or 0
3. `InventoryManager.AnalyzeInventoryAsync` sets `isBootstrapMode = positionSize == 0` → TRUE
4. `OperationalCapacityService.GetRecommendedState` returns `Degraded_Bootstrap` when `isBootstrapMode = true`
5. Grid creates sell orders above current price, but can't be filled without position

**The Chicken-and-Egg Problem:**
```
No position → Bootstrap mode → Grid places SELL orders →
Can't fill (nothing to sell) → No position → Bootstrap mode → Loop forever
```

**Location:**
- InventoryManager.cs:80 - `var isBootstrapMode = positionSize == 0;`
- OperationalCapacityService.cs:147-150 - Returns `Degraded_Bootstrap` when `isBootstrapMode = true`
- GridLifecycleService.cs - No bootstrap-aware grid logic

### Issue 2: Order Size Too Small

**The Problem:**
- Each grid level is allocated ~0.000168 BTC (~$15 at $90k)
- Minimum order size is 0.0002 BTC (~$18)
- Orders get bumped to minimum, causing 18.79% precision loss

**Impact:**
- Capital allocation per level is below exchange minimums
- All orders end up at minimum size regardless of calculated amount
- This suggests grid is spread too thin or capital too low

### Issue 3: Capacity Degradation (50% → 25%)

**The Problem:**
- `Degraded_Bootstrap` caps capacity at 50% (OperationalCapacityService.cs:51)
- 429 rate limit errors trigger API error reduction: -25%
- Final capacity: 50% - 25% = 25%

**Code Flow:**
- OperationalCapacityService.cs:78-79: `if (hasApiErrors) capacity -= 25;`

## Why Grid Only Creates SELL Orders?

Looking at the order prices in the log:
- 898360 → 902073 → 905785 → 909497 → 913209
- All prices are ABOVE current price (ascending pattern)
- All have `IsAsk":1` (sell orders)

The grid is creating orders above current price only, which are all sell orders. This suggests:
1. The grid is designed to sell into strength (normal grid behavior)
2. BUT it doesn't consider that you need a POSITION to sell

## Required Fixes

### Fix 1: Bootstrap-Aware Grid Logic
The grid should NOT place sell orders when position = 0:

```csharp
// In InitializeGridAsync, after calculating levels:
if (isBootstrapMode)
{
    levels = levels.Where(l => l.IsBid).ToList(); // Keep only bids (buys)
    _logger.LogInformation("Bootstrap mode: Creating BUY-only grid to build position");
}
```

### Fix 2: Skew-Based Grid Asymmetry
Consider position/collateral ratio when creating grid:
- More collateral than position → More BUY orders
- More position than collateral → More SELL orders
- Balanced → Equal buy/sell

### Fix 3: Capital Allocation Review
Ensure order sizes meet minimum requirements:
- Calculate total capital available
- Divide by number of grid levels
- Ensure each level > minimum order size
- Reduce grid levels if needed

## Technical Details

**Files Involved:**
- `GridBot.ApiService\Services\Grid\GridLifecycleService.cs` - Grid initialization logic
- `GridBot.ApiService\Services\Inventory\InventoryManager.cs` - Bootstrap detection
- `GridBot.ApiService\Services\Capacity\OperationalCapacityService.cs` - State/capacity calculation
- `GridBot.ApiService\Services\MarketData\MarketScalingService.cs` - Order size scaling

**Key Variables:**
- `positionSize` = 0 (from account.Positions[marketId].Positionn)
- `isBootstrapMode` = true (no position)
- Capacity: 50% → 25% (bootstrap cap + API error reduction)
- All 12 orders are ASK (sell) orders with `IsAsk":1`

## Session Status
**FIXED** - 2025-12-07

### Root Cause (Revised)
The `isBootstrapMode = positionSize == 0` logic was designed for **spot trading** where you need inventory to sell.

For **perpetual futures**, position = 0 (flat) is normal operation - you can freely open long or short positions without existing inventory.

### Fix Applied
**File:** `GridBot.ApiService\Services\Inventory\InventoryManager.cs`

**Change:** Line 79-82
```csharp
// Before:
var isBootstrapMode = positionSize == 0;

// After:
// For perpetual futures, position = 0 (flat) is normal operation, not a bootstrap condition.
// Unlike spot trading where you need inventory to sell, perpetuals allow opening
// long or short positions freely. Bootstrap mode is not applicable.
var isBootstrapMode = false;
```

### Result
- Bot will no longer enter `Degraded_Bootstrap` state when position = 0
- State will be `Active` or other appropriate state based on actual conditions
- Capacity will not be artificially capped at 50% just for being flat

---

## Lighter Rate Limiting Research (Added 2025-12-07)

### Key Findings

**How limits are counted:**
- Rate limits apply to BOTH IP address AND L1 wallet address
- Sub-accounts SHARE the main account's limit (same L1 wallet)
- API keys share the wallet's limit (not counted separately)

**Rate Limit Thresholds:**
- Premium: 24,000 weighted requests per rolling 60 seconds
- Standard: 60 weighted requests per rolling 60 seconds (400x less!)
- WebSocket: 200 messages/minute, 100 connections max

**Key Endpoint Weights:**
- `sendTx`/`sendTxBatch`: 6 weight (efficient!)
- `nextNonce`: 6 weight
- `recentTrades`: 1,200 weight (expensive!)
- Most other endpoints: 300 weight

**429 Behavior:**
- HTTP 429 returned when limits exceeded
- WebSocket may disconnect for excessive messages
- Retry-After header not explicitly documented

**Current Implementation Gap:**
The existing LighterCommandClient and LighterQueryClient have NO rate limit handling or 429 retry logic.

**Full research:** See `.claude/doc/lighter-rate-limits-research.md`

---

## Lighter WebSocket API Research (Added 2025-12-07)

### Purpose
Research for replacing REST polling with WebSocket streaming to reduce API calls and improve latency.

### Key Findings

**Connection URLs:**
- Mainnet: `wss://mainnet.zklighter.elliot.ai/stream`
- Testnet: `wss://testnet.zklighter.elliot.ai/stream`

**Available Channels:**

| Channel | Format | Auth Required |
|---------|--------|---------------|
| Order Book | `order_book/{MARKET_INDEX}` | No |
| Market Stats | `market_stats/{MARKET_INDEX}` | No |
| Trade | `trade/{MARKET_INDEX}` | No |
| Account All | `account_all/{ACCOUNT_ID}` | Yes |
| Account Orders | `account_all_orders/{ACCOUNT_ID}` | Yes |
| User Stats | `user_stats/{ACCOUNT_ID}` | Yes |
| Notifications | `notification/{ACCOUNT_ID}` | Yes |

**Authentication:**
- Private channels require auth token in subscription message
- Existing `SignerClient.CreateAuthTokenAsync()` generates tokens
- Default validity: 600 seconds (10 minutes)

**Ping/Pong:**
- Server sends `{"type": "ping"}`
- Client must respond with `{"type": "pong"}`

**Rate Limits (WebSocket):**
- Max connections per IP: 100
- Max subscriptions per connection: 100
- Messages per minute: 200
- Excessive messages may cause disconnect

**Transaction Submission:**
- Can submit transactions via WebSocket (not just REST)
- `jsonapi/sendtx` for single, `jsonapi/sendtxbatch` for up to 50

**Full research:** See `.claude/doc/lighter-websocket-api-research.md`

---

## WebSocket Client Implementation Plan (Added 2025-12-07)

### Objective
Replace REST polling with WebSocket streaming using `System.Threading.Channels` for high-performance event processing.

### Architecture Summary

```
WebSocket Connection
        │
        ▼
LighterWebSocketClient (receives, routes messages)
        │
        ├──► OrderBookChannel (bounded, DropOldest)
        │         └──► OrderBook Processor
        │
        ├──► AccountChannel (bounded, DropOldest)
        │         └──► Account Processor
        │
        ├──► OrdersChannel (bounded, DropOldest)
        │         └──► Orders Processor
        │
        └──► MarketStatsChannel (bounded, DropOldest)
                  └──► MarketStats Processor
                           │
                           ▼
                  ILighterRealtimeState
                  (Thread-safe snapshots)
                           │
                           ▼
                  HybridMarketDataService
                  (WebSocket first, REST fallback)
                           │
                           ▼
                  TradingDecisionEngine
```

### Key Design Decisions

1. **Bounded Channels with DropOldest**: Prevents memory growth; old market data is stale anyway
2. **Hybrid Data Provider**: Transparent fallback to REST when WebSocket unavailable
3. **Thread-safe State**: `ILighterRealtimeState` provides atomic snapshots
4. **Auth Token Refresh**: Automatic refresh before expiry (8 min intervals)
5. **Reconnection**: Exponential backoff with jitter

### Files to Create

| File | Project | Purpose |
|------|---------|---------|
| `WebSocketOptions.cs` | GridBot.Lighter | Configuration |
| `Models/WebSocket/WebSocketMessages.cs` | GridBot.Lighter | Server message models |
| `Models/WebSocket/ChannelEvents.cs` | GridBot.Lighter | Channel event types |
| `ILighterWebSocketClient.cs` | GridBot.Lighter | Interface |
| `LighterWebSocketClient.cs` | GridBot.Lighter | Implementation |
| `Services/Realtime/ILighterRealtimeState.cs` | GridBot.ApiService | State interface |
| `Services/Realtime/LighterRealtimeStateService.cs` | GridBot.ApiService | State processor |
| `Services/MarketData/HybridMarketDataService.cs` | GridBot.ApiService | Hybrid data provider |

### Expected Impact

| Metric | Before | After |
|--------|--------|-------|
| REST weight/min | ~12,000 | ~300 |
| Data latency | 5 seconds | <50ms |
| 429 errors | Common | Rare |

### Full Plan
See `.claude/plans/websocket-client-implementation.md`

### Status
**IMPLEMENTED** - 2025-12-07

---

## WebSocket Client Implementation (Completed 2025-12-07)

### Implementation Summary

Successfully implemented the WebSocket client infrastructure for real-time Lighter DEX data streaming. This replaces REST polling for market data with WebSocket streaming, significantly reducing API usage and improving latency.

### Files Created

#### GridBot.Lighter (Core Library)

| File | Description |
|------|-------------|
| `WebSocketOptions.cs` | Configuration class for WebSocket client (reconnect delays, channel capacity, auth refresh interval) |
| `Models/WebSocket/WebSocketMessages.cs` | JSON message models for WebSocket protocol (subscribe, ping/pong, order book, account, orders, market stats, notifications) |
| `Models/WebSocket/ChannelEvents.cs` | Channel event types with parsed data (OrderBookUpdateEvent, AccountUpdateEvent, OrderUpdateEvent, MarketStatsUpdateEvent, ConnectionStateEvent, NotificationEvent) |
| `ILighterWebSocketClient.cs` | Interface defining WebSocket client contract with ChannelReader<T> properties for each event type |
| `LighterWebSocketClient.cs` | Full implementation with connection management, reconnection logic, auth token refresh, message routing |

#### GridBot.ApiService (Application Layer)

| File | Description |
|------|-------------|
| `Services/Realtime/ILighterRealtimeState.cs` | Interface for thread-safe access to latest WebSocket data snapshots |
| `Services/Realtime/LighterRealtimeStateService.cs` | BackgroundService that processes channel events and maintains state dictionaries |
| `Services/MarketData/HybridMarketDataService.cs` | IMarketDataService implementation that prefers WebSocket data with REST fallback |

#### Modified Files

| File | Changes |
|------|---------|
| `LighterServiceCollectionExtensions.cs` | Added `AddLighterWebSocket()` method, exposed SignerClient via internal property |
| `LighterCommandClient.cs` | Added internal `Signer` property to expose SignerClient for WebSocket auth |

### Key Features

1. **Bounded Channels with DropOldest**
   - Prevents memory growth under high message rates
   - Old market data is stale anyway, safe to drop
   - Configurable capacity (default 100)

2. **Automatic Reconnection**
   - Exponential backoff with jitter
   - Maximum reconnect attempts configurable (default 100)
   - Automatic resubscription after reconnect

3. **Auth Token Management**
   - Automatic refresh before expiry (8 minutes by default)
   - Uses existing SignerClient.CreateAuthTokenAsync()
   - Thread-safe token storage

4. **Hybrid Data Provider**
   - WebSocket data preferred when connected and fresh
   - Automatic REST fallback when WebSocket unavailable
   - Stale data threshold: 30 seconds

5. **Thread-Safe State Access**
   - ConcurrentDictionary for market-specific data
   - Interlocked for timestamp tracking
   - Volatile for account snapshot

### Usage

```csharp
// In Program.cs
builder.Services.AddLighterClient(builder.Configuration);
builder.Services.AddLighterWebSocket(builder.Configuration);

// Register realtime state service
builder.Services.AddSingleton<ILighterRealtimeState, LighterRealtimeStateService>();
builder.Services.AddHostedService(sp =>
    (LighterRealtimeStateService)sp.GetRequiredService<ILighterRealtimeState>());

// Use HybridMarketDataService instead of MarketDataService
builder.Services.AddSingleton<IMarketDataService, HybridMarketDataService>();
```

### Configuration (appsettings.json)

```json
{
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

### Build Status
- All files compile successfully
- Solution builds with 0 warnings, 0 errors

### Service Registration (Completed 2025-12-07)
Services have been registered in `GridBot.ApiService/Program.cs`:
- `AddLighterWebSocket` for WebSocket client
- `ILighterRealtimeState` / `LighterRealtimeStateService` as hosted service
- `HybridMarketDataService` overrides default `MarketDataService`

WebSocket streaming is now **ACTIVE** - the bot will automatically:
1. Connect to WebSocket on startup
2. Subscribe to order book, account, and order channels
3. Use real-time WebSocket data when available
4. Fall back to REST API when WebSocket is unavailable or data is stale (>30 seconds)

### Next Steps (Optional Enhancements)
1. ~~Register services in ApiService Program.cs to activate WebSocket streaming~~ **DONE**
2. Add WebSocket connection health check endpoint
3. Add metrics for WebSocket message rates and latency
4. Consider adding trade channel subscription for real-time fill notifications

---

## Perpetual Futures Skew/Inventory Management Fix (Completed 2025-12-07)

### Problem Summary
The inventory/skew management system was designed for **spot trading** and failed fundamentally for perpetual futures:
- Used `Math.Abs(positionSize)` which ignored position direction (long vs short)
- A short position was treated identically to a long position
- Target skews assumed 0-100% range (long only), not -100% to +100% (short to long)
- Skew correction signals were inverted for short positions

### Changes Implemented

#### 1. InventoryManager.CalculatePortfolioValues (CRITICAL FIX)
**File:** `GridBot.ApiService\Services\Inventory\InventoryManager.cs`

```csharp
// OLD (wrong):
positionSize = size;
var cryptoValueUsd = Math.Abs(positionSize) * currentPrice;

// NEW (correct - uses Sign field for direction):
positionSize = size * position.Sign;  // Sign: 1=Long, -1=Short, 0=None
var cryptoValueUsd = positionSize * currentPrice;  // Preserves sign!
```

#### 2. Target Skew Values
**Files:** `InventoryState.cs`, `RiskConfiguration.cs`

| Trend State | Old Target | New Target |
|-------------|------------|------------|
| StrongBull | 80% | +80% (same) |
| MildBull | 70% | +50% |
| Neutral | 50% | **0% (flat)** |
| MildBear | 30% | **-50% (short)** |
| StrongBear | 20% | **-80% (heavily short)** |

#### 3. Acceptable Skew Ranges
**File:** `InventoryManager.cs`

| Trend State | Old Range | New Range |
|-------------|-----------|-----------|
| StrongBull | 60-95% | +60% to +95% |
| MildBull | 50-85% | +30% to +70% |
| Neutral | 35-65% | **-20% to +20%** |
| MildBear | 15-50% | **-70% to -30%** |
| StrongBear | 5-40% | **-95% to -60%** |

#### 4. SkewCorrectionDirection Enum Renamed
**File:** `InventoryAnalysis.cs`

```csharp
// OLD:
NeedMoreCrypto, NeedLessCrypto

// NEW:
IncreaseExposure,  // Buy/cover - increase position
ReduceExposure     // Sell/short - decrease position
```

#### 5. Emergency Rebalance Clamp
**File:** `RebalancingService.cs`

```csharp
// OLD:
Math.Clamp(..., 10m, 90m)  // 10-90% range

// NEW:
Math.Clamp(..., -95m, 95m)  // -95% to +95% range (allows shorts)
```

#### 6. InventoryAnalysis.Empty Defaults
**File:** `InventoryAnalysis.cs`

```csharp
// OLD: 50% neutral
CurrentSkew = 50m, TargetSkew = 50m, AcceptableSkewMin = 35m, AcceptableSkewMax = 65m

// NEW: 0% flat (neutral for perpetuals)
CurrentSkew = 0m, TargetSkew = 0m, AcceptableSkewMin = -20m, AcceptableSkewMax = 20m
```

### Files Modified
1. `GridBot.ApiService\Services\Inventory\InventoryManager.cs` - Main fix + skew ranges + correction logic
2. `GridBot.ApiService\Models\Trading\InventoryAnalysis.cs` - Enum rename + Empty defaults + doc comments
3. `GridBot.ApiService\Models\Trading\InventoryState.cs` - Target skew values + doc comments
4. `GridBot.ApiService\Configuration\RiskConfiguration.cs` - Target skew values
5. `GridBot.ApiService\Services\Rebalancing\RebalancingService.cs` - Emergency clamp
6. `GridBot.ApiService\Services\Trend\TrendIntelligenceService.cs` - Use new enum values
7. `GridBot.ApiService\Services\Grid\GridLifecycleService.cs` - Updated log messages

### Expected Behavior After Fix

| Scenario | Old Behavior | New Behavior |
|----------|--------------|--------------|
| Short -0.1 BTC | Shows +10% (wrong) | Shows **-10%** (correct) |
| StrongBear trend | Target 20% long | Target **-80% short** |
| Neutral trend | Target 50% long | Target **0% flat** |
| Short in StrongBear | "NeedLessCrypto" signal (wrong) | Position optimal (no correction) |
| Trend flip Bear->Bull | Cannot go short | **Can flip from short to long** |

### Build Status
- All files compile successfully
- Solution builds with 0 warnings, 0 errors

### Post-Review Fix: EC-002 Short Position Detection (Added after code review)

**File:** `GridBot.ApiService\Services\Grid\GridLifecycleService.cs:263`

Both the csharp-code-reviewer and trading-bot-auditor identified this issue:

```csharp
// OLD (broken for shorts):
var hasPosition = inventory.CurrentSkew > 5; // Only detects longs!

// NEW (works for both longs and shorts):
var hasPosition = Math.Abs(inventory.CurrentSkew) > 5;
```

This ensures EC-002 protection (rebuild grid when orders cancelled) works for short positions.

### Code Review Reference
- Code review: `.claude/doc/code-review-perpetual-skew-changes-2025-12-07.md`
- Trading audit: `.claude/doc/trading-bot-audit-perpetual-futures-skew.md`

### Specification Reference
Full specification: `.claude/doc/perpetual-futures-skew-specification.md`

---

## WebSocket Model Fixes (Completed 2025-12-07)

### Problem
After reviewing the WebSocket models against the official Lighter API documentation, several discrepancies were identified that would cause deserialization errors at runtime.

### Issues Identified and Fixed

#### 1. MarketStatsData Field Names
**File:** `GridBot.Lighter\Models\WebSocket\WebSocketMessages.cs`

| Old Field | New Field | Issue |
|-----------|-----------|-------|
| `market_index` | `market_id` | Wrong property name |
| N/A | `last_trade_price` | Missing field |
| N/A | `current_funding_rate` | Missing field |
| `volume_24h` | `daily_quote_token_volume` | Wrong property name |

#### 2. NotificationMessage Structure
**File:** `GridBot.Lighter\Models\WebSocket\WebSocketMessages.cs`

| Old | New | Issue |
|-----|-----|-------|
| `notification` (single object) | `notifs` (array) | Wrong structure |
| `Notification.Type` | `NotificationItem.Kind` | Wrong property name |
| `Notification.Message` | Content-based message | Missing field |
| `Notification.MarketIndex` | `NotificationItem.Content.MarketIndex` | Wrong location |

#### 3. UserStatsMessage Field
**File:** `GridBot.Lighter\Models\WebSocket\WebSocketMessages.cs`

| Old | New | Issue |
|-----|-----|-------|
| `user_stats` | `stats` | Wrong property name |
| N/A | `cross_stats` | Missing field |
| N/A | `total_stats` | Missing field |

#### 4. AccountAllMessage Additional Fields
Added missing fields that may be present in API responses:
- `trades` (Dictionary<string, List<TradeData>>)
- `shares` (List<PoolSharesData>)
- `funding_histories` (Dictionary<string, List<FundingHistoryData>>)

### Handler Updates

**File:** `GridBot.Lighter\LighterWebSocketClient.cs`

1. **HandleMarketStatsMessageAsync** (line 707-729)
   - Changed `msg.MarketStats.MarketIndex` → `msg.MarketStats.MarketId`
   - Changed `msg.MarketStats.Volume24h` → `msg.MarketStats.DailyQuoteTokenVolume`
   - Changed `msg.MarketStats.FundingRate` → `msg.MarketStats.CurrentFundingRate`

2. **HandleNotificationMessageAsync** (line 731-771)
   - Changed from single `msg.Notification` to iterating `msg.Notifs` array
   - Added `BuildNotificationMessage()` helper to construct message from content based on `Kind`
   - Handles liquidation, deleverage, and announcement notification types

3. **RouteMessageAsync** (line 530)
   - Changed property check from `"notification"` to `"notifs"`

### Build Status
- All files compile successfully
- Solution builds with 0 warnings, 0 errors

### Models Now Match Official Documentation
The WebSocket models now correctly match the Lighter API WebSocket specification for:
- Order book channel (`order_book/{MARKET_INDEX}`)
- Account all channel (`account_all/{ACCOUNT_ID}`)
- Account orders channel (`account_all_orders/{ACCOUNT_ID}`)
- Market stats channel (`market_stats/{MARKET_INDEX}`)
- User stats channel (`user_stats/{ACCOUNT_ID}`)
- Notification channel (`notification/{ACCOUNT_ID}`)
- Trade channel (`trade/{MARKET_INDEX}`)

---

## Order Placement Analysis (2025-12-07)

### Current Implementation

**Orders are placed via REST API, NOT WebSocket**

1. **Single order placement via REST**
   - `LighterCommandClient.SendTransactionAsync` uses HTTP POST to `sendTx` endpoint
   - File: `GridBot.Lighter\LighterCommandClient.cs:447`

2. **Sequential order placement in GridOrderManager**
   - Orders placed one-by-one in a foreach loop
   - File: `GridBot.ApiService\Services\Grid\GridOrderManager.cs:102-270`
   - Each order = separate HTTP request

3. **REST Batch exists but is INTERNAL and UNUSED**
   - `LighterCommandClient.SendTransactionBatchAsync` (lines 467-494) exists
   - Marked `internal` - not exposed via `ILighterCommandClient`
   - `GridOrderManager` doesn't use it

4. **WebSocket client doesn't support transaction submission**
   - `LighterWebSocketClient` only handles subscriptions (order book, account, orders)
   - No `SendTx` or `SendTxBatch` methods

### API Support for WebSocket Transactions

**Lighter DOES support WebSocket transaction submission:**

| Message Type | Description |
|--------------|-------------|
| `jsonapi/sendtx` | Single transaction |
| `jsonapi/sendtxbatch` | Batch up to 50 transactions |

### Key Implementation Difference

| Aspect | REST | WebSocket |
|--------|------|-----------|
| Format | Comma-separated strings | JSON-encoded array strings |
| `tx_types` | `"1,1,5"` | `"[1,1,5]"` |
| `tx_infos` | `"{...},{...}"` | `"[{...},{...}]"` |

### Batch Transaction WebSocket Format

```json
{
  "type": "jsonapi/sendtxbatch",
  "data": {
    "id": "unique_request_id",
    "tx_types": "[1,1,5]",           // JSON-encoded array
    "tx_infos": "[{...},{...},{...}]" // JSON-encoded array
  }
}
```

**CRITICAL**: `tx_types` and `tx_infos` are **double-encoded** (JSON strings containing JSON arrays).

### Constraints

- All transactions in batch MUST use same `api_key_index`
- Sequential nonces required for each transaction
- Batch size limit: ~50 transactions (recommended: start with 10-20)

### Potential Improvements

1. **Expose REST batch via interface**
   - Add `SendTransactionBatchAsync` to `ILighterCommandClient`
   - Update `GridOrderManager.PlaceGridOrdersAsync` to batch orders

2. **Add WebSocket transaction support**
   - Extend `ILighterWebSocketClient` with `SendTransactionBatchAsync`
   - Implement in `LighterWebSocketClient`
   - Lower latency than REST for persistent connections

### Documentation Reference
Full specification: `.claude/doc/lighter-websocket-batch-transactions-specification.md`

---

## Batch Order Placement Implementation (Completed 2025-12-07)

### Summary
Implemented REST batch order placement to replace sequential order submission. All grid orders are now submitted in a single HTTP request instead of one request per order.

### Changes Made

#### 1. ILighterCommandClient Interface (`GridBot.Lighter\ILighterCommandClient.cs`)

Added new types and methods:

```csharp
// New result types
public sealed record SignedOrderResult(int TxType, string TxInfo, string? Error);

public sealed record BatchOrderResult
{
    public required bool IsSuccess { get; init; }
    public required int OrdersSubmitted { get; init; }
    public required string[] TxHashes { get; init; }
    public string? ErrorMessage { get; init; }
    public int Code { get; init; }
}

// New interface methods
Task<SignedOrderResult> SignOrderAsync(CreateOrderRequest request);
Task<BatchOrderResult> SubmitOrderBatchAsync(SignedOrderResult[] signedOrders, CancellationToken ct);
Task<BatchOrderResult> CreateOrderBatchAsync(CreateOrderRequest[] requests, CancellationToken ct);
```

#### 2. LighterCommandClient (`GridBot.Lighter\LighterCommandClient.cs`)

Implemented the new batch methods:

- `SignOrderAsync` - Signs a single order without submitting (for batch preparation)
- `SubmitOrderBatchAsync` - Submits pre-signed orders in a single batch
- `CreateOrderBatchAsync` - Convenience method that signs and submits all orders

Key implementation details:
- Orders are signed sequentially (each needs unique, incrementing nonce)
- All signed orders submitted in single `sendTxBatch` REST call
- Existing `SendTransactionBatchAsync` (internal) now used by public batch methods

#### 3. GridOrderManager (`GridBot.ApiService\Services\Grid\GridOrderManager.cs`)

Refactored `PlaceGridOrdersAsync` to use batch placement:

**Before (sequential):**
```
foreach order:
  validate → sign → submit → check response
```

**After (batch):**
```
Phase 1: foreach order: validate → prepare request
Phase 2: submit all orders in single batch → update all levels
```

Key changes:
- Removed circuit breaker logic (batch is all-or-nothing)
- Cached position lookup to avoid redundant API calls
- All orders succeed or fail together
- Single log entry for batch success/failure

### Impact

| Metric | Before | After |
|--------|--------|-------|
| HTTP requests for 12 orders | 12 | 1 |
| API weight for 12 orders | 72 (12×6) | 6 |
| Error handling | Per-order | All-or-nothing |
| Latency | ~12× single request | ~1× single request |

### Build Status
- Solution builds with 0 warnings, 0 errors

### Files Modified
1. `GridBot.Lighter\ILighterCommandClient.cs` - Added batch types and methods
2. `GridBot.Lighter\LighterCommandClient.cs` - Implemented batch methods
3. `GridBot.ApiService\Services\Grid\GridOrderManager.cs` - Refactored to use batch

### Notes
- The existing `SendTransactionBatchAsync` method was already implemented but marked `internal`
- REST batch uses comma-separated strings for `tx_types` and `tx_infos`
- WebSocket batch (future) would use JSON-encoded arrays instead

### Critical Bug Fixes During Implementation

#### 1. JSON Encoding Format (21501 "invalid tx info" error)

The API requires a specific double-encoded format:

```csharp
// WRONG - comma-separated
formContent.Add(new StringContent("14,14,14"), "tx_types");
formContent.Add(new StringContent("{...},{...}"), "tx_infos");

// WRONG - JSON arrays of objects
formContent.Add(new StringContent("[14,14,14]"), "tx_types");
formContent.Add(new StringContent("[{...},{...}]"), "tx_infos");

// CORRECT - JSON array of integers + JSON array of STRINGS (double-encoded)
formContent.Add(new StringContent("[14,14,14]"), "tx_types");
formContent.Add(new StringContent("[\"{\\"AccountIndex\\":293...}\",\"...\"]"), "tx_infos");
```

**Solution**: `JsonSerializer.Serialize(txInfos)` where `txInfos` is `string[]` creates the correct double-encoded format.

#### 2. Base64 Signature Escaping

The `+` character in base64-encoded signatures was being escaped to `\u002B` by default JSON serialization, corrupting the signature.

**Solution**: Use `UnsafeRelaxedJsonEscaping` when re-serializing tx_info strings.

#### 3. Response Model Flexibility (`RespSendTxBatch.cs`)

The API returns `predicted_execution_time_ms` and `tx_hashes` in various formats (string, number, or array). The original model expected only strings.

**Solution**: Changed to `JsonElement` with accessor properties:

```csharp
[JsonPropertyName("tx_hashes")]
public JsonElement TxHashesRaw { get; set; }

[JsonPropertyName("predicted_execution_time_ms")]
public JsonElement PredictedExecutionTimeMsRaw { get; set; }

[JsonIgnore]
public string[] TxHashArray => /* handles String and Array */;

[JsonIgnore]
public int[] PredictedExecutionTimeMsArray => /* handles String, Number, and Array */;
```

### Implementation Status
**COMPLETE** - Ready for testing
