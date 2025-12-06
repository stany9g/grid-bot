# GridBot Trading System Comprehensive Audit Report

**Audit Date:** 2025-12-06
**Auditor:** Trading Systems Auditor
**System:** ALTE (Adaptive Liquidity & Trend Engine) GridBot
**Files Reviewed:** 15+ core trading files

---

## Executive Summary

The GridBot trading system demonstrates solid architectural foundations with proper use of `decimal` for financial calculations, comprehensive thread-safety mechanisms, and layered risk management. However, several HIGH and MEDIUM risk issues were identified that could cause financial loss or operational failures in production.

**Critical Finding:** The system has a TOCTOU vulnerability in the GridLevel class that could cause fill detection failures.

---

## Finding 1: GridLevel Class is Mutable in Thread-Unsafe Ways

**Risk Level:** HIGH
**Category:** Race Condition | Safety
**Location:** `GridBot.ApiService/Models/Trading/GridLevel.cs` and `GridBot.ApiService/Services/Grid/GridOrderManager.cs:165-167`
**Financial Impact:** Could miss fill detection, leading to orphaned orders or double-placement
**Performance Impact:** N/A

**Problem:**
The `GridLevel` class has mutable properties (`Status`, `OrderId`, `ClientOrderIndex`, `Size`, `LastUpdatedAt`) that are modified directly without synchronization. While the `GridOrderManager` uses a SemaphoreSlim for order placement, the `GridLifecycleService` accesses and modifies these same objects in `UpdateGridAsync` and `SyncOrderStatusAsync` under a different lock.

**Evidence:**
```csharp
// GridOrderManager.cs:165-167 - Mutates GridLevel inside _orderLock
level.ClientOrderIndex = clientOrderIndex;
level.Status = GridLevelStatus.Active;
level.LastUpdatedAt = DateTimeOffset.UtcNow;

// GridLifecycleService.cs:341-346 - Mutates same GridLevel inside _gridLock
foreach (var level in filledLevels)
{
    level.Status = GridLevelStatus.Pending;
    level.OrderId = null;
    level.ClientOrderIndex = null;
}
```

The two services use DIFFERENT locks (`_orderLock` vs `_gridLock`), creating a race condition where:
1. GridLifecycleService reads `level.Status == Filled`
2. GridOrderManager sets `level.Status = Active`
3. GridLifecycleService resets status to `Pending` (corrupting state)

**Fix:**
Either:
1. Make GridLevel immutable (return new instances)
2. Use a single lock for all GridLevel mutations
3. Add thread-safe property accessors with interlocked operations

**Verdict:** FAIL

---

## Finding 2: Fill Detection Logic Has TOCTOU Vulnerability

**Risk Level:** HIGH
**Category:** Race Condition | Trading Logic
**Location:** `GridBot.ApiService/Services/Grid/GridLifecycleService.cs:229-242`
**Financial Impact:** Could double-count fills, affecting P&L tracking and order replacement logic
**Performance Impact:** N/A

**Problem:**
Fill detection uses a pattern vulnerable to time-of-check-time-of-use (TOCTOU) race:

**Evidence:**
```csharp
// Step 1: Capture previously filled (snapshot)
var previouslyFilled = gridState.Levels
    .Where(l => l.Status == GridLevelStatus.Filled)
    .Select(l => l.ClientOrderIndex)
    .ToHashSet();

// Step 2: Sync with exchange (async operation - takes network time)
await _orderManager.SyncOrderStatusAsync(marketId, gridState.Levels, ct);

// Step 3: Count new fills
var fillsDetected = gridState.Levels.Count(l =>
    l.Status == GridLevelStatus.Filled &&
    !previouslyFilled.Contains(l.ClientOrderIndex));
```

Between Step 1 and Step 3, another thread could modify `gridState.Levels` (e.g., grid shift). The `previouslyFilled` snapshot becomes stale.

**Fix:**
Hold the grid lock during the entire fill detection cycle, or use immutable snapshots.

**Verdict:** FAIL

---

## Finding 3: Post-Only Orders Without Fill Guarantee Handling

**Risk Level:** MEDIUM
**Category:** Trading Logic | Safety
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:136`
**Financial Impact:** Orders may be rejected silently in fast markets, leaving grid gaps
**Performance Impact:** N/A

**Problem:**
All grid orders use `TimeInForce = TimeInForce.PostOnly`. Post-only orders are rejected if they would immediately match (cross the spread). In volatile markets, the spread can move between price calculation and order submission.

**Evidence:**
```csharp
var request = new CreateOrderRequest
{
    // ...
    TimeInForce = TimeInForce.PostOnly,  // Always PostOnly
    // ...
};
```

The code checks `response.Code == 0 || response.Code == 200` but doesn't specifically handle PostOnly rejection codes. If an order is rejected, it's logged but the grid level remains in a bad state (not Active, not retried).

**Fix:**
1. Add specific handling for PostOnly rejection
2. Implement automatic retry with adjusted price
3. Track PostOnly rejection rate as a metric
4. Consider using IOC for certain conditions (e.g., very tight spreads)

**Verdict:** FAIL

---

## Finding 4: No Fee Accounting in Order Sizing

**Risk Level:** MEDIUM
**Category:** Trading Logic | Precision
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:380-426`
**Financial Impact:** True P&L could be 0.1-0.5% worse than calculated per trade
**Performance Impact:** N/A

**Problem:**
Order size calculation (`CalculateOrderSizeAsync`) does not account for trading fees. The code calculates:
```
perLevelAllocation = maxDeployable / totalLevels
orderSize = orderSizeUsd / price
```

But actual cost is `orderSizeUsd * (1 + feeRate)`. With typical DEX fees of 0.05-0.10%, this compounds across many grid fills.

**Evidence:**
```csharp
// GridOrderManager.cs:407-414
var perLevelAllocation = maxDeployable / totalLevels;
var orderSizeUsd = Math.Max(perLevelAllocation, _config.Capital.MinOrderSizeUsd);
orderSizeUsd = Math.Min(orderSizeUsd, availableBalance * (_config.Capital.MaxOrderSizePercent / 100m));
var orderSize = orderSizeUsd / price;  // No fee adjustment
```

**Fix:**
Adjust available balance by expected fee costs:
```csharp
var feeRate = 0.001m; // 0.1% - get from config or exchange
var effectiveDeployable = maxDeployable / (1 + feeRate);
```

**Verdict:** FAIL

---

## Finding 5: Division by Zero Potential in Grid Calculations

**Risk Level:** MEDIUM
**Category:** Precision | Safety
**Location:** `GridBot.ApiService/Services/Grid/GridCalculator.cs:60`
**Financial Impact:** Could crash decision cycle, leaving grid in unknown state
**Performance Impact:** Decision cycle failure

**Problem:**
Grid width calculation divides by 200, which is safe. However, when width is constrained and recalculated, there's a potential division by zero:

**Evidence:**
```csharp
// GridCalculator.cs:56-57
var effectiveSpacing = totalWidth / (ordersPerSide * 2);
```

If `ordersPerSide` is somehow 0 (e.g., config misconfiguration, deserialization error), this would throw `DivideByZeroException`.

Also in `GridLifecycleService.cs:268`:
```csharp
var atrPercent = currentPrice > 0 ? (atr / currentPrice) * 100 : 1.0m;
```

While `currentPrice > 0` is checked, `gridState.Parameters.GridSpacing` is not checked before:
```csharp
var spacingChange = Math.Abs(currentSpacing - gridState.Parameters.GridSpacing) /
                   gridState.Parameters.GridSpacing;  // Could be 0!
```

**Fix:**
Add explicit guards:
```csharp
if (gridState.Parameters.GridSpacing <= 0)
{
    _logger.LogError("Invalid grid spacing: {Spacing}", gridState.Parameters.GridSpacing);
    // Force rebuild with default params
}
```

**Verdict:** FAIL

---

## Finding 6: Stale Price Data Used for Grid Operations Without Timestamp Check

**Risk Level:** MEDIUM
**Category:** Safety | Stale Data
**Location:** `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs:717-726`
**Financial Impact:** Orders placed at stale prices during connectivity issues
**Performance Impact:** N/A

**Problem:**
When price fetch times out, cached data is used if within `CacheValidityMs` (30 seconds). However, grid operations proceed with `IsPriceStale = true` flag but no guard prevents trading at stale prices.

**Evidence:**
```csharp
// DecisionEngine:717-726
catch (OperationCanceledException)
{
    Interlocked.Exchange(ref timedOut, 1);
    if (_priceCache.TryGetValue(marketId, out var cached) &&
        DateTimeOffset.UtcNow - cached.Timestamp < cacheValidity)
    {
        currentPrice = cached.Price;
        Interlocked.Exchange(ref isPriceStale, 1);  // Set flag...
    }
    // ...
}
```

Then in grid update flow:
```csharp
// GridLifecycleService - uses currentPrice without checking staleness
var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct);
// If this returns stale cached data, grid shift/rebuild uses it
```

A 30-second old price in volatile markets could be 1-2% off, causing grid misalignment.

**Fix:**
1. Check `IsPriceStale` flag before grid operations
2. Use wider spreads when operating with stale data
3. Reduce cache validity to 5-10 seconds for grid operations
4. Add explicit stale data guard in GridLifecycleService

**Verdict:** FAIL

---

## Finding 7: FlashCrashDetector ReaderWriterLockSlim Potential Deadlock

**Risk Level:** MEDIUM
**Category:** Race Condition | Deadlock
**Location:** `GridBot.ApiService/Services/Risk/FlashCrashDetector.cs:72-88, 147-164, 280-289`
**Financial Impact:** Flash crash detection could freeze, failing to protect during crash
**Performance Impact:** Thread starvation

**Problem:**
The FlashCrashDetector uses a single `ReaderWriterLockSlim` for both price history and crash events. The `TriggerCrashProtectionAsync` method acquires write lock while holding it (indirectly via `GetCrashCount24h` which acquires read lock).

**Evidence:**
```csharp
// TriggerCrashProtectionAsync calls:
_rwLock.EnterWriteLock();  // Line 281
try
{
    state.CrashEvents.Add(now);
}
finally
{
    _rwLock.ExitWriteLock();
}

var crashCount = GetCrashCount24h(marketId);  // This acquires READ lock!
```

While `ReaderWriterLockSlim` supports recursive reads, if this code path is entered while another thread holds a write lock, deadlock could occur.

**Fix:**
Acquire the write lock once and perform all operations:
```csharp
_rwLock.EnterWriteLock();
try
{
    state.CrashEvents.Add(now);
    var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
    crashCount = state.CrashEvents.Count(e => e >= cutoff);
}
finally
{
    _rwLock.ExitWriteLock();
}
```

**Verdict:** FAIL

---

## Finding 8: ConcurrentDictionary TOCTOU in Trend Detection

**Risk Level:** MEDIUM
**Category:** Race Condition
**Location:** `GridBot.ApiService/Services/Trend/TrendDetector.cs:118-139`
**Financial Impact:** Could apply wrong trend state, affecting inventory skew
**Performance Impact:** N/A

**Problem:**
The trend confirmation logic has a TOCTOU vulnerability:

**Evidence:**
```csharp
// Step 1: Check if pending confirmation exists and matches
if (_pendingConfirmations.TryGetValue(marketId, out var pending) &&
    pending.State == proposedState &&
    DateTimeOffset.UtcNow >= pending.ConfirmTime)
{
    // Step 2: Apply new state
    effectiveState = proposedState;

    // Step 3: Remove confirmation (TOCTOU - another thread could have modified!)
    _pendingConfirmations.TryRemove(marketId, out _);

    RecordTrendFlip(marketId, currentState, proposedState);
}
```

Between TryGetValue and TryRemove, another thread could update the pending confirmation. The removed value might not be the one that was checked.

**Fix:**
Use atomic compare-and-remove or lock the confirmation sequence.

**Verdict:** FAIL

---

## Finding 9: Telemetry Uses Double Instead of Decimal

**Risk Level:** LOW
**Category:** Precision
**Location:** `GridBot.ApiService/Services/Telemetry/TradingMetrics.cs:21-28`
**Financial Impact:** Minor precision loss in metrics (not trading logic)
**Performance Impact:** N/A

**Problem:**
Telemetry stores prices and equity as `double` instead of `decimal`. The comment explains this is intentional for atomicity, which is valid. However, `double` loses precision for large values.

**Evidence:**
```csharp
// Thread-safe per-market gauge storage using double to avoid torn reads
private static readonly ConcurrentDictionary<int, double> _currentPriceByMarket = new();
private static readonly ConcurrentDictionary<int, double> _currentEquityByMarket = new();
```

For BTC at $100,000+, double precision is sufficient for display. This is acceptable for telemetry but should not be used for trading calculations.

**Verdict:** PASS (acceptable for telemetry only)

---

## Finding 10: Partial Order Fill Handling is Incomplete

**Risk Level:** MEDIUM
**Category:** Trading Logic
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:315-376`
**Financial Impact:** Partial fills not tracked, position sizing could drift
**Performance Impact:** N/A

**Problem:**
The `SyncOrderStatusAsync` method marks orders as either Active or Filled. There's no handling for partial fills.

**Evidence:**
```csharp
if (orderLookup.TryGetValue(level.ClientOrderIndex.Value, out var order))
{
    // Order is still active
    level.OrderId = long.TryParse(order.OrderId, out var id) ? id : null;
    level.Status = GridLevelStatus.Active;  // Always Active if found
}
else if (level.Status == GridLevelStatus.Active)
{
    // Order was active but no longer in active orders - likely filled
    level.Status = GridLevelStatus.Filled;  // Assumed fully filled
}
```

If an order is partially filled, it may still be in active orders with reduced size. The grid logic doesn't track this, leading to:
1. Position size mismatch
2. Incorrect P&L calculation
3. Grid imbalance

**Fix:**
1. Compare `InitialBaseAmount` vs `RemainingBaseAmount` from API
2. Track partial fill percentage
3. Adjust replacement order size based on filled portion
4. Record partial fills for P&L tracking

**Verdict:** FAIL

---

## Finding 11: Data Collection Timeout Doesn't Cancel Grid Operations

**Risk Level:** LOW
**Category:** Latency | Safety
**Location:** `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs:683-687`
**Financial Impact:** Grid operations continue with stale data
**Performance Impact:** Decision cycle runs longer than expected

**Problem:**
The data collection uses a timeout (`DataCollectionTimeoutMs = 2000`), but if timeout occurs, the decision cycle continues with whatever data was collected. Grid operations then make additional API calls without the same timeout protection.

**Evidence:**
```csharp
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeoutCts.CancelAfter(timeout);  // 2000ms timeout for data collection

// But later, GridLifecycleService makes its own API calls:
var currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, ct);  // Original ct, not timed
```

**Fix:**
Propagate timeout token through entire decision cycle, or add separate timeout for grid operations.

**Verdict:** PASS (low impact, existing timeout handling reduces risk)

---

## Finding 12: Moon Bag Protection Check Has Network Call in Hot Path

**Risk Level:** LOW
**Category:** Performance
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:97-119`
**Financial Impact:** Delayed order placement during high activity
**Performance Impact:** +50-200ms latency per sell order

**Problem:**
For every sell order placement, the code fetches current position from the exchange:

**Evidence:**
```csharp
if (!level.IsBid) // This is a sell order
{
    var currentPosition = await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);  // Network call!
    var shouldBlock = await _moonBagManager.ShouldBlockSellOrderAsync(
        marketId, level.Size, currentPosition, ct);
    // ...
}
```

If placing 5 sell orders, this makes 5 sequential position queries.

**Fix:**
1. Cache position from decision context
2. Fetch position once at start of PlaceGridOrdersAsync
3. Pass position as parameter

**Verdict:** PASS (functional but suboptimal)

---

## Finding 13: Client Order Index Generation Could Collide

**Risk Level:** LOW
**Category:** Trading Logic
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:655-662`
**Financial Impact:** Order rejection if collision occurs
**Performance Impact:** N/A

**Problem:**
The client order index generation uses timestamp + sequence, but the sequence wraps at 1000:

**Evidence:**
```csharp
private static long GenerateClientOrderIndex(GridLevel level)
{
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var sequence = Interlocked.Increment(ref _orderSequence) % 1000;  // Wraps at 1000
    var sideIndicator = level.IsBid ? 0 : 1;
    return (timestamp % 10_000_000_000) * 10000 + sequence * 10 + sideIndicator * 5 + level.LevelIndex;
}
```

If the bot places >1000 orders within the same millisecond (unlikely but possible during grid rebuild), collisions could occur.

**Fix:**
Increase sequence range or use GUID-based approach.

**Verdict:** PASS (extremely unlikely in practice)

---

## Finding 14: No Circuit Breaker for Exchange API Errors

**Risk Level:** MEDIUM
**Category:** Safety | Reliability
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs` (throughout)
**Financial Impact:** Repeated failed API calls waste rate limits, could miss opportunities
**Performance Impact:** N/A

**Problem:**
When API calls fail (auth token, order placement, cancellation), the code logs and continues. There's no circuit breaker pattern to back off after repeated failures.

**Evidence:**
Order placement loop continues even after multiple failures:
```csharp
foreach (var level in levels)
{
    try
    {
        // Place order...
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        errors.Add(...);
        ordersFailed++;
        _logger.LogError(...);
        // No break or circuit breaker!
    }
}
```

If the exchange is rate limiting or down, this exhausts rate limits quickly.

**Fix:**
Implement circuit breaker:
```csharp
if (ordersFailed >= 3)
{
    _logger.LogWarning("Circuit breaker triggered after {Failed} failures", ordersFailed);
    break;
}
```

**Verdict:** FAIL

---

## Finding 15: Decision Loop Interval May Be Too Slow for Grid Strategy

**Risk Level:** LOW
**Category:** Latency
**Location:** `GridBot.ApiService/Configuration/TradingBotOptions.cs:63`
**Financial Impact:** Missed fill detection in fast markets
**Performance Impact:** N/A

**Problem:**
`DecisionLoopIntervalMs = 5000` (5 seconds) is reasonable for a grid strategy, but fill detection and order replacement could benefit from faster iteration.

**Evidence:**
```csharp
public int DecisionLoopIntervalMs { get; set; } = 5000;
```

In volatile markets, a 5-second delay between fill detection means:
- Orders stay unfilled for up to 5 seconds after detection
- Price could move significantly
- Grid could become unbalanced

**Recommendation:**
Consider:
1. Faster fill detection loop (1-2 seconds)
2. Websocket-based fill notifications
3. Separate threads for fill detection vs strategy

**Verdict:** PASS (acceptable for stated strategy)

---

## Finding 16: Grid Shift Tolerance Calculation May Cause Thrashing

**Risk Level:** LOW
**Category:** Trading Logic
**Location:** `GridBot.ApiService/Services/Grid/GridLifecycleService.cs:490-520`
**Financial Impact:** Excessive order cancellation fees
**Performance Impact:** Rate limit exhaustion

**Problem:**
The grid shift logic uses `tolerance = oldParams.GridSpacing / 100m * 0.5m` to determine which orders to keep. With tight spacing (0.2%), tolerance becomes 0.1% - too tight.

**Evidence:**
```csharp
var tolerance = oldParams.GridSpacing / 100m * 0.5m; // 0.1% for tight grids

var hasMatchingNewLevel = newLevels.Any(nl =>
    nl.IsBid == oldLevel.IsBid &&
    Math.Abs(nl.Price - oldLevel.Price) / oldLevel.Price <= tolerance);
```

Small price movements (< 0.1%) could trigger full order replacement.

**Fix:**
Use minimum tolerance floor:
```csharp
var tolerance = Math.Max(oldParams.GridSpacing / 100m * 0.5m, 0.003m); // Min 0.3%
```

**Verdict:** PASS (monitoring recommended)

---

## Positive Findings

### Decimal Usage for Financial Calculations
All price, size, and P&L calculations correctly use `decimal` type. The codebase is consistent in avoiding `double` for financial values except in telemetry (acceptable).

### Proper Lot Size Compliance
`MarketScalingService.ScaleBaseAmountAsync` correctly snaps amounts to lot sizes:
```csharp
if (lotSize > 1)
{
    result = ((rawResult + lotSize / 2) / lotSize) * lotSize;
}
```

### CultureInfo.InvariantCulture for Parsing
All `decimal.Parse` calls use `CultureInfo.InvariantCulture`:
```csharp
decimal.TryParse(account.Collateral, NumberStyles.Number, CultureInfo.InvariantCulture, out var c)
```

### Flash Crash Protection with Layered Response
The `FlashCrashDetector` implements graduated response (Minor -> Moderate -> Severe -> Extreme) with appropriate actions.

### Never-Halt Philosophy
The system correctly implements "never halt" - degraded states reduce capacity rather than stopping trading entirely, protecting existing positions.

### Grid Initialization Order Cancellation Fix
The startup order cancellation (addressed in session 1) now includes retry with verification.

---

## Audit Summary

```
================================================================================
                            AUDIT SUMMARY
================================================================================

Total Findings: 16
+-- HIGH Risk: 2 (BLOCKING)
|   +-- Finding 1: GridLevel Thread-Safety
|   +-- Finding 2: Fill Detection TOCTOU
+-- MEDIUM Risk: 8 (RECOMMENDED FIX)
|   +-- Finding 3: Post-Only Rejection Handling
|   +-- Finding 4: Fee Accounting Missing
|   +-- Finding 5: Division by Zero Potential
|   +-- Finding 6: Stale Price Data Guard
|   +-- Finding 7: ReaderWriterLock Deadlock Risk
|   +-- Finding 8: Trend Detection TOCTOU
|   +-- Finding 10: Partial Fill Handling
|   +-- Finding 14: No API Circuit Breaker
+-- LOW Risk: 6 (ACCEPTABLE / MONITOR)
    +-- Finding 9: Telemetry Double Precision
    +-- Finding 11: Timeout Propagation
    +-- Finding 12: Moon Bag Network Call
    +-- Finding 13: Client Order Index Collision
    +-- Finding 15: Decision Loop Timing
    +-- Finding 16: Grid Shift Tolerance

Overall Verdict: CONDITIONAL PASS

Deployment Recommendation:
--------------------------
DO NOT deploy to production with real capital until:
1. HIGH risk findings (1, 2) are resolved
2. MEDIUM risk findings (3, 4, 5, 6, 14) are addressed

For testnet deployment:
- Acceptable with monitoring
- Set conservative position limits
- Enable verbose logging for all flagged areas
- Monitor fill detection accuracy closely

================================================================================
```

---

## Recommended Priority Order for Fixes

1. **Finding 1 & 2** (HIGH): GridLevel thread-safety and fill detection TOCTOU
2. **Finding 14** (MEDIUM): API circuit breaker - prevents runaway failures
3. **Finding 6** (MEDIUM): Stale price guard - prevents bad orders
4. **Finding 5** (MEDIUM): Division by zero - prevents crashes
5. **Finding 4** (MEDIUM): Fee accounting - affects P&L accuracy
6. **Finding 3** (MEDIUM): Post-Only handling - improves fill rate
7. **Finding 10** (MEDIUM): Partial fill handling - affects position accuracy
8. **Finding 7 & 8** (MEDIUM): Lock fixes - prevents edge case failures

---

## Testing Recommendations

1. **Concurrency Test:** Run multiple decision cycles simultaneously and verify grid state consistency
2. **Timeout Test:** Simulate API timeouts and verify grid doesn't operate with stale data
3. **Fill Rate Test:** Measure Post-Only rejection rate in volatile market conditions
4. **Circuit Breaker Test:** Simulate exchange API failures and verify backoff behavior
5. **Flash Crash Test:** Inject rapid price drops and verify protection activates correctly
6. **Partial Fill Test:** Create partially filled orders and verify correct state tracking

---

*Report generated by Trading Systems Auditor. This audit covers code correctness and trading logic. Security audit (authentication, API key handling) and infrastructure audit (deployment, monitoring) are separate concerns.*
