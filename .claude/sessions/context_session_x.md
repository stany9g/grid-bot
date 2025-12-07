# Session Context - Race Condition Fixes

## Date
2025-12-07

## Task
Fix critical race conditions in `DashboardStateService.cs` identified by the code reviewer.

## Issues Fixed

### 1. Race Condition in `AcknowledgeAlert`
**Problem:** The queue rebuild pattern (ToList -> Clear -> Re-enqueue) was not thread-safe. Alerts added by other threads during the rebuild would be lost.

**Solution:** Replaced with lock-protected in-place modification:
```csharp
lock (_alertsLock)
{
    var alertIndex = _alerts.FindIndex(a => a.Id == alertId);
    if (alertIndex >= 0)
    {
        _alerts[alertIndex] = _alerts[alertIndex] with { IsAcknowledged = true };
        alertsCopy = [.. _alerts];
    }
}
```

### 2. Race Condition in `ClearAlerts`
**Problem:** Same issue - alerts could be added and immediately lost during the clear loop.

**Solution:** Simple lock around the clear operation:
```csharp
lock (_alertsLock)
{
    _alerts.Clear();
}
```

### 3. Race Condition in `AddAlertAsync` Trim Loop
**Problem:** Multiple concurrent calls could over-trim the alert queue due to non-atomic read-modify-write.

**Solution:** All operations inside lock:
```csharp
lock (_alertsLock)
{
    _alerts.Add(alert);
    while (_alerts.Count > _maxAlerts)
    {
        _alerts.RemoveAt(0);
    }
    alertsCopy = [.. _alerts];
}
```

### 4. Race Condition in `BuildDashboardStateAsync`
**Problem:** Direct access to `_alerts.ToList()` without lock protection.

**Solution:** Added helper method `GetAlertsCopy()` that locks before copying.

## Changes Made

**File:** `GridBot.ApiService/Services/Dashboard/DashboardStateService.cs`

1. Removed `using System.Collections.Concurrent;` (no longer needed)
2. Replaced `ConcurrentQueue<AlertItem> _alerts` with `List<AlertItem> _alerts = []`
3. Added `object _alertsLock = new()` for synchronization
4. Updated class XML comment to document thread-safety guarantee
5. Refactored `AddAlertAsync` to use lock-protected list operations
6. Refactored `ClearAlerts` to use lock-protected clear
7. Refactored `AcknowledgeAlert` to use lock-protected in-place modification
8. Added `GetAlertsCopy()` helper method for thread-safe list copying
9. Updated `BuildDashboardStateAsync` to use `GetAlertsCopy()`

## Build Status
Build succeeded with 0 warnings and 0 errors.

## Thread Safety Pattern Used
Simple lock-based synchronization with `object _alertsLock`:
- All mutations to `_alerts` occur inside `lock (_alertsLock) { ... }`
- Copies of the list are taken inside the lock, then used outside
- State updates (`_currentState = ...`) happen outside the lock to minimize lock duration
- Event notifications (`OnStateChanged`) happen outside the lock

This pattern is appropriate because:
- Alert operations are not high-frequency hot paths
- Operations are short-lived (no I/O inside locks)
- Simple locking is easier to reason about than lock-free alternatives
