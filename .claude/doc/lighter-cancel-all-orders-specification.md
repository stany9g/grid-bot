# Lighter DEX CancelAllOrders Transaction Specification

## Issue Summary

The current implementation is failing with error:
```
"CancelAllTime should be larger than 0 and not larger than 9223372036854775807"
```

The root cause is that the `tif` parameter (currently passed as `0`) is incorrectly named and misunderstood. Based on the Python SDK analysis, this parameter is actually a **Unix timestamp in milliseconds**, not a time-in-force enum.

## Python SDK Reference Implementation

From the official `elliottech/lighter-python` SDK:

### Native Signer Function Signature

```python
signer.SignCancelAllOrders.argtypes = [
    ctypes.c_int,       # time_in_force (0=immediate, 1=scheduled, 2=abort)
    ctypes.c_longlong,  # timestamp_ms (Unix timestamp in milliseconds)
    ctypes.c_longlong,  # nonce
    ctypes.c_int,       # api_key_index
    ctypes.c_longlong   # account_index
]
```

### Python SDK Constants

```python
CANCEL_ALL_TIF_IMMEDIATE = 0
CANCEL_ALL_TIF_SCHEDULED = 1
CANCEL_ALL_TIF_ABORT = 2
```

### Python SDK Method Signature

```python
async def cancel_all_orders(
    self,
    time_in_force,      # 0=immediate, 1=scheduled, 2=abort
    timestamp_ms,       # Unix timestamp in milliseconds
    nonce: int = DEFAULT_NONCE,
    api_key_index: int = DEFAULT_API_KEY_INDEX
)
```

## Key Discovery

**The native signer function takes 5 parameters, not 3:**

| # | Parameter | Type | Description |
|---|-----------|------|-------------|
| 1 | time_in_force | int | Cancellation mode: 0=immediate, 1=scheduled, 2=abort |
| 2 | timestamp_ms | long long | Unix timestamp in milliseconds |
| 3 | nonce | long long | Transaction nonce |
| 4 | api_key_index | int | API key index (0-254) |
| 5 | account_index | long long | Account identifier |

## The Problem

The current C# implementation (NativeMethods.cs) declares the function with only 3 parameters:

```csharp
// INCORRECT - Missing timestamp_ms, api_key_index, account_index
internal static extern StrOrErr SignCancelAllOrders(
    int marketIndex,    // This doesn't exist in Python SDK!
    long tif,           // This is actually timestamp_ms
    long nonce);
```

However, looking at the error message `"CancelAllTime should be larger than 0"`, it appears:
1. The native library expects `CancelAllTime` (timestamp_ms) to be a **positive Unix timestamp**
2. Passing `0` is invalid - it must be greater than 0
3. The maximum value is `Int64.MaxValue` (9223372036854775807)

## Interpretation of Parameters

Based on the error and Python SDK analysis:

### For Immediate Cancellation (time_in_force = 0)

Even when requesting immediate cancellation, the `timestamp_ms` parameter must be a valid future timestamp. The interpretation is:

- **timestamp_ms**: The timestamp UNTIL which orders created BEFORE this time should be cancelled
- For immediate/all orders: Use current timestamp + buffer (e.g., `DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()`)

### For Scheduled Cancellation (time_in_force = 1)

- **timestamp_ms**: The specific time at which to cancel orders

### For Abort (time_in_force = 2)

- **timestamp_ms**: Used to abort a previously scheduled cancellation

## Recommended Fix

### Option A: Simple Fix (If current 3-parameter signature is correct for this DLL version)

The `tif` parameter is misnamed - it's actually `timestamp_ms`. Pass a future timestamp:

```csharp
// In SignerClient.CancelAllOrdersAsync:
long cancelAllTime = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds();
var result = NativeMethods.SignCancelAllOrders(marketIndex, cancelAllTime, nonce);
```

### Option B: Full Fix (If 5-parameter signature is required)

Update NativeMethods.cs to match Python SDK:

```csharp
[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal static extern StrOrErr SignCancelAllOrders(
    int timeInForce,        // 0=immediate, 1=scheduled, 2=abort
    long timestampMs,       // Unix timestamp in milliseconds
    long nonce,             // Transaction nonce
    int apiKeyIndex,        // API key index (0-254)
    long accountIndex);     // Account identifier
```

Update SignerClient.cs:

```csharp
public async Task<(string? txInfo, string? error)> CancelAllOrdersAsync(
    int timeInForce = 0)  // 0=immediate, 1=scheduled, 2=abort
{
    if (!_isInitialized)
        return (null, "Client not initialized. Call InitializeAsync first.");

    return await Task.Run(() =>
    {
        long nonce = GetNextNonce();
        // For immediate cancellation, use a future timestamp
        long timestampMs = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        var result = NativeMethods.SignCancelAllOrders(
            timeInForce,
            timestampMs,
            nonce,
            ApiKeyIndex,
            AccountIndex);
        return ProcessStrOrErr(result);
    });
}
```

## IMPORTANT: Market Index Discrepancy

The current C# implementation passes `marketIndex` as the first parameter, but the Python SDK does NOT have a market index parameter at all. This suggests:

1. The DLL being used may be a different version than the Python SDK references
2. OR the market index is specified differently (possibly in the signer initialization)
3. Further investigation needed by checking the actual DLL exports or header file

## Verification Steps

1. **Check DLL exports**: Use `dumpbin /exports signer-amd64.dll` on Windows to see the actual function signature
2. **Check header file**: Download from lighter-go releases to see the C header definition
3. **Test with timestamp**: Try passing `DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds()` instead of `0`

## Quick Test Fix

To immediately test if the timestamp interpretation is correct:

```csharp
// In GridOrderManager.cs line 293:
// Change:
var response = await _commandClient.CancelAllOrdersAsync(marketId, timeInForce: 0, ct)

// To:
long cancelTime = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
var response = await _commandClient.CancelAllOrdersAsync(marketId, timeInForce: cancelTime, ct)
```

## Sources

- [elliottech/lighter-python GitHub Repository](https://github.com/elliottech/lighter-python)
- [Lighter Python SDK - Account.md](https://github.com/elliottech/lighter-python/blob/main/docs/Account.md) - Documents `cancel_all_time` as `int` type
- [elliottech/lighter-go GitHub Repository](https://github.com/elliottech/lighter-go) - Reference for native signer library
- [Lighter Docs](https://docs.lighter.xyz) - Official documentation
- [Lighter API Docs](https://apidocs.lighter.xyz/reference/status) - API reference

## Status

**REQUIRES IMPLEMENTATION** - The exact fix depends on verifying the actual DLL signature being used. The most likely fix is to pass a valid future timestamp instead of `0`.

## Confidence Level

**MEDIUM** - The analysis is based on the Python SDK which may use a different version of the native library. The C# NativeMethods declaration shows only 3 parameters while Python shows 5, suggesting either:
- Different DLL versions
- The C# declaration is incorrect
- The market handling is done differently

The error message strongly suggests the second parameter (`tif` in C#, `timestamp_ms` in Python) must be a positive Unix timestamp, not `0`.
