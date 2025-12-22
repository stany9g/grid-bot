# Session 7: Fix False Fill Detection for Cancelled PostOnly Orders

## Date
2025-12-14

## Status
**COMPLETE** - Fix already implemented, build verified

## Problem
When a PostOnly order is cancelled (due to crossing the spread), `GridOrderManager.SyncOrderStatusAsync` incorrectly marked it as "filled" because it only checked if the order exists in active orders - it didn't distinguish between cancelled and filled.

Log example showing the bug:
```
2025-12-14 12:54:31.958 +00:00 [INF] GridBot.ApiService | Detected fill at bid level 1, price 89569.6
```
But this order was actually CANCELLED, not filled.

## Root Cause
In `GridOrderManager.cs`, the code assumed: "order disappeared from active orders = filled"
But orders can disappear because they were CANCELLED (PostOnly crossing) too!

## Solution Implemented (Already in Codebase)

### 1. ILighterRealtimeState.cs (lines 183-190)
Added interface method to query removed order status:
```csharp
/// <summary>
/// Gets the final status of a recently removed order.
/// Returns null if the order is not in the removed cache (either still active or cache expired).
/// Used to distinguish between filled and cancelled orders in grid sync.
/// </summary>
string? GetRemovedOrderStatus(int marketId, long clientOrderIndex);
```

### 2. LighterRealtimeStateService.cs
- Added removed order tracking cache (lines 48-52):
```csharp
private readonly ConcurrentDictionary<int, ConcurrentDictionary<long, (string Status, DateTimeOffset RemovedAt)>> _recentlyRemovedOrders = new();
private static readonly TimeSpan RemovedOrderCacheTtl = TimeSpan.FromSeconds(60);
```

- Implemented tracking in `ApplyOrderDelta` method (lines 698-722):
  - When an order is removed (status = "cancelled" or "filled"), cache its final status with ClientOrderIndex as key
  - Cache entries expire after 60 seconds

- Implemented `GetRemovedOrderStatus` method (lines 883-895):
  - Returns the final status if order was recently removed
  - Returns null if not found or expired

- Added cleanup logic (lines 901-923) that runs during order delta processing

### 3. GridOrderManager.cs (lines 506-540)
Updated `SyncOrderStatusAsync` to use the new status tracking:
```csharp
else if (level.Status == GridLevelStatus.Active)
{
    // Order was active but no longer in active orders
    // Check WebSocket cache to determine if it was filled or cancelled
    var finalStatus = _realtimeState.GetRemovedOrderStatus(marketId, level.ClientOrderIndex.Value);

    if (finalStatus == "cancelled")
    {
        // Order was cancelled (e.g., PostOnly crossing) - NOT filled
        // Reset to Pending so it can be retried on the next cycle
        level.Status = GridLevelStatus.Pending;
        level.OrderId = null;
        level.ClientOrderIndex = null;
        level.PartialFillPercent = 0m;

        _logger.LogWarning(
            "Order CANCELLED (not filled) at {Side} level {Index}, price {Price}. " +
            "Likely PostOnly rejection - order crossed the spread.",
            ...);
    }
    else
    {
        // Status is "filled" or unknown (assume filled for safety)
        level.Status = GridLevelStatus.Filled;
        level.PartialFillPercent = 100m;
        _logger.LogInformation(
            "Detected fill at {Side} level {Index}, price {Price} (status: {Status})",
            ...);
    }
}
```

## Additional Fix During This Session

Fixed compilation error in `PreTradeValidator.cs` - the `ValidatePostOnlyPriceAsync` method was using incorrect property names:
- Changed `orderBook.BestBid` to `orderBook.BestBidPrice`
- Changed `orderBook.BestAsk` to `orderBook.BestAskPrice`

## Files Modified
1. `GridBot.Lighter/ILighterRealtimeState.cs` - GetRemovedOrderStatus method (already existed)
2. `GridBot.Lighter/LighterRealtimeStateService.cs` - Removed order tracking cache (already implemented)
3. `GridBot.ApiService/Services/Grid/GridOrderManager.cs` - Fill vs cancel detection (already implemented)
4. `GridBot.ApiService/Services/Validation/PreTradeValidator.cs` - Fixed property names for BestBid/BestAsk

## Expected Outcome
- Cancelled PostOnly orders are correctly detected as cancelled, not filled
- Grid levels are reset to Pending so they can be retried
- Logging clearly distinguishes "CANCELLED" from actual fills

## Build Status
**SUCCESS** - All projects build without errors
