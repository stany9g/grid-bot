# Phase 6 Moon Bag Module - Trading Systems Audit Report

**Audit Date:** 2025-11-26
**Auditor Role:** Trading Systems Auditor
**Scope:** Moon Bag Module implementation correctness from crypto trading perspective
**Files Reviewed:**
- `GridBot.ApiService/Services/MoonBag/TrailingGridService.cs`
- `GridBot.ApiService/Services/MoonBag/MoonBagManager.cs`
- `GridBot.ApiService/Services/MoonBag/TrailingStopService.cs`
- `GridBot.ApiService/Models/Trading/MoonBagStatus.cs`
- `GridBot.ApiService/Models/Trading/GridShiftResult.cs`
- `GridBot.ApiService/Configuration/TradingBotOptions.cs`

---

## CRITICAL FINDINGS (Could Cause Significant Losses)

### CRITICAL-001: Position Size Not Fetched from Exchange During Stop Execution

**Risk Level:** CRITICAL
**Category:** Trading Logic
**Location:** `TrailingStopService.cs:250-252`
**Financial Impact:** Could sell more than intended, potentially entire position including moon bag

**Problem:**
When executing the trailing stop, the code uses `status.MaxPositionAchieved` as the current position size, not the actual current position from the exchange:

```csharp
// Get current position from moon bag status
// Note: In a real implementation, we'd get this from the exchange
var currentPosition = status.MaxPositionAchieved; // Simplified
var sellQuantity = currentPosition - moonBagThreshold;
```

If the actual position has already been partially filled by normal grid trading, this calculation will attempt to sell more than exists, or the order will fail, leaving no protection executed.

**Financial Impact:**
- If position is already at 50% of max, the bot will try to sell 35% more than exists
- Could result in order rejection with no stop protection executed
- Position may plummet without any protective action taken

**Fix:**
```csharp
// Get actual current position from exchange
var currentPosition = await _positionService.GetCurrentPositionAsync(marketId, ct);
if (currentPosition <= 0)
{
    _logger.LogWarning("No position found for market {MarketId}, cannot execute stop", marketId);
    return false;
}
var sellQuantity = Math.Max(0, currentPosition - moonBagThreshold);
```

---

### CRITICAL-002: Trailing Stop Order Uses Stale Position Size

**Risk Level:** CRITICAL
**Category:** Trading Logic
**Location:** `TrailingStopService.cs:395-398`
**Financial Impact:** Stop order quantity may be incorrect, either under-protecting or over-selling

**Problem:**
The `UpdateTrailingStopOrderAsync` method also uses `MaxPositionAchieved` instead of actual current position:

```csharp
var sellQuantity = status.MaxPositionAchieved - moonBagThreshold;
```

This means if the position has changed through grid trading, the stop order quantity is wrong. When the stop triggers, it may:
- Sell more than available (order fails or partial fill)
- Sell less than intended (leaves non-moon-bag portion exposed)

**Fix:**
Same as CRITICAL-001 - fetch actual position from exchange.

---

### CRITICAL-003: Moon Bag Threshold Based on Max Position, Not Continuously Updated

**Risk Level:** HIGH
**Category:** Trading Logic
**Location:** `MoonBagManager.cs:537-539`
**Financial Impact:** Moon bag protection may lock incorrect quantity if max position tracking fails

**Problem:**
The moon bag locked quantity is recalculated when max position increases, but the calculation assumes `UpdateMaxPositionAsync` is called consistently. If this method is not called on every fill, the moon bag threshold becomes stale.

```csharp
// Recalculate moon bag threshold
var options = _config.MoonBag;
status.LockedQuantity = currentPositionSize * options.MoonBagPercentage;
```

However, `LockedQuantity` is only updated when `currentPositionSize > MaxPositionAchieved`. If position increases but the update call is missed, the locked quantity remains based on old max.

**Financial Impact:** Could protect wrong amount - either too little (loss of upside) or too much (blocks legitimate sales)

**Fix:** Add validation that `UpdateMaxPositionAsync` is called on every position change, or fetch max position from exchange order history.

---

### CRITICAL-004: No Validation of Position Direction Change

**Risk Level:** HIGH
**Category:** Trading Logic
**Location:** `MoonBagManager.cs:517-566`
**Financial Impact:** Moon bag could protect wrong direction, leading to amplified losses

**Problem:**
If a long position is closed and a short position is opened on the same market, the moon bag state persists with `IsLongPosition = true`. There is no check when updating max position to verify the position direction has not changed.

```csharp
public async Task<bool> UpdateMaxPositionAsync(int marketId, decimal currentPositionSize, CancellationToken ct = default)
{
    // ... NO CHECK FOR POSITION DIRECTION CHANGE ...
    if (currentPositionSize <= status.MaxPositionAchieved)
    {
        return false;
    }
    // ...
}
```

**Scenario:**
1. Long position established, moon bag initialized
2. Long position closed, short position opened
3. `UpdateMaxPositionAsync` called with new short size
4. Moon bag logic blocks "sells" (which are actually covering shorts)
5. Short position cannot be closed when market reverses

**Fix:**
```csharp
public async Task<bool> UpdateMaxPositionAsync(
    int marketId,
    decimal currentPositionSize,
    bool isLong,
    CancellationToken ct = default)
{
    // Check for direction change
    if (status.IsLongPosition != isLong)
    {
        await ResetMoonBagAsync(marketId, ct);
        // Re-initialize for new direction if enabled
        if (isLong || options.EnableShortMoonBag)
        {
            return await InitializeMoonBagAsync(...);
        }
    }
    // ... rest of logic ...
}
```

---

### CRITICAL-005: State Not Persisted - System Restart Loses All Protection

**Risk Level:** HIGH
**Category:** State Management
**Location:** `MoonBagManager.cs:25`, `TrailingStopService.cs:32`, `TrailingGridService.cs:23`
**Financial Impact:** Position left unprotected after any system restart

**Problem:**
All state is stored in `ConcurrentDictionary` in memory only:

```csharp
private readonly ConcurrentDictionary<int, MoonBagStatus> _moonBagStates = new();
private readonly ConcurrentDictionary<int, TrailingStopState> _stopStates = new();
private readonly ConcurrentDictionary<int, MarketTrailingState> _marketStates = new();
```

The specification (Section 7.2) explicitly requires:
> Persisted Data (survive restart):
> - Current moon bag state
> - Max position size achieved
> - High watermark price
> - Trailing stop price
> - Moon bag locked quantity

On restart, all moon bag protection is lost. Position may be sold without protection.

**Fix:** Implement state persistence to Redis (as noted in session context for Phase 8) or database. For now, add startup recovery logic that reconstructs state from exchange position history.

---

## HIGH RISK FINDINGS (Incorrect Trading Behavior)

### HIGH-001: Flash Spike Detection Only Checks Upward Movement

**Risk Level:** HIGH
**Category:** Trading Logic
**Location:** `TrailingGridService.cs:319-329`
**Financial Impact:** Does not protect against pump-and-dump manipulation

**Problem:**
Flash spike detection only checks for upward price spikes:

```csharp
// Check for upward spike (price increased significantly)
if (minPrice > 0)
{
    var increase = (currentPrice - minPrice) / minPrice;
    if (increase >= options.FlashSpikeThreshold)
    {
        // ... pause shift ...
    }
}
```

It does not check if the price then reverses, which is the dangerous scenario (pump-and-dump). The spec mentions:
> Flash spike reversal: Price returns to within 5% of pre-spike level within 15 min -> Discard spike from high watermark calculation

This is not implemented.

**Fix:** Add reversal detection and high watermark rollback logic.

---

### HIGH-002: High Watermark Updated During Flash Spike Period

**Risk Level:** HIGH
**Category:** Trading Logic
**Location:** `MoonBagManager.cs:158-199`
**Financial Impact:** Artificially high trailing stop that triggers prematurely on reversal

**Problem:**
The `UpdateHighWatermarkAsync` method does not check if a flash spike is active. Per spec Rule EC-SPIKE-001:
> IF price_increase_5min > 20%
> THEN suspend_high_watermark_updates(10_minutes)

But the code has no coordination between `TrailingGridService` (which detects flash spikes) and `MoonBagManager` (which updates high watermark).

**Financial Impact:** During a pump-and-dump, high watermark gets set to the spike peak. When price dumps, the trailing stop (even at 15% below high) triggers immediately, forcing a sale at the worst price.

**Fix:**
```csharp
public async Task<bool> UpdateHighWatermarkAsync(int marketId, decimal price, CancellationToken ct = default)
{
    // Check if flash spike is active - coordinate with TrailingGridService
    if (await _trailingGridService.IsFlashSpikeActiveAsync(marketId, ct))
    {
        _logger.LogDebug("High watermark update suspended during flash spike for market {MarketId}", marketId);
        return false;
    }
    // ... rest of logic ...
}
```

---

### HIGH-003: Trailing Stop Tier Comparison is Backwards

**Risk Level:** HIGH
**Category:** Trading Logic
**Location:** `TrailingStopService.cs:137`
**Financial Impact:** Trailing stop never tightens properly

**Problem:**
The tier comparison uses `>` but the enum values are likely ordered wrong for this comparison:

```csharp
// One-way tightening - only update if tighter
if (newTier > state.CurrentTier)
{
    state.CurrentTier = newTier;
```

Looking at `TrailingStopTier` enum (from context), if it's ordered `Standard=0, Tightened=1, Aggressive=2, Emergency=3`, then this is correct. However, the logging on line 142-143 shows:

```csharp
_logger.LogInformation(
    "Trailing stop tier tightened for market {MarketId}: {OldTier} -> {NewTier} (profit: {Profit:P1})",
    marketId, state.CurrentTier, newTier, status.CurrentProfitPercent);
```

This logs `state.CurrentTier` (old value) as `{OldTier}` but `state.CurrentTier` was just updated to `newTier`. This is a logging bug that suggests the logic wasn't thoroughly tested.

**Fix:** Capture old tier before updating:
```csharp
var oldTier = state.CurrentTier;
state.CurrentTier = newTier;
_logger.LogInformation(
    "Trailing stop tier tightened: {OldTier} -> {NewTier}",
    oldTier, newTier, ...);
```

---

### HIGH-004: Missing TRACKING -> TRAILING State Transition Trigger

**Risk Level:** HIGH
**Category:** State Machine
**Location:** `MoonBagManager.cs`
**Financial Impact:** Trailing stop may never activate

**Problem:**
Per the specification:
> TRACKING -> TRAILING: price > initial_grid * 1.10

But there is no code in `MoonBagManager` that triggers this transition. The `UpdateProfitPercentAsync` method handles warm-up completion but NOT the transition to TRAILING state.

The caller must explicitly call `TransitionStateAsync` to move from TRACKING to TRAILING, but there's no orchestration code visible that does this based on the 10% threshold.

**Financial Impact:** If the orchestration is not implemented elsewhere, the trailing stop never activates and positions are unprotected during the most critical phase (strong uptrend).

**Fix:** Add transition logic in `UpdateProfitPercentAsync` or create an orchestrator that checks the condition:
```csharp
if (status.State == MoonBagState.Tracking)
{
    var activationPrice = status.InitialGridUpperBound * (1 + options.TrailingStopActivationThreshold);
    if (currentPrice > activationPrice)
    {
        status.State = MoonBagState.Trailing;
        status.StateReason = $"Price {currentPrice} > activation threshold {activationPrice}";
    }
}
```

---

### HIGH-005: No Integration with Grid Lifecycle for Sell Order Blocking

**Risk Level:** HIGH
**Category:** Integration
**Location:** `MoonBagManager.cs:242-294`
**Financial Impact:** Moon bag protection is advisory only, not enforced

**Problem:**
`ShouldBlockSellOrderAsync` returns a boolean indicating if a sell should be blocked, but there's no evidence this method is called by the grid engine before placing sell orders.

The method exists, but without integration into the actual order placement flow, moon bag protection is never enforced.

**Financial Impact:** Entire moon bag could be sold through normal grid operations.

**Fix:** Ensure grid order placement calls `ShouldBlockSellOrderAsync` and respects the result:
```csharp
// In GridOrderService or equivalent
if (order.IsSell)
{
    var shouldBlock = await _moonBagManager.ShouldBlockSellOrderAsync(
        marketId, order.Quantity, currentPosition, ct);
    if (shouldBlock)
    {
        _logger.LogWarning("Sell order blocked by moon bag protection");
        return OrderResult.BlockedByMoonBag;
    }
}
```

---

## MEDIUM RISK FINDINGS (Suboptimal Behavior)

### MEDIUM-001: 50 MA Release Condition Not In Original Spec

**Risk Level:** MEDIUM
**Category:** Specification Deviation
**Location:** `MoonBagManager.cs:431-439`
**Financial Impact:** Release conditions harder to meet than specified

**Problem:**
The implementation adds a 50 MA check that's not in the original specification:

```csharp
// Also check 50 MA as additional confirmation
var ma50 = _indicatorService.CalculateSma(closePrices, 50);
if (currentPrice >= ma50)
{
    return false;
}
```

The spec only requires:
> IF (trend_state == STRONG_BEAR AND price < 200_day_MA...)

This makes release conditions harder to meet, which could be intentional conservatism but deviates from spec.

**Recommendation:** Document the deviation or remove the 50 MA check to match spec.

---

### MEDIUM-002: No Wick Filter Implementation

**Risk Level:** MEDIUM
**Category:** Missing Feature
**Location:** Spec Rule EC-SPIKE-002
**Financial Impact:** Manipulation via thin wicks could set false high watermarks

**Problem:**
Spec requires:
> IF candle_wick_percentage > 50% AND candle_duration < 1_minute
> THEN use_candle_body_high (not wick high) for trailing stop

This is not implemented. The code uses raw price for high watermark without wick filtering.

**Recommendation:** Implement wick detection when updating high watermark.

---

### MEDIUM-003: No Leverage Check in Moon Bag Mode

**Risk Level:** MEDIUM
**Category:** Missing Safety Check
**Location:** Spec Rule PF-LEV-001
**Financial Impact:** Over-leveraged moon bag could be liquidated

**Problem:**
Spec requires:
> IF moon_bag_mode == ACTIVE THEN max_leverage_allowed = MIN(3x, configured_max_leverage)

No leverage capping is implemented. The moon bag could be on a 10x position and get liquidated during a pullback.

**Recommendation:** Add leverage check and reduce if necessary when entering moon bag mode.

---

### MEDIUM-004: No Liquidation Distance Monitoring

**Risk Level:** MEDIUM
**Category:** Missing Safety Check
**Location:** Spec Rule PF-LEV-002
**Financial Impact:** Moon bag could be liquidated without warning

**Problem:**
Spec requires alerting when liquidation price is within 20% of current price. This is not implemented.

**Recommendation:** Add liquidation distance monitoring with alerts.

---

### MEDIUM-005: Release Is Immediate, Not Gradual

**Risk Level:** MEDIUM
**Category:** Specification Deviation
**Location:** `MoonBagManager.cs:454-514`
**Financial Impact:** Could trigger panic selling if released all at once

**Problem:**
Spec requires:
> allow_sale_of(moon_bag_quantity * 0.50) per 24 hours

But `ApproveReleaseAsync` immediately transitions to Released state, allowing full moon bag sale without gradual release enforcement.

**Recommendation:** Implement 50% per 24 hour release limit.

---

### MEDIUM-006: Trailing Stop Order Type May Not Be Supported

**Risk Level:** MEDIUM
**Category:** Exchange Integration
**Location:** `TrailingStopService.cs:420`
**Financial Impact:** Stop order may fail to place

**Problem:**
The code uses `OrderType.StopLossLimit` but it's unclear if Lighter DEX supports this order type. Per the spec note:
> Lighter DEX does not have native trailing stops

If StopLossLimit is also unsupported, all trailing stop orders will fail.

**Recommendation:** Verify Lighter DEX order type support. If not supported, implement software-only monitoring with market orders on trigger.

---

## RECOMMENDATIONS

### REC-001: Add Position Validation Before Order Placement

Before any order execution in `ExecuteTrailingStopAsync` or `UpdateTrailingStopOrderAsync`:
1. Fetch actual position from exchange
2. Validate position direction matches expected
3. Validate position size >= intended sell quantity + moon bag threshold

### REC-002: Implement State Persistence

Priority: HIGH - Should be done before production use.
Options:
- Redis with atomic operations
- Database with transaction support
- File-based with WAL for crash recovery

### REC-003: Add Health Check for Moon Bag State

Implement a periodic validation that:
- Compares local state with exchange position
- Verifies stop orders are still active
- Alerts on state drift

### REC-004: Add Integration Tests

Test scenarios:
- Position changes during trailing stop execution
- System restart recovery
- Flash spike followed by reversal
- Direction change (long to short)

### REC-005: Implement Flash Crash Override for Trailing Stop

Per spec Rule TS-FC-001:
> IF flash_crash_detected AND trailing_stop_active
> THEN pause_trailing_stop_execution(15_minutes)

This interaction is not implemented.

### REC-006: Add Funding Rate Monitoring

Per spec Rules PF-FUND-001 through PF-FUND-003, implement funding rate impact analysis for long-held moon bags.

---

## AUDIT SUMMARY

```
===============================================
AUDIT SUMMARY
===============================================
Total Findings: 17

CRITICAL Risk: 5 (BLOCKING DEPLOYMENT)
  - CRITICAL-001: Position size not fetched from exchange
  - CRITICAL-002: Trailing stop uses stale position
  - CRITICAL-003: Moon bag threshold may become stale
  - CRITICAL-004: No position direction change validation
  - CRITICAL-005: State not persisted (acknowledged for Phase 8)

HIGH Risk: 5 (INCORRECT BEHAVIOR)
  - HIGH-001: Flash spike only detects upward movement
  - HIGH-002: High watermark updated during flash spike
  - HIGH-003: Tier comparison logging bug
  - HIGH-004: Missing TRACKING->TRAILING transition
  - HIGH-005: Sell order blocking not integrated

MEDIUM Risk: 6
  - MEDIUM-001: Extra 50 MA release condition
  - MEDIUM-002: No wick filter
  - MEDIUM-003: No leverage cap
  - MEDIUM-004: No liquidation monitoring
  - MEDIUM-005: Release is immediate not gradual
  - MEDIUM-006: Stop order type support unclear

RECOMMENDATIONS: 6

Overall Verdict: FAIL

Deployment Recommendation: DO NOT DEPLOY TO PRODUCTION
===============================================
```

### Blocking Issues That Must Be Resolved:

1. **CRITICAL-001 & CRITICAL-002**: The system uses `MaxPositionAchieved` instead of actual current position. This WILL cause order failures or incorrect quantities in production.

2. **HIGH-004**: The TRACKING -> TRAILING state transition has no implementation. Without this, trailing stops never activate.

3. **HIGH-005**: Moon bag sell blocking is not integrated with the order placement system. Moon bags CAN be sold.

### Conditional Pass Requirements:

To achieve CONDITIONAL PASS:
1. Fix CRITICAL-001 and CRITICAL-002 (position fetching)
2. Implement HIGH-004 (state transition trigger)
3. Integrate HIGH-005 (sell order blocking)
4. Add flash spike -> high watermark coordination (HIGH-002)

CRITICAL-005 (persistence) is acknowledged as Phase 8 work, but should have a workaround for graceful recovery.

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | trading-bot-auditor | Initial trading logic audit |
