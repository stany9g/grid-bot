# Code Review: Perpetual Futures Skew/Inventory Management Changes

**Date:** 2025-12-07
**Reviewer:** csharp-code-reviewer
**Status:** ISSUES FOUND

## Summary

The changes implement signed skew values (-100% to +100%) for perpetual futures trading, replacing the spot-trading model (0% to 100%). The core logic in `InventoryManager.CalculatePortfolioValues` is correct, but one **CRITICAL** bug was found and two **WARNINGS** identified.

---

## Issues Found

### **[CRITICAL]** EC-002 Position Detection Broken for Short Positions

- **Location:** `GridLifecycleService.cs:263`
- **Problem:** The position existence check only detects LONG positions:
  ```csharp
  var hasPosition = inventory.CurrentSkew > 5; // More than 5% crypto = has position
  ```
  This will return `false` for SHORT positions (negative skew like -50%), meaning the EC-002 safety mechanism will NOT trigger when orders are externally cancelled while holding a short position. A short position at -50% skew is just as much "at risk" as a long position at +50%.

- **Fix:** Use absolute value to detect any position:
  ```csharp
  var hasPosition = Math.Abs(inventory.CurrentSkew) > 5; // More than 5% exposure = has position
  ```

---

### **[WARNING]** TrendIntelligenceService UsdtAllocation Calculation Inconsistent

- **Location:** `TrendIntelligenceService.cs:218-219`
- **Problem:** When updating inventory state, the calculation assumes:
  ```csharp
  UsdtAllocation = 100m - newSkew,
  ```
  For negative skew (short positions), this produces values > 100%. For example, if `newSkew = -50`, then `UsdtAllocation = 150%`. While this may be intentional for perpetuals (representing collateral), it is inconsistent with the field's documentation which states "USDT allocation percentage."

- **Impact:** Low - This value appears to be used primarily for display/logging, not core trading logic.

- **Recommendation:** Either update the documentation to clarify the meaning for perpetuals, or use a different formula like:
  ```csharp
  UsdtAllocation = 100m - Math.Abs(newSkew),
  ```

---

### **[WARNING]** GetAcceptableSkewRange for MildBear Has Inverted Min/Max

- **Location:** `InventoryManager.cs:192`
- **Problem:** The range definition shows:
  ```csharp
  TrendState.MildBear => (-70m, -30m),     // -30% to -70% short
  ```
  The comment says "-30% to -70%" but the tuple is `(-70, -30)`. Mathematically, `-70 < -30` so this is correct (Min=-70, Max=-30), but the comment is confusing and inverted from the pattern.

- **Impact:** None functionally - the code is correct. The comparison `currentSkew > maxSkew` and `currentSkew < minSkew` will work correctly.

- **Recommendation:** Fix comment for clarity:
  ```csharp
  TrendState.MildBear => (-70m, -30m),     // -70% (min) to -30% (max) short
  ```

---

## Verified Correct

### 1. InventoryManager.CalculatePortfolioValues - Position Sign Handling

**Code:**
```csharp
if (position != null && decimal.TryParse(position.Positionn, NumberStyles.Number, CultureInfo.InvariantCulture, out var size))
{
    // Position size is in base asset units
    // CRITICAL: Multiply by Sign to get signed position value
    // Sign: 1 = Long, -1 = Short, 0 = None
    positionSize = size * position.Sign;
}
```

**Verdict:** CORRECT
- `position.Sign` is an `int` (1, -1, or 0) from the Lighter API
- The `Positionn` field is always positive (it's the absolute size)
- Multiplying `size * Sign` correctly produces signed position values
- No null reference risk: `position` is null-checked before accessing

### 2. Enum Rename (NeedMoreCrypto/NeedLessCrypto to IncreaseExposure/ReduceExposure)

**Verdict:** COMPLETE
- Grep search confirms no remaining usages of old enum names in code
- Old names only appear in documentation files (`.claude/doc/`, `.claude/sessions/`)
- All functional code uses `IncreaseExposure`/`ReduceExposure`

### 3. Negative Skew Range Comparisons

**Verified locations:**
- `InventoryManager.cs:95-108` - Uses `currentSkew > maxSkew` and `currentSkew < minSkew` - CORRECT
- `GridLifecycleService.cs:643-660` - Uses `skewDelta > 5` and `skewDelta < -5` - CORRECT
- `RebalancingService.cs:120-124` - Uses `Math.Clamp(..., -95m, 95m)` - CORRECT

All comparisons work correctly with negative values.

### 4. Thread Safety

**Verdict:** NO ISSUES
- `InventoryManager` uses `ConcurrentDictionary` for `_hourlyRebalanceTracker` with atomic `AddOrUpdate`
- `RebalancingService` uses `SemaphoreSlim` per-market locks for rebalance operations
- `GridLifecycleService` uses `SemaphoreSlim` per-market locks for grid operations
- No shared mutable state accessed without synchronization

---

## Required Actions

| Priority | Issue | File | Line |
|----------|-------|------|------|
| CRITICAL | EC-002 position detection ignores shorts | GridLifecycleService.cs | 263 |
| WARNING | UsdtAllocation > 100% for shorts | TrendIntelligenceService.cs | 218-219 |
| WARNING | Confusing comment on MildBear range | InventoryManager.cs | 192 |

---

## Files Reviewed

1. `GridBot.ApiService/Services/Inventory/InventoryManager.cs` - Core skew calculation
2. `GridBot.ApiService/Models/Trading/InventoryAnalysis.cs` - Enum and model definitions
3. `GridBot.ApiService/Models/Trading/InventoryState.cs` - State model
4. `GridBot.ApiService/Configuration/RiskConfiguration.cs` - Configuration access
5. `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs` - Rebalance execution
6. `GridBot.ApiService/Services/Trend/TrendIntelligenceService.cs` - Trend cycle orchestration
7. `GridBot.ApiService/Services/Grid/GridLifecycleService.cs` - Grid lifecycle management
8. `GridBot.Lighter/Models/Api/Account.cs` - Position model (Sign property reference)
