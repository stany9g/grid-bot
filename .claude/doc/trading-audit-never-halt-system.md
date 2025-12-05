# Trading System Audit: "Never Halt" Implementation

**Audit Date:** 2025-12-05
**Auditor:** Trading Systems Auditor
**Scope:** ALTE Grid Trading Bot - "Never Halt" Implementation
**Specification Reference:** `inventory-skew-risk-framework.md` v2.0

---

## Executive Summary

The "Never Halt" implementation replaces binary halt/run states with a graceful degradation system using operational capacity (10-100%). The core principle is maintained: **THE BOT NEVER HALTS**. However, several HIGH and MEDIUM risk findings require attention before production deployment.

---

## AUDIT FINDINGS

---

### FINDING 1: Position Property Inconsistency in TrailingStopService

**Risk Level:** HIGH
**Category:** Precision | Logic Error
**Location:** `GridBot.ApiService/Services/MoonBag/TrailingStopService.cs:587`
**Financial Impact:** Could cause trailing stop to fail silently, leaving position unprotected during crash

**Problem:**
The `GetCurrentPositionAsync` method in `TrailingStopService` parses `position.PositionSize` but the `InventoryManager` parses `position.Positionn`. This inconsistency suggests a data model mismatch that could cause one service to fail parsing while the other succeeds.

**Evidence:**
```csharp
// TrailingStopService.cs:587
if (!decimal.TryParse(position.PositionSize, NumberStyles.Number, CultureInfo.InvariantCulture, out var size))

// InventoryManager.cs:202 (and GridOrderManager.cs:445)
if (decimal.TryParse(position.Positionn, NumberStyles.Number, CultureInfo.InvariantCulture, out var size))
```

**Fix:**
Verify which property is correct in the Lighter API model (`Position` class). All services must use the same property consistently. The property name `Positionn` (with double 'n') suggests a typo - verify against API documentation.

**Verdict:** FAIL - Critical path inconsistency

---

### FINDING 2: 10% Minimum Capacity May Be Insufficient for Position Protection

**Risk Level:** HIGH
**Category:** Safety | Trading Logic
**Location:** `GridBot.ApiService/Services/Capacity/OperationalCapacityService.cs:15`
**Financial Impact:** At 10% capacity with 2 orders minimum, may not have enough liquidity to exit position in flash crash

**Problem:**
At 10% capacity, `GetOrderCountMultiplier` returns minimum 2 orders (1 buy, 1 sell). In protective mode during a flash crash, if the position is large, a single sell order may not provide adequate exit liquidity. The spec says "monitoring market, managing trailing stops" but trailing stops are software-managed and may fail if order placement is restricted.

**Evidence:**
```csharp
// OperationalCapacityService.cs:113
public int GetOrderCountMultiplier(int capacity, int normalCount)
{
    var scaled = (int)(normalCount * (capacity / 100m));
    return Math.Max(scaled, 2);  // Only 2 orders minimum!
}
```

**Scenario:**
- Bot enters protective mode at 10% capacity
- Position size: 10 BTC
- Only 1 sell order placed (grid has 2 orders, one buy one sell)
- Flash crash continues
- Trailing stop triggered but order size is 10% of normal = 1 BTC
- 9 BTC remains exposed

**Fix:**
In protective mode:
1. Trailing stops should use FULL position size, not capacity-scaled size
2. Add exception for ReduceOnly orders to bypass capacity scaling
3. Consider separate capacity multipliers for position-closing vs. position-opening orders

**Verdict:** FAIL - Position protection inadequate

---

### FINDING 3: Protective Mode Still Allows Grid Teardown But Not Rebuild

**Risk Level:** HIGH
**Category:** Safety | Logic Error
**Location:** `GridBot.ApiService/Services/Grid/GridLifecycleService.cs:89-96`
**Financial Impact:** Position becomes unmanaged if grid is torn down in protective mode

**Problem:**
`TeardownGridAsync` can be called in protective mode (during flash crash or loss limit breach - see `TradingDecisionEngine.cs:862,882`), but `InitializeGridAsync` explicitly blocks in protective mode. This creates a state where:
1. Crisis triggers protective mode
2. Grid is torn down (all orders cancelled)
3. Position still exists on exchange
4. Grid cannot be rebuilt until exiting protective mode
5. Position is completely unmanaged

**Evidence:**
```csharp
// GridLifecycleService.cs:89-96 - BLOCKS initialization in protective mode
if (state == TradingState.Degraded_ProtectiveMode)
{
    throw new InvalidOperationException(
        "Cannot initialize grid in protective mode - position reduction only");
}

// TradingDecisionEngine.cs:862 - Tears down grid in protective mode
await _gridLifecycle.TeardownGridAsync(marketId, ct).ConfigureAwait(false);
```

**Scenario:**
1. 60% drop in 15 minutes - Severe flash crash detected
2. `TeardownGridAsync` called - all orders cancelled
3. State transitions to `Degraded_ProtectiveMode`
4. Position of 5 BTC still exists
5. Grid cannot reinitialize
6. Market drops another 20%
7. Bot is "watching" but cannot place protective orders

**Fix:**
Either:
1. Don't teardown grid in protective mode - just pause it
2. Allow limited grid rebuilding in protective mode (reduce-only orders)
3. Ensure trailing stop orders are maintained independently of grid state

**Verdict:** FAIL - Contradictory logic creates unmanaged position state

---

### FINDING 4: Trailing Stop Uses Client Order Index as Order ID

**Risk Level:** MEDIUM
**Category:** Safety | Race Condition
**Location:** `GridBot.ApiService/Services/MoonBag/TrailingStopService.cs:486`
**Financial Impact:** Stop order may not be properly cancelled when updating, leading to duplicate stop orders

**Problem:**
The `UpdateTrailingStopOrderAsync` method stores `clientOrderIndex` in `state.StopOrderId` but `CancelOrderAsync` expects the actual exchange order ID, not the client order index. The comment on line 465 says "this will be our order identifier" but Lighter API cancellation requires the exchange-assigned order ID.

**Evidence:**
```csharp
// TrailingStopService.cs:465-486
var clientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var stopOrder = new CreateOrderRequest
{
    ClientOrderIndex = clientOrderIndex,
    // ...
};
// Storing CLIENT order index, not exchange order ID
state.StopOrderId = clientOrderIndex;

// Later attempting to cancel with wrong ID type:
// TrailingStopService.cs:418
await _commandClient.CancelOrderAsync(marketId, state.StopOrderId.Value, ct)
```

**Impact:**
- Cancel request fails silently
- Multiple stop orders accumulate
- On trigger, ALL stop orders execute
- Over-selling position below moon bag threshold

**Fix:**
After order creation, query exchange for the actual order ID using `clientOrderIndex` lookup, similar to `GridOrderManager.SyncOrderStatusAsync`.

**Verdict:** FAIL - Order management logic error

---

### FINDING 5: Bootstrap Mode Exit Condition Not Connected

**Risk Level:** MEDIUM
**Category:** Logic Error | Missing Implementation
**Location:** `GridBot.ApiService/Services/Inventory/InventoryManager.cs:80`
**Financial Impact:** Bot may remain in bootstrap mode indefinitely, only placing buy orders

**Problem:**
Bootstrap mode is detected (`isBootstrapMode = positionSize == 0`) and included in `InventoryAnalysis`, but there is no logic in `TradingDecisionEngine` or `GridLifecycleService` that:
1. Reads `IsBootstrapMode` from inventory analysis
2. Configures buy-only grid
3. Monitors for exit condition (position >= 10% of target)
4. Transitions out of bootstrap mode

**Evidence:**
```csharp
// InventoryManager.cs:80 - Detection exists
var isBootstrapMode = positionSize == 0;

// Spec says (inventory-skew-risk-framework.md:149-153):
// Exit Condition: position_size >= min_threshold (e.g., 10% of target)
// THEN: Transition to Active with normal grid

// NOT FOUND: Implementation of exit logic
```

**Impact:**
- New bot with 0 position enters bootstrap mode
- Buys accumulate
- Bot never transitions to Active even when position is built
- Sells never happen because still in "buy-only" mode

**Fix:**
Add bootstrap mode handling to `TradingDecisionEngine.ExecuteDecisionCycleAsync`:
1. Get inventory analysis
2. If `IsBootstrapMode` was true but position now >= threshold, transition state
3. Configure grid calculator to use buy-only mode when bootstrap

**Verdict:** FAIL - Incomplete feature implementation

---

### FINDING 6: Skew Correction Not Applied to Grid Orders

**Risk Level:** MEDIUM
**Category:** Logic Error | Missing Implementation
**Location:** `GridBot.ApiService/Services/Grid/GridLifecycleService.cs:168-313`
**Financial Impact:** Skew may never self-correct, leading to persistent risk

**Problem:**
The spec defines skew correction behavior:
- If crypto > max: reduce buys by 75%, increase sells by 50%
- If crypto < min: increase buys by 50%, reduce sells by 75%

`InventoryAnalysis` calculates `SkewCorrectionMode` and `CorrectionDirection`, but `GridLifecycleService.UpdateGridAsync` and `GridOrderManager.PlaceGridOrdersAsync` never read or apply this bias.

**Evidence:**
```csharp
// Spec (inventory-skew-risk-framework.md:118-130):
// IF crypto_skew > acceptable_max THEN
//   buy_orders = reduce by 75%
//   sell_orders = increase by 50%

// GridLifecycleService.UpdateGridAsync - NO skew correction logic
// Just replaces filled orders with same parameters
```

**Impact:**
- Bot in strong bull trend with 95% crypto
- Spec says reduce sells aggressively
- Instead, equal buy/sell orders placed
- Skew never corrects toward 80% target
- If trend reverses, massive loss from over-concentration

**Fix:**
Implement asymmetric grid sizing based on `CorrectionDirection`:
```csharp
if (correctionDirection == SkewCorrectionDirection.NeedLessCrypto)
{
    buyOrderSize *= 0.25m;  // 75% reduction
    sellOrderSize *= 1.5m;  // 50% increase
}
```

**Verdict:** FAIL - Core feature not implemented

---

### FINDING 7: Capacity Calculation Ignores Inventory Skew Deviation

**Risk Level:** MEDIUM
**Category:** Logic Error
**Location:** `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs:413-422`
**Financial Impact:** Capacity not reduced when inventory is dangerously skewed

**Problem:**
`CalculateCurrentCapacity` always passes `skewDeviation = 0m` because inventory analysis is not fetched before capacity calculation.

**Evidence:**
```csharp
// TradingDecisionEngine.cs:415
private int CalculateCurrentCapacity(int marketId, DecisionContext context)
{
    var skewDeviation = 0m;  // ALWAYS ZERO!
    // ...
    return _capacityService.CalculateCapacity(state, skewDeviation, hasApiErrors, highVolatility, lowLiquidity);
}
```

**Impact:**
- Inventory at 95% crypto (15% above 80% target)
- `skewDeviation` should be 15%
- Instead passed as 0
- No capacity reduction applied
- Bot continues at full capacity with dangerous concentration

**Fix:**
Fetch inventory analysis before capacity calculation and pass actual `skewDeviation`.

**Verdict:** FAIL - Safety feature bypassed

---

### FINDING 8: Race Condition in Price History During Flash Crash Check

**Risk Level:** MEDIUM
**Category:** Race Condition | Memory
**Location:** `GridBot.ApiService/Services/Risk/FlashCrashDetector.cs:81-82`
**Financial Impact:** Missed flash crash detection due to incomplete data snapshot

**Problem:**
The `CheckForFlashCrashAsync` method takes a snapshot of price history under read lock, but between snapshot creation and `CalculateDrop` execution, the data could become stale. More critically, the `.ToList()` creates a new allocation on every check cycle.

**Evidence:**
```csharp
// FlashCrashDetector.cs:81-82
// Take a snapshot for safe iteration
priceHistorySnapshot = state.PriceHistory.ToList();  // NEW ALLOCATION!
```

**Performance Impact:**
- Decision loop runs every 5 seconds
- Flash crash check creates new List allocation
- With 60 minutes of second-by-second data = 3600 price points
- Memory allocation: ~28KB per cycle
- GC pressure in hot path

**Fix:**
Use ArrayPool or pre-allocated buffer for price history snapshot. Consider using a lock-free ring buffer for price history.

**Verdict:** CONDITIONAL PASS - Not critical but adds GC pressure

---

### FINDING 9: API Timeout Does Not Refresh Cached Position During Protective Mode

**Risk Level:** MEDIUM
**Category:** Safety | Stale Data
**Location:** `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs:692-706`
**Financial Impact:** Decision based on stale position data during most critical time

**Problem:**
When API times out during data collection, cached position is used if within `cacheValidity` (default unknown). In protective mode, position tracking is most critical, yet the system uses potentially stale data with no additional validation.

**Evidence:**
```csharp
// TradingDecisionEngine.cs:694-700
catch (OperationCanceledException)
{
    Interlocked.Exchange(ref timedOut, 1);
    if (_positionCache.TryGetValue(marketId, out var cached) &&
        DateTimeOffset.UtcNow - cached.Timestamp < cacheValidity)
    {
        position = cached.Position;
        Interlocked.Exchange(ref isPositionStale, 1);
    }
    // ...
}
```

**Scenario:**
1. Flash crash occurring
2. API under load, times out
3. Cached position from 30 seconds ago: 10 BTC
4. Actual position (after stop-loss hit elsewhere): 0 BTC
5. Bot calculates trailing stop for 10 BTC
6. Trailing stop order fails (no position)
7. Decision cycle completes with false success

**Fix:**
In protective mode, position staleness should:
1. Force retry with longer timeout
2. If still fails, transition to more defensive state
3. Log critical warning for operator attention

**Verdict:** FAIL - Stale data in critical path

---

### FINDING 10: Missing Edge Case - All Orders Cancelled Externally

**Risk Level:** MEDIUM
**Category:** Missing Scenario
**Location:** `GridBot.ApiService/Services/Grid/GridLifecycleService.cs`
**Financial Impact:** Position left unmanaged if external force cancels all orders

**Problem:**
The spec defines EC-002 "All Orders Cancelled" scenario, but implementation is incomplete. If all orders are cancelled externally (exchange maintenance, liquidation engine, manual intervention), there is no detection mechanism.

**Evidence:**
```
// Spec (inventory-skew-risk-framework.md:218-223):
// EC-002: All Orders Cancelled
// Detection: active_orders == 0 AND position exists
// Response:
//   1. Immediately rebuild grid around current price
//   2. Log event for analysis
//   3. Continue normal operation

// NOT FOUND: Detection logic in GridLifecycleService or TradingDecisionEngine
```

**Scenario:**
1. Grid running with 5 BTC position
2. Exchange maintenance cancels all orders
3. Grid state still shows orders as "Active" (not synced)
4. Price moves 5%
5. No fills because no orders exist
6. Profit opportunity missed OR loss from no protection

**Fix:**
Add detection in `UpdateGridAsync`:
```csharp
var activeOrderCount = gridState.Levels.Count(l => l.Status == GridLevelStatus.Active && l.OrderId.HasValue);
var hasPosition = context.Position > 0;
if (activeOrderCount == 0 && hasPosition)
{
    _logger.LogWarning("EC-002: All orders cancelled externally. Rebuilding grid.");
    await InitializeGridAsync(marketId, ct);
}
```

**Verdict:** FAIL - Edge case unhandled

---

### FINDING 11: Liquidation Detection Not Implemented

**Risk Level:** HIGH
**Category:** Missing Scenario
**Location:** Not Found
**Financial Impact:** Bot continues operating after liquidation, placing orders with no position

**Problem:**
The spec defines EC-001 "Position Fully Closed Unexpectedly" with liquidation detection, but this is not implemented. If the exchange liquidates the position, the bot will:
1. Not detect the liquidation
2. Continue placing grid orders
3. Potentially build new position during adverse conditions

**Evidence:**
```
// Spec (inventory-skew-risk-framework.md:201-214):
// EC-001: Position Fully Closed Unexpectedly
// Detection: position went from X% to 0%
// Response:
//   1. Check if intentional (stop hit) or liquidation
//   2. IF liquidation:
//      - Enter Protective Mode at 10% capacity
//      - Wide spreads, small orders
//      - Wait for market stabilization

// NOT FOUND: Any implementation
```

**Fix:**
Add position change detection:
1. Track previous position size
2. If position goes from X > 0 to 0 in single cycle, check why
3. Query exchange for liquidation event
4. If liquidation: enter protective mode, alert operator

**Verdict:** FAIL - Critical safety feature missing

---

### FINDING 12: Decision Loop Exception May Orphan Position

**Risk Level:** LOW
**Category:** Safety
**Location:** `GridBot.ApiService/Services/TradingBotHostedService.cs:200-203`
**Financial Impact:** Low - exception is caught and loop continues

**Problem:**
If `RunDecisionLoopAsync` throws, the exception is caught and logged, but no specific recovery action is taken. While the loop continues, if the exception is persistent (e.g., configuration error), the position remains unmanaged.

**Evidence:**
```csharp
// TradingBotHostedService.cs:200-203
catch (Exception ex)
{
    _logger.LogError(ex, "Error in decision loop");
    // Continue running after errors
}
```

**Fix:**
Add consecutive error tracking:
```csharp
if (++consecutiveErrors >= 5)
{
    // Force protective mode after 5 consecutive errors
    await _stateService.TransitionToAsync(TradingState.Degraded_ProtectiveMode, "Consecutive errors");
}
```

**Verdict:** PASS with recommendation

---

### FINDING 13: Moon Bag Protection Check Adds API Call Per Sell Order

**Risk Level:** LOW
**Category:** Performance
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:100-101`
**Financial Impact:** Increased latency during order placement, potential rate limiting

**Problem:**
For every sell order in `PlaceGridOrdersAsync`, `GetCurrentPositionAsync` is called, which queries the exchange API. With 10 sell orders per grid update, this is 10 additional API calls.

**Evidence:**
```csharp
// GridOrderManager.cs:100-101
var currentPosition = await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);
var shouldBlock = await _moonBagManager.ShouldBlockSellOrderAsync(...);
```

**Fix:**
Cache position at start of `PlaceGridOrdersAsync` and reuse for all orders in batch:
```csharp
var currentPosition = await GetCurrentPositionAsync(marketId, ct).ConfigureAwait(false);
foreach (var level in levels)
{
    if (!level.IsBid)
    {
        var shouldBlock = await _moonBagManager.ShouldBlockSellOrderAsync(
            marketId, level.Size, currentPosition, ct);
        // ...
    }
}
```

**Verdict:** PASS with recommendation

---

## SUMMARY OF EDGE CASES NOT HANDLED

| Edge Case | Spec Reference | Status |
|-----------|----------------|--------|
| EC-001: Position Fully Closed Unexpectedly | Section 6 | NOT IMPLEMENTED |
| EC-002: All Orders Cancelled | Section 6 | NOT IMPLEMENTED |
| EC-003: Price Gaps Beyond Grid | Section 6 | Partially (grid shift logic exists) |
| EC-004: API Errors | Section 6 | IMPLEMENTED (cache fallback) |
| Liquidation Detection | EC-001 | NOT IMPLEMENTED |
| Bootstrap Mode Exit | Section 4 | NOT IMPLEMENTED |
| Skew Correction Grid Bias | Section 3 | NOT IMPLEMENTED |

---

## POSITIVE FINDINGS

1. **Core Loop Never Stops:** `TradingBotHostedService.ExecuteAsync` correctly catches all exceptions and continues. The main loop genuinely never halts.

2. **State Machine is Clean:** `TradingState` enum has no "Halted" or "Paused" state. All states are operational.

3. **Trailing Stop Exception for Flash Crash:** Code correctly allows trailing stop execution even when sells are blocked (line 219-229 of TradingDecisionEngine).

4. **Thread Safety:** Proper use of `ConcurrentDictionary`, `SemaphoreSlim`, and `Interlocked` operations throughout.

5. **Telemetry:** Comprehensive metrics for monitoring all states, capacity, and recovery phases.

6. **Capacity System Design:** The conceptual design of 10-100% capacity with multipliers for size/spread/count is sound.

7. **Recovery Manager:** Phased recovery with condition checking is well-implemented.

---

## AUDIT SUMMARY

```
===============================================================
AUDIT SUMMARY
===============================================================
Total Findings: 13
|-- HIGH Risk: 4 (BLOCKING)
|   |-- Finding 1: Position property inconsistency
|   |-- Finding 2: 10% capacity insufficient for protection
|   |-- Finding 3: Grid teardown but no rebuild in protective mode
|   |-- Finding 11: Liquidation detection not implemented
|
|-- MEDIUM Risk: 6
|   |-- Finding 4: Trailing stop uses wrong order ID type
|   |-- Finding 5: Bootstrap mode exit not implemented
|   |-- Finding 6: Skew correction not applied to grid
|   |-- Finding 7: Capacity ignores inventory skew
|   |-- Finding 9: Stale position data in protective mode
|   |-- Finding 10: All orders cancelled externally not handled
|
|-- LOW Risk: 3
|   |-- Finding 8: GC pressure from price history allocation
|   |-- Finding 12: Consecutive error tracking missing
|   |-- Finding 13: Moon bag check performance

Overall Verdict: FAIL
Deployment Recommendation: DO NOT DEPLOY until HIGH findings resolved
===============================================================
```

---

## REQUIRED ACTIONS BEFORE DEPLOYMENT

### Priority 1 (BLOCKING - Must Fix)

1. **Fix Position Property Inconsistency (Finding 1)**
   - Audit all uses of position parsing
   - Verify correct property from Lighter API
   - Unit test position parsing

2. **Fix Protective Mode Position Management (Finding 2 & 3)**
   - Trailing stops must use full position size regardless of capacity
   - If grid torn down, maintain trailing stop order independently
   - OR: Never teardown grid in protective mode, only pause

3. **Implement Liquidation Detection (Finding 11)**
   - Track position changes between cycles
   - Query exchange for liquidation events
   - Enter protective mode on liquidation

### Priority 2 (Should Fix Before Production)

4. **Fix Trailing Stop Order ID (Finding 4)**
   - Query for actual order ID after placement
   - Verify cancellation works

5. **Complete Bootstrap Mode (Finding 5)**
   - Add exit condition check
   - Implement buy-only grid mode

6. **Implement Skew Correction Bias (Finding 6)**
   - Pass correction direction to grid calculator
   - Apply asymmetric sizing

7. **Pass Actual Skew Deviation (Finding 7)**
   - Fetch inventory analysis before capacity calculation

### Priority 3 (Recommended)

8. Handle EC-002 (all orders cancelled)
9. Improve stale position handling in protective mode
10. Optimize moon bag position query caching
11. Add consecutive error tracking

---

## CONCLUSION

The "Never Halt" philosophy is correctly implemented at the conceptual level - the bot genuinely never stops its decision loop. However, several implementation gaps undermine the safety guarantees:

1. **Protective mode may leave positions unmanaged** due to grid teardown without rebuild capability
2. **Core safety features are incomplete** (bootstrap mode, skew correction, liquidation detection)
3. **Data inconsistencies** (position property name) could cause silent failures

The system is architecturally sound but requires completion of missing features and fixes for the identified logic errors before production deployment.

---

**Document Path:** `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\trading-audit-never-halt-system.md`
