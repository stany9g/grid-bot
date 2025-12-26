# GridBot.Extended Trading Safety Audit

**Audit Date:** 2025-12-26
**Auditor:** Trading Bot Auditor Agent
**Scope:** GridBot.Extended project - Starknet DEX integration
**Risk Framework:** Crypto Trading Safety

---

## Executive Summary

The GridBot.Extended integration demonstrates **strong architectural patterns** for trading safety. The codebase shows awareness of critical DEX integration challenges including asynchronous order confirmation, nonce management, rate limiting, and WebSocket reliability.

**Overall Assessment:** CONDITIONAL PASS - Production-ready with 3 HIGH severity fixes required.

---

## FINDINGS

### 1. Order State Machine Completeness

**Risk Level:** HIGH
**Category:** Race Condition | Order Safety
**Location:** `ExtendedOrderAdapter.cs:81-157` and `ExtendedRealtimeAdapter.cs:308-324`
**Financial Impact:** Orders could be executed multiple times if HTTP success is trusted without WS confirmation

**Problem:**
The order state machine correctly tracks PendingConfirmation state, but there is a race condition window between HTTP response and WebSocket confirmation where the bot could submit duplicate orders.

**Evidence:**
```csharp
// ExtendedOrderAdapter.cs:126-138
var response = await _httpClient.CreateOrderAsync(extendedRequest, ct);

if (response.Success && response.Order != null)
{
    localOrder.ExchangeOrderId = response.Order.Id;
    // CRITICAL: HTTP 200 does NOT mean order is active!
    // Keep in PendingConfirmation until WebSocket confirms
    return OrderResult.PendingConfirmation(clientOrderId, response.Order.Id);
}
```

The code correctly recognizes this issue (comment on line 132), but there is no mechanism to prevent duplicate order submission if the caller retries before WebSocket confirmation arrives. The `MaxPendingOrdersPerMarket` check on line 93-102 is per-market but does not track order idempotency.

**Missing Safety:**
1. No idempotency key tracking - if caller retries with same parameters, a duplicate order could be created
2. No timeout mechanism to transition PendingConfirmation -> Rejected if no WebSocket confirmation arrives
3. `CleanupOrphanedOrders()` is defined but never called automatically

**Fix Required:**
- Add automatic periodic cleanup of orphaned orders via a background timer
- Add idempotency key tracking to prevent duplicate submission with same parameters
- Add explicit timeout transition from PendingConfirmation to Unknown/Failed state

**Verdict:** FAIL - Requires implementation of automatic orphan cleanup and idempotency keys

---

### 2. Nonce Consumption on HTTP Failure

**Risk Level:** HIGH
**Category:** Precision | Safety
**Location:** `NonceManager.cs:84-117` and `ExtendedOrderAdapter.cs:341`
**Financial Impact:** Nonce exhaustion, blocked order submission, potential account lockout

**Problem:**
The nonce is consumed (incremented) in `BuildOrderRequest()` BEFORE the HTTP call, but if the HTTP call fails for network reasons (not API rejection), the nonce is permanently consumed. This is correct behavior for Stark signatures (nonces cannot be reused), but the code does not handle this edge case properly.

**Evidence:**
```csharp
// ExtendedOrderAdapter.cs:338-375
private Models.Api.CreateOrderRequest BuildOrderRequest(CreateOrderRequest request, string clientOrderId)
{
    // Get nonce for this order - THIS CONSUMES THE NONCE
    var nonce = _nonceManager.GetNextNonce();
    // ... build request ...
}

// Called from CreateOrderAsync
var extendedRequest = BuildOrderRequest(request, clientOrderId);  // Nonce consumed
var response = await _httpClient.CreateOrderAsync(extendedRequest, ct);  // Could fail
```

If `CreateOrderAsync` throws an exception (timeout, network error), the nonce is consumed but the server never saw the order. The next order will use nonce+1, leaving a gap. This is technically correct (gaps are allowed), but:

1. No logging of consumed-but-never-sent nonces for auditing
2. No tracking of nonce "waste rate" for monitoring
3. No mechanism to re-sync nonce if gap becomes too large

**Fix Required:**
- Log when a nonce is consumed but HTTP fails (for audit trail)
- Track nonce waste rate and alert if excessive (indicates network instability)
- Add periodic nonce re-sync from server (currently only synced on connect)

**Verdict:** CONDITIONAL PASS - Behavior is correct but needs monitoring/alerting

---

### 3. Stark Signature Not Implemented

**Risk Level:** CRITICAL
**Category:** Safety
**Location:** `ExtendedOrderAdapter.cs:352-358`
**Financial Impact:** 100% of orders will be rejected by the exchange

**Problem:**
The Stark signature implementation is stubbed out with placeholder values. Orders cannot be submitted without valid signatures.

**Evidence:**
```csharp
// ExtendedOrderAdapter.cs:352-358
var settlement = new SettlementObject
{
    StarkKey = _options.StarkPublicKey,
    R = "0x0", // TODO: Implement actual Stark signature
    S = "0x0", // TODO: Implement actual Stark signature
    Nonce = nonce
};
```

**Impact:** All trading operations will fail. This is a **development blocker**.

**Fix Required:**
- Implement Stark signature using StarkSharp, starknet.net, or native Rust bindings
- Follow EIP-712 + Stark key derivation as documented in session context

**Verdict:** FAIL - Blocking issue for production

---

### 4. Rate Limiter Does Not Record All Requests

**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `ExtendedHttpClient.cs:259`
**Financial Impact:** Rate limit violations leading to temporary account ban

**Problem:**
The rate limiter's `RecordRequest()` is only called after the HTTP response is received. If the HTTP request hangs or times out, the request is still counted by the exchange but not by the local rate limiter.

**Evidence:**
```csharp
// ExtendedHttpClient.cs:256-259
using var response = await _httpClient.SendAsync(request, ct);

_rateLimiter.RecordRequest();  // Only recorded after response
```

If the request times out after the exchange receives it, our local counter is too low.

**Fix Required:**
- Call `RecordRequest()` BEFORE `SendAsync()`, not after
- Alternatively, track in-flight requests separately

**Verdict:** FAIL - Needs reordering of rate limit recording

---

### 5. Cancel Order Priority Correctly Configured

**Risk Level:** N/A - PASS
**Category:** Safety
**Location:** `ExtendedHttpClient.cs:177-178`
**Financial Impact:** N/A

**Evidence:**
```csharp
// ExtendedHttpClient.cs:177-178
$"/user/order/{Uri.EscapeDataString(orderId)}",
RequestPriority.Critical,
```

Cancel orders are correctly marked as `Critical` priority, ensuring they bypass soft rate limits. This is correct safety behavior.

**Verdict:** PASS

---

### 6. WebSocket Reconnection with Exponential Backoff

**Risk Level:** LOW
**Category:** Performance
**Location:** `ExtendedWebSocketClient.cs:746-752`
**Financial Impact:** Extended downtime during network issues

**Problem:**
The exponential backoff implementation is correct, but `MaxReconnectAttempts` defaults to `int.MaxValue`. While this ensures persistent reconnection attempts, combined with backoff up to 60 seconds, it could result in very long outages before manual intervention.

**Evidence:**
```csharp
// WebSocketOptions.cs:55
public int MaxReconnectAttempts { get; set; } = int.MaxValue;

// ExtendedWebSocketClient.cs:746-752
private TimeSpan CalculateBackoff(int attempt)
{
    var exponentialDelay = Math.Min(
        _wsOptions.MaxReconnectDelayMs,
        _wsOptions.ReconnectDelayMs * Math.Pow(2, attempt - 1));
    var jitter = Random.Shared.Next(0, 500);
    return TimeSpan.FromMilliseconds(exponentialDelay + jitter);
}
```

After 6 attempts: 1s, 2s, 4s, 8s, 16s, 32s = 63 seconds before reaching max backoff.

**Recommendation:**
- Add alerting after N consecutive failures (e.g., 10 attempts)
- Consider circuit breaker pattern to escalate to REST-only mode

**Verdict:** CONDITIONAL PASS - Works correctly but add monitoring

---

### 7. Data Staleness Detection Implemented

**Risk Level:** N/A - PASS
**Category:** Safety
**Location:** `ExtendedWebSocketClient.cs:426-435` and `WebSocketOptions.cs:59`
**Financial Impact:** N/A

**Evidence:**
```csharp
// ExtendedWebSocketClient.cs:427-435
var timeSinceLastMessage = TimeSinceLastMessage;
if (timeSinceLastMessage.HasValue &&
    timeSinceLastMessage.Value.TotalSeconds > _wsOptions.DataStalenessThresholdSeconds)
{
    _logger.LogWarning(
        "Data staleness detected: {Seconds}s since last message (threshold: {Threshold}s)",
        timeSinceLastMessage.Value.TotalSeconds,
        _wsOptions.DataStalenessThresholdSeconds);
}
```

Staleness detection is implemented with configurable threshold (default 5 seconds). The `IsDataStale` property in `ExtendedRealtimeAdapter.cs:62` allows callers to check before trading.

**Verdict:** PASS

---

### 8. Decimal Precision Correctly Used

**Risk Level:** N/A - PASS
**Category:** Precision
**Location:** `ExtendedScalingAdapter.cs:28-97`
**Financial Impact:** N/A

**Evidence:**
```csharp
// All price and quantity values use decimal
public long ScalePrice(decimal price, MarketScaling scaling)
public long ScaleAmount(decimal amount, MarketScaling scaling)
public decimal UnscalePrice(long scaledPrice, MarketScaling scaling)
public decimal UnscaleAmount(long scaledAmount, MarketScaling scaling)
```

All financial calculations use `decimal` type. String parsing uses `CultureInfo.InvariantCulture` throughout:

```csharp
// ExtendedScalingAdapter.cs:151
return price.ToString($"F{decimals}", System.Globalization.CultureInfo.InvariantCulture);
```

**Verdict:** PASS

---

### 9. Quantity Rounding Direction

**Risk Level:** N/A - PASS
**Category:** Precision
**Location:** `ExtendedScalingAdapter.cs:78-79` and `ExtendedScalingAdapter.cs:138-139`
**Financial Impact:** N/A

**Evidence:**
```csharp
// ExtendedScalingAdapter.cs:78-79
// Round DOWN for amounts to never exceed intended quantity
return (long)Math.Floor(amount * scaling.AmountScale);

// ExtendedScalingAdapter.cs:138-139
// Always round down
return Math.Floor(quantity / stepSize) * stepSize;
```

Quantities are correctly rounded DOWN to prevent exceeding intended position size.

**Verdict:** PASS

---

### 10. Price Rounding Direction

**Risk Level:** N/A - PASS
**Category:** Precision
**Location:** `ExtendedScalingAdapter.cs:107-122`
**Financial Impact:** N/A

**Evidence:**
```csharp
// ExtendedScalingAdapter.cs:114-122
// Round to tick size, rounding toward less aggressive price
// Buy: round down, Sell: round up
if (isBuy)
{
    return Math.Floor(price / tickSize) * tickSize;
}
else
{
    return Math.Ceiling(price / tickSize) * tickSize;
}
```

Price rounding correctly rounds toward less aggressive prices (buy down, sell up), preventing unfavorable fills.

**Verdict:** PASS

---

### 11. Order Cache Thread Safety

**Risk Level:** MEDIUM
**Category:** Race Condition
**Location:** `ExtendedRealtimeAdapter.cs:366-408`
**Financial Impact:** Inconsistent order state, potential double execution

**Problem:**
The order cache uses `ConcurrentDictionary<string, List<OrderInfo>>`, but the `List<OrderInfo>` values are not thread-safe. The `UpdateOrderCache` method modifies the list without synchronization.

**Evidence:**
```csharp
// ExtendedRealtimeAdapter.cs:27
private readonly ConcurrentDictionary<string, List<OrderInfo>> _orders = new();

// ExtendedRealtimeAdapter.cs:373-407
var orders = _orders[e.MarketId];
var existing = orders.FindIndex(o => o.OrderId == e.OrderId);

if (e.Status is "filled" or "cancelled" or "rejected")
{
    if (existing >= 0)
    {
        orders.RemoveAt(existing);  // NOT THREAD SAFE!
    }
}
```

The `ConcurrentDictionary` protects dictionary access, but the `List<T>` inside is accessed without locking. Multiple WebSocket messages for the same market could corrupt the list.

**Fix Required:**
- Replace `List<OrderInfo>` with `ConcurrentDictionary<string, OrderInfo>` keyed by OrderId
- Or add explicit locking around list modifications
- Or use `ImmutableList<T>` with atomic replacement

**Verdict:** FAIL - Thread safety bug in order cache

---

### 12. Partial Fill Handling

**Risk Level:** LOW
**Category:** Safety
**Location:** `ExtendedRealtimeAdapter.cs:308-324` and `ExtendedWebSocketClient.cs:574-600`
**Financial Impact:** Potential position tracking errors

**Problem:**
Partial fills are handled at the WebSocket level, but there is no explicit validation that `FilledSize` is monotonically increasing. A malformed or replayed message could cause incorrect position tracking.

**Evidence:**
```csharp
// ExtendedWebSocketClient.cs:594
FilledSize = ParseDecimal(order.FilledQty),
```

The code trusts the server's `FilledQty` without validating it increased from the previous update.

**Recommendation:**
- Cache previous `FilledSize` per order
- Validate that new `FilledSize >= previousFilledSize`
- Log warning if decrease detected (indicates data corruption)

**Verdict:** CONDITIONAL PASS - Add validation for production

---

### 13. Cancel Request Silent Failure Scenario

**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `ExtendedOrderAdapter.cs:183-223`
**Financial Impact:** Orders may remain active when user believes they are cancelled

**Problem:**
The cancel logic treats HTTP 404 as "already cancelled" which is correct. However, if the cancel request returns HTTP 200 but the order is actually in PendingConfirmation state, there is no confirmation that the cancel was processed.

**Evidence:**
```csharp
// ExtendedOrderAdapter.cs:196-206
var success = await _httpClient.CancelOrderAsync(orderId, ct);

if (success)
{
    // Update local state if tracked
    if (_pendingOrders.TryGetValue(orderId, out var localOrder))
    {
        localOrder.State = LocalOrderState.Cancelled;
    }

    return OrderResult.Success(orderId, "CANCELLED");
}
```

Cancel assumes HTTP 200 = cancelled, but Extended processes orders asynchronously. The cancel could be rejected after the HTTP response.

**Fix Required:**
- Track cancel requests in PendingCancellation state
- Wait for WebSocket confirmation that order is actually cancelled
- Add timeout mechanism for cancel confirmation

**Verdict:** FAIL - Cancel confirmation not async-aware

---

### 14. Duplicate Order Prevention

**Risk Level:** HIGH
**Category:** Safety
**Location:** `ExtendedOrderAdapter.cs:93-103`
**Financial Impact:** Double position size, double exposure

**Problem:**
The pending order limit (`MaxPendingOrdersPerMarket = 50`) prevents flooding, but there is no idempotency mechanism. If a client submits the same order twice with different client order IDs, both will be processed.

**Evidence:**
```csharp
// ExtendedOrderAdapter.cs:108
var clientOrderId = Guid.NewGuid().ToString("N");
```

Every order gets a new GUID, so there is no way to detect duplicates.

**Fix Required:**
- Accept optional client idempotency key in `CreateOrderRequest`
- Track recently submitted idempotency keys (e.g., last 5 minutes)
- Reject orders with duplicate idempotency keys

**Verdict:** FAIL - No idempotency protection

---

### 15. WebSocket Channel Backpressure Configuration

**Risk Level:** N/A - PASS
**Category:** Performance
**Location:** `WebSocketOptions.cs:49-50` and `ExtendedWebSocketClient.cs:114-129`
**Financial Impact:** N/A

**Evidence:**
```csharp
// WebSocketOptions.cs:49-50
public BoundedChannelFullMode FullMode { get; set; } = BoundedChannelFullMode.DropOldest;

// ExtendedWebSocketClient.cs:115-121
var channelOptions = new BoundedChannelOptions(_wsOptions.ChannelCapacity)
{
    FullMode = _wsOptions.FullMode,
    SingleReader = false,
    SingleWriter = true,
    AllowSynchronousContinuations = false
};
```

Correct use of `DropOldest` for trading data - stale data is useless. Channel capacity of 100 provides reasonable buffer.

**Verdict:** PASS

---

### 16. Position Reconciliation on Reconnect

**Risk Level:** N/A - PASS
**Category:** Safety
**Location:** `ExtendedRealtimeAdapter.cs:241-283` and `ExtendedConnectionAdapter.cs:213-229`
**Financial Impact:** N/A

**Evidence:**
```csharp
// ExtendedConnectionAdapter.cs:217-225
try
{
    // Re-subscribe to account stream
    await _wsClient.SubscribeAccountAsync(default);

    // Reconcile state via REST
    await _realtimeAdapter.ReconcileStateAsync(default);
}
```

State reconciliation via REST API after WebSocket reconnection is correctly implemented.

**Verdict:** PASS

---

### 17. Market Mapper Initialization Check

**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `ExtendedScalingAdapter.cs:33-37`
**Financial Impact:** Orders with wrong precision could be submitted

**Problem:**
If an order is submitted for a market before `InitializeAsync` completes, the scaling adapter throws an exception. This is correct fail-safe behavior, but the error message could be more helpful.

**Evidence:**
```csharp
// ExtendedScalingAdapter.cs:33-37
var marketInfo = _marketMapper.GetMarketInfo(marketId);
if (marketInfo == null)
{
    throw new InvalidOperationException($"Market {marketId} not found. Ensure market mapper is initialized.");
}
```

**Verdict:** PASS - Correct fail-safe behavior

---

## Missing Safety Features (Not Implemented)

### A. Kill Switch Mechanism
No emergency shutdown mechanism is visible in the codebase. A kill switch should:
- Immediately cancel all open orders
- Disable new order submission
- Be accessible via API, signal, or watchdog

### B. Order Size Validation
No validation that order size is within:
- Market minimum order size
- Maximum order size
- User-defined position limits

### C. Price Bounds Validation
No validation that order price is within reasonable bounds (e.g., within X% of mark price).

### D. Self-Trade Prevention
No mechanism to prevent orders that would trade against own existing orders.

### E. Margin/Collateral Pre-Check
No pre-submission validation that account has sufficient collateral for the order.

---

## AUDIT SUMMARY

```
===============================================
AUDIT SUMMARY
===============================================
Total Findings: 17

HIGH Risk (BLOCKING):
  1. Order state machine lacks auto-cleanup
  2. Stark signature not implemented (CRITICAL)
  4. Rate limiter records after response, not before
 11. Order cache thread safety bug
 13. Cancel confirmation not async-aware
 14. No order idempotency protection

MEDIUM Risk:
  2. Nonce consumption monitoring needed
  6. WebSocket reconnection alerting
 12. Partial fill validation
 17. Market mapper initialization (handled correctly)

LOW Risk:
  (None standalone, issues consolidated above)

PASS:
  5. Cancel order priority correctly configured
  7. Data staleness detection implemented
  8. Decimal precision correctly used
  9. Quantity rounding direction correct
 10. Price rounding direction correct
 15. WebSocket channel backpressure configured
 16. Position reconciliation on reconnect

Overall Verdict: CONDITIONAL PASS
===============================================

Deployment Recommendation:
--------------------------
DO NOT DEPLOY to production until:

1. [CRITICAL] Implement Stark signature (Finding #3)
   - Current: All orders will be rejected
   - Required: Integrate StarkSharp or native library

2. [HIGH] Fix rate limiter ordering (Finding #4)
   - Move RecordRequest() before SendAsync()

3. [HIGH] Fix order cache thread safety (Finding #11)
   - Replace List<OrderInfo> with ConcurrentDictionary

4. [HIGH] Add order idempotency (Finding #14)
   - Prevent duplicate orders from same intent

5. [HIGH] Make cancel async-aware (Finding #13)
   - Track PendingCancellation state

AFTER fixes, system is suitable for:
- Paper trading / testnet with limited capital
- Gradual production rollout with monitoring

===============================================
```

---

## Appendix: Code Quality Notes

### Positive Patterns Observed:
- Consistent use of `CultureInfo.InvariantCulture` for parsing
- Bounded channels with appropriate backpressure
- Logging at appropriate levels
- Clean separation of concerns (adapters, clients, managers)
- DryRun mode for testing
- Proper cancellation token propagation

### Technical Debt:
- TODO comment for Stark signature implementation
- No unit tests visible in audit scope
- Some magic numbers could be constants (e.g., 500ms delay in `ConnectAsync`)

---

*Report generated by Trading Bot Auditor Agent*
*GridBot.Extended v1.0 Pre-Production Audit*
