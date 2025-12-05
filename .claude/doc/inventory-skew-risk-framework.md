# Inventory Skew Risk Management Framework v2.0

## Document Purpose
This document defines the risk management framework for the ALTE grid trading bot.

**CORE PRINCIPLE: THE BOT NEVER HALTS. EVER.**

---

## Why Never Halt?

### The Fatal Flaw of Halting
```
Scenario: Bot halts with 80% crypto position
Result:
  - Position still exists on exchange
  - Market crashes 20%
  - Bot is "paused" - doing nothing
  - User loses money while bot watches
```

**Halting with an open position is MORE DANGEROUS than any condition that triggered the halt.**

### The Correct Philosophy
> "The main loop should ALWAYS be checking and adjusting based on the rules"

Even in the worst conditions, the bot must:
1. Monitor the position
2. Manage risk (trailing stops, reduce exposure)
3. Adjust orders based on market
4. Protect capital actively

---

## Section 1: Operational States (No Halt State)

### State Machine

| State | Description | Actions Allowed |
|-------|-------------|-----------------|
| **Active** | Full operation | All grid operations, rebalancing |
| **Degraded_Bootstrap** | Starting fresh, building position | Buy-only grid, conservative sizes |
| **Degraded_SkewCorrection** | Skew outside target range | Asymmetric grid favoring correction |
| **Degraded_HighVolatility** | Volatility spike | Wider spreads, smaller orders |
| **Degraded_LowLiquidity** | Order book thin | Wider spreads, fewer levels |
| **Degraded_ProtectiveMode** | Loss approaching limits | Close-only mode, reduce position |
| **Recovering** | Post-event recovery | Gradual capacity increase |

**NOTE: There is NO "Halted", "Paused", or "Stopped" state.**

---

## Section 2: What Happens in "Emergency" Scenarios

### Old (Wrong): Halt
### New (Correct): Protective Mode

| Scenario | Old Response | New Response |
|----------|--------------|--------------|
| Loss limit approaching | HALT | Enter Protective Mode: reduce position, no new buys |
| Flash crash detected | HALT | Widen spreads 3x, enable trailing stops, monitor closely |
| API errors | HALT | Use cached data, reduce order frequency, retry with backoff |
| Extreme volatility | HALT | Pause NEW orders, keep existing, monitor for fills |
| Margin warning | HALT | Reduce position immediately, cancel distant orders |

### Protective Mode Rules

```
WHEN entering Protective Mode:
  1. KEEP the decision loop running (always!)
  2. STOP placing new grid orders
  3. ENABLE aggressive trailing stops on position
  4. REDUCE position size if market moves against us
  5. MONITOR continuously for recovery conditions
  6. LOG everything for post-analysis

NEVER:
  - Stop monitoring the market
  - Stop checking the position
  - Stop responding to price changes
  - "Pause" and wait for human intervention
```

---

## Section 3: Inventory Skew Management

### Remove MaxSkewPercent Halt Check

```csharp
// DELETE THIS ENTIRELY:
var haltRequired = currentSkew > trendOptions.MaxSkewPercent || usdtAllocation > trendOptions.MaxSkewPercent;
```

### Replace With: Skew Correction Mode

```
IF current_skew deviates from target:
  - Calculate correction direction (need more crypto OR more USDT)
  - Bias grid towards correction
  - Continue operating normally
  - Skew will naturally correct as orders fill
```

### Trend-Based Target Skew Ranges

| Trend State | Target Crypto | Acceptable Range | Correction Trigger |
|-------------|---------------|------------------|-------------------|
| StrongBull | 80% | 60-95% | Outside range |
| MildBull | 65% | 50-85% | Outside range |
| Neutral | 50% | 35-65% | Outside range |
| MildBear | 35% | 15-50% | Outside range |
| StrongBear | 20% | 5-40% | Outside range |

### Skew Correction Behavior

```
IF crypto_skew > acceptable_max THEN
  State = Degraded_SkewCorrection
  buy_orders = reduce by 75%
  sell_orders = increase by 50%
  // Grid naturally sells more, buys less
  // Skew corrects over time without halting

IF crypto_skew < acceptable_min THEN
  State = Degraded_SkewCorrection
  buy_orders = increase by 50%
  sell_orders = reduce by 75%
  // Grid naturally buys more, sells less
```

---

## Section 4: Bootstrap Mode (Fresh Start)

### Entry Condition
```
position_size == 0 AND collateral > 0
```

### Behavior
```
State = Degraded_Bootstrap
grid_type = "buy_only"  // Nothing to sell
order_size = normal_size * 0.5  // Conservative
price_range = current_price down to (current_price - ATR * 3)
```

### Exit Condition
```
position_size >= min_threshold (e.g., 10% of target)
THEN: Transition to Active with normal grid
```

### Why This Works
- 100% USDT is a valid starting state
- Bot places buy orders and waits for fills
- As fills occur, position builds naturally
- No halt, no intervention needed

---

## Section 5: Operational Capacity System

Instead of binary halt/run, use capacity percentage:

### Capacity Levels

| Capacity | Order Count | Order Size | Spread Width | Description |
|----------|-------------|------------|--------------|-------------|
| 100% | Full | Full | Normal | Active state |
| 75% | 75% | 75% | 1.25x | Minor degradation |
| 50% | 50% | 50% | 1.5x | Moderate degradation |
| 25% | 25% | 25% | 2x | Significant degradation |
| 10% | 10% | 10% | 3x | Minimal - protective mode |

**Capacity NEVER goes to 0%.** Even at 10%, the bot is:
- Monitoring market
- Managing position with trailing stops
- Ready to act on major moves

### Capacity Calculation

```
base_capacity = 100%

// Subtract for each condition
IF high_volatility: capacity -= 25%
IF low_liquidity: capacity -= 25%
IF skew_deviation > 20%: capacity -= 25%
IF loss_approaching_limit: capacity -= 50%
IF api_errors_recent: capacity -= 25%

// Minimum floor
capacity = max(capacity, 10%)
```

---

## Section 6: Edge Cases

### EC-001: Position Fully Closed Unexpectedly
```
Detection: position went from X% to 0%
Response:
  1. Check if intentional (stop hit) or liquidation
  2. IF liquidation:
     - Enter Protective Mode at 10% capacity
     - Wide spreads, small orders
     - Wait for market stabilization
  3. IF intentional:
     - Check current trend
     - Enter Bootstrap mode to rebuild if appropriate
```

### EC-002: All Orders Cancelled
```
Detection: active_orders == 0 AND position exists
Response:
  1. Immediately rebuild grid around current price
  2. Log event for analysis
  3. Continue normal operation
```

### EC-003: Price Gaps Beyond Grid
```
Detection: price moved > 2x grid_spacing from nearest order
Response:
  1. Cancel stale orders
  2. Recenter grid at current price
  3. Widen spreads temporarily (volatility protection)
```

### EC-004: API Errors
```
Detection: consecutive_api_failures > 3
Response:
  1. Use cached market data (if recent)
  2. Reduce order update frequency
  3. Exponential backoff on retries
  4. CONTINUE monitoring position
  5. NEVER stop the decision loop
```

---

## Section 7: Decision Loop Specification

```
EVERY cycle_interval (default: 5 seconds):

  1. FETCH market data
     - IF fails: use cache, reduce capacity

  2. CHECK position status
     - Size, unrealized PnL, liquidation price

  3. CHECK open orders
     - Count, prices, potential fills

  4. ANALYZE conditions
     - Volatility (ATR)
     - Liquidity (order book depth)
     - Trend (MA signals)
     - Inventory skew

  5. CALCULATE operational capacity
     - Apply all degradation factors
     - Never below 10%

  6. DETERMINE state
     - Active, Bootstrap, SkewCorrection, etc.

  7. ADJUST grid
     - Based on state and capacity
     - Add/remove/modify orders

  8. APPLY risk management
     - Update trailing stops
     - Check loss limits
     - Adjust exposure if needed

  9. LOG cycle metrics
     - State, capacity, orders, position

  10. REPEAT (never stop)
```

---

## Section 8: Configuration

```json
{
  "OperationalCapacity": {
    "MinimumCapacity": 10,
    "VolatilityReduction": 25,
    "LiquidityReduction": 25,
    "SkewDeviationReduction": 25,
    "LossApproachingReduction": 50,
    "ApiErrorReduction": 25
  },
  "SkewLimits": {
    "StrongBull": { "Target": 80, "Min": 60, "Max": 95 },
    "MildBull": { "Target": 65, "Min": 50, "Max": 85 },
    "Neutral": { "Target": 50, "Min": 35, "Max": 65 },
    "MildBear": { "Target": 35, "Min": 15, "Max": 50 },
    "StrongBear": { "Target": 20, "Min": 5, "Max": 40 }
  },
  "Bootstrap": {
    "OrderSizeMultiplier": 0.5,
    "MinPositionToExit": 0.1
  },
  "ProtectiveMode": {
    "TrailingStopEnabled": true,
    "TrailingStopPercent": 2.0,
    "MaxPositionReductionPerCycle": 5
  }
}
```

---

## Section 9: Implementation Checklist

### Remove
- [ ] `HaltRequired` property from `InventoryAnalysis`
- [ ] `TradingState.Paused` transitions for skew violations
- [ ] `TradingState.Halted` state entirely (if exists)
- [ ] `MaxSkewPercent` config (or repurpose as soft limit)

### Add
- [ ] `OperationalCapacity` service/calculator
- [ ] `TradingState.Degraded_*` states
- [ ] `SkewCorrectionMode` property to `InventoryAnalysis`
- [ ] Capacity-based order count/size scaling in `GridOrderManager`
- [ ] Bootstrap mode detection in `InventoryManager`

### Modify
- [ ] `TrendIntelligenceService.ProcessTrendCycleAsync` - remove halt logic
- [ ] `InventoryManager.AnalyzeInventoryAsync` - return correction mode instead of halt
- [ ] `TradingDecisionEngine` - apply capacity scaling to all operations
- [ ] `TradingBotHostedService` - ensure loop never stops

---

## Document Metadata

| Field | Value |
|-------|-------|
| Version | 2.0 |
| Updated | 2025-12-05 |
| Status | Approved |
| Core Principle | **NEVER HALT** |
