# Session Context - Native Library Crash Diagnosis

## Date
2025-12-07

## Previous Task (Completed)
Fixed critical race conditions in `DashboardStateService.cs` identified by the code reviewer.

## Current Task
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
