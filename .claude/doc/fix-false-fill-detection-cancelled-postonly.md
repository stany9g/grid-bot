# Fix: False Fill Detection for Cancelled PostOnly Orders

## Date
2025-12-14

## Problem
When a PostOnly order was cancelled (due to crossing the spread), `GridOrderManager.SyncOrderStatusAsync` incorrectly marked it as "filled" because it only checked if the order existed in active orders - it didn't distinguish between cancelled and filled.

### Log example showing the bug:
```
2025-12-14 12:54:31.958 +00:00 [INF] GridBot.ApiService | Detected fill at bid level 1, price 89569.6
```
But this order was actually CANCELLED, not filled.

### Root Cause
The original fill detection logic assumed: "order disappeared from active orders = filled"

But orders can disappear because they were:
1. **FILLED** - genuinely executed
2. **CANCELLED** - PostOnly order crossed the spread and was rejected

## Solution
Track the final status of removed orders in a cache within `LighterRealtimeStateService`, then use that cache in `GridOrderManager` to distinguish between filled and cancelled orders.

## Files Modified

### 1. `GridBot.Lighter/ILighterRealtimeState.cs`
Added new interface method:
```csharp
string? GetRemovedOrderStatus(int marketId, long clientOrderIndex);
```
Returns the final status ("filled", "cancelled", etc.) of a recently removed order.

### 2. `GridBot.Lighter/LighterRealtimeStateService.cs`
Added:
- **Removed order cache**: `ConcurrentDictionary` storing recently removed orders by `ClientOrderIndex` with their final status and removal timestamp
- **60-second TTL**: Orders are kept in cache for 60 seconds, then cleaned up
- **`GetRemovedOrderStatus()`**: Looks up the final status from the cache
- **`CleanupRemovedOrdersCache()`**: Removes expired entries, called during order delta processing
- **`ClearAllRemovedOrdersCache()`**: Clears cache on disconnect

Updated:
- **`ApplyOrderDelta()`**: When an order is removed, stores its `ClientOrderIndex` and final status in the cache
- **Disconnect handler**: Now also clears the removed orders cache

### 3. `GridBot.ApiService/Services/Grid/GridOrderManager.cs`
Added:
- **Constructor parameter**: `ILighterRealtimeState realtimeState`
- **Updated fill detection**: Now calls `GetRemovedOrderStatus()` to check the actual status

New fill detection logic:
```csharp
var finalStatus = _realtimeState.GetRemovedOrderStatus(marketId, level.ClientOrderIndex.Value);

if (finalStatus == "cancelled")
{
    // Reset to Pending for retry
    level.Status = GridLevelStatus.Pending;
    level.OrderId = null;
    level.ClientOrderIndex = null;
    level.PartialFillPercent = 0m;
    // Log as CANCELLED
}
else
{
    // Status is "filled" or unknown (assume filled for safety)
    level.Status = GridLevelStatus.Filled;
    level.PartialFillPercent = 100m;
}
```

## Expected Outcome
- Cancelled PostOnly orders are correctly detected as cancelled, NOT filled
- Grid levels are reset to `Pending` so they can be retried on the next cycle
- Logging clearly distinguishes "CANCELLED" from actual fills with specific messages
- Genuine fills continue to work as before

## Testing Notes
- The WebSocket already sends the order status when removing orders
- The cache uses `ClientOrderIndex` as the key since that's what `GridOrderManager` uses for matching
- The 60-second TTL is long enough for sync cycles but short enough to not waste memory
- Cache cleanup happens during normal order delta processing (incremental deltas)
