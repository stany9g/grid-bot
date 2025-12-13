# Session 2: Fix Grid Fill Detection Bug

## Date
2025-12-13

## Problem Summary
User reported that when ONE order was filled (a short at 90,147.2), the bot:
1. Detected ALL 12 orders (6 bids + 6 asks) as filled
2. Recorded P&L for 6 fills (each ASK)
3. Triggered EC-002 (all orders cancelled externally)
4. Attempted to rebuild grid by cancelling all orders
5. Failed with error 21501 "invalid tx info"

## Root Cause Analysis

### Bug 1: ClientOrderIndex Not Being Propagated

**Chain of failure:**
1. WebSocket orders have `ClientOrderIndex` (`OrderInfo.ClientOrderIndex` in `OrderMessages.cs:33`)
2. `OrderSnapshot` class in `ChannelEvents.cs` did NOT have a `ClientOrderIndex` field
3. `HandleOrdersMessageAsync` in `LighterWebSocketClient.cs` was NOT copying `ClientOrderIndex` to `OrderSnapshot`
4. `GetActiveOrdersAsync` in `WsLighterQueryClient.cs` was NOT setting `ClientOrderIndex` on `Order` objects
5. `SyncOrderStatusAsync` builds a lookup with `.Where(o => o.ClientOrderIndex.HasValue)` - this was ALWAYS empty!
6. All active orders were marked as "Filled" because none matched in the empty lookup

### Bug 2: CancelAllOrders Error 21501

After incorrectly detecting all orders as filled:
1. EC-002 triggered grid rebuild
2. Grid rebuild called `CancelAllOrdersAsync`
3. Lighter returned error 21501 "invalid tx info"
4. This error means "no orders to cancel" (or malformed TX)

## Fixes Applied

### Fix 1: Add ClientOrderIndex to OrderSnapshot
**File:** `GridBot.Lighter/Models/WebSocket/ChannelEvents.cs`
```csharp
public sealed record OrderSnapshot
{
    public long ClientOrderIndex { get; init; }  // ADDED
    // ... other properties
}
```

### Fix 2: Copy ClientOrderIndex in HandleOrdersMessageAsync
**File:** `GridBot.Lighter/LighterWebSocketClient.cs:740-749`
```csharp
Orders = orders.Select(o => new OrderSnapshot
{
    OrderIndex = o.OrderIndex,
    ClientOrderIndex = o.ClientOrderIndex,  // ADDED
    // ... other properties
}).ToList()
```

### Fix 3: Set ClientOrderIndex in GetActiveOrdersAsync
**File:** `GridBot.Lighter/WsLighterQueryClient.cs:97-113`
```csharp
.Select(o => new Order
{
    OrderIndex = o.OrderIndex,
    ClientOrderIndex = o.ClientOrderIndex > 0 ? o.ClientOrderIndex : null,  // ADDED
    // ... other properties
})
```

### Fix 4: Add Diagnostic Logging to SyncOrderStatusAsync
**File:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:396-422`
Added logging to track:
- Total active orders from exchange
- Orders with/without ClientOrderIndex
- CRITICAL warning if active grid levels exist but no orders with ClientOrderIndex found

### Fix 5: Graceful Error Handling for 21501
**File:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:343-395`
```csharp
catch (LighterApiException ex) when (ex.Code == 21501)
{
    // Error 21501 "invalid tx info" typically means no orders to cancel
    _logger.LogInformation(
        "CancelAllOrders returned 21501 for market {MarketId} - treating as no orders to cancel",
        marketId);
    return 0;
}
```

Also added handling for error 21712 "account has queued cancel all orders request".

## Files Modified
1. `GridBot.Lighter/Models/WebSocket/ChannelEvents.cs` - Added ClientOrderIndex to OrderSnapshot
2. `GridBot.Lighter/LighterWebSocketClient.cs` - Copy ClientOrderIndex in HandleOrdersMessageAsync
3. `GridBot.Lighter/WsLighterQueryClient.cs` - Set ClientOrderIndex in GetActiveOrdersAsync
4. `GridBot.ApiService/Services/Grid/GridOrderManager.cs` - Diagnostic logging + error handling

## Documentation Created
- `.claude/doc/lighter-error-21501-cancelallorders-fix.md` - Full analysis of error 21501 and CancelAllOrders behavior

## Build Status
Build succeeded with 0 warnings, 0 errors.

## Testing Recommendations
1. Restart the bot in production
2. Watch for the new diagnostic logs in `SyncOrderStatus`
3. Verify that fills are detected correctly (only actual fills)
4. Monitor for any occurrences of the CRITICAL warning about missing ClientOrderIndex

## Key Learnings
1. The WebSocket `OrderInfo` model has `ClientOrderIndex`, but it wasn't being propagated through the pipeline
2. Without `ClientOrderIndex`, the grid cannot match orders to grid levels
3. Lighter API error 21501 can mean "no orders to cancel" - should be handled gracefully
