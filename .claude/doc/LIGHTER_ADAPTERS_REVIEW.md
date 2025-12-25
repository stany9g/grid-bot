# Lighter Adapter Classes Code Review

**Date:** 2025-12-25
**Reviewer:** csharp-code-reviewer
**Files Reviewed:** 9 adapter files + 1 extension file in GridBot.Lighter

---

## Review Summary

**Production Readiness: APPROVED with 2 WARNINGS**

No critical issues found. The adapter implementations are well-structured with proper thread safety, correct model mappings, and appropriate error handling.

---

## Issues Found

### WARNING-1: LighterConnectionAdapter Event Handler Not Thread-Safe on Dispose

- **Location:** `LighterConnectionAdapter.cs` (lines 89-98)
- **Problem:** The `DisposeAsync` method unsubscribes from `HealthChanged` after setting `_disposed = true`. If `OnRealtimeHealthChanged` fires between these two operations on another thread, it could raise `HealthChanged?.Invoke` with disposed resources.
- **Impact:** Minor - race window is very small and would only cause a benign event invocation.
- **Fix:** Set `_disposed` flag AFTER unsubscribing from the event, or add a check in `OnRealtimeHealthChanged`:
```csharp
private void OnRealtimeHealthChanged(object? sender, WebSocketHealthChangedEventArgs e)
{
    if (_disposed) return;  // Add this guard
    // ... rest of handler
}
```
- **Effort:** 5 minutes

---

### WARNING-2: LighterOrderAdapter Batch Creates Scaling Cache Misses Per-Order

- **Location:** `LighterOrderAdapter.cs` (lines 117-141)
- **Problem:** `CreateOrderBatchAsync` calls `GetMarketScalingAsync` for each order in the batch sequentially. If orders are for different markets, this is correct. However, if multiple orders are for the same market (common case), this repeatedly fetches scaling info.
- **Impact:** Minor - `LighterScalingAdapter` has a cache, so second call is O(1). But the async overhead and lock contention is unnecessary.
- **Recommendation:** Pre-group orders by marketId and fetch scaling once per unique market:
```csharp
var scalingByMarket = new Dictionary<string, MarketScaling>();
foreach (var request in requests)
{
    if (!scalingByMarket.ContainsKey(request.MarketId))
        scalingByMarket[request.MarketId] = await _scalingProvider.GetMarketScalingAsync(request.MarketId, ct);
}
```
- **Effort:** 15 minutes

---

## Approved Components

### LighterMarketMapper.cs
- **Thread Safety:** Uses `lock(_lock)` correctly for all dictionary access
- **Initialization:** Double-check pattern for `_initialized` is correct
- **Defensive Copies:** `GetAllMarketIds()` and `GetAllMarkets()` return `.ToList()` copies - correct
- **Edge Cases:** `ToLighterId` handles both symbol lookup and numeric parse fallback

### LighterConnectionAdapter.cs
- **IAsyncDisposable:** Correctly implemented with idempotency check
- **Event Subscription:** Properly subscribes in constructor and unsubscribes in dispose
- **State Mapping:** `MapConnectionState` exhaustively handles all Lighter states

### LighterRealtimeAdapter.cs
- **Model Mapping:** All mappings are correct:
  - `MapOrderBook`: Correctly extracts bids/asks tuples to PriceLevel records
  - `MapAccount`: Correctly maps positions dictionary with market ID conversion
  - `MapPosition`: Handles nullable fields with sensible defaults
  - `MapOrder`: RemainingSize = Size - FilledSize is correct
- **Dead Code:** `MapOrderType` and `MapTimeInForce` methods exist but are unused - not a problem, may be used in future

### LighterOrderAdapter.cs
- **Critical Scaling:** `ScalePrice` and `ScaleAmount` calls are correct, using the scaling provider
- **TriggerPrice Handling:** Correctly casts to `(int)` for Lighter API format
- **Client Order Index:** Thread-safe generation with `_indexLock`
- **Error Handling:** Catches `LighterApiException` separately from general exceptions - good practice
- **TimeInForce Mapping:** `FillOrKill` maps to `ImmediateOrCancel` with comment explaining Lighter limitation

### LighterAccountAdapter.cs
- **Realtime vs REST:** Correct priority - uses realtime if connected and fresh (< 30s), falls back to REST
- **String Parsing:** Uses `decimal.TryParse` with fallback to 0 - defensive and correct
- **Leverage Calculation:** `(int)(1m / imf)` is mathematically correct for converting initial margin fraction to leverage

### LighterMarketDataAdapter.cs
- **Order Book Sorting:** REST fallback correctly sorts bids descending, asks ascending
- **Funding Time Calculation:** `GetNextFundingTime()` correctly handles 8-hour intervals at 00:00, 08:00, 16:00 UTC
- **Edge Case:** Handles `nextFundingHour == 24` by adding 1 day

### LighterScalingAdapter.cs
- **Math Verification:**
  - `ScalePrice`: `(long)Math.Round(price * scaling.PriceScale, MidpointRounding.AwayFromZero)` - correct
  - `ScaleAmount`: Same pattern - correct
  - `UnscalePrice`: `scaledPrice / (decimal)scaling.PriceScale` - correct
  - `UnscaleAmount`: Same pattern - correct
- **Cache Safety:** Uses `lock(_cacheLock)` for cache access
- **Default Scaling:** Returns sensible defaults (2 decimal price, 8 decimal size) when metadata unavailable

### LighterAuthAdapter.cs
- **Nonce Management:** Thread-safe with `_nonceLock`
- **Nonce Sync:** Correctly stores `syncedNonce - 1` so `GetNextNonceAsync` returns the correct value
- **SignMessage:** Throws `NotSupportedException` with clear message - appropriate for unused capability

### LighterExchangeClient.cs
- **Aggregate Pattern:** Clean composition of all interfaces
- **Dispose Chain:** Only disposes `Connection`, which will cascade to `ILighterRealtimeState`
- **Immutability:** All interface properties are read-only

### LighterAbstractionsExtensions.cs
- **DI Registration:** All registrations use `AddSingleton` - correct for stateless adapters
- **Keyed Services:** Uses keyed singletons for multi-exchange support - proper pattern
- **Initialization:** `InitializeLighterExchangeAsync` correctly initializes market mapper before connecting

---

## Thread Safety Analysis

| Component | Thread-Safe | Mechanism |
|-----------|-------------|-----------|
| LighterMarketMapper | Yes | `lock(_lock)` on all operations |
| LighterConnectionAdapter | Yes (with warning) | Immutable state, event handler concern noted |
| LighterRealtimeAdapter | Yes | Delegates to thread-safe ILighterRealtimeState |
| LighterOrderAdapter | Yes | `_indexLock` for client order index |
| LighterAccountAdapter | Yes | Stateless, delegates to thread-safe services |
| LighterMarketDataAdapter | Yes | Stateless, delegates to thread-safe services |
| LighterScalingAdapter | Yes | `lock(_cacheLock)` on cache operations |
| LighterAuthAdapter | Yes | `_nonceLock` for nonce management |
| LighterExchangeClient | Yes | Immutable after construction |

---

## Scaling Logic Verification

Verified against Lighter DEX documentation and existing code:

| Operation | Implementation | Verification |
|-----------|---------------|--------------|
| Price to integer | `price * PriceScale` | Correct - PriceScale = 10^PriceDecimals |
| Amount to integer | `amount * AmountScale` | Correct - AmountScale = 10^SupportedSizeDecimals |
| Integer to price | `scaledPrice / PriceScale` | Correct - inverse operation |
| Integer to amount | `scaledAmount / AmountScale` | Correct - inverse operation |
| Rounding | `MidpointRounding.AwayFromZero` | Correct - prevents truncation errors |

---

## Resource Management

| Resource | Properly Managed |
|----------|------------------|
| Event subscriptions | Yes - unsubscribed in DisposeAsync |
| IAsyncDisposable | Yes - LighterConnectionAdapter and LighterExchangeClient implement correctly |
| HttpClient | Not owned - injected via DI (correct) |
| WebSocket | Not owned - managed by ILighterWebSocketClient (correct) |

---

## DI Registration Review

All services registered with correct lifetimes:

| Service | Lifetime | Correct |
|---------|----------|---------|
| LighterMarketMapper | Singleton | Yes - maintains state |
| LighterScalingAdapter | Singleton | Yes - has cache |
| All other adapters | Singleton | Yes - stateless or thread-safe |
| IExchangeClient (keyed) | Singleton | Yes - aggregate pattern |
| IExchangeClient (default) | Singleton | Yes - forwards to keyed |

---

## Conclusion

**APPROVED for production use.**

The adapter implementations correctly:
1. Map between Lighter-specific and abstraction types
2. Handle scaling for prices and amounts
3. Manage thread safety with proper locking
4. Implement IAsyncDisposable correctly
5. Fall back from realtime to REST API when needed

The two warnings are minor:
- WARNING-1: Event handler race on dispose - benign if triggered
- WARNING-2: Redundant scaling lookups in batch - performance optimization only

Neither blocks production deployment.
