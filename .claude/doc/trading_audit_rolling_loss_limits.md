# Trading Correctness Audit: Rolling Window Loss Limits Implementation

**Audit Date:** 2025-12-06
**Auditor:** Trading Systems Auditor (trading-bot-auditor)
**System:** ALTE Grid Bot - Rolling Window Loss Limits

---

## Executive Summary

This audit evaluates the trading correctness of the rolling window loss limits implementation for the ALTE Grid Bot. The implementation migrates from calendar-based loss limits (daily/weekly/monthly with manual resets at midnight) to rolling window loss limits (24h/7d/30d continuous windows).

**OVERALL VERDICT: FAIL - CRITICAL ISSUES FOUND**

The implementation has a **CRITICAL** bug that renders the entire rolling window loss limit system non-functional: **Trade P&L is never recorded.**

---

## Finding 1: TRADE P&L RECORDING IS NEVER CALLED

**Risk Level:** CRITICAL
**Category:** Trading Logic / Safety
**Location:** System-wide - `RecordTradeResultAsync()` method
**Financial Impact:** TOTAL LOSS LIMIT SYSTEM FAILURE - Rolling windows will always show 0% P&L

**Problem:**

The method `RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct)` exists in both `ILossMonitor` (line 26) and `IRiskSentinel` (line 56), and is properly implemented in `LossMonitor.cs` (line 83) and `RiskSentinel.cs` (line 268).

HOWEVER, this method is **NEVER CALLED** from anywhere in the codebase.

**Evidence:**

```bash
# Search for calls to RecordTradeResultAsync
grep -r "\.RecordTradeResult" GridBot.ApiService --include="*.cs"
# Result: Only found in RiskSentinel.cs which delegates to LossMonitor

grep -r "_riskSentinel\.RecordTradeResult" GridBot.ApiService --include="*.cs"
# Result: NO MATCHES
```

The trade history (`state.TradeHistory`) will always be empty, meaning:
- `CalculateRollingPnl()` will always return `0m`
- `Rolling24hBreached`, `Rolling7dBreached`, `Rolling30dBreached` will NEVER trigger
- The only working limit is `MaxDrawdown` (equity-based)

**Impact Analysis:**

With $500 capital:
- Bot could lose $500 (100%) without any rolling limit triggering
- Only the drawdown limit would eventually trigger (at -35% = $175 loss)
- The 24h/7d/30d limits are completely ineffective

**Fix:**

Trade P&L must be calculated and recorded when grid fills are detected. The implementation should:

1. In `GridOrderManager.SyncOrderStatuses()` or fill detection logic:
```csharp
// When a fill is detected
decimal fillPnlPercent = CalculateFillPnl(fill, currentEquity);
await _riskSentinel.RecordTradeResultAsync(marketId, fillPnlPercent, ct).ConfigureAwait(false);
```

2. Create a method to calculate P&L from fills:
```csharp
private decimal CalculateFillPnl(OrderFill fill, decimal currentEquity)
{
    // For SELL fill: profit = (fillPrice - avgBuyPrice) * quantity
    // For BUY fill: no immediate P&L (position building)
    // Convert to percentage of equity
    return (realizedPnlUsd / currentEquity) * 100m;
}
```

**Verdict:** FAIL - BLOCKING

---

## Finding 2: P&L Percentage Summation Correctness

**Risk Level:** MEDIUM
**Category:** Precision / Trading Logic
**Location:** `LossMonitor.cs:407-418`
**Financial Impact:** Could misrepresent actual loss by 5-20% in volatile conditions

**Problem:**

The current implementation sums P&L percentages directly:

```csharp
private static decimal CalculateRollingPnl(RollingLossState state, TimeSpan window)
{
    var windowStart = DateTimeOffset.UtcNow - window;
    return state.TradeHistory
        .Where(t => t.Timestamp >= windowStart)
        .Sum(t => t.PnlPercent);
}
```

This is mathematically incorrect when equity changes significantly during the window. Consider:

| Trade | Equity Before | P&L USD | P&L % Recorded | True Impact |
|-------|---------------|---------|----------------|-------------|
| 1 | $500 | -$50 | -10% | -10% of original |
| 2 | $450 | -$45 | -10% | -9% of original |
| 3 | $405 | -$40.50 | -10% | -8.1% of original |

**Sum of percentages:** -30%
**Actual loss from original:** $135.50 = -27.1%

In a bear market with multiple losses, the summed percentages **overstate** losses.
In a bull market with multiple gains, the summed percentages **understate** gains.

**Correct Approach (Compounded Returns):**

```csharp
private static decimal CalculateRollingPnl(RollingLossState state, TimeSpan window)
{
    if (state.TradeHistory.Count == 0)
        return 0m;

    var windowStart = DateTimeOffset.UtcNow - window;
    var tradesInWindow = state.TradeHistory
        .Where(t => t.Timestamp >= windowStart)
        .OrderBy(t => t.Timestamp);

    // Compound the returns
    decimal cumulativeMultiplier = 1m;
    foreach (var trade in tradesInWindow)
    {
        cumulativeMultiplier *= (1m + trade.PnlPercent / 100m);
    }

    return (cumulativeMultiplier - 1m) * 100m;
}
```

**Mitigating Factor:**

For grid bots with many small trades (0.2-0.5% each), the error is small. But for single trade loss limits (-3%), the difference is negligible. This is MEDIUM risk because the current approach is conservative (overstates losses), which triggers protection earlier.

**Verdict:** CONDITIONAL PASS - Acceptable for production with caveat

---

## Finding 3: Threshold Appropriateness Analysis

**Risk Level:** MEDIUM
**Category:** Safety / Risk Management
**Location:** `TradingBotOptions.cs:129-192`

**Analysis of New Thresholds:**

| Window | Old Value | New Value | Assessment |
|--------|-----------|-----------|------------|
| 24-hour | -5% | -12% | AGGRESSIVE - 2.4x looser |
| 7-day | -10% | -20% | AGGRESSIVE - 2x looser |
| 30-day | -15% | -30% | AGGRESSIVE - 2x looser |
| Max drawdown | -20% | -35% | AGGRESSIVE - 1.75x looser |

**Impact with $500 Capital:**

| Threshold | $ Loss at Trigger | % to Recover |
|-----------|-------------------|--------------|
| 24h @ -12% | $60 | +13.6% |
| 7d @ -20% | $100 | +25.0% |
| 30d @ -30% | $150 | +42.9% |
| Drawdown @ -35% | $175 | +53.8% |

**Assessment:**

The user explicitly approved these thresholds in the specification document. The rationale provided is sound:
- Grid bots should profit in sideways markets (which is 60%+ of the time for BTC)
- The thresholds allow for volatile BTC movements without false triggers
- Cascading protection: 24h triggers first, then 7d, then 30d

**HOWEVER:** These thresholds are appropriate ONLY if:
1. The trade P&L recording is fixed (Finding 1)
2. The bot is running on testnet with monitored capital initially
3. An operator can manually intervene

**Risk Profile Summary:**
- Conservative user: These thresholds are TOO LOOSE
- Aggressive user: These thresholds are APPROPRIATE
- The spec document shows the user chose these knowingly

**Verdict:** PASS - User-approved risk appetite

---

## Finding 4: Recovery Logic Assessment

**Risk Level:** MEDIUM
**Category:** Safety / Trading Logic
**Location:** `LossMonitor.cs:426-458`

**Current Recovery Logic:**

```csharp
// For rolling limits: auto-clear when metrics improve to 50% of limit
return state.HaltReason switch
{
    "Rolling24hLimit" => pnl24h > limits.Rolling24HourLossPercent * 0.5m,
    "Rolling7dLimit" => pnl7d > limits.Rolling7DayLossPercent * 0.5m,
    "Rolling30dLimit" => pnl30d > limits.Rolling30DayLossPercent * 0.5m,
    _ => true
};

// Max drawdown: requires 75% of HWM recovery or manual override
if (state.HaltReason == "MaxDrawdown")
{
    var recoveryTarget = state.EquityHighWaterMark * 0.75m;
    return state.CurrentEquity >= recoveryTarget;
}
```

**Analysis:**

1. **24h Recovery (50% of -12% = -6%):**
   - After hitting -12% and waiting 4 hours, if rolling 24h loss improves to -6%, trading resumes
   - This means bad trades "roll off" the window naturally
   - RISK: If market continues falling, bot resumes right into further losses

2. **Max Drawdown Recovery (75% of HWM):**
   - If HWM was $600 and drawdown triggered at $390 (-35%), bot stays halted until equity reaches $450
   - This could take weeks/months in a bear market
   - APPROPRIATE: Max drawdown should require real recovery

**Concern:**

Auto-recovery at 50% of limit is AGGRESSIVE for crypto markets. A flash crash can continue for 24+ hours.

**Recommendation:**

Add a secondary condition: `AND no active flash crash protection AND 24h volatility < 2x normal`

**Verdict:** CONDITIONAL PASS - Works but could be more conservative

---

## Finding 5: "NEVER HALT" Philosophy Implementation

**Risk Level:** LOW
**Category:** Safety
**Location:** `LossMonitor.cs:484`, `RiskSentinel.cs:228-253`

**Assessment:**

The implementation correctly follows the "never halt" philosophy:

1. On breach, transitions to `Degraded_ProtectiveMode`:
```csharp
await _tradingState.TransitionToAsync(TradingState.Degraded_ProtectiveMode, "Rolling 24h loss limit breached").ConfigureAwait(false);
```

2. `TradingDecisionEngine.CanTrade()` checks:
```csharp
if (state == TradingState.Degraded_ProtectiveMode)
{
    // Allow trailing stops and position reduction, but not new grid orders
    return false;
}
```

3. Risk monitoring continues - bot can still:
   - Execute trailing stops
   - Reduce positions
   - Monitor equity
   - Detect further deterioration

**Verdict:** PASS

---

## Finding 6: Edge Case Handling

**Risk Level:** MEDIUM
**Category:** Edge Cases
**Location:** `LossMonitor.cs`

### Edge Case 1: No Trades for 24 Hours

```csharp
private static decimal CalculateRollingPnl(RollingLossState state, TimeSpan window)
{
    if (state.TradeHistory.Count == 0)
    {
        return 0m;
    }
    // ...
}
```

**Assessment:** CORRECT - Returns 0%, which is neutral (not breached)

### Edge Case 2: Bot Restart Mid-Period

```csharp
public async Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct)
{
    // Load trade history from Redis
    var retentionDays = _riskConfig.LossLimits.TradeRecordRetentionDays;
    var since = DateTimeOffset.UtcNow.AddDays(-retentionDays);
    var trades = await _stateRepository.LoadTradeRecordsAsync(marketId, since, ct);
    state.TradeHistory = [.. trades];
    // ...
}
```

**Assessment:** CORRECT - Trades are persisted to Redis with TTL, restored on startup

### Edge Case 3: Single Trade +1000% (Manipulation/Error)

**ISSUE:** No validation of trade P&L bounds!

```csharp
public async Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct)
{
    // No bounds checking!
    var trade = TradeRecord.Create(marketId, pnlPercent, state.CurrentEquity);
    state.TradeHistory.Add(trade);
}
```

A single +1000% trade would:
- Skew the 24h P&L massively positive
- Allow massive losses before limits trigger again

**Fix Required:**

```csharp
public async Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct)
{
    // Sanity check: single trade P&L should be bounded
    const decimal MaxReasonablePnl = 50m; // +/- 50% max per trade
    if (Math.Abs(pnlPercent) > MaxReasonablePnl)
    {
        _logger.LogCritical(
            "SUSPICIOUS TRADE P&L: {PnlPercent}% exceeds {Max}%. Capping for safety.",
            pnlPercent, MaxReasonablePnl);
        pnlPercent = Math.Sign(pnlPercent) * MaxReasonablePnl;
    }
    // ...
}
```

**Verdict:** FAIL - Missing bounds validation

---

## Finding 7: Position Safety During Halt

**Risk Level:** LOW
**Category:** Safety
**Location:** `TradingDecisionEngine.cs`, `RiskSentinel.cs`

**Assessment:**

When a rolling limit is breached:

1. Bot enters `Degraded_ProtectiveMode`
2. Grid trading stops (`CanTrade()` returns false)
3. Position monitoring CONTINUES
4. Trailing stops can still execute
5. Risk assessment continues every cycle

```csharp
// Even in protective mode, these run:
await RecordMetricsAsync(marketId, context, ct);
var assessment = await _riskSentinel.AssessRiskAsync(marketId, ct);
```

**What's Protected:**
- Existing positions are NOT force-closed
- Trailing stops remain active
- Flash crash protection remains active
- Equity monitoring continues

**What's NOT Protected:**
- If BTC continues falling 50%+ in a crash, existing positions take full loss
- No automatic position reduction in protective mode

**Verdict:** PASS - Appropriate for grid bot philosophy (ride out volatility)

---

## Finding 8: Auto-Recovery Risk Assessment

**Risk Level:** HIGH
**Category:** Safety
**Location:** `LossMonitor.cs:446-458`

**Problem:**

The auto-recovery logic allows trading to resume without human review:

```csharp
// After 4h wait + metrics at 50% of limit, trading resumes automatically
"Rolling24hLimit" => pnl24h > limits.Rolling24HourLossPercent * 0.5m,
```

**Scenario:**

1. 08:00 - Rolling 24h P&L hits -12%, halt triggered
2. 08:00-12:00 - No trading, bad trades "roll off" window
3. 12:00 - Rolling 24h P&L now shows -6% (half the limit)
4. 12:00 - Trading auto-resumes
5. 12:01 - Flash crash continues, bot loses another 12% in 4 hours
6. 16:00 - Halt triggers again

**Issue:**

The 50% recovery threshold is easily gamed by TIME passing, not actual market improvement. This is because rolling windows naturally "forget" old losses as they fall outside the window.

**Recommendation:**

Add additional recovery conditions:
1. Current volatility < 2x normal (ATR check)
2. No active flash crash protection
3. Order book depth > minimum threshold
4. Require at least one equity snapshot ABOVE the halt level

**Fix Required in `CanAutoClearHaltAsync`:**

```csharp
// Additional checks before auto-recovery
if (state.HaltReason?.StartsWith("Rolling") == true)
{
    // 1. Check volatility is normalized
    var currentAtr = await GetCurrentAtrAsync(marketId, ct);
    var normalAtr = await GetNormalAtrAsync(marketId, ct);
    if (currentAtr > normalAtr * 2)
        return false;

    // 2. Check no active flash crash
    if (_flashCrashDetector.IsInCrashProtection(marketId))
        return false;

    // 3. Check equity is stable (not just window rolloff)
    var recentEquityTrend = CalculateEquityTrend(state.EquitySnapshots, TimeSpan.FromHours(4));
    if (recentEquityTrend < 0) // Still declining
        return false;
}
```

**Verdict:** FAIL - Auto-recovery is too permissive

---

## Finding 9: Trade Recording Accuracy

**Risk Level:** CRITICAL (linked to Finding 1)
**Category:** Data Integrity
**Location:** `TradeRecord.cs:47-66`

**Assessment:**

The P&L calculation in `TradeRecord.Create()` is correct:

```csharp
public static TradeRecord Create(
    int marketId,
    decimal pnlPercent,
    decimal currentEquity,
    string? orderId = null)
{
    var pnlUsd = currentEquity * (pnlPercent / 100m);

    return new TradeRecord
    {
        Id = Guid.NewGuid(),
        MarketId = marketId,
        Timestamp = DateTimeOffset.UtcNow,
        PnlPercent = pnlPercent,
        PnlUsd = pnlUsd,
        EquityAtTrade = currentEquity,
        OrderId = orderId
    };
}
```

**HOWEVER:** The bigger issue is that `pnlPercent` must be calculated CORRECTLY at the call site, which doesn't exist (Finding 1).

**Issues with Potential P&L Calculation:**

1. Grid fills have two sides: BUY and SELL
2. P&L is only REALIZED when a SELL happens (for long positions)
3. The fill price must be compared to the average entry price
4. Fees must be subtracted from P&L

**Missing Requirements for Accurate Trade Recording:**

```csharp
// This is what SHOULD exist but DOESN'T:
private decimal CalculateRealizedPnl(OrderFill sell, GridLevel level, decimal currentEquity)
{
    // Get average buy price for this grid level
    decimal avgBuyPrice = level.EntryPrice;
    decimal sellPrice = sell.Price;
    decimal quantity = sell.Quantity;
    decimal fees = sell.Quantity * sell.Price * FeeRate;

    decimal grossPnl = (sellPrice - avgBuyPrice) * quantity;
    decimal netPnl = grossPnl - fees;
    decimal pnlPercent = (netPnl / currentEquity) * 100m;

    return pnlPercent;
}
```

**Verdict:** FAIL - Cannot evaluate recording accuracy when recording doesn't happen

---

## Summary Table

| # | Finding | Risk | Verdict |
|---|---------|------|---------|
| 1 | Trade P&L never recorded | CRITICAL | FAIL |
| 2 | P&L percentage summation | MEDIUM | CONDITIONAL PASS |
| 3 | Threshold appropriateness | MEDIUM | PASS (user-approved) |
| 4 | Recovery logic | MEDIUM | CONDITIONAL PASS |
| 5 | "Never halt" implementation | LOW | PASS |
| 6 | Edge case handling | MEDIUM | FAIL (no bounds check) |
| 7 | Position safety during halt | LOW | PASS |
| 8 | Auto-recovery risk | HIGH | FAIL |
| 9 | Trade recording accuracy | CRITICAL | FAIL (depends on #1) |

---

===============================================
AUDIT SUMMARY
===============================================

**Total Findings:** 9

- **CRITICAL Risk:** 2 (BLOCKING)
- **HIGH Risk:** 1 (BLOCKING)
- **MEDIUM Risk:** 4 (Recommended Fix)
- **LOW Risk:** 2 (Acceptable)

**Overall Verdict:** FAIL

**Deployment Recommendation:**

DO NOT DEPLOY TO PRODUCTION. The following issues MUST be fixed:

1. **CRITICAL:** Implement trade P&L recording in the fill detection flow
2. **CRITICAL:** Add P&L bounds validation to prevent manipulation
3. **HIGH:** Add additional conditions to auto-recovery logic

After fixes, the system should be:
1. Deployed to testnet for 7+ days
2. Verified that trade records accumulate correctly
3. Manually triggered breach scenarios to validate recovery
4. Only then deployed to mainnet with limited capital ($100-$500)

===============================================

---

## Appendix: Recommended Fix Priority

### Priority 1: CRITICAL (Must Fix)

**A. Implement Trade P&L Recording**

Location: `GridOrderManager.cs` or fill detection service

```csharp
// When a SELL fill is detected for a grid level
public async Task OnSellFillDetectedAsync(int marketId, GridLevel level, OrderFill fill, decimal currentEquity, CancellationToken ct)
{
    decimal avgBuyPrice = level.EntryPrice; // Or tracked average
    decimal sellPrice = fill.Price;
    decimal quantity = fill.Quantity;

    // Calculate realized P&L
    decimal grossPnl = (sellPrice - avgBuyPrice) * quantity;
    decimal feeEstimate = sellPrice * quantity * 0.0002m; // Maker fee estimate
    decimal netPnlUsd = grossPnl - feeEstimate;
    decimal pnlPercent = currentEquity > 0 ? (netPnlUsd / currentEquity) * 100m : 0m;

    // Record to loss monitor
    await _riskSentinel.RecordTradeResultAsync(marketId, pnlPercent, ct).ConfigureAwait(false);

    _logger.LogInformation(
        "Recorded grid fill P&L for market {MarketId}: {PnlPercent:F2}% ({PnlUsd:F2} USD)",
        marketId, pnlPercent, netPnlUsd);
}
```

**B. Add P&L Bounds Validation**

Location: `LossMonitor.cs:RecordTradeResultAsync()`

```csharp
public async Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default)
{
    // Sanity bounds check
    const decimal MaxSingleTradePnl = 50m; // +/- 50% maximum
    if (Math.Abs(pnlPercent) > MaxSingleTradePnl)
    {
        _logger.LogCritical(
            "TRADE P&L OUT OF BOUNDS: {PnlPercent}%. Capping at {Max}%. Investigate immediately.",
            pnlPercent, MaxSingleTradePnl);

        var alertEvent = RiskEvent.Create(
            "PNL-BOUNDS",
            AlertSeverity.Critical,
            $"Trade P&L {pnlPercent:F2}% exceeds bounds (+/- {MaxSingleTradePnl}%)",
            "Capped value recorded, manual review required",
            pnlPercent,
            MaxSingleTradePnl);
        await _eventLogger.LogEventAsync(alertEvent, ct).ConfigureAwait(false);

        pnlPercent = Math.Sign(pnlPercent) * MaxSingleTradePnl;
    }

    // Continue with existing logic...
}
```

### Priority 2: HIGH (Should Fix)

**Improve Auto-Recovery Logic**

Location: `LossMonitor.cs:CanAutoClearHaltAsync()`

Add volatility and trend checks before allowing auto-recovery.

### Priority 3: MEDIUM (Recommended)

**Use Compounded Returns Instead of Sum**

Location: `LossMonitor.cs:CalculateRollingPnl()`

This is lower priority because the current approach is conservative (overstates losses).

---

## Conclusion

The rolling window loss limits implementation has a sound design based on the specification document. The threshold selections are appropriate for an aggressive user with a grid trading strategy.

HOWEVER, the implementation is fundamentally broken because trade P&L is never recorded. Until Finding 1 is fixed, the rolling window system provides ZERO protection. Only the equity-based max drawdown limit is functional.

**Estimated Fix Effort:** 4-8 hours for a competent .NET developer
**Testing Requirement:** 7+ days on testnet to validate trade recording accuracy

---

*This audit was performed by the Trading Systems Auditor agent based on code review and trading domain expertise. Manual testing of the actual runtime behavior is still required.*
