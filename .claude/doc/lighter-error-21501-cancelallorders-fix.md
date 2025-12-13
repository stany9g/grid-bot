# Lighter DEX Error 21501 "invalid tx info" - CancelAllOrders Analysis

## Problem Statement

The bot encounters error code **21501** with message "invalid tx info" when calling `CancelAllOrdersAsync` on Lighter DEX. This occurs in `SendTransactionAsync`, meaning the signing succeeds but the server rejects the transaction as malformed.

## Error Code Reference

Based on [Lighter API Documentation](https://apidocs.lighter.xyz/docs/data-structures-constants-and-errors):

| Error Code | Name | Message |
|------------|------|---------|
| **21501** | `AppErrInvalidTxInfo` | "invalid tx info" |
| 21712 | `AppErrAccountHasAQueuedCancelAllOrdersRequest` | "account has a queued cancel all orders request" |
| 21713 | `AppErrInvalidCancelAllTimeInForce` | "invalid cancel all time in force" |
| 21714 | `AppErrInvalidCancelAllTime` | "invalid cancel all time" |

Error 21501 indicates **malformed transaction data** - the structure or parameters of the signed transaction do not meet the server's validation requirements.

---

## Root Cause Analysis

### CancelAllOrders TimeInForce Values

From the [Lighter Go SDK](https://github.com/elliottech/lighter-go/tree/main/types/txtypes):

| Value | Name | Time Parameter Requirement |
|-------|------|----------------------------|
| `0` | `ImmediateCancelAll` | Time MUST equal `NilOrderExpiry` (which is `0`) |
| `1` | `ScheduledCancelAll` | Time MUST be between `MinOrderExpiry` (1) and `MaxOrderExpiry` (MaxInt64) |
| `2` | `AbortScheduledCancelAll` | Time MUST be exactly `0` |

### Validation Logic (from lighter-go)

```go
// L2CancelAllOrdersTxInfo.Validate() pseudo-code:
switch TimeInForce {
case ImmediateCancelAll:  // 0
    if Time != NilOrderExpiry {  // NilOrderExpiry = 0
        return ErrCancelAllTimeisNotNill  // "CancelAllTime should be nil"
    }
case ScheduledCancelAll:  // 1
    if Time < MinOrderExpiry || Time > MaxOrderExpiry {
        return ErrCancelAllTimeIsNotInRange
    }
case AbortScheduledCancelAll:  // 2
    if Time != 0 {
        return ErrCancelAllTimeisNotNill
    }
default:
    return ErrInvalidCancelAllTimeInForce
}
```

### Current Implementation

```csharp
// SignerClient.cs:244-263
public async Task<(string? txInfo, string? error)> CancelAllOrdersAsync(
    int timeInForce = 0,      // ImmediateCancelAll
    long cancelTimestampMs = 0)  // Time parameter
{
    // ...
    var result = NativeMethods.SignCancelAllOrders(
        timeInForce,           // 0 = ImmediateCancelAll
        cancelTimestampMs,     // 0 (passed through)
        nonce,
        ApiKeyIndex,
        AccountIndex);
    // ...
}
```

**This looks correct:** `timeInForce=0` + `time=0` should satisfy `ImmediateCancelAll` validation.

---

## Possible Causes of Error 21501

### 1. NO ORDERS TO CANCEL (Most Likely)

**Hypothesis:** The server returns 21501 when attempting to cancel orders but there are no orders to cancel.

**Evidence:**
- Error occurs after "bot incorrectly detects all orders as filled"
- The grid rebuild logic tries to cancel all orders before placing new ones
- If orders were already filled/cancelled, there's nothing to cancel

**Validation:** Check if orders exist before calling `CancelAllOrdersAsync`:
```csharp
var activeOrders = await _queryClient.GetActiveOrdersAsync(accountIndex, marketId, authToken, ct);
if (activeOrders.Count == 0)
{
    _logger.LogInformation("No orders to cancel for market {MarketId}", marketId);
    return; // Skip cancellation
}
```

### 2. EXPIRY TIMESTAMP ISSUE

**Hypothesis:** The native signer might be setting `ExpiredAt` incorrectly.

The `L2CancelAllOrdersTxInfo` struct has two time fields:
- `Time` - For the cancellation operation itself
- `ExpiredAt` - When the transaction request expires

**Validation required:** Must be `0 <= ExpiredAt <= MaxTimestamp`

If the native library sets `ExpiredAt` to an invalid value (e.g., negative, or beyond MaxTimestamp), this would cause validation to fail.

### 3. STALE NONCE

**Hypothesis:** Nonce desync could cause the tx to be considered invalid.

However, nonce errors typically return error code **21104** ("invalid nonce"), not 21501.

### 4. QUEUED CANCEL ALL REQUEST

**Hypothesis:** A previous `CancelAllOrders` request is still pending.

Error **21712** specifically handles this case: "account has a queued cancel all orders request"

If the system is in a state where a previous cancel-all is still being processed, subsequent requests fail.

---

## Recommended Fixes

### Fix 1: Pre-Check for Active Orders (PRIMARY FIX)

Before calling `CancelAllOrdersAsync`, verify orders exist:

```csharp
// GridOrderManager.cs - CancelAllGridOrdersAsync
public async Task<int> CancelAllGridOrdersAsync(int marketId, CancellationToken ct = default)
{
    await _orderLock.WaitAsync(ct).ConfigureAwait(false);
    try
    {
        // NEW: Check if there are orders to cancel first
        var (authToken, authError) = await _commandClient.CreateAuthTokenAsync().ConfigureAwait(false);
        if (authError == null && !string.IsNullOrEmpty(authToken))
        {
            var activeOrders = await _queryClient.GetActiveOrdersAsync(
                AccountIndex, marketId, authToken, ct).ConfigureAwait(false);

            if (activeOrders.Count == 0)
            {
                _logger.LogInformation(
                    "No active orders to cancel for market {MarketId}", marketId);
                return 0;  // No orders to cancel - this is not an error
            }

            _logger.LogInformation(
                "Cancelling {Count} active orders for market {MarketId}",
                activeOrders.Count, marketId);
        }

        var response = await _commandClient.CancelAllOrdersAsync(marketId, cancelTimestampMs: 0, ct)
            .ConfigureAwait(false);
        // ... rest of existing logic
    }
    finally
    {
        _orderLock.Release();
    }
}
```

### Fix 2: Handle 21501 as "No Orders" Case

Add specific error handling for 21501:

```csharp
// In CancelAllOrdersAsync or calling code:
if (response.Code == 21501)
{
    // "invalid tx info" - likely no orders to cancel
    _logger.LogInformation(
        "CancelAllOrders returned 21501 for market {MarketId} - treating as no orders to cancel",
        marketId);
    return 0;  // Not a failure state
}
```

### Fix 3: Add Retry with Verification for Concurrent Requests

Handle case where a cancel-all is already queued:

```csharp
if (response.Code == 21712)  // "account has a queued cancel all orders request"
{
    _logger.LogWarning(
        "Cancel all orders already queued for market {MarketId} - waiting for completion",
        marketId);

    await Task.Delay(1000, ct);  // Wait for pending request

    // Re-query to verify status
    var activeOrders = await _queryClient.GetActiveOrdersAsync(...);
    if (activeOrders.Count == 0)
    {
        return -1;  // Success - previous request completed
    }
}
```

---

## Error Code Summary

### Transaction Errors (21500-21512)

| Code | Message | Likely Cause |
|------|---------|--------------|
| 21500 | "transaction not found" | TX hash doesn't exist |
| **21501** | "invalid tx info" | **Malformed TX or no-op operation** |
| 21502 | "marshal tx failed" | Serialization error |
| 21503 | "marshal event failed" | Event serialization error |
| 21504 | "fail to l1 signature" | L1 signature validation failed |
| 21505 | "unsupported tx type" | Unknown transaction type |
| 21506 | "too many pending txs" | Rate limit on pending transactions |
| 21507 | "account below maintenance margin" | Can't execute, liquidation risk |
| 21508 | "account below initial margin" | Can't execute, insufficient margin |
| 21511 | "invalid tx type for account" | Account type restriction |
| 21512 | "invalid l1 request id" | L1 request reference invalid |

### CancelAllOrders Specific Errors (21712-21714)

| Code | Message | Cause |
|------|---------|-------|
| 21712 | "account has a queued cancel all orders request" | Previous cancel-all still pending |
| 21713 | "invalid cancel all time in force" | TimeInForce not 0, 1, or 2 |
| 21714 | "invalid cancel all time" | Time parameter out of valid range |

---

## Recommended Implementation Approach

### Phase 1: Add Pre-Check (Immediate)
1. Before calling `CancelAllOrdersAsync`, query active orders
2. If no orders exist, skip the cancel operation
3. Log appropriately

### Phase 2: Add Error Handling (Immediate)
1. Treat error 21501 as "no orders to cancel" in grid rebuild context
2. Add specific handling for error 21712 (queued request)
3. Don't fail the grid rebuild if cancel fails with these codes

### Phase 3: Add Defensive Validation (Optional)
1. Log the actual txInfo being sent for debugging
2. Consider adding retry logic with backoff for transient errors

---

## Testing Recommendations

1. **Unit Test:** Mock scenario where no orders exist before cancel
2. **Integration Test:** Verify behavior when calling cancel-all with zero orders
3. **Manual Test:** On testnet, call `CancelAllOrdersAsync` when no orders exist

---

## References

- [Lighter API Error Codes](https://apidocs.lighter.xyz/docs/data-structures-constants-and-errors)
- [Lighter Go SDK - CancelAllOrders](https://github.com/elliottech/lighter-go/tree/main/types/txtypes)
- [Lighter Python SDK](https://github.com/elliottech/lighter-python)
- [WebSocket Reference](https://apidocs.lighter.xyz/docs/websocket-reference)

---

## Summary

**Error 21501 "invalid tx info"** most likely occurs because:

1. **The bot is trying to cancel orders when no orders exist** (after incorrectly detecting all orders as filled)
2. The Lighter server validates the cancel-all request and returns 21501 when there's nothing to cancel

**Primary Fix:** Check for active orders before calling `CancelAllOrdersAsync`. If no orders exist, skip the cancellation operation.

**Secondary Fix:** Handle error 21501 gracefully in the grid rebuild logic - treat it as "no orders to cancel" rather than a failure.
