# Session Context - Perpetual Futures Skew Management

## Date
2025-12-07

## Previous Tasks (Completed)
1. Fixed critical race conditions in `DashboardStateService.cs`
2. Fixed P/Invoke signature mismatch causing crashes on Raspberry Pi

## Current Task
Define correct inventory/skew management behavior for perpetual futures trading, replacing the broken spot-trading model.

## New Task: Perpetual Futures Skew Specification

### Problem Statement
The current inventory management system was designed for spot trading and fails for perpetual futures:
- Uses `Math.Abs(position)` - ignores position direction
- Skew range 0-100% cannot represent short positions
- Target skew for bearish = 20% (reduced long) instead of negative (short)
- Correction signals are inverted for short positions

### Specification Created
Full specification document created at:
`C:\Users\stany\source\repos\plan\GridBot\.claude\doc\perpetual-futures-skew-specification.md`

### Key Changes Defined

1. **Skew Definition:** Signed exposure ratio from -100% to +100%
   - Positive = Long exposure
   - Zero = Flat (no position)
   - Negative = Short exposure

2. **New Target Skew Mapping:**
   | Trend | Old Target | New Target |
   |-------|------------|------------|
   | StrongBull | 80% | +80% |
   | MildBull | 70% | +50% |
   | Neutral | 50% | 0% |
   | MildBear | 30% | -50% |
   | StrongBear | 20% | -80% |

3. **Correction Directions:**
   - Replace `NeedMoreCrypto`/`NeedLessCrypto` with `IncreaseExposure`/`ReduceExposure`

4. **Cross-Zero Capability:** System must support transitioning from long to short and vice versa

### Files Requiring Changes
- `GridBot.ApiService/Services/Inventory/InventoryManager.cs` - Core calculation logic
- `GridBot.ApiService/Models/Trading/InventoryState.cs` - Model definitions
- `GridBot.ApiService/Models/Trading/InventoryAnalysis.cs` - Enums and analysis result
- `GridBot.ApiService/Services/Grid/GridLifecycleService.cs` - Grid bias logic
- `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs` - Rebalance execution

### Next Steps
1. Review specification with dotnet-feature-builder for implementation
2. Implement changes per specification
3. Code review with csharp-code-reviewer
4. Validate with trading-bot-auditor

---

## Code Review Results (2025-12-07)

**Reviewer:** csharp-code-reviewer
**Full Report:** `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\code-review-perpetual-skew-changes-2025-12-07.md`

### CRITICAL Issue Found

**EC-002 Position Detection Broken for Short Positions**
- **Location:** `GridLifecycleService.cs:263`
- **Problem:** The check `inventory.CurrentSkew > 5` only detects LONG positions. Short positions (negative skew) will not trigger the EC-002 safety mechanism when orders are externally cancelled.
- **Fix Required:**
  ```csharp
  // OLD:
  var hasPosition = inventory.CurrentSkew > 5;

  // NEW:
  var hasPosition = Math.Abs(inventory.CurrentSkew) > 5;
  ```

### Verified Correct
1. `InventoryManager.CalculatePortfolioValues` - Position sign handling is correct (`size * position.Sign`)
2. Enum rename complete - No remaining `NeedMoreCrypto`/`NeedLessCrypto` in code
3. All negative skew comparisons work correctly
4. Thread safety is maintained

### Status
- CRITICAL fix required before deployment
- Implementation agent should fix `GridLifecycleService.cs:263`

---

## Trading Bot Audit Results (2025-12-07)

**Auditor:** trading-bot-auditor
**Full Report:** `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\trading-bot-audit-perpetual-futures-skew.md`

### Overall Verdict: CONDITIONAL PASS

### Audit Questions Assessment

| Question | Verdict | Notes |
|----------|---------|-------|
| Position Sign Logic | PASS | `size * position.Sign` correctly produces negative skew for shorts |
| Target Skew in Bear Markets | PASS | -80% for StrongBear is intentional design for directional trading |
| Skew Correction for Shorts | PASS | When short -50%, target -80%, correctly triggers ReduceExposure |
| Crossing Zero | CONDITIONAL | Works but no explicit two-phase handling |
| MoonBag Interaction | PASS | Correctly resets on direction change |
| Risk Considerations | CONDITIONAL | Safe at 1x leverage, needs validation for higher |

### Findings Summary

**MEDIUM Risk (2):**
1. **FINDING-001:** No explicit cross-zero logic - large rebalances attempted in single order
2. **FINDING-002:** No leverage validation - system assumes 1x but doesn't verify

**LOW Risk (3):**
3. **FINDING-003:** Potential skew correction oscillation (needs hysteresis)
4. **FINDING-004:** EC-002 check broken for shorts (confirmed by code review)
5. **FINDING-005:** RebalanceDirection enum uses spot-era naming (cosmetic)

### Numerical Verification (Correct)
- Short -0.1 BTC at $90k with $9k collateral = -100% skew (CORRECT)
- Short -50% to target -80%: triggers ReduceExposure (CORRECT)
- Grid bias: buy=0.25x, sell=1.5x to increase short (CORRECT)

### Deployment Recommendation
1. **REQUIRED:** Fix FINDING-004 (EC-002 position detection)
2. **RECOMMENDED:** Add leverage validation (FINDING-002)
3. **MONITOR:** Cross-zero transitions, skew oscillation

---

## Previous Task Context (Reference Only)
Investigate and fix silent crash on Raspberry Pi Docker deployment. The service was failing without error logs after showing:
```
Found 2 existing orders on exchange for market 1. Cancelling all before grid initialization.
```

## Root Cause Analysis

**CRITICAL: P/Invoke Signature Mismatch (FIXED)**

The crash occurred because the P/Invoke signatures in `NativeMethods.cs` did NOT match the actual native library signatures! Comparing the C header file with our code:

### SignCancelAllOrders - COMPLETELY WRONG
**Native (header file):**
```c
extern SignedTxResponse SignCancelAllOrders(int cTimeInForce, long long int cTime, long long int cNonce, int cApiKeyIndex, long long int cAccountIndex);
```

**Our OLD code (WRONG):**
```csharp
internal static extern StrOrErr SignCancelAllOrders(int marketIndex, long tif, long nonce);
// Problems:
// 1. Wrong return type (StrOrErr vs SignedTxResponse)
// 2. Missing apiKeyIndex and accountIndex parameters!
// 3. First param is timeInForce, NOT marketIndex
```

### All Sign* Functions - Missing Parameters
ALL signing functions were missing `apiKeyIndex` and `accountIndex` parameters, and returned wrong type:
- `SignCreateOrder` - missing 2 params, wrong return type
- `SignCreateGroupedOrders` - missing 2 params, wrong return type
- `SignCancelOrder` - missing 2 params, wrong return type
- `SignModifyOrder` - missing 2 params, wrong param order, wrong return type
- `SignUpdateLeverage` - wrong param order, missing 2 params, wrong return type

This caused the native library to read garbage values from the stack, leading to undefined behavior and crashes.

## Changes Made

### 1. Added SignedTxResponse Struct

**File:** `GridBot.Lighter/Native/NativeStructs.cs`

Added the correct return type struct:
```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct SignedTxResponse
{
    public byte TxType;
    public IntPtr TxInfo;
    public IntPtr TxHash;
    public IntPtr MessageToSign;
    public IntPtr Err;
}
```

### 2. Fixed ALL P/Invoke Signatures

**File:** `GridBot.Lighter/Native/NativeMethods.cs`

Updated all Sign* functions to match the native header file:
- Changed return types from `StrOrErr` to `SignedTxResponse`
- Added `apiKeyIndex` and `accountIndex` parameters to all
- Fixed `SignCancelAllOrders` parameters (was marketIndex, now timeInForce)
- Fixed `SignModifyOrder` parameters (added triggerPrice, removed newClientOrderIndex)
- Fixed `SignUpdateLeverage` parameters (added initialMarginFraction)

### 3. Updated SignerClient Method Calls

**File:** `GridBot.Lighter/SignerClient.cs`

- All Sign* methods now pass `ApiKeyIndex` and `AccountIndex`
- Added `ProcessSignedTxResponse()` method to handle new return type
- `CancelAllOrdersAsync` signature changed: now takes `(timeInForce, cancelTimestampMs)` instead of `(marketIndex, cancelTimestampMs)`
- `ModifyOrderAsync` now passes `triggerPrice` parameter
- `UpdateLeverageAsync` now uses correct parameter order

### 4. Updated Model Classes

**File:** `GridBot.Lighter/Models/OrderRequest.cs`

- `ModifyOrderRequest`: Removed `NewClientOrderIndex`, added `NewTriggerPrice`
- `UpdateLeverageRequest`: Renamed `Leverage` to `InitialMarginFraction`

### 5. Updated LighterCommandClient

**File:** `GridBot.Lighter/LighterCommandClient.cs`

- `CancelAllOrdersAsync`: Added warning that `marketId` is ignored (native lib cancels ALL orders)
- Updated call to use new SignerClient signature

### 6. Added Native Library Validation at Startup

**File:** `GridBot.Lighter/Native/NativeMethods.cs`

Added `ValidateNativeLibrary()` method that:
- Logs platform and architecture info
- Verifies the library file exists
- Attempts to load the library early
- Catches and logs specific errors (DllNotFoundException, BadImageFormatException)

### 7. Fixed Linux Architecture Detection

**File:** `GridBot.Lighter/Native/NativeMethods.cs`

Updated `GetNativeLibraryPath()` to properly detect CPU architecture on Linux:
- ARM64 → uses `signer-arm64.so`
- X64 → looks for `signer-amd64.so`, falls back to arm64

### 8. Added Startup Validation Call

**File:** `GridBot.Lighter/LighterServiceCollectionExtensions.cs`

Added early validation in `AddLighterClient()`:
```csharp
var nativeError = SignerClient.ValidateNativeLibrary();
if (nativeError != null)
{
    throw new InvalidOperationException($"Native library validation failed: {nativeError}");
}
```

### 9. Added P/Invoke Call Logging

**File:** `GridBot.Lighter/SignerClient.cs`

Added logging with flush before `SignCancelAllOrders` call to track crashes.

## Build Status

Build succeeded with 0 warnings and 0 errors.

## IMPORTANT: Breaking Changes

1. **`CancelAllOrdersAsync`**: No longer takes `marketId` - it cancels ALL orders across ALL markets
2. **`ModifyOrderRequest`**: `NewClientOrderIndex` removed, `NewTriggerPrice` added
3. **`UpdateLeverageRequest`**: `Leverage` renamed to `InitialMarginFraction`

## Expected Console Output (After Fix)

```
[NativeLibrary] Validating native library...
[NativeLibrary] Platform: Linux 5.x.x
[NativeLibrary] Architecture: Arm64
[NativeLibrary] Library path: /app/Native/signer-arm64.so
[NativeLibrary] File size: 11047776 bytes
[NativeLibrary] Library loaded successfully at 0x...
[LighterClient] Native library validated successfully
[SignerClient] Calling SignCancelAllOrders: timeInForce=0, timestamp=..., nonce=..., apiKeyIndex=0, accountIndex=...
[SignerClient] SignCancelAllOrders returned successfully
```

## Next Steps

1. Rebuild and redeploy Docker image to Raspberry Pi
2. Test all trading operations (create order, cancel, modify, leverage)
3. Verify operations work with correct apiKeyIndex and accountIndex

## Previous Issues Fixed (from earlier session)

### 1. Race Condition in `AcknowledgeAlert`
**Problem:** The queue rebuild pattern (ToList -> Clear -> Re-enqueue) was not thread-safe. Alerts added by other threads during the rebuild would be lost.

**Solution:** Replaced with lock-protected in-place modification:
```csharp
lock (_alertsLock)
{
    var alertIndex = _alerts.FindIndex(a => a.Id == alertId);
    if (alertIndex >= 0)
    {
        _alerts[alertIndex] = _alerts[alertIndex] with { IsAcknowledged = true };
        alertsCopy = [.. _alerts];
    }
}
```

### 2. Race Condition in `ClearAlerts`
**Problem:** Same issue - alerts could be added and immediately lost during the clear loop.

**Solution:** Simple lock around the clear operation:
```csharp
lock (_alertsLock)
{
    _alerts.Clear();
}
```

### 3. Race Condition in `AddAlertAsync` Trim Loop
**Problem:** Multiple concurrent calls could over-trim the alert queue due to non-atomic read-modify-write.

**Solution:** All operations inside lock:
```csharp
lock (_alertsLock)
{
    _alerts.Add(alert);
    while (_alerts.Count > _maxAlerts)
    {
        _alerts.RemoveAt(0);
    }
    alertsCopy = [.. _alerts];
}
```

### 4. Race Condition in `BuildDashboardStateAsync`
**Problem:** Direct access to `_alerts.ToList()` without lock protection.

**Solution:** Added helper method `GetAlertsCopy()` that locks before copying.

## Changes Made

**File:** `GridBot.ApiService/Services/Dashboard/DashboardStateService.cs`

1. Removed `using System.Collections.Concurrent;` (no longer needed)
2. Replaced `ConcurrentQueue<AlertItem> _alerts` with `List<AlertItem> _alerts = []`
3. Added `object _alertsLock = new()` for synchronization
4. Updated class XML comment to document thread-safety guarantee
5. Refactored `AddAlertAsync` to use lock-protected list operations
6. Refactored `ClearAlerts` to use lock-protected clear
7. Refactored `AcknowledgeAlert` to use lock-protected in-place modification
8. Added `GetAlertsCopy()` helper method for thread-safe list copying
9. Updated `BuildDashboardStateAsync` to use `GetAlertsCopy()`

## Build Status
Build succeeded with 0 warnings and 0 errors.

## Thread Safety Pattern Used
Simple lock-based synchronization with `object _alertsLock`:
- All mutations to `_alerts` occur inside `lock (_alertsLock) { ... }`
- Copies of the list are taken inside the lock, then used outside
- State updates (`_currentState = ...`) happen outside the lock to minimize lock duration
- Event notifications (`OnStateChanged`) happen outside the lock

This pattern is appropriate because:
- Alert operations are not high-frequency hot paths
- Operations are short-lived (no I/O inside locks)
- Simple locking is easier to reason about than lock-free alternatives
