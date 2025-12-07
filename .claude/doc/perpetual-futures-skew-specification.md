# Perpetual Futures Skew/Inventory Management Specification

## Document Purpose
This document defines the correct behavior for the inventory/skew management system when trading perpetual futures, replacing the current spot-trading-based model that incorrectly handles short positions.

---

## 1. Core Problem Analysis

### Current (Broken) System

The existing implementation was designed for **spot trading** and fails fundamentally for perpetual futures:

```
Current calculation (WRONG for perpetuals):
cryptoAllocation = |position| * price / collateral * 100
                   ^^^^^^^^^
                   Uses Math.Abs - ignores position direction!
```

**Critical Flaw:** A short position of -0.1 BTC is treated identically to a long position of +0.1 BTC. This causes:

1. **Inverted exposure calculation:** Short -0.1 BTC shows as +10% crypto allocation when it should show -10%
2. **Incorrect skew correction signals:** When short in a bearish market (correct position), system signals "NeedLessCrypto" when the position is already optimal
3. **Wrong rebalancing direction:** System may try to "reduce crypto" by selling more when already short

### Fundamental Difference: Spot vs Perpetuals

| Aspect | Spot Trading | Perpetual Futures |
|--------|--------------|-------------------|
| Position range | 0% to 100% long only | -100% to +100% (short to long) |
| "No exposure" | Hold USDT (0% crypto) | Flat position (0 contracts) |
| Bearish stance | Reduce to 20% crypto | Short position (-80% exposure) |
| Maximum bearish | Cannot go below 0% | Can short up to leverage limit |
| Collateral usage | Buy/Sell changes balance | Margin locked, P&L changes balance |

---

## 2. New Skew Definition for Perpetuals

### Definition

**Signed Exposure Ratio (SER):**
```
skew = (position_size * mark_price) / collateral * 100

Where:
- position_size: Signed value (+long, -short)
- mark_price: Current mark/index price
- collateral: Available margin/equity
- Result: Percentage from -100% to +100%
```

### Examples

| Position | Mark Price | Collateral | Skew Calculation | Interpretation |
|----------|------------|------------|------------------|----------------|
| +0.1 BTC | $50,000 | $10,000 | +0.1 * 50000 / 10000 * 100 = **+50%** | Half long |
| -0.1 BTC | $50,000 | $10,000 | -0.1 * 50000 / 10000 * 100 = **-50%** | Half short |
| +0.2 BTC | $50,000 | $10,000 | +0.2 * 50000 / 10000 * 100 = **+100%** | Fully long (1x) |
| -0.16 BTC | $50,000 | $10,000 | -0.16 * 50000 / 10000 * 100 = **-80%** | Heavily short |
| 0 BTC | $50,000 | $10,000 | 0 * 50000 / 10000 * 100 = **0%** | Flat/Neutral |

### Valid Range

**Standard Range:** -100% to +100%
- -100% = Fully short at 1x leverage
- 0% = Flat (no position)
- +100% = Fully long at 1x leverage

**Extended Range (with leverage):**
- At 2x leverage: -200% to +200%
- At 5x leverage: -500% to +500%

**Recommendation:** Clamp target skew to -100% to +100% for safety (1x effective leverage), even if actual positions may exceed this during volatile moves.

---

## 3. Target Skew Based on Trend

### New Target Mapping

The key insight: In perpetual futures, **bearish = short exposure**, not just "reduced long exposure".

| Trend State | Target Skew | Interpretation |
|-------------|-------------|----------------|
| **StrongBull** | **+80%** | Heavy long exposure, capture upside |
| **MildBull** | **+50%** | Moderate long exposure |
| **Neutral** | **0%** | Flat - no directional bias |
| **MildBear** | **-50%** | Moderate short exposure |
| **StrongBear** | **-80%** | Heavy short exposure, profit from downside |

### Comparison with Old System

| Trend State | Old Target | New Target | Change |
|-------------|------------|------------|--------|
| StrongBull | 80% | +80% | Semantically same |
| MildBull | 70% | +50% | Reduced, more conservative |
| Neutral | 50% | 0% | **Fundamental change**: flat, not half-long |
| MildBear | 30% | -50% | Now short instead of reduced long |
| StrongBear | 20% | -80% | Now heavily short instead of minimal long |

### Rationale for Changes

1. **Neutral = Flat (0%):** In perpetual futures, "neutral" means no directional bet. Holding 50% long is not neutral - it's a bullish position.

2. **Bear = Short:** The entire advantage of perpetual futures is the ability to profit from downtrends. A bearish stance should be expressed as a short position, not merely reduced long exposure.

3. **Symmetric Range:** +80% for strong bull, -80% for strong bear provides symmetric risk/reward.

4. **Capital Efficiency:** With perpeutals, you don't need to hold the underlying asset. Collateral efficiency is maximized when flat or short in bear markets.

---

## 4. Acceptable Skew Ranges by Trend

Define tolerance bands around target to determine when correction is needed:

| Trend State | Target | Min Acceptable | Max Acceptable | Band Width |
|-------------|--------|----------------|----------------|------------|
| StrongBull | +80% | +60% | +95% | 35% |
| MildBull | +50% | +30% | +70% | 40% |
| Neutral | 0% | -20% | +20% | 40% |
| MildBear | -50% | -70% | -30% | 40% |
| StrongBear | -80% | -95% | -60% | 35% |

### Edge Cases

- **+95% cap:** Prevents over-leverage on long side during rallies
- **-95% cap:** Prevents over-leverage on short side during crashes
- **Neutral band:** Allows small directional positions without triggering correction

---

## 5. Skew Correction Logic

### Direction Determination

**New Correction Directions:**
```
enum SkewCorrectionDirection
{
    None,           // Within acceptable range
    IncreaseExposure,  // Need more long OR less short (buy/close short)
    ReduceExposure     // Need less long OR more short (sell/open short)
}
```

### Decision Matrix

| Current Skew | Target Skew | Delta | Action Required | Direction |
|--------------|-------------|-------|-----------------|-----------|
| +30% | +80% | +50% | Buy more | IncreaseExposure |
| +90% | +80% | -10% | Sell some | ReduceExposure |
| +50% | 0% | -50% | Sell to flat | ReduceExposure |
| 0% | -50% | -50% | Open short | ReduceExposure |
| -30% | -80% | -50% | Increase short | ReduceExposure |
| -90% | -50% | +40% | Reduce short | IncreaseExposure |
| **-50%** | **+80%** | **+130%** | **Close short + go long** | IncreaseExposure |

### Crossing Zero (Short-to-Long or Long-to-Short)

This is a critical capability for perpetual futures:

**Example: Trend flips from StrongBear to StrongBull**
- Current position: Short -0.08 BTC (skew = -80%)
- New target: +80% (long)
- Required delta: +160%
- Actions:
  1. Close entire short (skew moves from -80% to 0%)
  2. Open long position (skew moves from 0% to +80%)

**Rule:** When crossing zero, execute as TWO atomic operations if exchange supports it, or as a single flip order if available. Never leave position in undefined state.

---

## 6. Rebalancing Boundaries

### New Boundary System

**Hard Boundaries (Never Exceed):**
```
MinSkew = -100%  // Maximum 1x short leverage
MaxSkew = +100%  // Maximum 1x long leverage
```

**Soft Boundaries (Trigger correction):**
Based on trend state (see Section 4 table)

### Crossing-Zero Rules

1. **Allowed:** Yes, the system must support transitioning from long to short and vice versa
2. **Rate Limiting:** When crossing zero, max hourly rebalance rate applies to total movement
3. **Two-Phase Execution:** For large swings (e.g., -80% to +80%), may require multiple hourly cycles

### Emergency Boundaries

When emergency rebalance triggers (delta > 30%):

| Scenario | Emergency Action |
|----------|------------------|
| Skew > target + 30% | Force reduce to target + 15% |
| Skew < target - 30% | Force increase to target - 15% |
| Extreme: Skew > +100% | Force reduce to +95% immediately |
| Extreme: Skew < -100% | Force reduce to -95% immediately |

---

## 7. Grid Asymmetry for Perpetuals

### Grid Order Types

In perpetual futures, grid orders work differently:

| Current Position | Buy Order Effect | Sell Order Effect |
|------------------|------------------|-------------------|
| Long (+) | Increase long | Reduce long |
| Flat (0) | Open long | Open short |
| Short (-) | Reduce short | Increase short |

### Skew Correction via Grid Bias

**When IncreaseExposure needed (need more long / less short):**
```
Buy order size = BaseSize * 1.5  (increase buying)
Sell order size = BaseSize * 0.25 (reduce selling)
```

**When ReduceExposure needed (need less long / more short):**
```
Buy order size = BaseSize * 0.25 (reduce buying)
Sell order size = BaseSize * 1.5  (increase selling)
```

### Position-Aware Grid Placement

**Long Position (skew > 0):**
- Buys = Add to position (increase exposure)
- Sells = Take profit (reduce exposure)
- Grid biased: More sells above entry for profit taking

**Short Position (skew < 0):**
- Buys = Cover short (reduce exposure)
- Sells = Add to short (increase exposure)
- Grid biased: More buys below entry for profit taking

**Flat Position (skew = 0):**
- Symmetric grid
- First fill determines position direction
- Then switch to position-aware mode

---

## 8. Numerical Examples

### Example 1: Bullish Trend with Long Position

**Scenario:**
- Position: Long +0.05 BTC
- Mark Price: $40,000
- Collateral: $10,000
- Trend: StrongBull (target +80%)

**Calculation:**
```
Current Skew = (+0.05 * 40000) / 10000 * 100 = +20%
Target Skew = +80%
Delta = +80% - (+20%) = +60%
Acceptable Range = +60% to +95%
Current 20% < Min 60%
=> SkewCorrectionMode = true
=> Direction = IncreaseExposure
=> Action: Increase buy sizes, reduce sell sizes
```

### Example 2: Bearish Trend with Short Position

**Scenario:**
- Position: Short -0.08 BTC
- Mark Price: $40,000
- Collateral: $10,000
- Trend: StrongBear (target -80%)

**Calculation:**
```
Current Skew = (-0.08 * 40000) / 10000 * 100 = -32%
Target Skew = -80%
Delta = -80% - (-32%) = -48%
Acceptable Range = -95% to -60%
Current -32% > Max -60%
=> SkewCorrectionMode = true
=> Direction = ReduceExposure (need more short)
=> Action: Increase sell sizes, reduce buy sizes
```

### Example 3: Trend Flip (Short to Long)

**Scenario:**
- Position: Short -0.1 BTC
- Mark Price: $50,000
- Collateral: $10,000
- Previous Trend: StrongBear
- New Trend: StrongBull (target +80%)

**Calculation:**
```
Current Skew = (-0.1 * 50000) / 10000 * 100 = -50%
Target Skew = +80%
Delta = +80% - (-50%) = +130%

This exceeds normal rebalance limits!
=> Emergency rebalance triggered
=> Phase 1: Close short (move from -50% to 0%)
=> Phase 2: Open long (move from 0% to +80%)
=> May take multiple cycles due to hourly rate limit
```

### Example 4: Neutral Market

**Scenario:**
- Position: Long +0.02 BTC
- Mark Price: $45,000
- Collateral: $10,000
- Trend: Neutral (target 0%)

**Calculation:**
```
Current Skew = (+0.02 * 45000) / 10000 * 100 = +9%
Target Skew = 0%
Delta = 0% - (+9%) = -9%
Acceptable Range = -20% to +20%
Current +9% is within range
=> SkewCorrectionMode = false
=> Grid remains symmetric
=> Natural fills will drift toward flat
```

---

## 9. Implementation Changes Required

### Model Changes

**InventoryState.cs:**
```
- CryptoAllocation range: 0-100 -> -100 to +100
- UsdtAllocation: REMOVE (not applicable to perpetuals)
- CurrentSkew: Now signed value
- TargetSkew: Now can be negative
```

**InventoryAnalysis.cs:**
```
- AcceptableSkewMin: Can be negative
- AcceptableSkewMax: Can be negative
- CryptoValueUsd: Should be signed (negative for shorts)
- SkewDeviation: Absolute value of (current - target)
```

**SkewCorrectionDirection enum:**
```
Replace:
- NeedMoreCrypto
- NeedLessCrypto

With:
- IncreaseExposure  (buy / close short)
- ReduceExposure    (sell / open short)
```

**RebalanceDirection enum:**
```
Replace:
- BuyCrypto
- SellCrypto

With:
- IncreasePosition (buy for long, cover for short)
- DecreasePosition (sell for long, add for short)
```

### Calculation Changes

**InventoryManager.CalculatePortfolioValues:**
```
OLD: cryptoValueUsd = Math.Abs(positionSize) * currentPrice
NEW: cryptoValueUsd = positionSize * currentPrice  // Preserve sign!
```

**InventoryState.GetTargetSkewForTrend:**
```
OLD:
  StrongBull => 80m
  MildBull => 70m
  Neutral => 50m
  MildBear => 30m
  StrongBear => 20m

NEW:
  StrongBull => +80m
  MildBull => +50m
  Neutral => 0m
  MildBear => -50m
  StrongBear => -80m
```

**GetAcceptableSkewRange:**
```
NEW:
  StrongBull => (+60m, +95m)
  MildBull => (+30m, +70m)
  Neutral => (-20m, +20m)
  MildBear => (-70m, -30m)
  StrongBear => (-95m, -60m)
```

### Grid Logic Changes

**GetSkewCorrectionMultipliers:**
```
OLD: Based on current - target (spot logic)
NEW: Based on signed delta and position direction

If delta > 0 (need to increase exposure):
  - If flat or long: buy more, sell less
  - If short: cover more aggressively

If delta < 0 (need to decrease exposure):
  - If flat or short: sell more, buy less
  - If long: take profit more aggressively
```

---

## 10. Edge Cases and Safety Rules

### EC-001: Insufficient Margin for Target Position
**Scenario:** Target requires larger position than margin allows
**Response:**
- Calculate maximum safe position at 80% of available margin
- Set effective target to achievable level
- Log warning about constrained target

### EC-002: Rapid Trend Oscillation
**Scenario:** Trend flips multiple times in short period
**Response:**
- Implement trend change cooldown (minimum 15 minutes between target changes)
- If oscillating, default to Neutral (0%) until stable

### EC-003: Extreme Market Move During Rebalance
**Scenario:** Price moves >5% while executing rebalance order
**Response:**
- Cancel pending rebalance orders
- Recalculate skew with new price
- Re-evaluate rebalance need

### EC-004: Liquidation Risk
**Scenario:** Current position approaching liquidation price
**Response:**
- Override all other rules
- Set target to direction that reduces position
- Emergency rate limit bypass allowed

### EC-005: Bootstrap Mode (No Position)
**Scenario:** Starting fresh with no position
**Response:**
- Not applicable for perpetuals (unlike spot)
- Simply use symmetric grid
- First fill establishes direction

---

## 11. Priority Rules

1. **Liquidation Prevention** - Always highest priority, overrides all others
2. **Hard Boundaries (-100% to +100%)** - Never exceed 1x leverage
3. **Emergency Rebalance (>30% delta)** - Immediate action required
4. **Trend-Based Target** - Normal operation
5. **Hourly Rate Limits** - Can be bypassed for Rule 1-3

---

## 12. Summary of Key Changes

| Aspect | Old (Spot) | New (Perpetuals) |
|--------|------------|------------------|
| Skew range | 0% to 100% | -100% to +100% |
| Position sign | Ignored (Math.Abs) | Preserved (signed) |
| Neutral target | 50% (half long) | 0% (flat) |
| Bearish stance | 20% (small long) | -80% (heavily short) |
| Correction direction | NeedMore/LessCrypto | Increase/DecreaseExposure |
| Cross zero | Not possible | Required capability |
| Bootstrap mode | Required | Not applicable |
| USDT allocation | Tracked | Not relevant |

---

## Document Metadata

- **Created:** 2025-12-07
- **Author:** Trading Risk Manager Agent
- **Version:** 1.0
- **Status:** Specification (Ready for Implementation)
- **Related Files:**
  - `GridBot.ApiService/Services/Inventory/InventoryManager.cs`
  - `GridBot.ApiService/Models/Trading/InventoryState.cs`
  - `GridBot.ApiService/Models/Trading/InventoryAnalysis.cs`
  - `GridBot.ApiService/Models/Trading/TrendState.cs`
  - `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs`
  - `GridBot.ApiService/Services/Grid/GridLifecycleService.cs`
