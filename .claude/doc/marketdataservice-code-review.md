# Code Review: MarketDataService REST API Optimization

**File:** `GridBot.ApiService/Services/MarketData/MarketDataService.cs`
**Reviewer:** csharp-code-reviewer
**Date:** 2025-12-12
**Status:** APPROVED WITH WARNINGS

---

## Summary

The changes to optimize REST API calls by using WebSocket data first with REST fallback are well-implemented. The code is thread-safe, follows .NET best practices, and achieves the stated goal of reducing REST API calls from ~5/loop to ~1 every 5 minutes.

---

## Issues Found

### **[WARNING]** Unbounded Cache Growth - Candlestick Cache

- **Location:** `MarketDataService` class, line 24
- **Problem:** The `ConcurrentDictionary` cache has no upper bound on entries. While expired entries are not removed proactively, they are replaced on access. However, if the application queries many different combinations of `(MarketId, Resolution, Count)` over time without repeated access, the cache will grow indefinitely.
- **Risk Level:** LOW in practice for this trading bot (limited markets and resolutions), but technically a memory leak.
- **Fix:** Consider implementing one of these solutions:
  1. **Simple:** Add a periodic cleanup (every 30 min) to remove expired entries
  2. **Better:** Use `MemoryCache` with absolute expiration which handles cleanup automatically
  3. **Minimal:** For this specific use case (trading bot with 1-2 markets), the current implementation is acceptable

```csharp
// Option 1: Simple periodic cleanup (add to class)
private readonly Timer _cacheCleanupTimer;

public MarketDataService(...)
{
    // ... existing code ...
    _cacheCleanupTimer = new Timer(CleanupExpiredCache, null,
        TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
}

private void CleanupExpiredCache(object? state)
{
    var now = DateTimeOffset.UtcNow;
    var keysToRemove = _candlestickCache
        .Where(kvp => (now - kvp.Value.CachedAt).TotalSeconds >= CandlestickCacheTtlSeconds)
        .Select(kvp => kvp.Key)
        .ToList();

    foreach (var key in keysToRemove)
    {
        _candlestickCache.TryRemove(key, out _);
    }
}
```

---

### **[SUGGESTION]** GetCandlesticksAsync Returns Mutable List

- **Location:** `GetCandlesticksAsync`, lines 113 and 143
- **Problem:** The method returns the same `List<CandlestickData>` instance that is stored in the cache. If a caller modifies this list, it corrupts the cached data for all subsequent callers.
- **Risk Level:** LOW (callers are unlikely to modify the list in this codebase)
- **Fix:** Return a new list or use `IReadOnlyList<CandlestickData>` as return type

```csharp
// Option 1: Return new list on cache hit (line 113)
return [..cached.Data]; // or cached.Data.ToList()

// Option 2: Change return type to IReadOnlyList<CandlestickData>
// This documents intent but still allows casting to List
```

---

### **[SUGGESTION]** Minor LINQ Optimization in GetOrderBookSnapshotAsync

- **Location:** `GetOrderBookSnapshotAsync`, lines 175-193 (REST fallback path)
- **Problem:** `orderBookOrders.Bids` and `orderBookOrders.Asks` are enumerated twice - once for grouping and once for creating the result. This is acceptable since these are already materialized collections.
- **Status:** Not an issue - the input is already a `List<OrderBookOrder>`, so no multiple enumeration of `IEnumerable` occurs.

---

## Code Quality Observations (No Action Required)

1. **Thread Safety:** `ConcurrentDictionary` is used correctly. The `TryGetValue` + check + set pattern is safe for this caching use case (worst case: two threads fetch the same data simultaneously, which is harmless).

2. **Null Handling:** Properly checks for null WebSocket data before using (`wsPrice.HasValue`, `wsOrderBook != null && wsOrderBook.Bids.Count > 0`).

3. **Fallback Logic:** Well-structured with clear logging for debugging. The WebSocket-first pattern is correct.

4. **Error Handling:** Exceptions are caught, logged, and re-thrown appropriately. The `GetFundingRateAsync` method returns `null` on failure which matches the nullable return type.

5. **LINQ Usage:** Proper use of `.ToList()` to materialize collections before returning. No multiple enumeration issues.

6. **Logging:** Appropriate use of `LogDebug` for cache behavior and `LogWarning`/`LogError` for actual issues. Not too verbose.

7. **Constructor Validation:** All dependencies are validated with `ArgumentNullException`.

---

## Verification Checklist

| Check | Status |
|-------|--------|
| Thread Safety (ConcurrentDictionary) | PASS |
| Memory Management (cache bounded?) | WARNING - see above |
| Error Handling | PASS |
| IEnumerable Multiple Enumeration | PASS - no issues |
| Null Handling | PASS |
| Performance (unnecessary allocations) | PASS |
| Logging | PASS |
| Code Simplification (KISS) | PASS |

---

## Final Verdict

**APPROVED** - The code is production-ready. The unbounded cache growth warning is low priority for this specific use case (trading bot with limited markets). Consider addressing it in a future cleanup if the bot expands to many markets.

---

## Files Reviewed

- `C:\Users\stany\source\repos\plan\GridBot\GridBot.ApiService\Services\MarketData\MarketDataService.cs`

## Related Files (Reference Only)

- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\ILighterRealtimeState.cs`
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\Models\WebSocket\ChannelEvents.cs`
