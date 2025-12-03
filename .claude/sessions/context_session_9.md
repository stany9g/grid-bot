# Session 9: Fix Circular Dependency in DI Container

## Date
2025-11-28

## Objective
Fix circular dependency preventing application startup:
```
IGridOrderManager → IMoonBagManager → ITrailingGridService → IGridLifecycleService → IGridOrderManager
```

## Current Status
**COMPLETE**

## Problem Analysis

### Circular Dependency Chain
1. `GridOrderManager` depends on `IMoonBagManager` - to check if sell orders should be blocked by moon bag protection
2. `MoonBagManager` depends on `ITrailingGridService` - to check if flash spike is active (suspends high watermark updates)
3. `TrailingGridService` depends on `IGridLifecycleService` - to get grid state and shift grids
4. `GridLifecycleService` depends on `IGridOrderManager` - to place/cancel orders

### Root Cause
`MoonBagManager` only needs `IsFlashSpikeActiveAsync()` from `TrailingGridService`, but `TrailingGridService` contains both:
- Flash spike detection logic (no grid dependencies)
- Grid shifting logic (requires grid dependencies)

### Solution: Extract IFlashSpikeDetector

Break the cycle by extracting flash spike detection into a separate service:

**New Interface: `IFlashSpikeDetector`**
```csharp
public interface IFlashSpikeDetector
{
    Task<bool> IsFlashSpikeActiveAsync(int marketId, CancellationToken ct = default);
    Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default);
    void ClearFlashSpikeProtection(int marketId);
}
```

**New Dependency Graph (No Cycle):**
```
FlashSpikeDetector (no dependencies on grid services)
       ↑
MoonBagManager → TrailingGridService → GridLifecycleService → GridOrderManager
       ↑_______________________________________________________________↓
```

Actually the correct graph is:
```
                    FlashSpikeDetector
                    ↗             ↖
MoonBagManager                     TrailingGridService
       ↑                                    ↓
       |                           GridLifecycleService
       |                                    ↓
       └─────────────────────────── GridOrderManager
```

No cycle because MoonBagManager depends on FlashSpikeDetector (not TrailingGridService).

## Files to Create
- `GridBot.ApiService/Services/MoonBag/IFlashSpikeDetector.cs`
- `GridBot.ApiService/Services/MoonBag/FlashSpikeDetector.cs`

## Files to Modify
- `GridBot.ApiService/Services/MoonBag/MoonBagManager.cs` - Replace ITrailingGridService with IFlashSpikeDetector
- `GridBot.ApiService/Services/MoonBag/TrailingGridService.cs` - Add IFlashSpikeDetector dependency
- `GridBot.ApiService/Program.cs` - Register IFlashSpikeDetector

## Implementation Progress
- [x] Create IFlashSpikeDetector interface
- [x] Create FlashSpikeDetector implementation
- [x] Update MoonBagManager
- [x] Update TrailingGridService
- [x] Update DI registration
- [x] Verify build

## Summary

The circular dependency was broken by extracting flash spike detection logic into a new `IFlashSpikeDetector` service:

### Files Created
- `GridBot.ApiService/Services/MoonBag/IFlashSpikeDetector.cs` - Interface for flash spike detection
- `GridBot.ApiService/Services/MoonBag/FlashSpikeDetector.cs` - Implementation with per-market state tracking

### Files Modified
- `GridBot.ApiService/Services/MoonBag/MoonBagManager.cs` - Now depends on `IFlashSpikeDetector` instead of `ITrailingGridService`
- `GridBot.ApiService/Services/MoonBag/TrailingGridService.cs` - Delegates to `IFlashSpikeDetector`, removed duplicate logic
- `GridBot.ApiService/Extensions/MoonBagServiceExtensions.cs` - Added `IFlashSpikeDetector` registration

### New Dependency Graph (No Cycle)
```
                    FlashSpikeDetector (no grid dependencies)
                    ↗             ↖
MoonBagManager                     TrailingGridService
       ↑                                    ↓
       |                           GridLifecycleService
       |                                    ↓
       └─────────────────────────── GridOrderManager
```

The fix follows the Single Responsibility Principle by separating flash spike detection (a monitoring concern) from grid management operations.
