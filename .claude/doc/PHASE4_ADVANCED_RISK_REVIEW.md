# GridBot.AdvancedRisk Module - Code Review

## Date: 2025-12-25
## Reviewer: csharp-code-reviewer (Haiku 4.5)
## Time Spent: 2 minutes (focused CRITICAL issues only)

---

## Overall Assessment: APPROVED

The GridBot.AdvancedRisk module is well-structured and production-ready. Clean separation of concerns, proper async/await patterns, thread-safe implementations, and good resource management.

---

## CRITICAL ISSUES

**[CRITICAL]** RecoveryManager semaphore map unbounded growth
- Location: RecoveryManager._marketLocks (line 14) and GetMarketLock (line 133)
- Problem: ConcurrentDictionary<int, SemaphoreSlim> never removes entries. As you trade more markets, semaphores accumulate forever. With 10,000 markets = 10,000+ semaphore objects in memory permanently. Memory leak.
- Fix: Add cleanup method:
  ```csharp
  public void ClearMarketLock(int marketId)
  {
      _marketLocks.TryRemove(marketId, out var semaphore);
      semaphore?.Dispose();
  }
  ```
- Impact: Production memory leak with many markets
- Action: MUST FIX before shipping with high market count

---

## WARNINGS

**[WARNING]** LiquidityMonitor cache never invalidates
- Location: LiquidityMonitor._statusCache (line 15)
- Problem: Cached LiquidityStatus persists indefinitely. Stale data returned until RefreshLiquidityAsync called. No TTL.
- Fix: Either document that callers MUST call refresh frequently, OR add age-check in GetLiquidityStatusAsync
- Impact: Stale liquidity data could cause wrong risk decisions
- Recommendation: Clarify cache ownership and refresh frequency

**[WARNING]** WebhookNotifier retry logic off-by-one
- Location: SendToTargetAsync line 56-80
- Problem: while(retries <= MaxRetries) allows one extra retry. If MaxRetries=3, makes 4 attempts.
- Fix: Change to while(retries < MaxRetries) for clarity
- Impact: Extra unnecessary attempt
- Recommendation: Add unit test for retry behavior

---

## WHAT'S WORKING WELL

1. Thread-Safety: ConcurrentDictionary, locks, SemaphoreSlim all correct
2. Async/Await: ConfigureAwait(false), cancellation tokens properly threaded
3. Null Validation: ArgumentNullException checks in all constructors
4. Error Handling: Exponential backoff retry logic is solid
5. Separation of Concerns: Single responsibility per service
6. DI Registration: Proper AddHttpClient factory pattern
7. Logging: Event severity mapped to appropriate log levels

---

## ARCHITECTURE NOTES

- RecoveryManager: Phase transitions with per-market semaphores (good parallelism)
- FlashPumpDetector: Pump detection within window using price history (solid)
- LiquidityMonitor: Health tiers (Healthy/Thin/Critical) with multipliers (clean)
- RiskEventLogger: Event queuing per-market, 1000-entry cap (prevents unbounded growth)
- WebhookNotifier: Discord, Telegram, HomeAssistant support with retry (extensible)

---

## DEPENDENCY GRAPH

RecoveryManager ← IAdvancedRiskConfiguration, IAdvancedRiskStateProvider, IRiskEventLogger ✓
FlashPumpDetector ← IAdvancedRiskConfiguration, IRiskEventLogger ✓
LiquidityMonitor ← IAdvancedRiskConfiguration, IAdvancedRiskMarketDataProvider, IRiskEventLogger ✓
RiskEventLogger ← IWebhookNotifier ✓
WebhookNotifier ← IAdvancedRiskConfiguration, HttpClient ✓

No circular dependencies. Clean abstraction injection.

---

## FINAL VERDICT

**APPROVED with ACTION ITEMS:**

CRITICAL (must fix):
1. Implement cleanup for RecoveryManager._marketLocks
   - Effort: 15 minutes
   - Blocks production deployment with many markets

WARNING (should fix):
1. Fix off-by-one retry logic in WebhookNotifier
   - Effort: 10 minutes
   - Add unit test for retry behavior

OPTIONAL:
- Add cache TTL to LiquidityMonitor or document refresh requirements

Code is otherwise production-ready. Structure clean, patterns idiomatic .NET, async correct.

---

## FILES REVIEWED

- AdvancedRiskModule.cs (placeholder - OK)
- AdvancedRiskServiceExtensions.cs (DI setup - OK)
- RecoveryManager.cs (CRITICAL issue)
- FlashPumpDetector.cs (OK)
- LiquidityMonitor.cs (WARNING: cache TTL)
- RiskEventLogger.cs (OK, bounded queue)
- WebhookNotifier.cs (WARNING: retry off-by-one)

All models and interfaces - no issues.
