# ALTE (Adaptive Liquidity & Trend Engine) - Comprehensive Risk Analysis

## Document Purpose
This document provides a comprehensive deep analysis of the ALTE trading bot's strategy, risk management, and behavior across different market scenarios. It identifies potential conflicts, edge cases, and risk gaps in the current implementation.

---

## A. STRATEGY EVALUATION

### 1. Overall Strategy Coherence Assessment

**Summary: WELL-DESIGNED but with SPECIFIC CONFLICTS requiring attention**

The ALTE system combines multiple strategy components that generally work well together, but there are specific interactions that create conflicts:

| Component | Purpose | Integration Quality |
|-----------|---------|---------------------|
| Dynamic Grid | Market making via ATR-based spacing | GOOD - adaptive to volatility |
| Trend Following | Inventory skew management | GOOD - clear state machine |
| Moon Bag Protection | Prevent selling early in bull runs | MODERATE - conflicts with trend |
| Flash Crash Detection | Capital preservation | GOOD - tiered response |
| Loss Limits | Rolling window P&L monitoring | GOOD - comprehensive |
| Recovery System | Gradual capacity restoration | GOOD - phased approach |

### 2. Grid Trading vs Trend Following Conflict Analysis

**CONFLICT IDENTIFIED: Grid becomes one-sided in strong trends**

#### Problem
In a strong trend:
- **Strong Bull (80% long target)**: Grid skews heavily toward bids (buys)
- **Strong Bear (-80% short target)**: Grid skews heavily toward asks (sells)

This creates the following issues:
1. **One-sided fill rate**: In strong trends, only one side of the grid fills repeatedly
2. **P&L drag**: Constant rebalancing costs (fees + spread) without completing round-trip trades
3. **Inventory accumulation**: Position grows in trend direction faster than the grid can manage

#### Current Mitigation in Code
```csharp
// From GridLifecycleService.cs:GetSkewCorrectionMultipliers()
if (skewDelta > 5)  // Over-exposed
    return (0.25m, 1.5m);  // Reduce buys 75%, increase sells 50%
if (skewDelta < -5)  // Under-exposed
    return (1.5m, 0.25m);  // Increase buys 50%, reduce sells 75%
```

#### Assessment
This mitigation is PARTIAL - it adjusts order sizes but does not:
- Reduce grid order count on the skew-aligned side
- Implement asymmetric grid spacing
- Pause grid trading when trend is confirmed strong

### 3. Moon Bag vs Inventory Management Conflict

**CRITICAL CONFLICT IDENTIFIED**

#### Scenario: Trend turns bearish but moon bag is protecting 15% of position

**What happens:**

1. **TrendDetector** identifies `TrendState.StrongBear` requiring -80% short target
2. **InventoryManager** calculates need to flip from long to short
3. **MoonBagManager** in `HoldMode` blocks sell orders via `ShouldBlockSellOrderAsync()`
4. **Result**: Bot cannot reach target skew because moon bag prevents selling

**Code Flow:**
```csharp
// MoonBagManager.cs:ShouldBlockSellOrderAsync()
if (status.State is MoonBagState.Inactive or MoonBagState.Released)
    return false;  // Only these states allow selling

var remainingAfterSell = currentPositionSize - sellQuantity;
if (remainingAfterSell < threshold)  // Would breach moon bag
    return true;  // BLOCKED
```

#### Current Resolution Mechanism
The moon bag can be released if:
1. State reaches `HoldMode`
2. `CheckReleaseConditionsAsync()` returns true:
   - Trend is `StrongBear`
   - Price < 50 MA < 200 MA
3. Operator calls `ApproveReleaseAsync()`

**GAP IDENTIFIED**: Release requires MANUAL OPERATOR APPROVAL - not automatic.

#### Recommendation
Consider automatic release when:
- `StrongBear` trend confirmed for > 4 hours
- Price below both 50 MA and 200 MA for > 2 hours
- Cumulative unrealized loss on moon bag exceeds 15%

---

## B. MARKET SCENARIO ANALYSIS

### SCENARIO 1: SUDDEN 20% PRICE DROP (Flash Crash)

**Timeline of Bot Response:**

| Time | Price Drop | Detection | Action | State Transition |
|------|------------|-----------|--------|------------------|
| T+0 | -3% in 1 min | `FlashCrashDetector` Minor | Pause BUYs (5 min) | Stays Active |
| T+1 | -5% in 5 min | `FlashCrashDetector` Moderate | Pause ALL orders (15 min) | -> `Degraded_HighVolatility` |
| T+3 | -10% in 15 min | `FlashCrashDetector` Severe | Cancel orders, reduce 50% | -> `Degraded_ProtectiveMode` |
| T+5 | -15% in 60 min | `FlashCrashDetector` Extreme | Full halt (240 min) | -> `Degraded_ProtectiveMode` |
| T+5 | -20% | Same as above | 24h halt if 2+ events | Extended protection |

**Detailed Walkthrough:**

1. **At -3% in 1 minute:**
   ```csharp
   // FlashCrashDetector triggers
   FlashCrashAction.PauseBuys -> 5 minutes
   // Bot continues selling to protect position
   ```

2. **At -5% in 5 minutes:**
   ```csharp
   FlashCrashAction.PauseAll -> 15 minutes
   await _tradingState.TransitionToAsync(TradingState.Degraded_HighVolatility, ...)
   ```

3. **At -10% in 15 minutes:**
   ```csharp
   FlashCrashAction.CancelAndReduceHalf
   // Position multiplier drops to 0.5m in RiskSentinel.CalculatePositionMultiplier()
   await _tradingState.TransitionToAsync(TradingState.Degraded_ProtectiveMode, ...)
   ```

4. **At -20% (within 60 min):**
   ```csharp
   FlashCrashAction.FullHalt
   protectionDuration = TimeSpan.FromMinutes(240)  // 4 hours
   ```

**CRITICAL BEHAVIOR: Grid is NOT torn down**

From `TradingDecisionEngine.HandleEmergencyResponseAsync()`:
```csharp
// DO NOT teardown grid - keep sell orders alive for position protection
// Only cancel buy orders to prevent adding to position
await _stateService.TransitionToAsync(TradingState.Degraded_ProtectiveMode, ...)
```

**What happens to existing position?**

| Aspect | Behavior |
|--------|----------|
| Existing longs | Protected - sell orders remain |
| Buy orders | Cancelled |
| Trailing stops | Continue to execute (exception per TSF-001) |
| Moon bag | Remains protected |

**Recovery Timeline:**
1. Protection period expires (4h minimum)
2. `RecoveryManager.CheckPhaseAdvancementAsync()` checks:
   - Minimum time elapsed
   - Volatility < 2%
   - No new circuit breakers
   - P&L stable
   - Order book depth > $100k
   - No API errors in 5 min
3. Phases advance: 25% -> 50% -> 75% -> 100% capacity
4. Total recovery: ~1.5-2 hours minimum per phase

---

### SCENARIO 2: SUDDEN 20% PRICE SPIKE (Pump)

**Timeline of Bot Response:**

| Time | Event | Detection | Action |
|------|-------|-----------|--------|
| T+0 | +5% in 5 min | `FlashSpikeDetector` | Suspend high watermark updates |
| T+0 | Price > Grid + 10% | `TrailingStopService` | Activate trailing stop |
| T+1 | Price > Initial grid + 2% | `TrailingGridService` | Shift grid upward |
| T+2 | +20% in 5 min | `FlashSpikeDetector` | 10-minute cooldown on grid shifts |

**Detailed Walkthrough:**

1. **Flash Spike Detection (EC-SPIKE-001):**
   ```csharp
   // FlashSpikeDetector.IsFlashSpikeActiveAsync()
   // IF price_increase_5min > 20% THEN suspend_high_watermark_updates(10_minutes)
   if (isFlashSpikeActive)
       return false;  // Don't update high watermark during spike
   ```

2. **Trailing Stop Activation:**
   ```csharp
   // MoonBagManager.UpdateProfitPercentAsync()
   var activationPrice = status.InitialGridUpperBound * (1 + options.TrailingStopActivationThreshold);  // +10%
   if (currentPrice > activationPrice)
       status.State = MoonBagState.Trailing;
   ```

3. **Grid Shift:**
   ```csharp
   // TrailingGridService.DetectBreakoutAsync()
   // If price > upper bound + TrailingGridStep (2%)
   // AND NOT flash spike active
   // AND cumulative shift < MaxCumulativeShift1h (20%)
   await ShiftGridUpwardAsync(marketId, ct);
   ```

4. **What if immediate dump after spike?**

   | Scenario | Protection |
   |----------|------------|
   | Dump to entry | Trailing stop at 15% distance (not triggered) |
   | Dump -20% from high | Trailing stop triggers, sells 85% of position |
   | Dump below entry | Moon bag (15%) preserved, rest sold |

**GAP IDENTIFIED**: No upward flash crash detection
- Current implementation only detects DOWNWARD crashes
- Parabolic moves up could result in buying at unsustainable prices

---

### SCENARIO 3: BULL MARKET (Sustained Uptrend)

**State Progression:**

| Week | Price | Trend State | Target Skew | Moon Bag | Capacity |
|------|-------|-------------|-------------|----------|----------|
| 1 | $100 | Neutral | 0% | Inactive | 100% |
| 2 | $110 | MildBull | +50% | WarmingUp | 100% |
| 3 | $125 | StrongBull | +80% | Tracking | 100% |
| 4 | $140 | StrongBull | +80% | Trailing | 100% |
| 5 | $150 | StrongBull | +80% | Trailing | 100% |

**Inventory Management Behavior:**

1. **Neutral -> MildBull:**
   - 15-minute confirmation delay
   - Target skew shifts from 0% to +50%
   - Grid biases toward buys

2. **MildBull -> StrongBull:**
   - Another 15-minute confirmation
   - Target skew shifts to +80%
   - Grid heavily biased toward buys

3. **Moon Bag Protection:**
   ```csharp
   // After 30 min warm-up OR >5% profit: Tracking state
   // After price > initial grid + 10%: Trailing state
   // After trailing stop triggered: HoldMode (15% preserved)
   ```

**Can we capture full upside?**

| Component | Upside Capture |
|-----------|----------------|
| Grid Trading | Limited - sells at grid levels |
| Trend Skew | Good - stays 80% long |
| Moon Bag | Good - protects 15% minimum |
| Trailing Grid | Good - shifts grid up |
| Trailing Stop | Good - tightens as profit grows |

**Net result**: Bot will capture ~70-85% of upside due to:
- Grid fills reducing position
- Trailing stop limiting max position

**When Trend Finally Reverses:**

1. **StrongBull -> MildBull:**
   - No immediate action (minor change)
   - Target skew drops to +50%

2. **MildBull -> MildBear (Major Change):**
   - 15-minute confirmation required
   - Cooldown check: if 2+ flips in 1 hour, 120-min cooldown
   - Moon bag: Stays in current state if not in HoldMode

3. **Moon Bag Conflict:**
   - If in HoldMode: Release conditions checked
   - Release requires: StrongBear + Price < MA50 < MA200 + **Manual Approval**

---

### SCENARIO 4: BEAR MARKET (Sustained Downtrend)

**State Progression:**

| Week | Price | Trend State | Target Skew | Capacity |
|------|-------|-------------|-------------|----------|
| 1 | $100 | Neutral | 0% | 100% |
| 2 | $90 | MildBear | -50% | 100% |
| 3 | $75 | StrongBear | -80% | 100% |
| 4 | $60 | StrongBear | -80% | May be reduced |

**Inventory Management Behavior:**

1. **Building Short Position:**
   ```csharp
   // Target skew: -80% = 80% short exposure
   // Grid biases toward asks (sells/shorts)
   // Skew correction: buy mult = 0.25, sell mult = 1.5
   ```

2. **Loss Limits Check (Rolling Windows):**

   | Metric | Threshold | Consequence |
   |--------|-----------|-------------|
   | 24h Rolling | -15% | Protective mode, 4h wait |
   | 7d Rolling | -25% | Protective mode, 24h wait |
   | 30d Rolling | -30% | Protective mode, 72h wait |
   | Max Drawdown | -35% | 75% position reduction |

3. **Can we profit from shorts?**

   YES, but with caveats:
   - Grid sells (shorts) at higher prices, buys back (covers) at lower
   - Profit = grid spacing profit on each round-trip
   - Risk: Funding rates can erode profits if highly negative

4. **Funding Rate Monitoring:**
   ```csharp
   // LiquidityMonitor checks:
   if (Math.Abs(fundingRate) > config.MaxFundingRatePercent)  // 0.1%
       warnings.Add("High funding rate");
       // 25% position reduction

   if (Math.Abs(fundingRate) > config.CriticalFundingRatePercent)  // 0.3%
       warnings.Add("Critical funding rate");
       // 100% position reduction recommended
   ```

---

### SCENARIO 5: SIDEWAYS/CHOPPY MARKET

**This is where the bot should EXCEL**

**Expected Behavior:**

| Aspect | Behavior | Effectiveness |
|--------|----------|---------------|
| Trend State | Neutral (oscillating) | Stays flat |
| Grid Trading | Both sides filling | HIGH |
| Moon Bag | Inactive | N/A |
| Capacity | 100% | Full operation |

**Trend Flip Cooldown Impact:**

```csharp
// TrendDetector.cs
if (recentFlips >= 2)  // 2+ flips in 1 hour
    cooldownExpiry = now.AddMinutes(trendOptions.TrendFlipCooldownMinutes);  // 120 min
```

**Effect:**
- After 2 trend flips in 1 hour: Bot locked to last confirmed trend for 2 hours
- Prevents whipsaw losses from trend following
- Grid trading continues normally

**Grid Trading Profitability:**

In sideways market with 3% daily range:
- Grid spacing: 0.5-1.0% (moderate volatility)
- Orders per side: 6-8
- Expected fills: 4-8 round-trips per day
- Gross profit per round-trip: ~0.5-1.0% (less fees)

**GAP IDENTIFIED**: No optimization for sideways detection
- Bot doesn't have explicit "range mode"
- Could tighten spreads more aggressively in confirmed ranges

---

### SCENARIO 6: EXTREME VOLATILITY (5%+ moves in minutes)

**ATR Adaptation:**

| ATR % | Grid Spacing | Orders/Side |
|-------|--------------|-------------|
| < 0.5% | 0.2% | 10 |
| 0.5-1.0% | 0.5% | 8 |
| 1.0-2.0% | 1.0% | 6 |
| 2.0-3.0% | 1.5% | 5 |
| > 3.0% | 2.0% | 4 |

**ATR Calculation Latency:**

```csharp
// Uses hourly candles, last 24 hours
var candles = await _marketDataService.GetCandlesticksAsync(marketId, "1h", 24, ct);
var atr = _indicatorService.CalculateAtr(candles);
```

**GAP IDENTIFIED**: 1-hour ATR is LAGGING indicator
- Won't capture intraday volatility spikes
- By the time ATR updates, extreme move may have ended

**Order Execution Issues in Extreme Volatility:**

| Issue | Current Handling |
|-------|------------------|
| Slippage | 1% tolerance on market orders |
| Partial fills | Grid syncs status, replaces unfilled |
| Order rejection | 3 retry attempts with nonce sync |
| Stale prices | 5s cache validity for grid ops |

---

## C. EDGE CASE ANALYSIS

### 1. Position Liquidation Detection

**Current Implementation:**
```csharp
// TradingDecisionEngine.cs line 187-199
if (previousSize > 0 && currentSize == 0)
{
    _logger.LogWarning("EC-001: Position closed unexpectedly...");
    await _stateService.TransitionToAsync(
        TradingState.Degraded_ProtectiveMode,
        "Unexpected position close - possible liquidation");
}
```

**What actually happens:**
1. Position detected as closed
2. State transitions to `Degraded_ProtectiveMode`
3. Capacity drops to 25%
4. Grid NOT torn down (sell orders remain for non-existent position)

**GAP IDENTIFIED**: No distinction between liquidation and intentional close
- Manual close triggers same flow
- Should check if close was exchange-initiated vs user-initiated

### 2. API Failures/Timeouts

**Timeout Handling:**
```csharp
// DataCollectionTimeoutMs: 2000 (2 seconds)
// MaxConsecutiveTimeouts: 5
// CriticalTimeoutThreshold: 10
```

**Escalation Path:**
| Consecutive Timeouts | Action |
|---------------------|--------|
| 1-2 | Use cached data |
| 3-4 | Reduce capacity, widen spreads |
| 5-9 | Pause grid operations |
| 10+ | Enter protective mode |

**WebSocket Disconnect During Flash Crash:**

```csharp
// Cache validity during disconnect
// CacheValidityMs: 30000 (30 seconds) - general
// GridOperationCacheValidityMs: 5000 (5 seconds) - grid specific
```

**Risk**: If WebSocket disconnects during flash crash:
- Price data goes stale within 5 seconds for grid operations
- Grid cannot update stop orders
- Position may not be protected

**GAP IDENTIFIED**: No WebSocket health monitoring visible in decision engine
- Should have explicit WebSocket reconnection handling
- Should pause grid immediately on disconnect

### 3. Nonce Management

**Current Retry Logic:**
```csharp
// WsLighterCommandClient.cs
while (retryCount < 3)
{
    // Sign all orders (consumes nonces)
    // Submit batch
    // If nonce error (21104): Sync nonce, RE-SIGN, retry
}
```

**What if all 3 fail during critical moment?**

| Scenario | Consequence | Mitigation |
|----------|-------------|------------|
| Trailing stop execution | Stop not placed | Soft stop via monitoring |
| Grid rebuild | Orders not placed | Position unprotected |
| Emergency close | Position not reduced | Manual intervention needed |

**GAP IDENTIFIED**: No fallback for persistent nonce failures
- Should alert immediately on 2nd failure
- Should have emergency circuit breaker

### 4. Order Book Depth Collapse

**Current Thresholds:**
```csharp
// LiquidityOptions
MinBookDepthUsd = 50_000m;        // Warning
CriticalBookDepthUsd = 25_000m;  // Critical
```

**At $10k depth during trade execution:**
1. `LiquidityMonitor` detects critical depth
2. `LiquidityLevel.Halted` set
3. `TradingAllowed = false`

**But what about in-flight orders?**

**GAP IDENTIFIED**: No pre-trade depth check
- Orders could be submitted to thin book
- Should check depth BEFORE each order placement

### 5. Funding Rate Extreme

**Current Handling:**
```csharp
// At 0.3% funding:
if (fundingCritical)
{
    level = LiquidityLevel.Critical;
    warnings.Add("Critical funding rate");
    // state.LastFundingReduction = 100m (recommend full close)
}
```

**GAP IDENTIFIED**: Funding reduction is RECOMMENDATION only
- No automatic position reduction
- Position multiplier reduced but existing position remains

---

## D. RISK GAPS IDENTIFICATION

### 1. Black Swan Events (50%+ drop in 1 hour)

**Current Coverage:**
- 15% drop in 1 hour triggers full halt (240 min)
- Max drawdown at -35% triggers 75% reduction

**GAP**: No handling for drops beyond thresholds
- At -50% in 1 hour: Same protection as -15%
- Position could be liquidated before recovery mechanisms engage

**RECOMMENDATION**:
```
IF drop > 25% in 60 minutes THEN:
  - Emergency close to 50% position via market order
  - Extended halt (24 hours minimum)
  - Require manual review before restart
```

### 2. Exchange Issues

**Current Coverage:**
- API rate limits: 24,000 requests/min (premium)
- Timeout handling with escalation
- Nonce retry with 3 attempts

**GAPs:**

| Issue | Coverage | Gap |
|-------|----------|-----|
| API rate limit hit | None | No rate limiting implementation visible |
| Order rejection | Retry 3x | No circuit breaker on repeated rejections |
| WebSocket disconnect | Cache fallback | No explicit reconnection tracking |
| Exchange maintenance | None | No scheduled downtime handling |

### 3. Correlation Risk

**Current Coverage:** NONE

**GAP IDENTIFIED**: Multi-market correlation not monitored
- If trading multiple markets (BTC, ETH)
- Simultaneous crash triggers independent flash crash responses
- No aggregate portfolio risk view

### 4. Slippage Risk

**Current Coverage:**
- 1% slippage tolerance on trailing stop execution
- IOC (Immediate or Cancel) order type for stops

**GAP IDENTIFIED**: No slippage monitoring over time
- Accumulated slippage not tracked
- Could erode P&L significantly in volatile markets

### 5. Counterparty/DEX Risk

**Current Coverage:** NONE

**Lighter DEX Specific Risks:**

| Risk | Mitigation |
|------|------------|
| Smart contract bug | None - trust assumption |
| Sequencer downtime | Cache fallback only |
| Liquidity provider withdrawal | Book depth monitoring |
| Oracle manipulation | None |

---

## E. PRIORITIZED RECOMMENDATIONS (UPDATED)

### CRITICAL (Implement Immediately)

| Priority | Item | Description |
|----------|------|-------------|
| 1 | **FlashPumpDetector** | **NEW** - Symmetric protection for SHORT positions from upward pumps (mirrors FlashCrashDetector) |
| 2 | **WebSocket Health Monitoring** | Add explicit disconnect detection, pause grid on disconnect |
| 3 | **Pre-Trade Depth Check** | Verify book depth before submitting orders |
| 4 | **Automatic Moon Bag Release** | Auto-release after 4h StrongBear + price below MAs |

**Why FlashPumpDetector is #1 Priority:**
- Current system has CRITICAL ASYMMETRY: LONG positions protected from crashes, SHORT positions NOT protected from pumps
- A +20% pump devastates shorts the same way a -20% crash devastates longs
- Without this, the bot is effectively "half protected" when running short strategies

### HIGH (Implement This Week)

| Priority | Item | Description |
|----------|------|-------------|
| 5 | **Black Swan Circuit Breaker** | Add -25% in 60 min threshold, emergency close to 50% |
| 6 | **Nonce Failure Alert** | Alert on 2nd consecutive failure, pause on 3rd |

### MEDIUM (Implement This Month)

| Priority | Item | Description |
|----------|------|-------------|
| 7 | **Intraday Volatility Indicator** | Add 5-minute ATR for rapid adaptation |
| 8 | **Slippage Tracking** | Record slippage, alert if 24h slippage > 1% |
| 9 | **Range Detection Mode** | Detect sideways market, tighten spreads |

### LOW (Future Enhancement)

| Priority | Item | Description |
|----------|------|-------------|
| 10 | **Multi-Market Correlation** | Track correlation, reduce aggregate position when > 0.8 |
| 11 | **Funding Rate Strategy** | Adjust grid bias based on funding |
| 12 | **Liquidation Distinction** | Query exchange for liquidation events |

---

## F. CONFIGURATION REVIEW

### Parameters That May Be Too Aggressive

| Parameter | Current | Risk | Recommendation |
|-----------|---------|------|----------------|
| MaxLeverage | 5x | High liquidation risk | 3x for initial deployment |
| MaxPositionSizePercent | 10% | Concentrated position | 5% for safety |
| MaxDrawdownPercent | -35% | Too much drawdown | -25% with staged reduction |
| SingleTradeLossPercent | -5% | High single trade loss | -3% with alert |

### Parameters That May Be Too Conservative

| Parameter | Current | Impact | Recommendation |
|-----------|---------|--------|----------------|
| ConfirmationDelayMinutes | 15 | Slow trend response | 10 min for faster markets |
| TrendFlipCooldownMinutes | 120 | Stuck in wrong trend | 60 min with override option |
| WarmUpPeriodMinutes | 30 | Slow moon bag activation | 15 min for volatile assets |

---

## G. SUMMARY

### Strengths of Current Implementation

1. **Never-Halt Philosophy**: Bot always runs, capacity controls behavior
2. **Multi-Layered Protection**: Flash crash + loss limits + liquidity monitoring
3. **State Machine Design**: Clear transitions with recovery phases
4. **Persistence**: Redis-backed state survives restarts
5. **Moon Bag Innovation**: Prevents premature selling in bull markets

### Weaknesses Requiring Attention

1. **Moon Bag vs Trend Conflict**: Requires manual intervention
2. **WebSocket Reliability**: No explicit health monitoring
3. **Black Swan Coverage**: Insufficient for extreme moves
4. **Lagging Indicators**: 1-hour ATR misses rapid volatility
5. **No Multi-Market View**: Correlation risk not managed

### Overall Risk Rating: MODERATE-HIGH

The system has comprehensive risk management but specific gaps could lead to significant losses in edge cases. The "never halt" philosophy is excellent but requires all edge cases to be covered - currently some are not.

**Recommended Action**: Address CRITICAL items before production deployment with significant capital.

---

## H. IMPLEMENTATION PLAN

This section provides detailed implementation specifications for all risk improvement recommendations. Each item includes complete specifications, implementation steps, files to modify, testing criteria, and edge case handling.

---

### H.1 CRITICAL: FlashPumpDetector (NEW - SHORT POSITION PROTECTION)

**Objective**: Protect SHORT positions from rapid upward price movements (pumps) with symmetric protection to what FlashCrashDetector provides for LONG positions.

**Problem Statement**:
The current system has a critical asymmetry:
- LONG positions have full protection via `FlashCrashDetector` against downward crashes
- SHORT positions have NO equivalent protection against upward pumps
- A 20% pump can devastate a short position the same way a 20% crash devastates a long position

**Specification**:

```
## Risk Category: Flash Pump Detection (Short Position Protection)

### Thresholds (Symmetric to FlashCrashDetector)
- 1-minute pump: +3% (Rationale: Matches -3% crash threshold)
- 5-minute pump: +5% (Rationale: Matches -5% crash threshold)
- 15-minute pump: +10% (Rationale: Matches -10% crash threshold)
- 60-minute pump: +15% (Rationale: Matches -15% crash threshold)

### Actions (Inverted for Short Protection)
| Severity | Threshold | Action | Duration |
|----------|-----------|--------|----------|
| Minor | +3% in 1min | PauseSells (block new shorts) | 5 minutes |
| Moderate | +5% in 5min | PauseAll orders | 15 minutes |
| Severe | +10% in 15min | CancelAndCoverHalf (buy back 50% of short) | 60 minutes |
| Extreme | +15% in 60min | FullHalt + cover to 50% position | 240 minutes |

### Rules
1. IF price_gain_1min >= +3% AND has_short_position THEN pause_sell_orders(5_minutes)
2. IF price_gain_5min >= +5% AND has_short_position THEN pause_all_orders(15_minutes)
3. IF price_gain_15min >= +10% AND has_short_position THEN cancel_orders_and_cover_half(60_minutes)
4. IF price_gain_60min >= +15% AND has_short_position THEN full_halt_and_cover(240_minutes)
5. IF pump_events_24h > 2 THEN extended_halt(24_hours)
6. IF flash_pump_active AND flash_crash_active THEN use_most_severe_action

### Edge Cases
- Scenario: Pump detected but no short position
  Response: Log warning but take no protective action (no position to protect)

- Scenario: Pump triggers during flash crash protection
  Response: Take the MORE restrictive action of the two protections

- Scenario: Short position opened during pump protection
  Response: Block order - cannot add to short during pump protection

- Scenario: Pump reverses immediately after detection
  Response: Protection period continues - no early exit (prevents whipsaw)

- Scenario: Market gaps up over multiple thresholds at once
  Response: Apply the most severe triggered threshold immediately

### Priority Level: CRITICAL
```

**Implementation Steps**:

1. **Create FlashPumpStatus model** (mirror of FlashCrashStatus):
   - Create `FlashPumpSeverity` enum: None, Minor, Moderate, Severe, Extreme
   - Create `FlashPumpAction` enum: None, PauseSells, PauseAll, CancelAndCoverHalf, FullHalt
   - Create `FlashPumpStatus` class with same properties as FlashCrashStatus

2. **Create IFlashPumpDetector interface**:
   - `Task<FlashPumpStatus> CheckForFlashPumpAsync(int marketId, CancellationToken ct)`
   - `Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct)` (shared with crash detector)
   - `bool IsInPumpProtection(int marketId)`
   - `DateTimeOffset? GetProtectionExpiry(int marketId)`
   - `FlashPumpAction GetCurrentAction(int marketId)`
   - `void ClearProtection(int marketId)`
   - `int GetPumpCount24h(int marketId)`

3. **Create FlashPumpDetector class**:
   - Mirror structure of FlashCrashDetector
   - Use `CalculateGain()` instead of `CalculateDrop()` (positive change detection)
   - Invert action semantics (PauseSells instead of PauseBuys)

4. **Add FlashPumpOptions to configuration**:
   - Add to `TradingBotOptions.cs`
   - Add to `IRiskConfiguration.cs` and `RiskConfiguration.cs`

5. **Integrate into RiskSentinel**:
   - Add `IFlashPumpDetector` dependency
   - Call in `AssessRiskAsync()` alongside `CheckForFlashCrashAsync()`
   - Add `SellsBlocked` flag based on pump detection
   - Update `CalculatePositionMultiplier()` for pump-related reductions

6. **Update TradingDecisionEngine**:
   - Check pump protection before allowing sell/short orders
   - Implement CancelAndCoverHalf action handler
   - Handle combined crash+pump scenarios

7. **Add Short Moon Bag considerations**:
   - If `EnableShortMoonBag` is true, protect minimum short position
   - Inverse logic: protect from buying back too much during pump

**Files to Create**:
- `GridBot.ApiService/Models/Trading/FlashPumpStatus.cs`
- `GridBot.ApiService/Services/Risk/IFlashPumpDetector.cs`
- `GridBot.ApiService/Services/Risk/FlashPumpDetector.cs`

**Files to Modify**:
- `GridBot.ApiService/Configuration/TradingBotOptions.cs` - Add `FlashPumpOptions`
- `GridBot.ApiService/Configuration/IRiskConfiguration.cs` - Add `FlashPump` property
- `GridBot.ApiService/Configuration/RiskConfiguration.cs` - Implement `FlashPump` property
- `GridBot.ApiService/Services/Risk/RiskSentinel.cs` - Add pump detection integration
- `GridBot.ApiService/Services/Risk/IRiskSentinel.cs` - Update interface if needed
- `GridBot.ApiService/Extensions/RiskServiceExtensions.cs` - Register FlashPumpDetector
- `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs` - Handle pump actions
- `GridBot.ApiService/Models/Trading/RiskAssessment.cs` - Add `FlashPumpStatus` property

**Testing Criteria**:
1. Unit test: +3% gain in 1 minute triggers Minor severity with PauseSells action
2. Unit test: +5% gain in 5 minutes triggers Moderate severity with PauseAll action
3. Unit test: +10% gain in 15 minutes triggers Severe severity with CancelAndCoverHalf action
4. Unit test: +15% gain in 60 minutes triggers Extreme severity with FullHalt action
5. Unit test: No action triggered when no short position exists
6. Unit test: Protection period persists even if price reverses
7. Unit test: Most severe action taken when both crash and pump detected
8. Integration test: Sell order rejected during pump protection
9. Integration test: Cover order executes on Severe+ detection

**Edge Cases**:
- Price gaps through multiple thresholds simultaneously - take most severe action
- WebSocket disconnects during pump - fall back to REST, continue protection
- Pump detection during existing crash protection - use most restrictive
- API failure during cover order - retry with nonce sync, alert if fails

---

### H.2 CRITICAL: WebSocket Health Monitoring

**Objective**: Ensure the trading system cannot operate on stale data by explicitly monitoring WebSocket connection health and pausing operations immediately on disconnect.

**Specification**:

```
## Risk Category: Data Freshness & Connectivity

### Thresholds
- Max WebSocket data age: 10 seconds (Rationale: Grid operations need fresh data)
- Disconnect detection: Immediate (Rationale: Cannot trade on stale data)
- Reconnection grace period: 30 seconds (Rationale: Allow normal reconnects)
- Extended outage threshold: 5 minutes (Rationale: Trigger protective mode)

### Rules
1. IF websocket_disconnected THEN pause_grid_immediately
2. IF websocket_reconnected AND data_age < 10s THEN resume_grid
3. IF websocket_disconnected > 5_minutes THEN enter_protective_mode
4. IF data_age > 10s AND websocket_connected THEN use_rest_fallback
5. IF rest_fallback_fails THEN pause_grid

### Edge Cases
- Scenario: WebSocket reconnects but data is stale
  Response: Wait for fresh data before resuming (up to 30s)

- Scenario: Multiple rapid disconnect/reconnect cycles
  Response: After 3 cycles in 5 minutes, pause for 10 minutes

- Scenario: Disconnect during active order execution
  Response: Complete pending operations, then pause new operations

- Scenario: WebSocket connected but no messages received
  Response: Treat as disconnect after 30 seconds of silence

### Priority Level: CRITICAL
```

**Implementation Steps**:

1. **Enhance ILighterRealtimeState interface**:
   - Add `bool IsConnected { get; }`
   - Add `DateTimeOffset? LastMessageReceived { get; }`
   - Add `TimeSpan? TimeSinceLastMessage { get; }`
   - Add `int DisconnectCount24h { get; }`
   - Add `event EventHandler<WebSocketHealthChangedEventArgs> HealthChanged`

2. **Update LighterRealtimeStateService**:
   - Track `_lastMessageReceived` timestamp on every message
   - Track `_disconnectCount` with 24h rolling window
   - Fire `HealthChanged` event on connect/disconnect
   - Implement `OldestDataAge` property checking all data sources

3. **Create WebSocketHealthMonitor service**:
   - Subscribe to `HealthChanged` events
   - Implement reconnect cycle detection (3 in 5 min = pause)
   - Implement silence detection (no messages for 30s)
   - Expose `IsHealthy` property combining all checks

4. **Integrate into TradingDecisionEngine**:
   - Check `IsHealthy` at start of each decision loop
   - If unhealthy, skip to next iteration (don't process stale data)
   - Log warning with reason for unhealthy status
   - Transition to `Degraded_ProtectiveMode` after 5 min disconnect

5. **Update GridLifecycleService**:
   - Check WebSocket health before any grid operation
   - Auto-pause grid on disconnect event
   - Auto-resume grid on healthy reconnect (with delay)

**Files to Create**:
- `GridBot.ApiService/Services/Connectivity/IWebSocketHealthMonitor.cs`
- `GridBot.ApiService/Services/Connectivity/WebSocketHealthMonitor.cs`
- `GridBot.ApiService/Models/Events/WebSocketHealthChangedEventArgs.cs`

**Files to Modify**:
- `GridBot.Lighter/Services/ILighterRealtimeState.cs` - Add health properties
- `GridBot.Lighter/Services/LighterRealtimeStateService.cs` - Implement health tracking
- `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs` - Add health check
- `GridBot.ApiService/Services/Grid/GridLifecycleService.cs` - Add auto-pause on disconnect
- `GridBot.ApiService/Extensions/TradingServiceExtensions.cs` - Register WebSocketHealthMonitor

**Testing Criteria**:
1. Unit test: `IsConnected` returns false after disconnect event
2. Unit test: `TimeSinceLastMessage` calculates correctly
3. Unit test: `HealthChanged` event fires on disconnect
4. Unit test: Reconnect cycle detection triggers after 3 disconnects in 5 min
5. Integration test: Grid pauses within 1 second of disconnect
6. Integration test: Grid resumes after healthy reconnect
7. Integration test: Protective mode entered after 5 min disconnect

**Edge Cases**:
- Disconnect during critical trailing stop execution - execute via REST fallback
- Multiple markets with mixed health - handle per-market
- Health check during startup before first connect - wait for initial connect

---

### H.3 CRITICAL: Pre-Trade Depth Check

**Objective**: Prevent order submission to thin order books that could result in excessive slippage or failed fills.

**Specification**:

```
## Risk Category: Order Execution Quality

### Thresholds
- Minimum depth for normal orders: $25,000 (Rationale: Ensure reasonable fill quality)
- Critical depth halt: $10,000 (Rationale: Book too thin for any trading)
- Order size to depth ratio max: 10% (Rationale: Don't exceed 10% of available liquidity)
- Depth check freshness: 5 seconds (Rationale: Book can change rapidly)

### Rules
1. IF order_size_usd > (available_depth * 0.10) THEN reduce_order_size
2. IF total_depth < $10,000 THEN reject_order
3. IF depth_data_age > 5s THEN refresh_depth_before_order
4. IF depth_on_order_side < order_size * 2 THEN reject_order
5. IF spread > 1% THEN reject_order_and_alert

### Edge Cases
- Scenario: Depth adequate at check but depletes before fill
  Response: Use IOC (Immediate or Cancel) with max slippage tolerance

- Scenario: Depth check fails (API error)
  Response: Reject order (fail closed, not open)

- Scenario: Buy order but only sell-side depth available
  Response: Check correct side (bid depth for sells, ask depth for buys)

- Scenario: Large order that exceeds depth
  Response: Split into multiple smaller orders or reduce to max safe size

### Priority Level: CRITICAL
```

**Implementation Steps**:

1. **Create IPreTradeValidator interface**:
   - `Task<PreTradeValidation> ValidateOrderAsync(OrderRequest order, CancellationToken ct)`
   - `Task<decimal> GetMaxSafeOrderSizeAsync(int marketId, bool isBuy, CancellationToken ct)`

2. **Create PreTradeValidator service**:
   - Inject `IMarketDataService` for order book access
   - Implement depth checks per side (bids for sells, asks for buys)
   - Implement spread validation
   - Return `PreTradeValidation` with `IsValid`, `Reason`, `RecommendedSize`

3. **Create PreTradeValidation model**:
   - `bool IsValid`
   - `string Reason`
   - `decimal? RecommendedSize` (if original size too large)
   - `decimal AvailableDepth`
   - `decimal CurrentSpread`

4. **Integrate into order submission flow**:
   - Call `ValidateOrderAsync()` before every order submission
   - If invalid, either reject or adjust size based on recommendation
   - Log all rejections with reason

5. **Add configuration options**:
   - `MinOrderBookDepthUsd` (default: $25,000)
   - `CriticalDepthThresholdUsd` (default: $10,000)
   - `MaxOrderToDepthRatio` (default: 0.10)
   - `MaxAcceptableSpreadPercent` (default: 1.0%)

**Files to Create**:
- `GridBot.ApiService/Services/Validation/IPreTradeValidator.cs`
- `GridBot.ApiService/Services/Validation/PreTradeValidator.cs`
- `GridBot.ApiService/Models/Trading/PreTradeValidation.cs`

**Files to Modify**:
- `GridBot.ApiService/Configuration/TradingBotOptions.cs` - Add validation options
- `GridBot.ApiService/Services/Grid/GridLifecycleService.cs` - Add validation before order placement
- `GridBot.ApiService/Services/MoonBag/TrailingStopService.cs` - Add validation before stop orders
- `GridBot.ApiService/Extensions/TradingServiceExtensions.cs` - Register PreTradeValidator

**Testing Criteria**:
1. Unit test: Order rejected when depth < $10,000
2. Unit test: Order size reduced when exceeds 10% of depth
3. Unit test: Order rejected when spread > 1%
4. Unit test: Correct side depth checked (bids for sells, asks for buys)
5. Integration test: Grid order uses recommended size when original too large
6. Integration test: Trailing stop uses IOC with slippage protection

**Edge Cases**:
- Depth changes between check and submission - use IOC, accept partial fill
- Book has depth but high concentration at single price - detect and warn
- Depth check during market halt - reject all orders

---

### H.4 CRITICAL: Automatic Moon Bag Release

**Objective**: Allow the system to automatically release moon bag protection when trend has definitively reversed, without requiring manual operator approval.

**Specification**:

```
## Risk Category: Position Management / Moon Bag

### Thresholds
- StrongBear confirmation: 4 hours (Rationale: Sufficient time to confirm trend reversal)
- Price below MA50: Required (Rationale: Technical confirmation)
- Price below MA200: Required (Rationale: Strong bearish signal)
- Unrealized loss threshold: -20% (Rationale: Significant loss indicates need to exit)

### Rules
1. IF moon_bag_state == HoldMode AND trend == StrongBear > 4h AND price < MA50 < MA200 THEN auto_release
2. IF moon_bag_state == HoldMode AND unrealized_loss > 20% THEN auto_release_with_alert
3. IF auto_release_triggered THEN notify_operator (but don't require approval)
4. IF operator_override_set THEN respect_override (never auto-release)

### Edge Cases
- Scenario: Trend flips back to bull during release execution
  Response: Complete release, operator can re-enable manually

- Scenario: Auto-release during flash crash protection
  Response: Queue release for after protection period ends

- Scenario: Multiple markets with moon bags
  Response: Evaluate each independently

- Scenario: MA data unavailable
  Response: Fall back to price-only check with extended time (6h StrongBear)

### Priority Level: CRITICAL
```

**Implementation Steps**:

1. **Add AutoReleaseEnabled option**:
   - Add `bool AutoReleaseEnabled` to `MoonBagOptions` (default: true)
   - Add `int AutoReleaseConfirmationHours` (default: 4)
   - Add `decimal AutoReleaseUnrealizedLossPercent` (default: -20)
   - Add `bool OperatorOverrideAutoRelease` (default: false)

2. **Enhance MoonBagManager**:
   - Add `CheckAutoReleaseConditionsAsync()` method
   - Track `StrongBearStartTime` when trend enters StrongBear
   - Check conditions on each cycle when in HoldMode
   - Trigger auto-release via existing `ReleaseAsync()` method

3. **Add notification on auto-release**:
   - Log critical event when auto-release triggers
   - Fire `MoonBagEvent` with `AutoReleased` event type
   - Include reason and market conditions in event

4. **Update MoonBagStatus**:
   - Add `AutoReleaseEligible` boolean
   - Add `AutoReleaseBlockedReason` string (if blocked by override or other)
   - Add `StrongBearDuration` TimeSpan

**Files to Modify**:
- `GridBot.ApiService/Configuration/TradingBotOptions.cs` - Add auto-release options
- `GridBot.ApiService/Services/MoonBag/MoonBagManager.cs` - Implement auto-release logic
- `GridBot.ApiService/Services/MoonBag/IMoonBagManager.cs` - Add `CheckAutoReleaseConditionsAsync`
- `GridBot.ApiService/Models/Trading/MoonBagStatus.cs` - Add auto-release status fields
- `GridBot.ApiService/Models/Trading/MoonBagEvent.cs` - Add `AutoReleased` event type

**Testing Criteria**:
1. Unit test: Auto-release triggers after 4h StrongBear + price < MA50 < MA200
2. Unit test: Auto-release blocked when OperatorOverrideAutoRelease is true
3. Unit test: Auto-release triggers immediately on -20% unrealized loss
4. Unit test: Notification event fired on auto-release
5. Integration test: Moon bag position actually sold after auto-release
6. Integration test: Auto-release queued during flash crash protection

**Edge Cases**:
- Release during low liquidity - split into smaller orders
- Release fails due to API error - retry with exponential backoff
- Partial release (only some sold) - track and complete remaining

---

### H.5 HIGH: Black Swan Circuit Breaker

**Objective**: Add protection for extreme market events beyond current flash crash thresholds.

**Specification**:

```
## Risk Category: Extreme Event Protection

### Thresholds
- Black swan threshold: -25% in 60 minutes (Rationale: Beyond normal flash crash)
- Emergency position reduction: 50% (Rationale: Preserve capital, don't panic sell all)
- Extended halt duration: 24 hours (Rationale: Requires manual review)
- Manual review required: Yes (Rationale: Extreme events need human judgment)

### Rules
1. IF drop > 25% in 60 minutes THEN emergency_reduce_to_50%
2. IF black_swan_triggered THEN halt_24_hours
3. IF black_swan_triggered THEN require_manual_restart
4. IF second_black_swan_in_7_days THEN halt_until_manual_review

### Edge Cases
- Scenario: Position already < 50%
  Response: Don't add to position, maintain current level

- Scenario: Cannot execute emergency reduction (API failure)
  Response: Retry every 30 seconds, alert after 3 failures

- Scenario: Black swan during existing protection
  Response: Extend protection to 24 hours, upgrade severity

### Priority Level: HIGH
```

**Implementation Steps**:

1. **Extend FlashCrashOptions**:
   - Add `BlackSwanDropPercent` (default: -25)
   - Add `BlackSwanHaltHours` (default: 24)
   - Add `BlackSwanRequiresManualRestart` (default: true)

2. **Add BlackSwanSeverity to FlashCrashSeverity enum**:
   - Add `BlackSwan` level above `Extreme`

3. **Add BlackSwanAction to FlashCrashAction enum**:
   - Add `EmergencyReduceAndHalt`

4. **Enhance FlashCrashDetector**:
   - Add black swan threshold check (before extreme check)
   - Trigger `EmergencyReduceAndHalt` action
   - Set `RequiresManualRestart` flag in status

5. **Implement emergency reduction logic**:
   - Calculate 50% of current position
   - Submit market sell/buy-to-cover order
   - Use IOC with 2% slippage tolerance
   - Retry on failure

**Files to Modify**:
- `GridBot.ApiService/Configuration/TradingBotOptions.cs` - Add black swan options
- `GridBot.ApiService/Models/Trading/FlashCrashStatus.cs` - Add severity and action values
- `GridBot.ApiService/Services/Risk/FlashCrashDetector.cs` - Add black swan detection
- `GridBot.ApiService/Services/DecisionEngine/TradingDecisionEngine.cs` - Handle emergency reduction

**Testing Criteria**:
1. Unit test: -25% in 60 min triggers BlackSwan severity
2. Unit test: Emergency reduction calculated correctly
3. Unit test: Manual restart required flag set
4. Integration test: Position reduced to 50% via market order
5. Integration test: 24-hour halt enforced

---

### H.6 HIGH: Nonce Failure Alert

**Objective**: Detect persistent nonce failures that could prevent critical operations and alert immediately.

**Specification**:

```
## Risk Category: API Reliability

### Thresholds
- Warning threshold: 2 consecutive failures (Rationale: First could be timing, second is pattern)
- Halt threshold: 3 consecutive failures (Rationale: System unable to execute orders)
- Reset after: 10 successful operations (Rationale: Confirm system recovered)

### Rules
1. IF nonce_failures >= 2 THEN alert_operator
2. IF nonce_failures >= 3 THEN pause_trading
3. IF nonce_success_count >= 10 THEN reset_failure_count
4. IF nonce_failure_during_emergency THEN escalate_to_critical

### Edge Cases
- Scenario: Nonce failure on trailing stop execution
  Response: Retry immediately (already implemented), alert if fails

- Scenario: Nonce sync succeeds but next operation fails again
  Response: Count as consecutive failure, likely systemic issue

- Scenario: Multiple concurrent operations cause nonce conflicts
  Response: Serialize critical operations, track per-operation-type

### Priority Level: HIGH
```

**Implementation Steps**:

1. **Add nonce tracking to SignerClient or wrapper**:
   - Add `ConsecutiveNonceFailures` counter
   - Add `LastNonceError` timestamp
   - Add `TotalNonceFailures24h` counter

2. **Create NonceHealthMonitor service**:
   - Subscribe to nonce error events
   - Track consecutive failures
   - Fire alerts at thresholds
   - Expose `IsNonceHealthy` property

3. **Integrate alerts**:
   - Log warning on 2nd consecutive failure
   - Transition to Degraded_ProtectiveMode on 3rd failure
   - Include nonce health in RiskAssessment

4. **Add configuration**:
   - `NonceWarningThreshold` (default: 2)
   - `NonceHaltThreshold` (default: 3)
   - `NonceRecoverySuccessCount` (default: 10)

**Files to Create**:
- `GridBot.ApiService/Services/Connectivity/INonceHealthMonitor.cs`
- `GridBot.ApiService/Services/Connectivity/NonceHealthMonitor.cs`

**Files to Modify**:
- `GridBot.Lighter/Clients/SignerClient.cs` - Add failure tracking
- `GridBot.ApiService/Services/Risk/RiskSentinel.cs` - Include nonce health in assessment
- `GridBot.ApiService/Configuration/TradingBotOptions.cs` - Add nonce health options

**Testing Criteria**:
1. Unit test: Warning logged on 2nd consecutive failure
2. Unit test: Trading paused on 3rd consecutive failure
3. Unit test: Counter resets after 10 successes
4. Integration test: System recovers after nonce sync

---

### H.7 MEDIUM: Intraday Volatility Indicator

**Objective**: Add faster volatility detection using 5-minute ATR alongside 1-hour ATR.

**Specification**:

```
## Risk Category: Volatility Adaptation

### Thresholds
- 5-minute ATR calculation: Last 24 candles (2 hours of 5-min data)
- ATR spike threshold: 5m ATR > 2x 1h ATR (Rationale: Significant intraday spike)
- Grid adjustment: Use max(1h ATR, 5m ATR) for spacing

### Rules
1. IF atr_5m > (atr_1h * 2) THEN widen_grid_immediately
2. IF atr_5m < atr_1h THEN use_1h_atr (more conservative)
3. IF both_high THEN use_maximum_spacing

### Priority Level: MEDIUM
```

**Implementation Steps**:

1. **Enhance IndicatorService**:
   - Add `Calculate5MinAtrAsync()` method
   - Cache 5-min candles separately from 1-hour

2. **Update grid calculation**:
   - Fetch both ATR values
   - Use `Math.Max(atr1h, atr5m)` for grid spacing
   - Log when 5m ATR overrides 1h ATR

**Files to Modify**:
- `GridBot.ApiService/Services/Indicators/IIndicatorService.cs`
- `GridBot.ApiService/Services/Indicators/IndicatorService.cs`
- `GridBot.ApiService/Configuration/RiskConfiguration.cs` - Update `CalculateGridParameters`

---

### H.8 MEDIUM: Slippage Tracking

**Objective**: Track slippage on executed orders to detect degrading execution quality.

**Specification**:

```
## Risk Category: Execution Quality

### Thresholds
- Alert threshold: > 1% cumulative slippage in 24 hours
- Critical threshold: > 2% cumulative slippage in 24 hours
- Per-trade alert: > 0.5% slippage on single trade

### Rules
1. IF slippage_24h > 1% THEN alert_operator
2. IF slippage_24h > 2% THEN widen_spreads_50%
3. IF single_trade_slippage > 0.5% THEN log_warning

### Priority Level: MEDIUM
```

**Implementation Steps**:

1. **Create SlippageTracker service**:
   - Record expected price vs actual fill price
   - Calculate running 24h slippage
   - Expose slippage metrics

2. **Integrate with order execution**:
   - Record slippage after each fill
   - Include in RiskAssessment

**Files to Create**:
- `GridBot.ApiService/Services/Monitoring/ISlippageTracker.cs`
- `GridBot.ApiService/Services/Monitoring/SlippageTracker.cs`

---

### H.9 MEDIUM: Range Detection Mode

**Objective**: Detect sideways markets and tighten grid spacing for increased profitability.

**Specification**:

```
## Risk Category: Market Regime Detection

### Thresholds
- ADX threshold: < 15 for 4+ hours (Rationale: Low directional movement)
- Price range: < 3% for 4+ hours (Rationale: Tight range = sideways)
- Grid tightening: 25% reduction in spacing (Rationale: Capture more trades)

### Rules
1. IF adx < 15 for 4h AND price_range < 3% for 4h THEN enter_range_mode
2. IF in_range_mode THEN tighten_grid_25%
3. IF adx > 20 OR price_breaks_range THEN exit_range_mode

### Priority Level: MEDIUM
```

**Implementation Steps**:

1. **Create RangeDetector service**:
   - Calculate ADX from existing indicator service
   - Track price range (high - low) over rolling window
   - Determine range mode eligibility

2. **Integrate with grid calculation**:
   - Reduce spacing by 25% in range mode
   - Increase order count slightly

---

### H.10 LOW: Multi-Market Correlation

**Objective**: Reduce aggregate exposure when trading correlated assets.

**Specification**:

```
## Risk Category: Portfolio Risk

### Thresholds
- Correlation threshold: > 0.8 (Rationale: Highly correlated = redundant risk)
- Position reduction: 30% each when correlated (Rationale: Maintain total risk)

### Rules
1. IF correlation(market_a, market_b) > 0.8 THEN reduce_both_30%
2. IF trading_multiple_markets THEN check_correlation_hourly

### Priority Level: LOW
```

---

### H.11 LOW: Funding Rate Strategy

**Objective**: Adjust grid bias based on funding rate to profit from or avoid funding payments.

**Specification**:

```
## Risk Category: Cost Optimization

### Thresholds
- Favorable funding: > 0.05% positive when short, negative when long
- Unfavorable funding: > 0.05% opposite to position

### Rules
1. IF funding_favors_position THEN increase_position_bias_10%
2. IF funding_opposes_position THEN reduce_position_bias_10%

### Priority Level: LOW
```

---

### H.12 LOW: Liquidation Distinction

**Objective**: Distinguish between manual position closes and exchange liquidations.

**Specification**:

```
## Risk Category: Event Classification

### Rules
1. IF position_closed_unexpectedly THEN query_exchange_for_liquidation
2. IF was_liquidation THEN enter_protective_mode_extended
3. IF was_manual_close THEN resume_normal_operations

### Priority Level: LOW
```

---

## I. IMPLEMENTATION PRIORITY MATRIX

| Week | Items | Estimated Effort |
|------|-------|-----------------|
| 1 | H.1 FlashPumpDetector, H.2 WebSocket Health | 16-20 hours |
| 1 | H.3 Pre-Trade Depth, H.4 Auto Moon Bag Release | 8-12 hours |
| 2 | H.5 Black Swan Circuit Breaker | 6-8 hours |
| 2 | H.6 Nonce Failure Alert | 4-6 hours |
| 3 | H.7 Intraday ATR, H.8 Slippage Tracking | 8-10 hours |
| 3 | H.9 Range Detection | 4-6 hours |
| 4+ | H.10, H.11, H.12 (as time permits) | 12-16 hours |

---

## J. TESTING STRATEGY

### Unit Tests Required
- Each detector/service should have comprehensive unit tests
- Mock dependencies for isolation
- Test all threshold boundaries
- Test edge cases explicitly

### Integration Tests Required
- Full flow tests with real (testnet) API
- Simulate market conditions via mock data injection
- Verify state machine transitions
- Verify order execution under protection

### Manual Testing Checklist
- [ ] FlashPumpDetector triggers on testnet pump
- [ ] WebSocket disconnect properly pauses grid
- [ ] Pre-trade validation rejects thin book orders
- [ ] Moon bag auto-releases in simulated bear market
- [ ] Black swan circuit breaker activates on extreme move
- [ ] Nonce alerts fire after consecutive failures

---

## K. ROLLOUT PLAN

### Phase 1: Testnet Validation (Week 1-2)
- Deploy all CRITICAL items to testnet
- Run for 48+ hours with simulated conditions
- Verify no false positives
- Verify protection activates when needed

### Phase 2: Mainnet Dry Run (Week 2-3)
- Enable DryRun mode on mainnet
- Monitor detection without execution
- Tune thresholds if needed

### Phase 3: Limited Production (Week 3-4)
- Enable with small position sizes
- Monitor closely for first 24 hours
- Gradually increase position limits

### Phase 4: Full Production (Week 4+)
- Remove position limits
- Continue monitoring
- Document any threshold adjustments

---

## L. CONFIGURATION SUMMARY

### New Configuration Options (appsettings.json)

```json
{
  "TradingBot": {
    "FlashPump": {
      "OneMinuteGainPercent": 3,
      "FiveMinuteGainPercent": 5,
      "FifteenMinuteGainPercent": 10,
      "OneHourGainPercent": 15,
      "OneMinutePauseDurationMinutes": 5,
      "FiveMinutePauseDurationMinutes": 15,
      "FifteenMinutePauseDurationMinutes": 60,
      "OneHourPauseDurationMinutes": 240,
      "MaxEventsIn24Hours": 2
    },
    "FlashCrash": {
      "BlackSwanDropPercent": -25,
      "BlackSwanHaltHours": 24,
      "BlackSwanRequiresManualRestart": true
    },
    "MoonBag": {
      "AutoReleaseEnabled": true,
      "AutoReleaseConfirmationHours": 4,
      "AutoReleaseUnrealizedLossPercent": -20,
      "OperatorOverrideAutoRelease": false
    },
    "Connectivity": {
      "MaxWebSocketDataAgeSeconds": 10,
      "ReconnectGracePeriodSeconds": 30,
      "ExtendedOutageMinutes": 5,
      "MaxReconnectCyclesIn5Min": 3
    },
    "PreTrade": {
      "MinOrderBookDepthUsd": 25000,
      "CriticalDepthThresholdUsd": 10000,
      "MaxOrderToDepthRatio": 0.10,
      "MaxAcceptableSpreadPercent": 1.0
    },
    "NonceHealth": {
      "WarningThreshold": 2,
      "HaltThreshold": 3,
      "RecoverySuccessCount": 10
    }
  }
}
```

---

## M. DOCUMENT HISTORY

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-12 | Risk Analysis | Initial risk analysis |
| 2.0 | 2025-12-12 | Risk Manager Agent | Added FlashPumpDetector (short protection), implementation plan sections H-L |
