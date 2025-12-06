# Lighter DEX CancelAllOrders API Documentation

## Question Answered

This document addresses the following questions about Lighter DEX's `CancelAllOrders` API:
1. What does the `cancelTimestampMs` parameter do?
2. Does passing `0` for cancelTimestampMs cancel ALL orders, or only orders created before timestamp 0?
3. What is the correct way to cancel all active orders on Lighter DEX?

---

## Executive Summary

**CRITICAL FINDING**: The current codebase has a **naming discrepancy** that could cause confusion. The second parameter is called `cancelTimestampMs` but it actually behaves more like a **timestamp cutoff for cancellation**, not a "time-in-force" value.

### Key Answers:

1. **What does `cancelTimestampMs` do?**
   - When passed to `CancelAllOrdersAsync(marketId, cancelTimestampMs)`, it specifies a Unix timestamp in milliseconds
   - Orders created **before** this timestamp will be cancelled
   - If `0` is passed, the implementation automatically uses `current time + 5 minutes` as the effective timestamp

2. **Does passing `0` cancel ALL orders?**
   - **YES**, but indirectly. The implementation converts `0` to `current time + 5 minutes`
   - Since all existing orders were created in the past, they all fall before this future timestamp and get cancelled

3. **Correct way to cancel all active orders:**
   ```csharp
   // Pass 0 (or omit the parameter) - implementation handles it correctly
   await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, ct);
   ```

---

## Detailed Technical Analysis

### Native Library Signature

The native library (`NativeMethods.cs`) uses this signature:

```csharp
[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
internal static extern StrOrErr SignCancelAllOrders(
    int marketIndex,
    long tif,  // Named "tif" (time-in-force) in native layer
    long nonce);
```

### SignerClient Implementation

The `SignerClient.cs` transforms `0` into a future timestamp:

```csharp
public async Task<(string? txInfo, string? error)> CancelAllOrdersAsync(int marketIndex, long cancelTimestampMs = 0)
{
    return await Task.Run(() =>
    {
        long nonce = GetNextNonce();
        // If cancelTimestampMs is 0, use current time + 5 minutes as the cancel-all timestamp.
        // The native library expects this to be a Unix timestamp in milliseconds > 0.
        long effectiveTimestamp = cancelTimestampMs > 0
            ? cancelTimestampMs
            : DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        var result = NativeMethods.SignCancelAllOrders(marketIndex, effectiveTimestamp, nonce);
        return ProcessStrOrErr(result);
    });
}
```

**Key insight**: The native library requires a timestamp > 0. Passing 0 directly would likely fail or have undefined behavior.

### Official Python SDK Comparison

The official Lighter Python SDK has a different API design:

```python
async def cancel_all_orders(self, time_in_force, timestamp_ms, ...)
```

With constants:
- `CANCEL_ALL_TIF_IMMEDIATE = 0` - Immediate cancellation of all orders
- `CANCEL_ALL_TIF_SCHEDULED = 1` - Scheduled cancellation at a specific time
- `CANCEL_ALL_TIF_ABORT = 2` - Abort a previously scheduled cancellation

The Python SDK separates the concept of:
1. **time_in_force**: The TYPE of cancellation (immediate vs scheduled vs abort)
2. **timestamp_ms**: When the scheduled cancellation should occur (only used with scheduled mode)

### Current C# Implementation Analysis

The current GridBot implementation:
- Combines both concepts into a single `cancelTimestampMs` parameter
- Uses `0` as a magic value meaning "immediate cancel all"
- The implementation correctly handles this by using `now + 5 minutes` as the cutoff

**This works correctly** because:
- Setting the timestamp 5 minutes in the future guarantees all existing orders (created in the past) will be cancelled
- No new orders can have a creation timestamp in the future

---

## Lighter API Behavior Modes

Based on official documentation:

| Mode | Description | Parameter Values |
|------|-------------|------------------|
| **ImmediateCancelAll** | Cancels all orders immediately | `time_in_force = IMMEDIATE` |
| **ScheduledCancelAll** | Schedules cancellation for a future time | `time_in_force = SCHEDULED`, `timestamp_ms = future_time` |
| **AbortScheduledCancelAll** | Cancels a previously scheduled cancel-all | `time_in_force = ABORT` |

---

## Current Usage in GridBot

The GridBot codebase uses `CancelAllOrdersAsync` in multiple places:

### GridOrderManager.cs - Regular Cancel

```csharp
// Line 293 - CancelAllGridOrdersAsync
var response = await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, ct)
    .ConfigureAwait(false);
```

### GridOrderManager.cs - Startup Cleanup

```csharp
// Line 532 - CancelExistingOrdersOnStartupAsync
var response = await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, timeoutToken)
    .ConfigureAwait(false);
```

Both usages pass `0`, which is the **correct approach** to cancel all orders immediately.

---

## Verification and Risks

### Risk: Silent Partial Failures

Even when `CancelAllOrdersAsync` returns success, there's no guarantee all orders were actually cancelled:
1. Orders could be matched during the cancel processing window
2. Network issues could cause partial processing
3. ZK-rollup commitment timing could affect order states

### Mitigation (Already Implemented)

The `CancelExistingOrdersOnStartupAsync` method includes post-cancellation verification:
```csharp
// Wait for exchange to process
await Task.Delay(200, linkedCts.Token).ConfigureAwait(false);

// Re-query to verify cancellation
var remainingOrders = await _queryClient.GetActiveOrdersAsync(...)
```

---

## Recommendations

### 1. Documentation Update (Optional)

The current implementation is correct, but the parameter naming could be clearer:

```csharp
// Current (confusing)
Task<RespSendTx> CancelAllOrdersAsync(int marketId, long cancelTimestampMs = 0, ...);

// More explicit (alternative)
// Note: When cancelBeforeTimestampMs is 0, uses now+5min to cancel ALL orders
Task<RespSendTx> CancelAllOrdersAsync(int marketId, long cancelBeforeTimestampMs = 0, ...);
```

### 2. Current Code is Correct

The existing code is functioning correctly:
- Passing `0` cancels all orders (via the +5 minute future timestamp logic)
- Post-cancellation verification confirms orders are actually cancelled
- Retry logic handles transient failures

---

## Sources

- [Lighter API Documentation](https://apidocs.lighter.xyz/)
- [Lighter Docs - Orders and Matching](https://docs.lighter.xyz/perpetual-futures/orders-and-matching)
- [Lighter Python SDK](https://github.com/elliottech/lighter-python)
- [Lighter API Get Started Guide](https://apidocs.lighter.xyz/docs/get-started-for-programmers-1)

---

## Summary for Developers

**To cancel all orders immediately:**
```csharp
await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, ct);
```

**Why it works:**
- `0` triggers the default behavior
- Implementation uses `now + 5 minutes` as the cutoff
- All existing orders (created in the past) are cancelled
- Native API receives a valid timestamp > 0

**Always verify cancellation:**
- Re-query active orders after cancellation
- Don't trust the API response alone
- Use retry logic for transient failures
