# Session 1: WebSocket-First Architecture & Order Book Waiting

## Status: ACTIVE

---

## COMPLETED: REST API Call Optimization in MarketDataService (December 12, 2025)

### Problem
The trading loop was making ~5 REST API calls per decision cycle (~2 seconds). This was excessive since most data can be retrieved from WebSocket or cached.

### Solution Implemented
Optimized `MarketDataService` to use WebSocket data first with REST fallback, and added candlestick caching.

### Changes Made

| File | Change |
|------|--------|
| `GridBot.ApiService/Services/MarketData/MarketDataService.cs` | Added `ILighterRealtimeState` dependency, candlestick cache, WebSocket-first data retrieval |

### Implementation Details

#### 1. Candlestick Caching (5 minute TTL)
- Added `ConcurrentDictionary<(int MarketId, string Resolution, int Count), (List<CandlestickData> Data, DateTimeOffset CachedAt)>` cache
- Cache TTL: 300 seconds (5 minutes)
- On cache hit: returns cached data immediately
- On cache miss/expired: fetches from REST, updates cache
- Debug logging for cache HIT/MISS/EXPIRED

#### 2. GetCurrentPriceAsync - WebSocket First
```csharp
// Try WebSocket first
var wsPrice = _realtimeState.GetCurrentPrice(marketId);
if (wsPrice.HasValue && wsPrice.Value > 0)
    return wsPrice.Value;

// Fall back to REST only if WebSocket unavailable
```
- Eliminates `GetOrderBookDetailsAsync` REST call when WebSocket is connected

#### 3. GetOrderBookSnapshotAsync - WebSocket First
```csharp
// Try WebSocket order book first
var wsOrderBook = _realtimeState.GetOrderBook(marketId);
if (wsOrderBook != null && wsOrderBook.Bids.Count > 0)
    return ConvertWebSocketOrderBook(marketId, wsOrderBook, depth);

// Fall back to REST only if WebSocket unavailable
```
- Added `ConvertWebSocketOrderBook()` helper method to map WebSocket `OrderBookSnapshot` to API `OrderBookSnapshot`
- For `LastPrice`, uses WebSocket `MidPrice` or falls back to `MarketStats.MarkPrice`
- Eliminates 2 REST calls: `GetOrderBookOrdersAsync` and `GetOrderBookDetailsAsync`

### Expected Results
- REST API calls reduced from ~5/loop to ~1 every 5 minutes (candlestick refresh)
- WebSocket data used for real-time price and order book
- Debug logs show cache behavior for troubleshooting

### New Constructor Signature
```csharp
public MarketDataService(
    ILighterQueryClient queryClient,
    ILighterRealtimeState realtimeState,  // NEW: WebSocket state
    ILogger<MarketDataService> logger)
```

---

## Risk Analysis Deep Dive + Implementation Plan (December 12, 2025)

### Documents Created
- **Risk Analysis**: `.claude/doc/alte-risk-analysis-deep-dive.md`
- **Implementation Plan**: Same document, sections H-M appended

### MAJOR FINDING: SHORT POSITION PROTECTION GAP

**Problem Identified**: The bot has FlashCrashDetector protecting LONG positions from crashes, but NO equivalent protection for SHORT positions from pumps!

| Scenario | Protection Level |
|----------|-----------------|
| LONG + crash (-20%) | FULL (FlashCrashDetector) |
| LONG + spike (+20%) | FULL (MoonBag + TrailingStop) |
| SHORT + crash (-20%) | N/A (making profit) |
| SHORT + spike (+20%) | **NONE** (CRITICAL GAP!) |

**Solution**: FlashPumpDetector - symmetric protection for shorts (now Priority #1)

### Implementation Priority (Updated)

| Priority | Item | Status |
|----------|------|--------|
| 1 | **FlashPumpDetector** | PLANNED - Short position protection |
| 2 | WebSocket Health Monitoring | PLANNED |
| 3 | Pre-Trade Depth Check | PLANNED |
| 4 | Automatic Moon Bag Release | PLANNED |
| 5 | Black Swan Circuit Breaker | PLANNED |
| 6 | Nonce Failure Alert | PLANNED |

### FlashPumpDetector Specification Summary

```
Thresholds (mirror FlashCrashDetector):
+3% in 1min  → PauseSells (5 min)
+5% in 5min  → PauseAll (15 min)
+10% in 15min → CancelAndCoverHalf (60 min)
+15% in 60min → FullHalt (240 min)
```

### Files to Create
- `GridBot.ApiService/Models/Trading/FlashPumpStatus.cs`
- `GridBot.ApiService/Services/Risk/IFlashPumpDetector.cs`
- `GridBot.ApiService/Services/Risk/FlashPumpDetector.cs`

### Other Key Findings

1. **Moon Bag vs Trend Conflict (CRITICAL)**
   - Moon bag release requires MANUAL OPERATOR APPROVAL
   - SOLUTION: Auto-release when StrongBear confirmed 4+ hours

2. **WebSocket Health Monitoring (CRITICAL GAP)**
   - No explicit disconnect detection
   - SOLUTION: Pause grid immediately on disconnect

3. **Black Swan Coverage (GAP)**
   - Only covers -15% in 60min
   - SOLUTION: Add -25% in 60min threshold with emergency 50% reduction

### Prioritized Recommendations

**CRITICAL (Implement Immediately):**
- WebSocket health monitoring with pause on disconnect
- Pre-trade depth check before order submission
- Automatic moon bag release in confirmed downtrend

**HIGH (This Week):**
- Black swan circuit breaker (-25%/60min threshold)
- Nonce failure alerts on 2nd consecutive failure
- Rate limit implementation

**MEDIUM (This Month):**
- 5-minute ATR for rapid volatility adaptation
- Slippage tracking and alerting
- Range detection mode for sideways markets

### Overall Risk Rating: MODERATE-HIGH
Comprehensive risk management but specific gaps could cause significant losses in edge cases.

---

## COMPLETED: Batch Transaction Error Handling & Retry (2025-12-11)

### Problem
1. Error responses from batch transactions weren't being parsed correctly - they have format `{"error":{"code":21104,...},"id":"txbatch_xxx"}` but code expected success format
2. Batch transactions had no retry logic for nonce errors (unlike single transactions)
3. `SendTxBatchWsResponse` model didn't match actual API response format

### Fixes Applied

| File | Change |
|------|--------|
| `LighterWebSocketClient.cs:502-509` | Added early check for `error` property to route error responses properly |
| `LighterWebSocketClient.cs:1156-1207` | Added `HandleTransactionErrorResponse()` to complete pending requests with error responses |
| `WsLighterCommandClient.cs:416-494` | Added retry loop to `CreateOrderBatchAsync` - on nonce error, syncs nonce and RE-SIGNS all orders |
| `TransactionResponses.cs` | Fixed `SendTxBatchWsResponse`: `tx_hash` is array (not comma-separated), `predicted_execution_time_ms` is long (not string) |

### How Batch Retry Works Now
```
CreateOrderBatchAsync([order1, order2, ...]):
  while (retryCount < 3):
    1. Sign all orders (consumes nonces)
    2. Submit batch
    3. If nonce error (21104):
       - Sync nonce from server
       - RE-SIGN all orders with new nonces
       - Retry
    4. Return result
```

---

## COMPLETED: Nonce Management Fixes (2025-12-11)

### Problem
Batch transactions were failing with error `21104: "invalid nonce"`. Investigation revealed TWO bugs:

1. **Off-by-one error in `SyncNonceAsync`**: When syncing nonce from server, the code was setting `_currentNonce = serverNonce` instead of `serverNonce - 1`. Since `GetNextNonce()` uses pre-increment (`++_currentNonce`), this caused the first transaction after sync to use nonce N+1 instead of N.

2. **No automatic nonce sync at startup**: With `InitialNonce=0` (default), the client started with nonce 0, but server expected nonce N (whatever the actual next nonce was).

### Root Cause Analysis
```
Nonce Flow:
- SignerClient._currentNonce starts at InitialNonce (default: 0)
- GetNextNonce() does: ++_currentNonce (pre-increment, returns N+1)
- First transaction sent with nonce=1, but server expects nonce=500

SyncNonceAsync Bug:
- Server returns: nextNonce = 500 (use nonce 500 for next tx)
- OLD (wrong): SetNonce(500) → _currentNonce=500 → GetNextNonce returns 501 ❌
- NEW (fixed): SetNonce(500-1) → _currentNonce=499 → GetNextNonce returns 500 ✅
```

### Fixes Applied

| File | Change |
|------|--------|
| `WsLighterCommandClient.cs:310-317` | Fixed `SetNonce(nonceResponse.Nonce)` → `SetNonce(nonceResponse.Nonce - 1)` with explanatory comment |
| `LighterRealtimeStateService.cs` | Added `SyncNonceFromServerAsync()` method that syncs nonce during `InitializeAsync()` when `InitialNonce=0` |
| `LighterRealtimeStateService.cs` | Added dependencies: `SignerClient`, `HttpClient`, `LighterOptions` to constructor |
| `LighterServiceCollectionExtensions.cs:80-90` | Updated DI registration to inject new dependencies |

### How It Works Now
1. `LighterRealtimeStateService.InitializeAsync()` is called at startup
2. If `InitialNonce == 0` (default), calls `SyncNonceFromServerAsync()`
3. Fetches next nonce via REST: `GET /api/v1/nextNonce?account_index=X&api_key_index=Y`
4. Sets `_currentNonce = serverNonce - 1` so `GetNextNonce()` returns correct value
5. WebSocket connects and subscriptions start
6. First transaction now uses correct nonce

### Retry Mechanism Still Active
The existing `ExecuteWithNonceRetryAsync` in `WsLighterCommandClient` provides fallback protection:
- If nonce error (21104) occurs, syncs nonce from server and retries
- Max 3 retries with 100ms delay between attempts

---

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

---

## COMPLETED: Comprehensive Trading Strategy Analysis (2025-12-12)

### Analysis Document
Full analysis created at: `.claude/doc/alte-risk-analysis-deep-dive.md`

### Key Findings Summary

**Critical Issues Identified:**
1. Moon Bag vs Trend Conflict - requires manual operator approval to release in bear market
2. WebSocket Health Gap - no explicit disconnect monitoring in decision engine
3. Black Swan Coverage - insufficient for extreme moves beyond -15% in 1 hour

**Scenario Walkthroughs Completed:**
- 20% flash crash (full timeline with state transitions)
- 20% pump (trailing stop and grid shift behavior)
- Bull market (inventory management and moon bag activation)
- Bear market (short position building and loss limits)
- Sideways/choppy market (where bot should excel)
- Extreme volatility (ATR adaptation latency issues - 1h ATR is lagging)

**Risk Rating: MODERATE-HIGH**

**Prioritized Recommendations:**
- CRITICAL: WebSocket health monitoring, pre-trade depth check, automatic moon bag release
- HIGH: Black swan circuit breaker (-25%/60min), nonce failure alerts
- MEDIUM: 5-minute ATR, slippage tracking, range detection mode

---

## Reference: API Limits & Nonce Management (2025-12-11)

Documentation created at: `.claude/doc/lighter-api-limits-nonce-management.md`

### Key Findings Summary

1. **Maximum Open Orders**: NOT explicitly documented. Practical limit constrained by Order Margin system and rate limits.

2. **Batch Transaction Limits**:
   - **50 transactions maximum per batch** (hard limit in `LighterWebSocketClient.cs:1099`)
   - Weight: 6 per `sendTxBatch` call

3. **Error Code 21104 (Invalid Nonce)**:
   - Occurs when nonce is too low, too high, stale, or server hasn't updated yet
   - Each API_KEY_INDEX has independent nonce stream
   - Up to 255 API keys available for parallel processing
   - Current implementation has retry mechanism: 3 retries with 100ms delay

4. **Rate Limits**:
   - Premium: 24,000 weighted requests/minute
   - Standard: 60 requests/minute
   - WebSocket: 100 connections/IP, 100 subscriptions/connection, 200 messages/minute

5. **Volume Quota**:
   - Starts at 1,000 TX
   - Max stackable: 5,000,000 TX
   - $10 volume = 1 additional TX allowance
   - 1 free TX per 15 seconds

---

## COMPLETED: Code Review - MarketDataService REST API Optimization (December 12, 2025)

### Review Document
Full review at: `.claude/doc/marketdataservice-code-review.md`

### Verdict: APPROVED WITH WARNINGS

### Summary
The changes to optimize REST API calls by using WebSocket data first with REST fallback are well-implemented. Thread-safe, follows .NET best practices.

### Issues Found

| Severity | Issue | Status |
|----------|-------|--------|
| WARNING | Unbounded Cache Growth - ConcurrentDictionary has no size limit | Low risk for trading bot with 1-2 markets |
| SUGGESTION | GetCandlesticksAsync returns mutable cached list | Low risk - callers unlikely to modify |

### Verification Results

- Thread Safety: PASS
- Memory Management: WARNING (acceptable for limited markets)
- Error Handling: PASS
- IEnumerable Multiple Enumeration: PASS
- Null Handling: PASS
- Performance: PASS
- Logging: PASS
- Code Simplification (KISS): PASS

### Recommendation
No critical issues found. Code is production-ready. Consider adding cache cleanup in future if bot expands to many markets.

---

## COMPLETED: Trading Safety Audit - MarketDataService (December 12, 2025)

### Audit Document
Full audit at: `.claude/doc/marketdataservice-trading-safety-audit.md`

### Verdict: CONDITIONAL PASS

### Critical Finding (BLOCKING)

**FINDING 3: No Staleness Check on WebSocket Data**
- Location: `MarketDataService.cs:52-58, 161-166`
- Risk: Could place orders on stale data after WebSocket reconnection
- Financial Impact: Up to 10% loss on filled orders during reconnection gaps
- **MUST FIX BEFORE PRODUCTION**

### Other Findings

| Risk Level | Finding | Verdict |
|------------|---------|---------|
| HIGH | MidPrice vs LastTradePrice semantic difference | ACCEPTABLE - MidPrice better for grid trading |
| MEDIUM | 5-minute candlestick cache during regime changes | ACCEPTABLE - trend detection has 15-min confirmation |
| MEDIUM | WebSocket order book depth may differ from REST | ACCEPTABLE - grid uses best bid/ask only |
| MEDIUM | No order book spread sanity validation | SHOULD FIX |
| LOW | REST fallback latency during high load | ACCEPTABLE - WebSocket is primary |

### Summary

| Category | Count |
|----------|-------|
| CRITICAL | 1 (BLOCKING) |
| HIGH | 1 (Acceptable) |
| MEDIUM | 3 (2 Acceptable, 1 Should Fix) |
| LOW | 2 (Pass) |

### Required Actions Before Production

1. **FIXED:** Add WebSocket data staleness validation (COMPLETED 2025-12-12)
   - Added `MaxWebSocketDataAgeSeconds = 10` constant
   - Added `IsWebSocketDataFresh()` helper method that checks:
     - `_realtimeState.IsConnected` - WebSocket must be connected
     - `_realtimeState.OldestDataAge.HasValue` - Data must exist
     - `dataAge.TotalSeconds < MaxWebSocketDataAgeSeconds` - Data must be fresh
   - Updated `GetCurrentPriceAsync()` to check staleness before using WebSocket price
   - Updated `GetOrderBookSnapshotAsync()` to check staleness before using WebSocket order book
   - Added warning logs when falling back to REST due to stale data

2. **SHOULD FIX:** Add order book sanity validation
   - Validate bid < ask
   - Validate spread < 5%

### Deployment Status
- CRITICAL FINDING 3 ADDRESSED - Staleness validation now in place
- ACCEPTABLE for production with monitoring
- ACCEPTABLE for testnet/paper trading
- ACCEPTABLE for significant positions with monitoring

---

## COMPLETED: Risk Improvement Implementation Plan (December 12, 2025)

### Plan Document
Full implementation plan created at: `.claude/doc/alte-risk-implementation-plan.md`

### Key Deliverables

1. **NEW CRITICAL FINDING: FlashPumpDetector**
   - Identified asymmetry: LONG positions protected from crashes, SHORT positions NOT protected from pumps
   - Created complete specification mirroring FlashCrashDetector for upward moves
   - Thresholds: +3%/1min, +5%/5min, +10%/15min, +15%/60min
   - Actions: PauseSells, PauseAll, CancelAndCoverHalf, FullHalt
   - Priority elevated to #1 CRITICAL

2. **Full Implementation Plans Created for:**
   - H.1 CRITICAL: FlashPumpDetector (NEW - short position protection)
   - H.2 CRITICAL: WebSocket Health Monitoring
   - H.3 CRITICAL: Pre-Trade Depth Check
   - H.4 CRITICAL: Automatic Moon Bag Release
   - H.5 HIGH: Black Swan Circuit Breaker
   - H.6 HIGH: Nonce Failure Alert
   - H.7-H.12: Medium and Low priority items

3. **Each Plan Includes:**
   - Objective and problem statement
   - Complete risk rule specification with thresholds
   - Detailed implementation steps
   - Files to create and modify
   - Testing criteria
   - Edge case handling

4. **Supporting Documentation:**
   - Configuration summary (new appsettings.json options)
   - Implementation priority matrix (4-week schedule)
   - Testing strategy (unit, integration, manual)
   - Rollout plan (testnet -> dry run -> limited -> full)

### Updated Priority Order

| Priority | Item | Description |
|----------|------|-------------|
| 1 | FlashPumpDetector | **NEW** - Symmetric protection for SHORT positions |
| 2 | WebSocket Health | Disconnect detection, pause on disconnect |
| 3 | Pre-Trade Depth | Verify book depth before orders |
| 4 | Auto Moon Bag Release | Auto-release after 4h StrongBear |
| 5 | Black Swan Circuit | -25% in 60 min = emergency close |
| 6 | Nonce Failure Alert | Alert on 2nd failure, pause on 3rd |

### Next Steps
- Pass implementation plan to `dotnet-feature-builder` agent
- Start with H.1 FlashPumpDetector (most critical gap)
- Follow with H.2 WebSocket Health (infrastructure dependency)
