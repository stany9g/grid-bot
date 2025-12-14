# ALTE Trading Bot Production Deployment Audit Report

**Audit Date:** 2025-12-14
**Auditor:** Trading Systems Auditor (Claude trading-bot-auditor)
**Deployment Target:** Lighter DEX Mainnet (ChainId 304)
**Collateral:** $200 USD
**Proposed Leverage:** 5x (currently configured at 2x)
**Session:** Context from Session 4

---

## EXECUTIVE SUMMARY

The ALTE trading bot codebase demonstrates mature risk management architecture with comprehensive thread-safety patterns. However, the audit identified **1 CRITICAL** issue and **4 HIGH** priority issues that MUST be addressed before production deployment with real capital.

**Overall Production Readiness Score: 6.5/10**

**Deployment Recommendation: CONDITIONAL PASS**

The system is architecturally sound but requires immediate remediation of the private key exposure and careful consideration of the leverage configuration.

---

## CRITICAL FINDINGS

### FINDING 1: HARDCODED PRIVATE KEY IN PRODUCTION CONFIG

**Risk Level:** CRITICAL
**Category:** Security
**Location:** `GridBot.ApiService/appsettings.Production.json:11`
**Financial Impact:** TOTAL LOSS - Compromised key allows draining of all funds

**Problem:**

The production configuration file contains a hardcoded private key that is checked into source control:

```json
"Lighter": {
    "DryRun": false,
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "PrivateKey": "f8d790b492e646a195181fc07b3809d792afe653b513623f039605c123ea3adc8d831dea807b6a00",
    "ChainId": 304,
    "ApiKeyIndex": 15,
    "AccountIndex": 281474976627527,
    "InitialNonce": 0
}
```

This is a **CATASTROPHIC SECURITY VULNERABILITY**. If this repository is ever exposed (pushed to GitHub, leaked, etc.), the trading account will be immediately compromised.

**Evidence:**
- `appsettings.Production.json` line 11 contains live mainnet private key
- `appsettings.Development.json` line 10 contains testnet private key
- `appsettings.json` line 11 contains another key

**Fix:**

1. **IMMEDIATELY** rotate all private keys - the current keys must be considered compromised
2. Remove ALL private keys from config files
3. Use environment variables or user secrets:

```bash
# Production deployment
export LIGHTER__PRIVATEKEY="your-new-key"
# OR using .NET User Secrets
dotnet user-secrets set "Lighter:PrivateKey" "your-new-key" --project GridBot.AppHost
```

4. Update `appsettings.Production.json`:
```json
"Lighter": {
    "DryRun": false,
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "PrivateKey": "", // Loaded from environment
    "ChainId": 304,
    ...
}
```

5. Add to `.gitignore`:
```
**/appsettings.*.json
!**/appsettings.json
```

**Verdict:** FAIL - BLOCKING

---

## HIGH PRIORITY FINDINGS

### FINDING 2: 5x LEVERAGE EXCEEDS RECOMMENDED LIMITS FOR $200 COLLATERAL

**Risk Level:** HIGH
**Category:** Safety
**Location:** `GridBot.ApiService/appsettings.Production.json:26-27` and user request
**Financial Impact:** With $200 collateral at 5x leverage, a 20% adverse move causes total liquidation

**Problem:**

The user wants to deploy with 5x leverage, but the production config is set to 2x for good reason. With $200 collateral:

- At 2x leverage: $400 notional, liquidation at ~50% move = SAFE
- At 5x leverage: $1000 notional, liquidation at ~20% move = DANGEROUS

BTC regularly moves 10-15% in a day during volatile periods. At 5x leverage with a grid strategy that holds positions, a flash crash could liquidate the entire account before risk protections engage.

Current production config (CORRECT):
```json
"Capital": {
    "MaxLeverage": 2.0,
    "MaxAggregateLeverage": 1.5
}
```

**Evidence:**

The flash crash protection at `FlashCrash.OneHourDropPercent: -12` would trigger at -12%, but:
1. Protection triggers AFTER the move happens
2. At 5x leverage, a -12% move = -60% account loss
3. Liquidation cascade could complete before protection activates

**Fix:**

For $200 collateral, I STRONGLY recommend:

```json
"Capital": {
    "MaxLeverage": 2.0,        // Keep at 2x or lower
    "MaxAggregateLeverage": 1.5,
    "MaxPositionSizePercent": 15,  // Reduce from 25%
    "MaxOrderSizePercent": 10      // Reduce from 15%
}
```

If user INSISTS on higher leverage, implement graduated scaling:
- $200-$500: Max 2x
- $500-$1000: Max 3x
- $1000+: Max 5x (with caution)

**Verdict:** FAIL - Recommend keeping 2x leverage for this collateral size

---

### FINDING 3: AUTOSTART ENABLED IN PRODUCTION WITHOUT OPERATOR CONFIRMATION

**Risk Level:** HIGH
**Category:** Safety
**Location:** `GridBot.ApiService/appsettings.Production.json:19`
**Financial Impact:** Bot starts trading immediately on deployment without health verification

**Problem:**

```json
"AutoStartTrading": true
```

This means the bot will begin placing orders immediately upon startup without:
1. Operator visual confirmation of market conditions
2. Verification that WebSocket data is fresh
3. Confirmation that all risk systems are operational

In a production environment with real money, this is dangerous. What if:
- Deployment happens during a flash crash?
- WebSocket is momentarily disconnected?
- Market data is stale from a previous session?

**Fix:**

```json
"AutoStartTrading": false  // Require manual start via API/dashboard
```

Add a startup health check that requires ALL of these before allowing trading:
1. WebSocket connected for > 30 seconds
2. Order book data < 5 seconds old
3. Account balance verification
4. All risk monitors initialized

**Verdict:** FAIL - Change to false for production

---

### FINDING 4: WEBSOCKET RECONNECTION COULD CAUSE DUPLICATE ORDERS

**Risk Level:** HIGH
**Category:** Race Condition
**Location:** `GridBot.Lighter/LighterRealtimeStateService.cs:625-629` and `GridBot.ApiService/Services/Grid/GridLifecycleService.cs`
**Financial Impact:** Double position size if orders are placed during reconnection

**Problem:**

When WebSocket disconnects, the code clears all order book and order state:

```csharp
private void ClearAllOrderBookState()
{
    var count = _mutableOrderBooks.Count;
    _mutableOrderBooks.Clear();
    _orderBooks.Clear();
    // ...
}

private void ClearAllOrderState()
{
    var count = _mutableOrders.Count;
    _mutableOrders.Clear();
    _orders.Clear();
    // ...
}
```

However, the grid lifecycle service may not be immediately aware of this state reset. If `SyncOrderStatusAsync` runs during the reconnection window, it could:
1. See 0 orders in local state (cleared)
2. Query exchange and get back real orders
3. GridLifecycle thinks orders are missing, places duplicates

The fix at line 448-455 of `GridOrderManager.cs` helps:
```csharp
if (activeLevelsCount > 0 && orderLookup.Count == 0)
{
    _logger.LogWarning(
        "Order sync skipped for market {MarketId}: {ActiveLevels} active grid levels but exchange returned 0 orders...");
    return; // Exit early - do not modify order states
}
```

But this relies on `activeLevelsCount` being accurate, which may not be true during state transitions.

**Fix:**

Add a connection state flag that GridLifecycleService respects:

```csharp
public interface ILighterRealtimeState
{
    // Add:
    bool IsStateReliable { get; } // False during and immediately after reconnection
    DateTimeOffset? LastStateResetTime { get; }
}
```

In GridLifecycleService:
```csharp
if (!_realtimeState.IsStateReliable ||
    (realtimeState.LastStateResetTime.HasValue &&
     DateTimeOffset.UtcNow - realtimeState.LastStateResetTime.Value < TimeSpan.FromSeconds(30)))
{
    _logger.LogWarning("Skipping grid sync - WebSocket state not reliable");
    return;
}
```

**Verdict:** CONDITIONAL PASS - Existing fix helps but could be more robust

---

### FINDING 5: LACK OF EXPLICIT LEVERAGE VALIDATION BEFORE ORDER SUBMISSION

**Risk Level:** HIGH
**Category:** Safety
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs` and `RebalancingService.cs`
**Financial Impact:** Could exceed configured leverage limits if calculations are wrong

**Problem:**

Orders are placed without final pre-submission validation that the resulting position won't exceed leverage limits. The code calculates order sizes based on available balance and position limits, but doesn't perform a final check:

```csharp
// In CalculateOrderSizeAsync:
var orderSizeUsd = Math.Max(perLevelAllocation, _config.Capital.MinOrderSizeUsd);
orderSizeUsd = Math.Min(orderSizeUsd, availableBalance * (_config.Capital.MaxOrderSizePercent / 100m));
```

This doesn't account for:
1. Existing position size
2. Other pending orders
3. Current leverage utilization

**Evidence:**

The Lighter DEX API may or may not reject over-leveraged orders. The system should NOT rely on exchange rejection for safety.

**Fix:**

Add pre-submission leverage check:

```csharp
public async Task<(bool Allowed, string? Reason)> ValidateLeverageBeforeOrderAsync(
    int marketId,
    decimal orderSizeUsd,
    bool isLong,
    CancellationToken ct)
{
    var account = await _queryClient.GetAccountAsync(AccountIndex, ct);
    var collateral = decimal.Parse(account.Collateral);
    var currentPosition = GetCurrentPositionValue(account, marketId);
    var pendingOrdersValue = GetPendingOrdersValue(marketId);

    var projectedNotional = currentPosition + pendingOrdersValue + orderSizeUsd;
    var projectedLeverage = projectedNotional / collateral;

    if (projectedLeverage > _config.Capital.MaxLeverage)
    {
        return (false, $"Order would exceed max leverage: {projectedLeverage:F2}x > {_config.Capital.MaxLeverage}x");
    }

    return (true, null);
}
```

**Verdict:** FAIL - Add pre-submission leverage validation

---

## MEDIUM PRIORITY FINDINGS

### FINDING 6: DOUBLE TYPE USAGE IN TELEMETRY COULD MASK PRECISION ISSUES

**Risk Level:** MEDIUM
**Category:** Precision
**Location:** `GridBot.ApiService/Services/Telemetry/TradingMetrics.cs:21-26`
**Financial Impact:** Telemetry may show slightly incorrect values; debugging harder

**Problem:**

```csharp
// Thread-safe per-market gauge storage using double to avoid torn reads (decimal is 128-bit, not atomic)
private static readonly ConcurrentDictionary<int, double> _positionMultiplierByMarket = new();
private static readonly ConcurrentDictionary<int, double> _spreadMultiplierByMarket = new();
private static readonly ConcurrentDictionary<int, double> _currentPriceByMarket = new();
private static readonly ConcurrentDictionary<int, double> _currentEquityByMarket = new();
```

The comment acknowledges this is intentional for thread-safety, but precision loss in telemetry could make debugging difficult. For BTC at $100,000, the double precision is adequate, but the pattern sets a bad precedent.

**Fix:**

Keep the current implementation but add documentation that this is TELEMETRY ONLY and the core trading logic uses decimal. Consider using `Interlocked` operations with `long` for better precision:

```csharp
// Store as scaled long for atomic operations
private static readonly ConcurrentDictionary<int, long> _currentPriceScaled = new();

public static void SetCurrentPrice(int marketId, decimal price)
{
    _currentPriceScaled[marketId] = (long)(price * 100_000_000m); // 8 decimal places
}
```

**Verdict:** PASS - Acceptable for telemetry, core logic uses decimal

---

### FINDING 7: REBALANCING USES HARDCODED MAKER SPREAD

**Risk Level:** MEDIUM
**Category:** Trading Logic
**Location:** `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs:159`
**Financial Impact:** Rebalance orders may not fill in fast markets

**Problem:**

```csharp
const decimal aggressiveMakerSpread = 0.0003m; // 0.03%
var limitPrice = isAsk
    ? currentPrice * (1 - aggressiveMakerSpread)  // Sell slightly below mid for faster fill
    : currentPrice * (1 + aggressiveMakerSpread); // Buy slightly above mid for faster fill
```

This 0.03% spread is hardcoded. In volatile markets, this may not be aggressive enough for fills. In calm markets, it may be giving away edge.

**Fix:**

Make configurable and tie to current market spread:

```csharp
var currentSpread = orderBook.SpreadPercent;
var rebalanceSpread = Math.Min(
    _config.Rebalancing.AggressiveMakerSpread,
    currentSpread * 0.8m); // 80% of current spread
```

**Verdict:** PASS - Works but could be improved

---

### FINDING 8: NO CIRCUIT BREAKER FOR RAPID ORDER PLACEMENT

**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `GridBot.ApiService/Services/Grid/GridOrderManager.cs:33`
**Financial Impact:** Rapid order failures could indicate account issues

**Problem:**

```csharp
private const int CircuitBreakerThreshold = 3;
```

This constant is defined but the actual circuit breaker implementation is minimal. The system continues trying after failures without proper backoff at the grid level.

**Fix:**

Implement proper circuit breaker pattern:

```csharp
private int _consecutiveFailures = 0;
private DateTimeOffset? _circuitOpenUntil = null;

private async Task<bool> TryPlaceOrderWithCircuitBreakerAsync(...)
{
    if (_circuitOpenUntil.HasValue && DateTimeOffset.UtcNow < _circuitOpenUntil.Value)
    {
        _logger.LogWarning("Circuit breaker open, skipping order placement");
        return false;
    }

    try
    {
        // Place order...
        _consecutiveFailures = 0;
        return true;
    }
    catch
    {
        _consecutiveFailures++;
        if (_consecutiveFailures >= CircuitBreakerThreshold)
        {
            _circuitOpenUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            _logger.LogCritical("Circuit breaker OPEN after {Failures} consecutive failures", _consecutiveFailures);
        }
        return false;
    }
}
```

**Verdict:** CONDITIONAL PASS - Basic protection exists

---

## LOW PRIORITY FINDINGS

### FINDING 9: WEBHOOK URL HARDCODED

**Risk Level:** LOW
**Category:** Configuration
**Location:** `GridBot.ApiService/appsettings.Production.json:156-157`
**Financial Impact:** None - operational concern

**Problem:**

```json
"Webhook": {
    "Enabled": true,
    "Url": "http://192.168.1.175:8123/api/webhook/-Y-QjbVoWNw1UzxMqW2OhAOpP"
}
```

Local network webhook URL in production config.

**Fix:**

Use environment variable or ensure this is your actual production webhook.

**Verdict:** PASS - Operational preference

---

### FINDING 10: GOOD PRACTICES OBSERVED

The audit identified several positive patterns:

1. **Decimal used for all monetary calculations** - Proper use of `decimal` type throughout `GridCalculator.cs`, `MarketScalingService.cs`, and `RebalancingService.cs`

2. **Thread-safe state management** - Appropriate use of `ConcurrentDictionary`, `SemaphoreSlim`, and `lock` statements in risk services

3. **Lot size compliance** - `MarketScalingService.ScaleBaseAmountAsync()` properly snaps to lot sizes:
```csharp
if (lotSize > 1)
{
    result = ((rawResult + lotSize / 2) / lotSize) * lotSize;
}
```

4. **CultureInfo.InvariantCulture for parsing** - Consistent use throughout:
```csharp
decimal.TryParse(order.InitialBaseAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out var initialAmount)
```

5. **Order delta accumulation fix** - Session 4 fix properly handles WebSocket delta accumulation

6. **False fill detection prevention** - `GridOrderManager.SyncOrderStatusAsync()` guards against false fills

7. **Comprehensive risk monitoring** - FlashCrash, FlashPump, Loss limits, and Liquidity monitors work together

---

## LEVERAGE DEPLOYMENT RECOMMENDATIONS

For $200 collateral deployment, here are the recommended settings:

### Conservative (RECOMMENDED for first 30 days)
```json
{
    "Capital": {
        "MaxLeverage": 1.5,
        "MaxAggregateLeverage": 1.0,
        "MaxPositionSizePercent": 15,
        "MaxOrderSizePercent": 8,
        "ReserveBalancePercent": 40
    },
    "Grid": {
        "MinOrdersPerSide": 3,
        "MaxOrdersPerSide": 5,
        "DefaultOrdersPerSide": 4
    },
    "LossLimits": {
        "Rolling24HourLossPercent": -8,
        "SingleTradeLossPercent": -3
    }
}
```

### Moderate (After 30 days of profitable operation)
```json
{
    "Capital": {
        "MaxLeverage": 2.0,
        "MaxAggregateLeverage": 1.5,
        "MaxPositionSizePercent": 20,
        "MaxOrderSizePercent": 12
    }
}
```

### Aggressive (NOT RECOMMENDED at this capital level)
5x leverage is not recommended for $200 collateral under any circumstances.

---

## PRE-DEPLOYMENT CHECKLIST

Before deploying to production:

- [ ] **CRITICAL**: Rotate ALL private keys - current keys are exposed
- [ ] **CRITICAL**: Move private keys to environment variables or user secrets
- [ ] **HIGH**: Set `AutoStartTrading: false`
- [ ] **HIGH**: Keep leverage at 2x or lower
- [ ] **HIGH**: Add pre-submission leverage validation
- [ ] **MEDIUM**: Test full system with testnet for 24h continuous operation
- [ ] **MEDIUM**: Verify webhook endpoint is accessible
- [ ] **LOW**: Review and confirm all loss limits are appropriate

---

## AUDIT SUMMARY

```
====================================================================
AUDIT SUMMARY
====================================================================
Total Findings: 10
+--- CRITICAL Risk: 1 (BLOCKING)
|    +-- Private key hardcoded in production config
+--- HIGH Risk: 4
|    +-- 5x leverage too high for $200 collateral
|    +-- AutoStartTrading enabled without safeguards
|    +-- WebSocket reconnection race condition
|    +-- No pre-submission leverage validation
+--- MEDIUM Risk: 4
|    +-- Double type in telemetry
|    +-- Hardcoded rebalancing spread
|    +-- Circuit breaker incomplete
|    +-- Webhook URL hardcoded
+--- LOW Risk: 1
     +-- Configuration preferences

Overall Verdict: CONDITIONAL PASS
Deployment Recommendation: DO NOT DEPLOY until CRITICAL issue fixed

Required Actions:
1. IMMEDIATELY rotate all private keys
2. Move secrets to environment variables
3. Set AutoStartTrading to false
4. Keep leverage at 2x for $200 collateral
5. Run 24h testnet validation before mainnet
====================================================================
```

---

## FILES REVIEWED

| File | Status |
|------|--------|
| `GridBot.ApiService/Services/TradingBotHostedService.cs` | Reviewed |
| `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs` | Reviewed |
| `GridBot.ApiService/Services/Grid/GridOrderManager.cs` | Reviewed |
| `GridBot.ApiService/Services/Grid/GridLifecycleService.cs` | Reviewed |
| `GridBot.ApiService/Services/Grid/GridCalculator.cs` | Reviewed |
| `GridBot.ApiService/Services/Risk/RiskSentinel.cs` | Reviewed |
| `GridBot.ApiService/Services/Risk/FlashCrashDetector.cs` | Reviewed |
| `GridBot.ApiService/Services/Risk/FlashPumpDetector.cs` | Reviewed |
| `GridBot.ApiService/Services/Risk/LossMonitor.cs` | Reviewed |
| `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs` | Reviewed |
| `GridBot.ApiService/Services/MarketData/MarketScalingService.cs` | Reviewed |
| `GridBot.ApiService/Configuration/TradingBotOptions.cs` | Reviewed |
| `GridBot.ApiService/appsettings.Production.json` | Reviewed |
| `GridBot.Lighter/LighterRealtimeStateService.cs` | Reviewed |

---

*Audit completed by Trading Systems Auditor*
*Document Version: 1.0*
*Session: 4*
