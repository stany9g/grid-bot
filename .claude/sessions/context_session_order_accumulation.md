# Session: Order Accumulation on Restart Analysis

## Status: IMPLEMENTED ✅

## Issue Summary
On app restart, the bot creates new grid orders without cancelling or reconciling with existing orders on Lighter DEX. This causes order accumulation - each restart adds more orders.

## Root Cause Analysis

### The Problem Flow

1. **App Starts** → `TradingBotHostedService.StartAsync()`
2. **Loads persisted state from Redis** (trading state, recovery, moon bag, loss status)
3. **BUT grid state is NOT persisted** - stored only in `ConcurrentDictionary<int, GridState>` in memory
4. **Calls `_decisionEngine.InitializeAsync()`**
5. **`GetCurrentGridStateAsync()` returns `null`** (memory is empty after restart)
6. **Calls `InitializeGridAsync()`** - creates NEW grid levels
7. **Places NEW orders** without knowledge of existing ones on DEX
8. **Result: Order Accumulation** - old orders still live + new orders created

### Code References

**GridLifecycleService.cs** - In-memory state only:
```csharp
// Line 30 - purely in-memory
private readonly ConcurrentDictionary<int, GridState> _gridStates = new();
```

**TradingDecisionEngine.cs:478-491** - Initialize without cleanup:
```csharp
public async Task<bool> InitializeAsync(int marketId, CancellationToken ct = default)
{
    var gridState = await _gridLifecycle.GetCurrentGridStateAsync(marketId, ct)
        .ConfigureAwait(false);

    if (gridState is null && !_capacityService.IsDegradedState(currentState))
    {
        await _gridLifecycle.InitializeGridAsync(marketId, ct).ConfigureAwait(false);
    }
    // No cancellation of existing orders!
}
```

**GridOrderManager.cs:88-91** - Skip logic doesn't help on restart:
```csharp
if (level.Status == GridLevelStatus.Active || level.Status == GridLevelStatus.Filled)
{
    continue; // Only skips if level.Status is set - but on restart all are Pending
}
```

## What Controls Order Count

1. **GridCalculator.CalculateGridParameters()** - determines `OrdersPerSide` based on ATR/volatility
2. **GridCalculator.CalculateGridLevels()** - creates level objects based on parameters
3. **No persistence** - so order count is re-calculated fresh each restart

## Solution Options

### Option A: Cancel-All-First (Recommended - Simple)
```csharp
// In GridLifecycleService.InitializeGridAsync or TradingDecisionEngine.InitializeAsync
await _orderManager.CancelAllGridOrdersAsync(marketId, ct).ConfigureAwait(false);
// Then proceed with new grid creation
```
**Pros:** Simple, clean state, no stale order issues
**Cons:** Brief period with no orders, may miss fills during startup

### Option B: Persist Grid State to Redis
- Add `IGridStatePersistence` service
- Persist `GridState` on every change
- Load on startup and reconcile

**Pros:** Continuous operation, can resume from exact state
**Cons:** More complex, need staleness handling, sync issues

### Option C: Discover from Exchange
- Query active orders from Lighter DEX on startup
- Match by ClientOrderIndex pattern to rebuild grid state
- Sync before creating new orders

**Pros:** Works even if Redis lost state
**Cons:** Complex matching, may not recover full grid structure

## Recommendation

**Implement Option A first** - it's the safest and simplest fix:

1. Add `CancelAllGridOrdersAsync` call in `InitializeGridAsync` BEFORE creating new orders
2. This ensures clean slate on every startup
3. Later, implement Option B for zero-downtime restarts if needed

## Implementation (Completed)

### Files Modified

1. **IGridOrderManager.cs** - Added new interface method:
   ```csharp
   Task<int> CancelExistingOrdersOnStartupAsync(int marketId, CancellationToken ct = default);
   ```

2. **GridOrderManager.cs:430-496** - Implemented the method:
   - Gets auth token for API call
   - Queries active orders from Lighter DEX
   - If orders exist, logs them and cancels all
   - Returns count of cancelled orders
   - Error handling allows grid init to proceed even on failure (NEVER HALT philosophy)

3. **GridLifecycleService.cs:86-96** - Added call at start of `InitializeGridAsync`:
   ```csharp
   // CRITICAL: Cancel any existing orders on the exchange before creating new grid
   var cancelledCount = await _orderManager.CancelExistingOrdersOnStartupAsync(marketId, ct)
       .ConfigureAwait(false);
   ```

### Build Status
✅ Build succeeded with 0 warnings, 0 errors

### Code Review
✅ Approved by csharp-code-reviewer - no blocking issues found
