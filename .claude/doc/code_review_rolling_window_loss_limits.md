# Code Review: Rolling Window Loss Limits Implementation

**Reviewer:** csharp-code-reviewer
**Date:** 2025-12-06
**Status:** CONDITIONAL PASS - 2 CRITICAL issues must be fixed before production

## Files Reviewed

1. `GridBot.ApiService/Models/Trading/TradeRecord.cs`
2. `GridBot.ApiService/Models/Trading/EquitySnapshot.cs`
3. `GridBot.ApiService/Models/Trading/RollingLossStatus.cs`
4. `GridBot.ApiService/Configuration/TradingBotOptions.cs` (LossLimitOptions class)
5. `GridBot.ApiService/Services/Risk/ILossMonitor.cs`
6. `GridBot.ApiService/Services/Risk/LossMonitor.cs`
7. `GridBot.ApiService/Services/Persistence/IStateRepository.cs`
8. `GridBot.ApiService/Services/Persistence/RedisStateRepository.cs`

---

## Issues Found

### **[CRITICAL]** Thread Safety: List Operations Outside of Lock in GetCurrentLossStatusAsync

- **Location:** `LossMonitor.GetCurrentLossStatusAsync()` lines 50-80
- **Problem:** The method reads from `state.TradeHistory` (a `List<T>`) without acquiring `_updateLock`. Since `RecordTradeResultAsync`, `RecordTradeAsync`, `CleanupOldRecordsAsync`, and `LoadPersistedStateAsync` all modify this list while holding the lock, concurrent calls to `GetCurrentLossStatusAsync` can read partially modified lists, causing:
  - `InvalidOperationException` ("Collection was modified during enumeration")
  - Incorrect P&L calculations leading to wrong trading decisions
  - Potential false limit breach detections or missed breaches
- **Fix:** Either:
  1. Acquire `_updateLock` in `GetCurrentLossStatusAsync` (impacts latency), OR
  2. Change `List<TradeRecord>` to `ImmutableList<TradeRecord>` and use atomic swap pattern, OR
  3. Take a snapshot of the list under lock, then calculate outside lock

**Recommended fix (option 3):**
```csharp
public Task<RollingLossStatus> GetCurrentLossStatusAsync(int marketId, CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();

    List<TradeRecord> tradesCopy;
    decimal currentEquity, equityHwm, currentDrawdown;
    DateTimeOffset? haltUntil;
    string? haltReason;

    // Take snapshot under lock
    _updateLock.Wait(ct);
    try
    {
        var state = GetOrCreateState(marketId);
        tradesCopy = state.TradeHistory.ToList(); // defensive copy
        currentEquity = state.CurrentEquity;
        equityHwm = state.EquityHighWaterMark;
        currentDrawdown = state.CurrentDrawdown;
        haltUntil = state.HaltUntil;
        haltReason = state.HaltReason;
    }
    finally
    {
        _updateLock.Release();
    }

    // Calculate outside lock using snapshot
    var pnl24h = CalculateRollingPnlFromCopy(tradesCopy, TimeSpan.FromHours(24));
    // ... rest of calculation
}
```

---

### **[CRITICAL]** IEnumerable Multiple Enumeration in CalculateRollingPnl

- **Location:** `LossMonitor.CalculateRollingPnl()` lines 407-418 and `CountTradesInWindow()` lines 420-424
- **Problem:** These methods iterate `state.TradeHistory` (a `List<T>`) which is NOT safe when other threads modify the list. Additionally, `GetCurrentLossStatusAsync` calls both methods, resulting in multiple iterations over the same collection - but since the collection can be modified between iterations, results can be inconsistent within the same status snapshot.
- **Fix:** Pass a pre-materialized copy to these methods:

```csharp
private static decimal CalculateRollingPnl(IReadOnlyList<TradeRecord> trades, TimeSpan window)
{
    if (trades.Count == 0)
        return 0m;

    var windowStart = DateTimeOffset.UtcNow - window;
    return trades
        .Where(t => t.Timestamp >= windowStart)
        .Sum(t => t.PnlPercent);
}
```

---

### **[WARNING]** Redis Read-Modify-Write Race Condition

- **Location:** `RedisStateRepository.SaveTradeRecordAsync()` lines 348-388 and `SaveEquitySnapshotAsync()` lines 464-504
- **Problem:** Both methods perform a read-modify-write pattern:
  1. Read existing JSON from Redis
  2. Deserialize to list
  3. Add new item
  4. Serialize and save back

  If two concurrent calls execute between steps 1 and 4, one write will overwrite the other, losing data.
- **Impact:** Under high trade frequency, trade records could be lost, causing incorrect P&L calculations.
- **Fix:** Either:
  1. Use Redis LPUSH/RPUSH operations instead of full key overwrite (requires changing data structure)
  2. Use distributed lock (e.g., `RedLock`) around the operation
  3. Use optimistic concurrency with Redis WATCH/MULTI/EXEC

---

### **[WARNING]** Missing Cancellation Token Propagation

- **Location:** `LossMonitor.CheckLossLimitsAsync()` lines 230-284
- **Problem:** The method accepts a `CancellationToken` but does not propagate it to `CanAutoClearHaltAsync()`. If the caller cancels, the inner operation will continue running.
- **Fix:**
```csharp
// Line 243
if (await CanAutoClearHaltAsync(marketId, state, ct).ConfigureAwait(false))
```
This is already correct - my error on initial review. However, `CalculateRollingPnl` is called 4 times in succession without any chance to check cancellation between calls. Consider:
```csharp
ct.ThrowIfCancellationRequested();
var pnl24h = CalculateRollingPnl(state, TimeSpan.FromHours(24));
ct.ThrowIfCancellationRequested();
var pnl7d = CalculateRollingPnl(state, TimeSpan.FromDays(7));
// etc.
```

---

### **[WARNING]** Potential Memory Growth with Large Trade History

- **Location:** `LossMonitor.RollingLossState.TradeHistory` line 585
- **Problem:** With 35-day retention and active trading (e.g., 100 trades/day = 3,500 records), the in-memory list will grow. Each `TradeRecord` is approximately 100-150 bytes, so ~500KB per market. However, the `CalculateRollingPnl` method iterates ALL trades even when only calculating 24h P&L.
- **Impact:** Performance degradation over time as list grows. LINQ `Where().Sum()` creates intermediate enumerables.
- **Fix:** Consider:
  1. Pre-aggregate by day (store daily summaries, not individual trades)
  2. Maintain separate lists/indices for different windows
  3. Use a more efficient data structure (e.g., sorted by timestamp for binary search)

---

### **[WARNING]** Cleanup Method Could Miss Records

- **Location:** `RedisStateRepository.CleanupOldTradeRecordsAsync()` lines 429-462
- **Problem:** The cleanup only attempts to delete keys for `daysBack = retentionDays + 1` to `retentionDays + 30`. If the system was offline for more than 30 days, older keys would never be cleaned up.
- **Impact:** Redis memory bloat if system has extended downtime.
- **Fix:** Use Redis SCAN with key pattern `alte:trades:{marketId}:*` and delete keys where the date suffix is older than retention.

---

### **[SUGGESTION]** TradeRecord.Create Uses DateTimeOffset.UtcNow Internally

- **Location:** `TradeRecord.Create()` lines 48-66
- **Problem:** The method always uses `DateTimeOffset.UtcNow` for timestamp. This makes the class difficult to test (can't inject time) and prevents recording historical trades with their actual timestamps.
- **Fix:** Add optional timestamp parameter:
```csharp
public static TradeRecord Create(
    int marketId,
    decimal pnlPercent,
    decimal currentEquity,
    string? orderId = null,
    DateTimeOffset? timestamp = null)
{
    var ts = timestamp ?? DateTimeOffset.UtcNow;
    // ...
}
```

---

### **[SUGGESTION]** EquitySnapshot.Create Has Same Testability Issue

- **Location:** `EquitySnapshot.Create()` lines 32-41
- **Problem:** Same as TradeRecord - hardcoded `DateTimeOffset.UtcNow`.
- **Fix:** Add optional timestamp parameter.

---

### **[SUGGESTION]** Decimal Precision for P&L Sum

- **Location:** `LossMonitor.CalculateRollingPnl()` line 416-417
- **Problem:** The code uses `.Sum(t => t.PnlPercent)` which can have precision issues with many small decimal values. While unlikely to cause significant errors with trading percentages, it's worth noting.
- **Impact:** Negligible - decimal has sufficient precision for percentage sums.
- **Assessment:** Acceptable as-is, just documenting for awareness.

---

### **[SUGGESTION]** RollingLossStatus.ToLegacyLossStatus() Mapping

- **Location:** `RollingLossStatus.ToLegacyLossStatus()` lines 110-124
- **Problem:** The mapping is semantically misleading:
  - `DailyPnlPercent = Rolling24hPnlPercent` - "Daily" implies calendar day, not rolling 24h
  - `DailyLimitBreached = Rolling24hBreached` - Same issue
- **Impact:** Consumers using the legacy API may misinterpret the values.
- **Assessment:** Acceptable for backward compatibility, but consider adding XML doc comment warning about the semantic difference.

---

## Verified Correct

1. **Decimal Type Usage:** All P&L and equity calculations correctly use `decimal`, not `double` or `float`.

2. **Null Safety:**
   - Empty trade history correctly returns `0m` (line 409-411)
   - `LoadPersistedStateAsync` correctly handles null from repository (line 321)
   - Collections initialized as empty arrays using `[]` syntax

3. **IDisposable Implementation:** `LossMonitor` correctly implements `IDisposable`, disposing the `SemaphoreSlim` and clearing the dictionary.

4. **Backward Compatibility:** `ToLegacyLossStatus()` correctly maps all fields from rolling to legacy structure.

5. **Configuration Validation:** `LossLimitOptions` has sensible defaults:
   - Rolling24HourLossPercent: -12%
   - Rolling7DayLossPercent: -20%
   - Rolling30DayLossPercent: -30%
   - MaxDrawdownPercent: -35%
   - TradeRecordRetentionDays: 35 (> 30 + buffer)

6. **TTL Handling in Redis:** Trade records and equity snapshots correctly use 35-day TTL (lines 368-371 and 484-487).

7. **ConcurrentDictionary Usage:** `_marketStates` correctly uses `ConcurrentDictionary` with `GetOrAdd` pattern (line 403-404).

---

## Summary

| Risk Level | Count | Items |
|------------|-------|-------|
| CRITICAL | 2 | Thread safety on List reads, IEnumerable multiple enumeration |
| WARNING | 4 | Redis race condition, cancellation propagation, memory growth, cleanup gaps |
| SUGGESTION | 4 | Testability, decimal precision, legacy mapping semantics |

---

## Verdict

**CONDITIONAL PASS** - The two CRITICAL issues MUST be fixed before production deployment:

1. `GetCurrentLossStatusAsync` must not read `TradeHistory` without synchronization
2. `CalculateRollingPnl` and `CountTradesInWindow` must operate on a snapshot, not the live list

The WARNING issues should be addressed but are not blocking:
- Redis race condition is low probability but should be fixed for robustness
- Memory growth is acceptable for expected trade volumes
- Cleanup gaps only matter if system is offline for extended periods

Testnet deployment is acceptable with current code for validation purposes.
