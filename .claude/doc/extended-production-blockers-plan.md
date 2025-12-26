# Extended DEX Production Blockers - Implementation Plan

**Created:** 2025-12-26
**Status:** ✅ COMPLETED
**Completed:** 2025-12-26
**Priority:** Must complete before production deployment

---

## Overview

This document outlines the implementation plan for fixing the remaining production blockers identified in the trading bot audit. These issues must be resolved before the Extended DEX integration can be deployed to production.

---

## Blocker 1: Rate Limiter Ordering

**Severity:** HIGH
**File:** `GridBot.Extended/ExtendedHttpClient.cs:259`
**Issue:** `RecordRequest()` is called AFTER `SendAsync()` returns, not accounting for in-flight requests.

### Problem
```csharp
// Current (WRONG):
using var response = await _httpClient.SendAsync(request, ct);
_rateLimiter.RecordRequest();  // Only recorded after response
```

If the HTTP request hangs or times out after the exchange receives it, our local counter is too low.

### Solution
Move `RecordRequest()` before the HTTP call:

```csharp
// Fixed:
_rateLimiter.RecordRequest();  // Record BEFORE sending
try
{
    using var response = await _httpClient.SendAsync(request, ct);
    _rateLimiter.RecordSuccessfulRequest();
    // ... handle response
}
catch (Exception)
{
    // Request was recorded but failed - that's correct behavior
    // The rate limit should still count it
    throw;
}
```

### Implementation Steps
1. Open `ExtendedHttpClient.cs`
2. In `SendRequestAsync` method, move `RecordRequest()` before `SendAsync()`
3. Add `RecordSuccessfulRequest()` call on success (to clear consecutive 429 counter)
4. Test with simulated timeouts to verify counting

### Estimated Changes
- 1 file modified
- ~10 lines changed

---

## Blocker 2: Order Cache Thread Safety

**Severity:** HIGH
**File:** `GridBot.Extended/Adapters/ExtendedRealtimeAdapter.cs:27`
**Issue:** `ConcurrentDictionary<string, List<OrderInfo>>` - the `List<T>` values are NOT thread-safe.

### Problem
```csharp
// Current (WRONG):
private readonly ConcurrentDictionary<string, List<OrderInfo>> _orders = new();

// In UpdateOrderCache:
var orders = _orders[e.MarketId];
var existing = orders.FindIndex(o => o.OrderId == e.OrderId);  // NOT THREAD SAFE
if (existing >= 0)
{
    orders.RemoveAt(existing);  // NOT THREAD SAFE
}
```

Multiple WebSocket messages for the same market could corrupt the list.

### Solution
Replace `List<OrderInfo>` with `ConcurrentDictionary<string, OrderInfo>` keyed by OrderId:

```csharp
// Fixed:
private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, OrderInfo>> _orders = new();

// In UpdateOrderCache:
var marketOrders = _orders.GetOrAdd(e.MarketId, _ => new ConcurrentDictionary<string, OrderInfo>());

if (e.Status is "filled" or "cancelled" or "rejected")
{
    marketOrders.TryRemove(e.OrderId, out _);
}
else
{
    marketOrders[e.OrderId] = MapOrder(e);
}
```

### Implementation Steps
1. Change `_orders` field type to `ConcurrentDictionary<string, ConcurrentDictionary<string, OrderInfo>>`
2. Update `UpdateOrderCache` method to use thread-safe operations
3. Update `GetActiveOrders` method to return values from inner dictionary
4. Update `ReconcileStateAsync` to properly populate the new structure
5. Update any other methods that access `_orders`

### Estimated Changes
- 1 file modified
- ~30-50 lines changed

---

## Blocker 3: Order Idempotency Protection

**Severity:** HIGH
**File:** `GridBot.Extended/Adapters/ExtendedOrderAdapter.cs:108`
**Issue:** Every order gets a new GUID, no mechanism to detect duplicate submissions.

### Problem
```csharp
// Current (WRONG):
var clientOrderId = Guid.NewGuid().ToString("N");  // Always new ID
```

If a client submits the same order twice (e.g., retry on timeout), both will be processed.

### Solution
Add idempotency key tracking with a time-based cache:

```csharp
// Add field:
private readonly ConcurrentDictionary<string, DateTimeOffset> _recentIdempotencyKeys = new();

// Add method to generate idempotent key from order parameters:
private string GenerateIdempotencyKey(CreateOrderRequest request)
{
    // Hash of: market + side + price + size + reduceOnly
    var input = $"{request.MarketId}|{request.Side}|{request.Price:F8}|{request.Size:F8}|{request.ReduceOnly}";
    using var sha = SHA256.Create();
    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
    return Convert.ToHexString(hash)[..16];  // First 16 chars
}

// In CreateOrderAsync, before creating the order:
var idempotencyKey = GenerateIdempotencyKey(request);
if (_recentIdempotencyKeys.TryGetValue(idempotencyKey, out var lastSubmitted))
{
    if (DateTimeOffset.UtcNow - lastSubmitted < TimeSpan.FromMinutes(1))
    {
        _logger.LogWarning("Duplicate order detected within 1 minute. IdempotencyKey: {Key}", idempotencyKey);
        return OrderResult.Failure("Duplicate order detected. Wait before retrying with same parameters.");
    }
}
_recentIdempotencyKeys[idempotencyKey] = DateTimeOffset.UtcNow;

// Add cleanup in CleanupOrphanedOrders:
var oldKeys = _recentIdempotencyKeys
    .Where(x => DateTimeOffset.UtcNow - x.Value > TimeSpan.FromMinutes(5))
    .Select(x => x.Key)
    .ToList();
foreach (var key in oldKeys)
{
    _recentIdempotencyKeys.TryRemove(key, out _);
}
```

### Implementation Steps
1. Add `_recentIdempotencyKeys` dictionary field
2. Add `GenerateIdempotencyKey` method
3. Add idempotency check at start of `CreateOrderAsync`
4. Add cleanup logic in `CleanupOrphanedOrders`
5. Optionally: Accept explicit idempotency key in `CreateOrderRequest`

### Estimated Changes
- 1 file modified
- ~40 lines added

---

## Blocker 4: Async-Aware Cancel Confirmation

**Severity:** HIGH
**File:** `GridBot.Extended/Adapters/ExtendedOrderAdapter.cs:196-206`
**Issue:** Cancel assumes HTTP 200 = cancelled, but Extended processes cancels asynchronously.

### Problem
```csharp
// Current (WRONG):
var success = await _httpClient.CancelOrderAsync(orderId, ct);
if (success)
{
    localOrder.State = LocalOrderState.Cancelled;  // Assumes it's done
    return OrderResult.Success(orderId, "CANCELLED");
}
```

The cancel could be rejected after the HTTP response.

### Solution
Add `PendingCancellation` state and wait for WebSocket confirmation:

```csharp
// Add to LocalOrderState enum:
PendingCancellation,

// Update CancelOrderAsync:
public async Task<OrderResult> CancelOrderAsync(string marketId, string orderId, CancellationToken ct = default)
{
    // ... validation ...

    try
    {
        // Mark as pending cancellation BEFORE sending
        if (_pendingOrders.TryGetValue(orderId, out var localOrder))
        {
            localOrder.State = LocalOrderState.PendingCancellation;
        }

        var success = await _httpClient.CancelOrderAsync(orderId, ct);

        if (success)
        {
            // HTTP 200 received, but order may not be cancelled yet
            // Return pending status - WebSocket will confirm actual cancellation
            return OrderResult.PendingConfirmation(orderId, "CANCEL_PENDING");
        }
        else
        {
            // Revert state if HTTP failed
            if (localOrder != null)
            {
                localOrder.State = LocalOrderState.Active;
            }
            return OrderResult.Failure("Cancel request failed");
        }
    }
    catch (ExtendedApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
    {
        // Order not found - already cancelled or filled
        return OrderResult.Success(orderId, "ALREADY_CANCELLED");
    }
}

// Add method to confirm cancellation from WebSocket:
public void ConfirmCancellation(string clientOrderId)
{
    if (_pendingOrders.TryGetValue(clientOrderId, out var order))
    {
        if (order.State == LocalOrderState.PendingCancellation)
        {
            order.State = LocalOrderState.Cancelled;
            _logger.LogInformation("Cancel confirmed for order {OrderId}", clientOrderId);
        }
    }
}
```

### Implementation Steps
1. Add `PendingCancellation` to `LocalOrderState` enum
2. Update `CancelOrderAsync` to use pending state
3. Add `ConfirmCancellation` method
4. Update `ExtendedRealtimeAdapter` to call `ConfirmCancellation` on WebSocket cancel event
5. Add timeout handling for pending cancellations in `CleanupOrphanedOrders`

### Estimated Changes
- 2 files modified
- ~50 lines added/changed

---

## Blocker 5: Automatic Orphan Order Cleanup

**Severity:** MEDIUM
**File:** `GridBot.Extended/Adapters/ExtendedOrderAdapter.cs:318-336`
**Issue:** `CleanupOrphanedOrders()` exists but is never called automatically.

### Problem
```csharp
// Method exists but is never called:
public void CleanupOrphanedOrders()
{
    var cutoff = DateTimeOffset.UtcNow.AddSeconds(-ExtendedConstants.OrphanOrderAgeSeconds);
    // ... cleanup logic ...
}
```

Orphaned orders in `PendingConfirmation` state accumulate forever.

### Solution
Add a periodic cleanup timer:

```csharp
// Add fields:
private Timer? _cleanupTimer;
private const int CleanupIntervalSeconds = 30;

// In constructor or Initialize method:
public void StartCleanupTimer()
{
    _cleanupTimer = new Timer(
        _ => CleanupOrphanedOrders(),
        null,
        TimeSpan.FromSeconds(CleanupIntervalSeconds),
        TimeSpan.FromSeconds(CleanupIntervalSeconds));
}

// Add disposal:
public void StopCleanupTimer()
{
    _cleanupTimer?.Dispose();
    _cleanupTimer = null;
}
```

### Alternative Solution
Call cleanup from `ExtendedRealtimeAdapter` processing loop:

```csharp
// In ExtendedRealtimeAdapter, add periodic cleanup call:
private async Task ProcessOrderUpdatesAsync(CancellationToken ct)
{
    var lastCleanup = DateTimeOffset.UtcNow;

    await foreach (var update in _wsClient.OrderUpdates.ReadAllAsync(ct))
    {
        // Process update...

        // Periodic cleanup every 30 seconds
        if (DateTimeOffset.UtcNow - lastCleanup > TimeSpan.FromSeconds(30))
        {
            _orderAdapter.CleanupOrphanedOrders();
            lastCleanup = DateTimeOffset.UtcNow;
        }
    }
}
```

### Implementation Steps
1. Choose approach (Timer or processing loop integration)
2. Add timer/cleanup call
3. Ensure proper disposal
4. Add logging for cleanup operations

### Estimated Changes
- 1-2 files modified
- ~20 lines added

---

## Implementation Order

Recommended order based on dependencies and risk:

| Order | Blocker | Reason |
|-------|---------|--------|
| 1 | Rate Limiter Ordering | Simple fix, prevents rate limit violations |
| 2 | Order Cache Thread Safety | Critical for data integrity, no dependencies |
| 3 | Orphan Cleanup Timer | Enables proper state management |
| 4 | Order Idempotency | Prevents duplicate orders |
| 5 | Async Cancel Confirmation | Depends on cleanup timer for timeout handling |

---

## Testing Plan

### Unit Tests
- Rate limiter records requests before sending
- Order cache handles concurrent updates correctly
- Idempotency key generation is deterministic
- Duplicate orders within 1 minute are rejected
- Cancel state transitions are correct

### Integration Tests
- Submit order, verify pending state, receive WebSocket confirmation
- Submit duplicate order, verify rejection
- Cancel order, verify pending cancel state, receive WebSocket confirmation
- Orphaned orders are cleaned up after timeout

### Manual Testing
- Run with simulated network delays
- Test reconnection scenarios
- Verify state consistency after WebSocket reconnect

---

## Rollout Plan

1. **Phase 1: Testnet Only**
   - Deploy all fixes to testnet
   - Run with limited capital
   - Monitor for 48 hours

2. **Phase 2: Mainnet Paper Trading**
   - Enable DryRun mode on mainnet
   - Verify order flow without real execution
   - Monitor for 24 hours

3. **Phase 3: Mainnet Limited**
   - Deploy with position size limits
   - Start with 10% of intended capital
   - Monitor for 1 week

4. **Phase 4: Full Production**
   - Remove position limits
   - Full monitoring and alerting
   - Gradual capital increase

---

## Success Criteria

**Implementation Complete (2025-12-26):**
- [x] Blocker 1: Rate limiter records requests before sending
- [x] Blocker 2: Order cache uses thread-safe nested ConcurrentDictionary
- [x] Blocker 3: Orphan cleanup timer runs every 30 seconds
- [x] Blocker 4: Order idempotency protection with 1-minute deduplication
- [x] Blocker 5: Cancel requests use PendingCancellation state with WebSocket confirmation
- [x] Full solution builds with 0 errors, 0 warnings

**Testing Verification (Pending):**
- [ ] All unit tests pass
- [ ] All integration tests pass
- [ ] No duplicate orders in 48-hour testnet run
- [ ] No orphaned orders accumulate beyond timeout
- [ ] Cancel operations complete within 5 seconds
- [ ] Rate limit violations: 0
- [ ] Order cache corruption: 0 incidents
