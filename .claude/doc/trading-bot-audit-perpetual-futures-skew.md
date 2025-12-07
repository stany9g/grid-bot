# Trading Bot Audit: Perpetual Futures Skew Management

## Audit Date: 2025-12-07
## Audit Scope: Perpetual Futures Trading Logic Changes
## Auditor: Trading Systems Auditor Agent

---

## EXECUTIVE SUMMARY

The perpetual futures skew management implementation has been reviewed for trading logic correctness. The changes successfully transform the system from a spot-trading model to a proper perpetual futures model with signed position exposure.

**Overall Verdict: CONDITIONAL PASS**

The core logic is sound, but there are several concerns that require attention before production deployment.

---

## AUDIT QUESTIONS ASSESSMENT

### 1. Position Sign Logic

**Question:** When position.Sign = -1 (short), is the skew correctly negative?

**VERDICT: PASS**

**Evidence:**
```csharp
// InventoryManager.cs:210-215
if (position != null && decimal.TryParse(position.Positionn, NumberStyles.Number, CultureInfo.InvariantCulture, out var size))
{
    // Position size is in base asset units
    // CRITICAL: Multiply by Sign to get signed position value
    // Sign: 1 = Long, -1 = Short, 0 = None
    positionSize = size * position.Sign;
}

// Line 220
var cryptoValueUsd = positionSize * currentPrice;
```

**Analysis:**
- The Lighter API returns `position.Sign` as: 1 = Long, -1 = Short, 0 = None
- The code correctly multiplies the absolute position size by Sign
- Example verification:
  - Short -0.1 BTC at $90k with $9k collateral:
  - `positionSize = 0.1 * (-1) = -0.1`
  - `cryptoValueUsd = -0.1 * 90000 = -9000`
  - `skew = -9000 / 9000 * 100 = -100%`
  - **Correct:** Short position correctly shows negative skew.

---

### 2. Target Skew in Bear Markets

**Question:** StrongBear target is -80% (short position). Is this correct?

**VERDICT: PASS (with risk acknowledgment)**

**Evidence:**
```csharp
// InventoryState.cs:58-68
public static decimal GetTargetSkewForTrend(TrendState trend)
{
    return trend switch
    {
        TrendState.StrongBull => 80m,    // +80% long exposure
        TrendState.MildBull => 50m,      // +50% long exposure
        TrendState.Neutral => 0m,        // 0% = flat (no position)
        TrendState.MildBear => -50m,     // -50% short exposure
        TrendState.StrongBear => -80m,   // -80% short exposure
        _ => 0m                          // Default to flat
    };
}
```

**Trading Logic Assessment:**
- **PRO:** Perpetual futures allow profiting from downtrends via short positions. A -80% target in StrongBear is consistent with the ALTE system's goal of being an "adaptive liquidity engine" that profits in both directions.
- **PRO:** The symmetric exposure (+80% for bull, -80% for bear) provides balanced risk/reward.
- **CONCERN:** Going short introduces directional risk that spot trading does not have. If trend detection is wrong, losses can accumulate.

**Risk Acknowledgment:**
The specification (Section 2, "Fundamental Difference: Spot vs Perpetuals") explicitly states:
> "Bearish stance: Short position (-80% exposure)"

This is the **intended design**. The system is designed to take directional bets, not just reduce long exposure in bear markets.

**Recommendation:** Ensure trend detection has sufficient latency/confirmation to avoid whipsaws. Consider adding a "trend stability" requirement before transitioning to short positions.

---

### 3. Skew Correction for Shorts

**Question:** When short at -50% and target is -80%, does the bot correctly try to "increase short" (sell more)?

**VERDICT: PASS**

**Evidence:**
```csharp
// InventoryManager.cs:95-108
if (currentSkew > maxSkew)
{
    // Current exposure is higher than maximum acceptable
    // Need to reduce exposure (sell if long, increase short if short)
    skewCorrectionMode = true;
    correctionDirection = SkewCorrectionDirection.ReduceExposure;
}
else if (currentSkew < minSkew)
{
    // Current exposure is lower than minimum acceptable
    // Need to increase exposure (buy if flat/long, cover if short)
    skewCorrectionMode = true;
    correctionDirection = SkewCorrectionDirection.IncreaseExposure;
}
```

**Analysis:**
- StrongBear acceptable range: (-95%, -60%)
- Current skew: -50% (short 50%)
- Target: -80%
- Since -50% > -60% (maxSkew), `currentSkew > maxSkew` is TRUE
- Direction: `ReduceExposure` = sell more / increase short
- **Correct behavior.**

**Grid Bias Verification:**
```csharp
// GridLifecycleService.cs:629-664
var skewDelta = inventory.CurrentSkew - inventory.TargetSkew;

if (skewDelta > 5) // Current exposure higher than target - need to reduce exposure
{
    // Per framework spec: buy orders = reduce by 75%, sell orders = increase by 50%
    return (0.25m, 1.5m);
}
else if (skewDelta < -5) // Current exposure lower than target - need to increase exposure
{
    // Per framework spec: buy orders = increase by 50%, sell orders = reduce by 75%
    return (1.5m, 0.25m);
}
```

**Verification for -50% to -80% scenario:**
- skewDelta = -50 - (-80) = +30
- Since +30 > 5, we get `(0.25m, 1.5m)` = reduce buys, increase sells
- **Correct:** More sells placed, fewer buys, which increases short position.

---

### 4. Crossing Zero (Long to Short)

**Question:** Can the system handle going from long to short when trend flips?

**VERDICT: CONDITIONAL PASS - NEEDS TESTING**

**Evidence:**
```csharp
// RebalancingService.cs:120-125 (Emergency rebalance)
var emergencyTarget = Math.Clamp(
    analysis.TargetSkew + (analysis.RebalanceDelta > 0 ? -15m : 15m),
    -95m,  // Maximum short exposure (95% short)
    95m    // Maximum long exposure (95% long)
);
```

**Analysis:**
The rebalancing system uses market orders and does NOT have explicit "close then open" logic. It simply calculates the delta needed and executes.

**Example: +50% long to -80% short**
- RebalanceDelta = -80 - 50 = -130%
- Emergency threshold (>30%) triggered
- Emergency target = -80 + 15 = -65%
- targetDelta = -65 - 50 = -115%

**Potential Issues:**

1. **Single Order vs Multi-Step:**
   The current implementation attempts to execute the entire rebalance in one market order. For a 130% swing, this is:
   - Close the long (50% of portfolio)
   - Open the short (80% of portfolio)
   - Total trade size: 130% of portfolio

   **CONCERN:** This is a very large market order that could cause significant slippage.

2. **Hourly Rate Limit:**
   The 10%/hour rate limit would prevent a full swing in one hour. The system would need 13+ hours to complete the transition under normal conditions.

3. **No Explicit Zero-Crossing Guard:**
   There is no code that explicitly handles the "crossing zero" case. The system relies on:
   - Emergency rebalance for large swings
   - Gradual grid bias for smaller adjustments

**Finding:**
```
## [FINDING-001] No Explicit Cross-Zero Logic
**Risk Level:** MEDIUM
**Category:** Trading Logic
**Location:** `RebalancingService.cs` / `InventoryManager.cs`
**Financial Impact:** Large slippage during trend reversals (could be 1-2% of position)
**Performance Impact:** N/A

**Problem:**
When transitioning from long to short (or vice versa), the system treats it as a single
large rebalance rather than a two-phase operation. This can result in:
1. Attempting very large market orders
2. Rate limiting preventing timely execution
3. No explicit handling of the "flat" state during transition

**Evidence:**
No code exists that detects cross-zero scenarios and handles them specially.

**Recommended Fix:**
Add cross-zero detection in `RebalancingService.ExecuteRebalanceInternalAsync`:
```csharp
// Detect cross-zero scenario
var crossingZero = (analysis.CurrentSkew > 0 && analysis.TargetSkew < 0) ||
                   (analysis.CurrentSkew < 0 && analysis.TargetSkew > 0);

if (crossingZero)
{
    // Phase 1: Move to flat
    var flattenDelta = -analysis.CurrentSkew;
    // Execute flatten trade

    // Phase 2: Open opposite position
    var openDelta = analysis.TargetSkew;
    // Execute open trade (may be rate limited to next cycle)
}
```

**Verdict:** CONDITIONAL PASS - Works but could be more robust
```

---

### 5. MoonBag Interaction

**Question:** Does the new skew system interact correctly with MoonBag?

**VERDICT: PASS (with minor concern)**

**Evidence:**
```csharp
// MoonBagManager.cs:557-597
if (isLong.HasValue && status.State != MoonBagState.Inactive)
{
    if (status.IsLongPosition != isLong.Value)
    {
        _logger.LogWarning(
            "Position direction changed for market {MarketId}: {OldDirection} -> {NewDirection}. Resetting moon bag state.",
            marketId,
            status.IsLongPosition ? "Long" : "Short",
            isLong.Value ? "Long" : "Short");

        // Reset the moon bag state for direction change
        status.State = MoonBagState.Inactive;
        status.MaxPositionAchieved = 0;
        status.HighWatermarkPrice = 0;
        status.LockedQuantity = 0;
        status.LastStateTransition = DateTimeOffset.UtcNow;
        status.StateReason = "Position direction changed";
        // ...
    }
}
```

**Analysis:**
- MoonBag correctly tracks `IsLongPosition`
- When position direction changes (long to short or vice versa), MoonBag is reset to Inactive
- This is correct behavior - you cannot hold a "moon bag" of a position that no longer exists

**Minor Concern:**
The MoonBag system has `EnableShortMoonBag` configuration option (line 112-113):
```csharp
if (!isLong && !options.EnableShortMoonBag)
{
    // Short moon bag protection disabled
}
```

**Question:** Is short moon bag enabled by default? If not, when in StrongBear mode targeting -80% short, there would be no moon bag protection for the short position.

**Recommendation:** Verify `EnableShortMoonBag` configuration and document the expected behavior.

---

### 6. Risk Considerations

**Question:** Is -95% to +95% safe as emergency boundaries?

**VERDICT: CONDITIONAL PASS**

**Evidence:**
```csharp
// GetAcceptableSkewRange (InventoryManager.cs:185-196)
return trend switch
{
    TrendState.StrongBull => (60m, 95m),     // +60% to +95% long
    TrendState.StrongBear => (-95m, -60m),   // -60% to -95% short
    // ...
};

// RebalancingService.cs emergency clamp
Math.Clamp(emergencyTarget, -95m, 95m)
```

**Analysis:**
- 95% exposure at 1x leverage = 0.95x effective leverage
- This is conservative and safe from liquidation perspective
- However, the code does NOT check actual leverage settings on the exchange

```
## [FINDING-002] No Leverage Validation
**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `InventoryManager.cs:185-196`
**Financial Impact:** Could exceed comfortable leverage if exchange leverage is set differently
**Performance Impact:** N/A

**Problem:**
The system assumes 1x leverage but does not validate the actual leverage setting on the exchange.
If the account has 5x leverage enabled, a 95% skew target could result in:
- Position value: 95% of collateral
- At 5x leverage: 4.75x effective leverage
- This is much higher risk than intended

**Evidence:**
No code exists that reads or validates leverage settings from the account.

**Recommended Fix:**
Add leverage check in `AnalyzeInventoryAsync`:
```csharp
// Get position's initial margin fraction
var position = account.Positions?.FirstOrDefault(p => p.MarketId == marketId);
if (position != null)
{
    var marginFraction = decimal.Parse(position.InitialMarginFraction, CultureInfo.InvariantCulture);
    var maxLeverage = 1m / marginFraction;

    // Scale acceptable ranges by leverage
    // At 2x leverage, max skew should be 50% to achieve 1x effective exposure
    var effectiveMaxSkew = 100m / maxLeverage;
}
```

**Verdict:** CONDITIONAL PASS - Safe at 1x, needs validation for higher leverage
```

---

## ADDITIONAL FINDINGS

```
## [FINDING-003] Potential Infinite Loop in Skew Correction
**Risk Level:** LOW
**Category:** Trading Logic
**Location:** `GridLifecycleService.cs:643-663`
**Financial Impact:** Minimal - would result in excessive grid churn
**Performance Impact:** CPU usage, API rate limit exhaustion

**Problem:**
The skew correction threshold is 5% for both trigger and recovery:
- Trigger when skewDelta > 5 or skewDelta < -5
- But the correction itself may not move skew by exactly the right amount

If current skew oscillates around target +/- 5%, the system could repeatedly toggle
between correction modes.

**Evidence:**
```csharp
if (skewDelta > 5) // Threshold
{
    return (0.25m, 1.5m);
}
else if (skewDelta < -5) // Same threshold
{
    return (1.5m, 0.25m);
}
```

**Recommended Fix:**
Add hysteresis - use different thresholds for entering and exiting correction mode:
- Enter correction: |delta| > 10%
- Exit correction: |delta| < 5%

**Verdict:** LOW RISK - Monitoring recommended
```

```
## [FINDING-004] EC-002 Check Uses Spot-Era Skew Threshold
**Risk Level:** LOW
**Category:** Trading Logic
**Location:** `GridLifecycleService.cs:264-265`
**Financial Impact:** False positives for grid rebuild in short positions
**Performance Impact:** Unnecessary grid rebuilds

**Problem:**
The EC-002 check (all orders cancelled externally) uses a spot-trading assumption:
```csharp
var hasPosition = inventory.CurrentSkew > 5; // More than 5% crypto = has position
```

For perpetual futures with short positions, this would be FALSE when skew is negative,
even though a position exists.

**Evidence:**
- Short position at -50% skew
- `inventory.CurrentSkew > 5` evaluates to FALSE
- EC-002 protection does not trigger even though position exists

**Recommended Fix:**
```csharp
var hasPosition = Math.Abs(inventory.CurrentSkew) > 5; // Any exposure = has position
```

**Verdict:** PASS (after fix) - Simple fix required
```

```
## [FINDING-005] RebalanceDirection Enum Still Uses Spot-Era Naming
**Risk Level:** LOW
**Category:** Code Quality
**Location:** `InventoryAnalysis.cs:6-22`
**Financial Impact:** None (logic is correct)
**Performance Impact:** None

**Problem:**
The `RebalanceDirection` enum still uses `BuyCrypto` and `SellCrypto` which are
spot-trading concepts. For perpetual futures, these should be:
- `IncreaseExposure` / `ReduceExposure` (already used in SkewCorrectionDirection)

The direction is set correctly in InventoryManager.cs:111-113:
```csharp
var direction = rebalanceDelta > 0 ? RebalanceDirection.BuyCrypto :
                rebalanceDelta < 0 ? RebalanceDirection.SellCrypto :
                RebalanceDirection.None;
```

This works because:
- Positive delta = need more exposure = buy (for long) or cover (for short)
- Negative delta = need less exposure = sell (for long) or add short

**Evidence:**
The logic is CORRECT but the naming is confusing for perpetual futures context.

**Recommended Fix:**
Rename enum values for clarity:
- `BuyCrypto` -> `IncreaseExposure` or keep for backward compatibility
- `SellCrypto` -> `ReduceExposure` or keep for backward compatibility

**Verdict:** PASS - Cosmetic issue only
```

---

## NUMERICAL VERIFICATION

### Test Case 1: Short Position Skew Calculation
| Input | Value |
|-------|-------|
| Position Size (from API) | "0.1" |
| Sign | -1 |
| Mark Price | $90,000 |
| Collateral | $9,000 |

**Calculation:**
```
positionSize = 0.1 * (-1) = -0.1
cryptoValueUsd = -0.1 * 90000 = -$9,000
usdtBalance = $9,000 (collateral)
totalPortfolioUsd = $9,000 (collateral includes unrealized PnL)
cryptoAllocation = (-9000 / 9000) * 100 = -100%
currentSkew = -100%
```
**Result:** CORRECT - Short -0.1 BTC at $90k with $9k collateral = -100% skew

### Test Case 2: Skew Correction Direction
| Input | Value |
|-------|-------|
| Current Skew | -50% |
| Trend | StrongBear |
| Target Skew | -80% |
| Acceptable Range | -95% to -60% |

**Analysis:**
```
currentSkew (-50%) > maxSkew (-60%) ? TRUE
=> correctionDirection = ReduceExposure
=> Grid bias: buy=0.25x, sell=1.5x
```
**Result:** CORRECT - More sells to increase short position

### Test Case 3: Cross-Zero Emergency Rebalance
| Input | Value |
|-------|-------|
| Current Skew | +50% (long) |
| New Trend | StrongBear |
| Target Skew | -80% |
| Delta | -130% |

**Analysis:**
```
IsEmergency = |delta| > 30% ? TRUE (130% > 30%)
emergencyTarget = -80 + 15 = -65% (clamped to -95..+95)
targetDelta = -65 - 50 = -115%
Direction = SellCrypto (reduce exposure)
```
**Result:** CORRECT - Emergency rebalance triggers, but 115% swing is very large

---

## AUDIT SUMMARY

```
===================================================================
AUDIT SUMMARY
===================================================================
Total Findings: 5
+-- HIGH Risk: 0
+-- MEDIUM Risk: 2
|   +-- FINDING-001: No Explicit Cross-Zero Logic
|   +-- FINDING-002: No Leverage Validation
+-- LOW Risk: 3
    +-- FINDING-003: Potential Infinite Loop in Skew Correction
    +-- FINDING-004: EC-002 Check Uses Spot-Era Skew Threshold
    +-- FINDING-005: RebalanceDirection Enum Still Uses Spot-Era Naming

Overall Verdict: CONDITIONAL PASS

Deployment Recommendation:
- FINDING-004 must be fixed before deployment (simple one-line fix)
- FINDING-001 and FINDING-002 should be addressed in near-term roadmap
- Deploy with monitoring for:
  * Cross-zero transitions
  * Skew correction oscillation
  * Effective leverage exceeding 1x
===================================================================
```

---

## CRITICAL FIXES REQUIRED BEFORE DEPLOYMENT

### Fix 1: EC-002 Position Detection (FINDING-004)
**File:** `GridBot.ApiService/Services/Grid/GridLifecycleService.cs`
**Line:** 264-265

**Change:**
```csharp
// OLD (broken for shorts)
var hasPosition = inventory.CurrentSkew > 5;

// NEW (works for both longs and shorts)
var hasPosition = Math.Abs(inventory.CurrentSkew) > 5;
```

### Fix 2 (Recommended): Add Effective Leverage Check
**File:** `GridBot.ApiService/Services/Inventory/InventoryManager.cs`
**Location:** Inside `CalculatePortfolioValues` or `AnalyzeInventoryAsync`

Add validation that effective leverage does not exceed 1x:
```csharp
// Calculate effective leverage
var effectiveLeverage = Math.Abs(cryptoValueUsd) / totalPortfolioUsd;
if (effectiveLeverage > 1.0m)
{
    _logger.LogWarning(
        "Effective leverage {Leverage:F2}x exceeds 1x for market {MarketId}. Consider reducing position.",
        effectiveLeverage, marketId);
}
```

---

## APPENDIX: Code Flow Verification

### Inventory Analysis Flow
1. `InventoryManager.AnalyzeInventoryAsync(marketId)`
2. `_queryClient.GetAccountAsync()` - gets position with Sign
3. `CalculatePortfolioValues()` - multiplies position by Sign
4. `cryptoAllocation = cryptoValueUsd / totalPortfolioUsd * 100`
5. `currentSkew = cryptoAllocation` (signed value)
6. `targetSkew = GetTargetSkewForTrend(trend)` (signed value)
7. `GetAcceptableSkewRange(trend)` (signed range)
8. `if (currentSkew > maxSkew)` - works because both are signed

### Grid Bias Flow
1. `GridLifecycleService.UpdateOrderSizesAsync()`
2. `GetSkewCorrectionMultipliers()`
3. `skewDelta = inventory.CurrentSkew - inventory.TargetSkew`
4. For short positions: both values are negative, delta calculation correct
5. Apply multipliers to buy/sell orders

### Rebalance Flow
1. `RebalancingService.ExecuteRebalanceAsync()`
2. `direction = analysis.Direction` (BuyCrypto/SellCrypto)
3. `isAsk = direction == SellCrypto`
4. For short positions needing more short: direction=SellCrypto, isAsk=true
5. Market order executes as SELL, increasing short position

---

## Document Metadata
- **Audit Date:** 2025-12-07
- **Auditor:** Trading Systems Auditor Agent
- **Version:** 1.0
- **Status:** Complete
- **Files Reviewed:**
  - `GridBot.ApiService/Services/Inventory/InventoryManager.cs`
  - `GridBot.ApiService/Models/Trading/InventoryState.cs`
  - `GridBot.ApiService/Models/Trading/InventoryAnalysis.cs`
  - `GridBot.ApiService/Services/Grid/GridLifecycleService.cs`
  - `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs`
  - `GridBot.ApiService/Services/MoonBag/MoonBagManager.cs`
  - `GridBot.ApiService/Models/Trading/MoonBagState.cs`
  - `GridBot.Lighter/Models/Api/Account.cs`
  - `.claude/doc/perpetual-futures-skew-specification.md`
