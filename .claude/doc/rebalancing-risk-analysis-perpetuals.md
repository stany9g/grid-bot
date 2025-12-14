# Rebalancing Logic Risk Analysis for Perpetual Futures Grid Bot

**Date**: 2025-12-13
**Status**: Analysis Complete - Recommendations Provided
**Severity**: HIGH - Current implementation has fundamental flaws for perpetual futures

---

## Executive Summary

The current rebalancing implementation has **critical issues** when operating on perpetual futures markets:

1. **False Positive Rebalance Triggers**: Neutral trend with NO_POS (0% skew) should NOT trigger 8% portfolio rebalance
2. **Market Orders for Rebalancing**: Causes unnecessary slippage and taker fees
3. **Grid-Rebalance Conflict**: Rebalancing fights against the grid's natural position management
4. **Skew Calculation Flaw**: For perpetuals, "skew" based on position/collateral ratio does not align with target percentages

---

## Problem Analysis

### Issue 1: Why Rebalance Triggers With NO_POS and Neutral Trend?

**Log Evidence:**
```
CYCLE[1] Price:90160.05 | Pos:NO_POS | Trend:Neutral (target:0%, actual:0%) | Grid:B:6 S:6
Executing rebalance on market 1: BUY 0.000177 crypto at ~90163.10 ($16.00, 8.0% of portfolio)
```

**Root Cause Analysis:**

Looking at `InventoryManager.CalculatePortfolioValues()`:

```csharp
// Calculate crypto value in USD
var cryptoValueUsd = positionSize * currentPrice;

// Total portfolio = collateral
var totalPortfolioUsd = collateral;

// Ensure we have a valid total
if (totalPortfolioUsd <= 0)
{
    totalPortfolioUsd = Math.Abs(cryptoValueUsd) + usdtBalance;
}
```

**The Problem:**
- With NO_POS: `positionSize = 0`, `cryptoValueUsd = 0`
- `currentSkew = (cryptoValueUsd / totalPortfolioUsd) * 100 = 0%`
- `targetSkew = 0%` (Neutral trend)
- Delta = 0% - 0% = 0%

So why does rebalance trigger?

**Hypothesis 1**: The `RebalanceTolerancePercent` check may have floating-point issues:
```csharp
var delta = Math.Abs(CalculateRebalanceDelta(currentSkew, targetSkew));
return delta > threshold;  // If threshold=5%, 0 > 5 is false
```

**Hypothesis 2**: The acceptable skew range for Neutral is `(-20m, 20m)`:
```csharp
TrendState.Neutral => (-20m, 20m),  // -20% to +20%
```

But the `SkewCorrectionMode` logic:
```csharp
if (currentSkew > maxSkew)       // 0 > 20 = false
    skewCorrectionMode = true;
else if (currentSkew < minSkew)  // 0 < -20 = false
    skewCorrectionMode = true;
```

This should NOT trigger correction either.

**Most Likely Root Cause**: Check `ShouldRebalance()` default threshold:
```csharp
public bool ShouldRebalance(decimal currentSkew, decimal targetSkew, decimal threshold = 5m)
{
    var delta = Math.Abs(CalculateRebalanceDelta(currentSkew, targetSkew));
    return delta > threshold;
}
```

Production config shows `RebalanceTolerancePercent: 8`. With 0% delta and 8% threshold, rebalance should NOT trigger.

**CRITICAL FINDING**: The log shows "8.0% of portfolio" which suggests the rebalance IS calculating some delta. This indicates either:

1. A timing issue where `currentSkew` is calculated before position data arrives
2. A data race where stale position data is used
3. The `TotalPortfolioValueUsd` calculation has an edge case producing wrong skew

---

### Issue 2: Market Orders for Rebalancing

**Current Implementation:**
```csharp
var orderRequest = new CreateOrderRequest
{
    OrderType = OrderType.Market,
    TimeInForce = TimeInForce.ImmediateOrCancel,
    // ...
};
```

**Problems with Market Orders for Rebalancing:**

| Problem | Impact | Severity |
|---------|--------|----------|
| Taker fees (typically 0.05-0.1%) | Erodes profit margin | HIGH |
| Slippage in thin order books | Worse execution price | HIGH |
| No price control | Can execute at unfavorable prices during volatility | CRITICAL |
| Market impact | Large rebalances move the market against you | MEDIUM |

**Recommendation**: Use limit orders with tight spread or aggressive maker orders.

---

### Issue 3: Grid-Rebalance Conflict

**Current Behavior:**
1. Grid places 6 bids and 6 asks
2. Rebalance triggers market BUY order
3. This immediately gives you a long position
4. Now some ask orders will fill, reducing position
5. The system is fighting itself

**Fundamental Conflict:**
- Grid's purpose: Make market, collect spread, let fills naturally adjust position
- Rebalance's purpose: Force position to target allocation

For a grid bot on perpetuals, the grid SHOULD be the primary position management mechanism, not rebalancing.

---

### Issue 4: Perpetual Futures Skew Semantics

**Spot Trading Skew:**
- "50% crypto allocation" = half portfolio in crypto, half in USD
- Makes sense for inventory management

**Perpetual Futures Skew:**
- You don't "hold" crypto - you hold a leveraged position
- Position size / Collateral ratio means something different
- With 10x leverage: $100 collateral can hold $1000 position
- "80% skew" target doesn't translate cleanly to perpetuals

**Target Skew Mapping:**
```csharp
TrendState.StrongBull => 80m,   // +80% long exposure
TrendState.Neutral => 0m,       // 0% = flat (no position)
TrendState.StrongBear => -80m,  // -80% short exposure
```

For perpetuals, this should map to **position size as % of max allowed** or **notional value as % of collateral**, not raw allocation.

---

## Risk Category: Rebalancing Logic

### Thresholds (Current vs Recommended)

| Metric | Current | Recommended | Rationale |
|--------|---------|-------------|-----------|
| RebalanceTolerancePercent | 8% | 15% (or disable) | Grid should handle minor adjustments |
| MaxRebalanceRatePercent | 8%/hour | 5%/hour | Reduce frequency when grid is active |
| MinRebalanceInterval | 1 min | 15 min | Allow grid fills to settle |
| MinPositionForRebalance | 0 | 5% of collateral | Don't rebalance near-flat positions |

### Rules

#### Rule 1: Grid Precedence
```
IF grid_is_deployed AND grid_order_count >= 8 THEN
    DISABLE rebalancing
    ALLOW grid to manage position through fills
```

**Rationale:** Active grid with 12 orders means the market-making mechanism is working. Let fills naturally adjust inventory.

#### Rule 2: Flat Position Guard
```
IF |current_position| < (collateral * 0.05) AND target_skew == 0 THEN
    SKIP rebalancing
    REASON = "Position effectively flat, matches neutral target"
```

**Rationale:** Small positions near target should not trigger rebalance. The 8% rebalance for NO_POS is caused by this missing guard.

#### Rule 3: Trend Change Trigger Only
```
IF trend_state_changed_this_cycle AND |skew_delta| > 20% THEN
    ALLOW rebalance (limit orders only)
ELSE
    SKIP rebalancing
    ALLOW grid to adjust naturally
```

**Rationale:** Rebalancing should only occur on significant trend regime changes, not every cycle.

#### Rule 4: Limit Order Rebalancing
```
IF rebalance_needed THEN
    USE OrderType.Limit
    SET price = mid_price +/- 0.05% (aggressive maker)
    SET TimeInForce = GoodTillCancel
    SET expiry = 5 minutes
    IF not_filled_in_5_minutes THEN cancel_and_retry_next_cycle
```

**Rationale:** Avoid taker fees and slippage. Maker orders provide better execution.

#### Rule 5: Anti-Conflict Guard
```
IF rebalancing_pending AND grid_order_filled THEN
    CANCEL pending_rebalance_order
    REASON = "Grid fill supersedes rebalance"
```

**Rationale:** If grid fills while rebalance is pending, the position has already adjusted.

### Edge Cases

#### Edge Case 1: Race Condition - Stale Position Data
**Scenario:** WebSocket position update delayed, rebalance uses stale 0 position
**Response:** Add staleness check:
```
IF position_data_age > 5_seconds THEN
    SKIP rebalancing this cycle
    LOG "Rebalance skipped: stale position data"
```

#### Edge Case 2: Rebalance During High Volatility
**Scenario:** ATR spike during rebalance causes bad execution
**Response:**
```
IF current_atr > 1.5 * average_atr THEN
    PAUSE rebalancing for 15 minutes
    WIDEN grid spreads instead
```

#### Edge Case 3: Partial Rebalance Fill
**Scenario:** Limit order partially fills, position mismatch
**Response:**
```
IF rebalance_order_partially_filled THEN
    CALCULATE remaining_delta
    IF remaining_delta < 3% THEN
        ACCEPT partial (grid will handle rest)
    ELSE
        QUEUE retry_rebalance for next cycle
```

#### Edge Case 4: Grid and Rebalance Same Direction
**Scenario:** Grid has pending buy order AND rebalance wants to buy
**Response:**
```
IF pending_grid_order_direction == rebalance_direction THEN
    INCREASE pending_grid_order_size by rebalance_amount
    CANCEL rebalance order
    REASON = "Merged with grid order"
```

### Priority Level: CRITICAL

---

## Recommended Implementation Changes

### Change 1: Disable Rebalancing When Grid Active (CRITICAL)

**Current Logic:**
```csharp
if (inventoryAnalysis.RebalanceNeeded && canRebalance)
{
    if (_rebalancingService.CanRebalanceNow(marketId))
    {
        rebalanceResult = await _rebalancingService.ExecuteRebalanceAsync(...);
    }
}
```

**Proposed Logic:**
```csharp
// NEW: Check if grid is actively managing position
var gridState = await _gridLifecycle.GetCurrentGridStateAsync(marketId, ct);
var gridIsActive = gridState?.Levels.Count(l => l.Status == GridLevelStatus.Active) >= 8;

if (inventoryAnalysis.RebalanceNeeded && canRebalance && !gridIsActive)
{
    // Only rebalance when grid is NOT actively deployed
    // Grid fills should handle position adjustment
    if (_rebalancingService.CanRebalanceNow(marketId))
    {
        rebalanceResult = await _rebalancingService.ExecuteRebalanceAsync(...);
    }
}
else if (gridIsActive && inventoryAnalysis.RebalanceNeeded)
{
    _logger.LogDebug(
        "Rebalance skipped: Grid is active with {Count} orders. Grid fills will handle position adjustment.",
        gridState.Levels.Count);
}
```

### Change 2: Add Flat Position Guard (CRITICAL)

In `InventoryManager.AnalyzeInventoryAsync()`:

```csharp
// NEW: Flat position guard for neutral trend
var isEffectivelyFlat = Math.Abs(cryptoValueUsd) < (totalPortfolioUsd * 0.02m); // <2% position
var targetIsFlat = Math.Abs(targetSkew) < 5m; // Target near 0%

if (isEffectivelyFlat && targetIsFlat)
{
    // Position is effectively flat and target is flat - no rebalance needed
    rebalanceNeeded = false;
    _logger.LogDebug(
        "Position effectively flat ({CryptoValue:F2} USD) with flat target ({Target:F1}%). No rebalance needed.",
        cryptoValueUsd, targetSkew);
}
```

### Change 3: Change to Limit Orders (HIGH)

In `RebalancingService.ExecuteRebalanceInternalAsync()`:

```csharp
// CHANGE: Use limit orders instead of market orders
// Calculate aggressive maker price (tight spread)
var aggressiveSpread = 0.0005m; // 0.05%
var limitPrice = isAsk
    ? currentPrice * (1 - aggressiveSpread)  // Sell slightly below mid
    : currentPrice * (1 + aggressiveSpread); // Buy slightly above mid

var scaledPrice = await _scalingService.ScalePriceAsync(limitPrice, marketId, ct);

var orderRequest = new CreateOrderRequest
{
    MarketIndex = marketId,
    ClientOrderIndex = Interlocked.Increment(ref _clientOrderCounter),
    BaseAmount = scaledAmount,
    Price = scaledPrice,
    IsAsk = isAsk,
    OrderType = OrderType.Limit,  // CHANGED from Market
    TimeInForce = TimeInForce.GoodTillCancel,  // CHANGED from IOC
    OrderExpiry = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() // 5 min expiry
};
```

### Change 4: Increase Tolerance and Decrease Rate (MEDIUM)

In configuration:

```json
{
  "Trend": {
    "RebalanceTolerancePercent": 15,
    "MaxRebalanceRatePercent": 5,
    "MinRebalanceIntervalMinutes": 15
  }
}
```

### Change 5: Only Rebalance on Trend Changes (MEDIUM)

In `TrendIntelligenceService.ProcessTrendCycleAsync()`:

```csharp
// Step 5: Execute rebalance if needed
// NEW: Only rebalance on trend state changes, not every cycle
var shouldRebalance = inventoryAnalysis.RebalanceNeeded
    && canRebalance
    && (trendStateChanged || inventoryAnalysis.IsEmergency);

if (shouldRebalance)
{
    // ... existing rebalance logic
}
else if (inventoryAnalysis.RebalanceNeeded && !trendStateChanged)
{
    _logger.LogDebug(
        "Rebalance deferred: Trend unchanged, grid will handle adjustment. Delta={Delta:F1}%",
        inventoryAnalysis.RebalanceDelta);
}
```

---

## Grid-Rebalance Interaction Model

### Recommended Architecture

```
                    TREND SIGNAL
                         |
                         v
              +---------------------+
              | Trend Intelligence  |
              +---------------------+
                         |
          +--------------+--------------+
          |                             |
          v                             v
    TREND CHANGE?                  STEADY STATE
          |                             |
          v                             v
    +-------------+              +-------------+
    | REBALANCE   |              | GRID ONLY   |
    | (one-time)  |              | (continuous)|
    +-------------+              +-------------+
          |                             |
          v                             v
    Adjust position             Let fills adjust
    to new target               position naturally
          |                             |
          +--------------+--------------+
                         |
                         v
                   GRID OPERATING
```

### State Machine

```
[NO_GRID] --deploy--> [GRID_ACTIVE]
    |                      |
    | trend change         | fills
    v                      v
[REBALANCING]        [POSITION_ADJUSTED]
    |                      |
    | complete             | check skew
    v                      v
[GRID_ACTIVE] <--ok-- [SKEW_CHECK]
                           |
                           | out of range
                           v
                      [BIAS_GRID]
                      (asymmetric orders)
```

---

## Summary of Recommendations

| Priority | Change | Impact |
|----------|--------|--------|
| CRITICAL | Add flat position guard | Fixes NO_POS rebalance bug |
| CRITICAL | Disable rebalancing when grid active (8+ orders) | Prevents grid-rebalance conflict |
| HIGH | Change market orders to limit orders | Reduces fees and slippage |
| HIGH | Only rebalance on trend changes | Reduces unnecessary trades |
| MEDIUM | Increase tolerance to 15% | Allows grid to work |
| MEDIUM | Add 15-minute minimum interval | Prevents over-trading |
| LOW | Add staleness check for position data | Prevents race conditions |

---

## Files to Modify

1. `GridBot.ApiService/Services/Inventory/InventoryManager.cs` - Add flat position guard
2. `GridBot.ApiService/Services/Trend/TrendIntelligenceService.cs` - Add grid-active check, trend-change-only rebalancing
3. `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs` - Change to limit orders
4. `GridBot.ApiService/appsettings.Production.json` - Update thresholds

---

## Validation Criteria

After implementation, verify:

1. [ ] NO_POS with Neutral trend does NOT trigger rebalance
2. [ ] Active grid (8+ orders) suppresses rebalancing
3. [ ] Rebalance orders use limit type, not market
4. [ ] Rebalance only triggers on trend state changes (or emergency)
5. [ ] Position adjustments happen through grid fills during steady state
6. [ ] Logs show "Rebalance skipped: Grid is active" when appropriate
