# Rolling Window Loss Limits Specification

## Executive Summary

This document specifies the migration from calendar-based loss limits (Daily/Weekly/Monthly with manual resets) to **rolling window loss limits** for a 24/7 BTC grid trading bot. The current implementation requires manual resets at arbitrary calendar boundaries that have no relevance to continuous crypto markets.

## Current State Analysis

### Existing Implementation (LossMonitor.cs)
- **DailyPnl**: Cumulative counter, reset via `ResetDailyLimits()` at midnight
- **WeeklyPnl**: Cumulative counter, reset via `ResetWeeklyLimits()` at week start
- **MonthlyPnl**: Cumulative counter, reset via `ResetMonthlyLimits()` at month start
- **MaxDrawdown**: Equity-based calculation from high water mark (already rolling)

### Problems with Current Approach
1. **Arbitrary Boundaries**: UTC midnight has no significance in 24/7 crypto markets
2. **Gaming the System**: A bot losing 4.9% at 23:59 UTC resets to 0% at 00:00 UTC
3. **Manual Intervention**: Requires scheduled jobs or manual calls to reset counters
4. **Inconsistent Protection**: Protection varies based on when in the "period" losses occur

---

## Threshold Analysis and Recommendations

### Context
- **Starting Capital**: $500 USD
- **Asset**: BTC (high volatility)
- **Strategy**: Grid bot (profits from volatility, naturally mean-reverting)
- **Risk Appetite**: Moderate-aggressive (user wants 15% daily threshold)

### Proposed Thresholds

| Window | User Proposed | Recommended | Rationale |
|--------|---------------|-------------|-----------|
| Rolling 24h | -15% | **-12%** | See analysis below |
| Rolling 7d | -25% | **-20%** | See analysis below |
| Rolling 30d | -35% | **-30%** | See analysis below |
| Max Drawdown | -40% | **-35%** | See analysis below |

### Detailed Threshold Analysis

#### Rolling 24-Hour Window: RECOMMEND -12% (Not -15%)

**Why -15% is too loose:**
- BTC average daily volatility: 3-5% (normal), 8-12% (high), 15%+ (extreme)
- A -15% daily loss represents a **3-sigma event** in normal conditions
- At -15%, your $500 becomes $425 - you need +17.6% just to break even
- Grid bots should NOT be losing 15% in a day unless something is fundamentally wrong

**Why -12% is appropriate:**
- Still allows for high volatility days (BTC can move 10%+ intraday)
- Triggers before catastrophic loss (-15%+ usually means exchange issues, massive flash crash, or strategy failure)
- At -12%, you lose $60, need +13.6% to recover - manageable
- Provides buffer before the 7-day limit kicks in

#### Rolling 7-Day Window: RECOMMEND -20% (Not -25%)

**Rationale:**
- If you're hitting -20% in 7 days, something is wrong with the market or strategy
- Grid bots in a proper sideways market should be **positive** over 7 days
- -20% in 7 days = severe bear market or broken strategy
- At $500, -20% = $400 remaining, need +25% to recover

#### Rolling 30-Day Window: RECOMMEND -30% (Not -35%)

**Rationale:**
- Monthly loss limit is your **circuit breaker for extended bear markets**
- -30% over 30 days means the strategy is not working in current conditions
- Better to preserve $350 than $325
- At -35%, recovery becomes increasingly difficult (+53.8% needed)

#### Max Drawdown: RECOMMEND -35% (Not -40%)

**Rationale:**
- Drawdown is from ALL-TIME HIGH, not rolling
- At -40%, a $500 account that grew to $600 would trigger at $360
- -35% provides earlier intervention while still allowing for significant volatility
- Recovery from -35% requires +53.8%; from -40% requires +66.7%

### Alternative Threshold Set (Conservative)

If you prefer more conservative protection:

| Window | Conservative | Notes |
|--------|--------------|-------|
| Rolling 24h | -8% | Standard institutional limit |
| Rolling 7d | -15% | Matches original daily limit |
| Rolling 30d | -25% | Aggressive protection |
| Max Drawdown | -30% | Early intervention |

### Final Recommended Configuration

```json
{
  "LossLimits": {
    "Rolling24HourLossPercent": -12.0,
    "Rolling7DayLossPercent": -20.0,
    "Rolling30DayLossPercent": -30.0,
    "MaxDrawdownPercent": -35.0,
    "SingleTradeLossPercent": -3.0,
    "DrawdownPositionReductionPercent": 75
  }
}
```

---

## Rolling Window Mechanics

### Fundamental Design Decision: Trade P&L vs Equity Snapshots

Two approaches exist:

#### Option A: Trade-Level P&L Tracking (RECOMMENDED)
```
Window P&L = SUM(trade.pnl_percent) for all trades WHERE trade.timestamp >= window_start
```

**Pros:**
- Precise attribution of gains/losses to specific trades
- No interpolation needed
- Works correctly even with gaps in trading
- Can reconstruct exact sequence of events

**Cons:**
- Requires storing individual trade records
- More storage overhead

#### Option B: Equity Snapshot Comparison
```
Window P&L = (current_equity - equity_at_window_start) / equity_at_window_start
```

**Pros:**
- Simple calculation
- Less storage (just periodic snapshots)

**Cons:**
- Sensitive to snapshot timing
- Deposits/withdrawals break the calculation
- Doesn't account for unrealized P&L changes

### RECOMMENDATION: Hybrid Approach

Use **Trade-Level P&L** as primary source, with **Equity Snapshots** as validation/sanity check.

```
PRIMARY: Trade P&L accumulation within window
SECONDARY: Equity delta validation (should be within 1% of trade sum)
ALERT: If |trade_sum - equity_delta| > 1%, flag reconciliation needed
```

---

## Data Structure Specification

### Trade Record (New)

```csharp
public sealed class TradeRecord
{
    /// <summary>
    /// Unique identifier for this trade record.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Market ID where trade occurred.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// UTC timestamp when trade was executed.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// P&L of this trade as percentage of equity at time of trade.
    /// </summary>
    public required decimal PnlPercent { get; init; }

    /// <summary>
    /// Absolute P&L in USD.
    /// </summary>
    public required decimal PnlUsd { get; init; }

    /// <summary>
    /// Equity value at time of trade (for percentage calculations).
    /// </summary>
    public required decimal EquityAtTrade { get; init; }

    /// <summary>
    /// Order ID that generated this P&L (for audit trail).
    /// </summary>
    public string? OrderId { get; init; }
}
```

### Equity Snapshot (New)

```csharp
public sealed class EquitySnapshot
{
    /// <summary>
    /// UTC timestamp of this snapshot.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Market ID for this snapshot.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Total equity value in USD.
    /// </summary>
    public required decimal Equity { get; init; }

    /// <summary>
    /// Unrealized P&L at snapshot time.
    /// </summary>
    public decimal UnrealizedPnl { get; init; }
}
```

### Rolling Loss State (Replaces MarketLossState)

```csharp
public sealed class RollingLossState
{
    /// <summary>
    /// Market ID for this state.
    /// </summary>
    public int MarketId { get; init; }

    /// <summary>
    /// Trade records within the 30-day retention window.
    /// Sorted by timestamp descending for efficient window queries.
    /// </summary>
    public List<TradeRecord> TradeHistory { get; set; } = [];

    /// <summary>
    /// Hourly equity snapshots for the past 30 days.
    /// </summary>
    public List<EquitySnapshot> EquitySnapshots { get; set; } = [];

    /// <summary>
    /// Current equity value.
    /// </summary>
    public decimal CurrentEquity { get; set; }

    /// <summary>
    /// All-time high equity (for drawdown calculation).
    /// </summary>
    public decimal EquityHighWaterMark { get; set; }

    /// <summary>
    /// Current halt status.
    /// </summary>
    public DateTimeOffset? HaltUntil { get; set; }

    /// <summary>
    /// Reason for halt.
    /// </summary>
    public string? HaltReason { get; set; }

    /// <summary>
    /// Timestamp when halt was initiated (for graduated recovery).
    /// </summary>
    public DateTimeOffset? HaltStarted { get; set; }
}
```

---

## Calculation Methods

### Rolling Window P&L Calculation

```
FUNCTION CalculateRollingPnl(window_hours: int) -> decimal:
    window_start = UtcNow - TimeSpan.FromHours(window_hours)

    relevant_trades = TradeHistory
        .Where(t => t.Timestamp >= window_start)
        .ToList()

    IF relevant_trades.Count == 0:
        RETURN 0.0

    // Sum percentage P&L (already normalized to equity at trade time)
    total_pnl_percent = relevant_trades.Sum(t => t.PnlPercent)

    RETURN total_pnl_percent
```

### Drawdown Calculation (Unchanged)

```
FUNCTION CalculateDrawdown() -> decimal:
    IF EquityHighWaterMark <= 0:
        RETURN 0.0

    drawdown = (CurrentEquity - EquityHighWaterMark) / EquityHighWaterMark * 100
    RETURN drawdown  // Returns negative value (e.g., -15.5%)
```

### Window Status Check

```
FUNCTION GetRollingLossStatus() -> RollingLossStatus:
    pnl_24h = CalculateRollingPnl(24)
    pnl_7d = CalculateRollingPnl(168)    // 7 * 24
    pnl_30d = CalculateRollingPnl(720)   // 30 * 24
    drawdown = CalculateDrawdown()

    RETURN new RollingLossStatus {
        Rolling24hPnlPercent = pnl_24h,
        Rolling7dPnlPercent = pnl_7d,
        Rolling30dPnlPercent = pnl_30d,
        DrawdownPercent = drawdown,

        Rolling24hBreached = pnl_24h <= config.Rolling24HourLossPercent,
        Rolling7dBreached = pnl_7d <= config.Rolling7DayLossPercent,
        Rolling30dBreached = pnl_30d <= config.Rolling30DayLossPercent,
        DrawdownBreached = drawdown <= config.MaxDrawdownPercent
    }
```

---

## Time Window Specification

### Recommended Windows

| Window | Duration | Rationale |
|--------|----------|-----------|
| Short-term | **24 hours** | Standard daily risk period, catches flash crashes and bad trades |
| Medium-term | **7 days** | Week of trading, filters out single bad days |
| Long-term | **30 days** | Full trading month, strategic performance assessment |

### Why NOT Different Periods?

**12 hours (rejected):**
- Too short for grid bots which naturally oscillate
- Would trigger false positives during volatile Asian/European sessions

**3 days (rejected):**
- Awkward period, doesn't align with market cycles
- Weekend effect in traditional markets doesn't apply to crypto

**14 days (rejected):**
- Arbitrary, provides no benefit over 7 days
- Just adds another threshold to manage

### Window Overlap

Windows MUST overlap. They are not exclusive periods.

```
|------------------30d window-------------------|
              |-------7d window-------|
                              |--24h--|
                                     NOW
```

A single bad trade affects ALL three windows simultaneously. This is intentional - cascading limits provide layered protection.

---

## Edge Cases and Handling

### Edge Case 1: Bot Offline During Window

**Scenario:** Bot was offline for 6 hours within the 24-hour window.

**Handling:**
```
RULE: Time gaps do NOT reset or reduce the window calculation.

IF bot was offline from T1 to T2:
    - Trades before T1 still count normally
    - No trades during T1-T2 (obviously)
    - Trades after T2 count normally

    The window still covers full 24 hours.
    The P&L is simply the sum of all recorded trades.
```

**Rationale:** Offline time is not a "fresh start". If you lost 10% before going offline, that 10% still counts.

### Edge Case 2: No Trades in Window

**Scenario:** No trades executed in the rolling 24-hour period.

**Handling:**
```
RULE: Zero trades = 0% P&L for that window.

IF TradeHistory.Where(t >= window_start).Count == 0:
    RETURN 0.0%  // Not breached, trading allowed
```

**Rationale:** No trading = no losses = no breach. However, DRAWDOWN still applies based on equity.

### Edge Case 3: First 24h/7d/30d (Insufficient History)

**Scenario:** Bot just started, doesn't have 30 days of history yet.

**Handling:**
```
RULE: Use available history, do NOT pro-rate.

Day 1: 24h window = full day's P&L
Day 3: 7d window = 3 days of P&L (NOT multiplied by 7/3)
Day 10: 30d window = 10 days of P&L (NOT multiplied by 30/10)

SPECIAL: If history < 1 hour, use more conservative temporary limits:
    - 24h limit applies immediately
    - 7d limit = 24h limit * 2 (until 7 days of history)
    - 30d limit = 7d limit * 2 (until 30 days of history)
```

**Example:**
```
Day 3, accumulated P&L = -8%

Against -12% 24h limit: Check last 24h only
Against -20% 7d limit: -8% > -20%, NOT breached
Against -30% 30d limit: -8% > -30%, NOT breached

// We do NOT calculate: -8% * (30/3) = -80% and breach
```

**Rationale:** Pro-rating unfairly penalizes new accounts. A -8% loss over 3 days is concerning but not catastrophic.

### Edge Case 4: Partial Trade Records (Data Corruption)

**Scenario:** Some trade records are missing due to storage failure.

**Handling:**
```
RULE: If trade history appears incomplete, fall back to equity snapshots.

DETECTION:
    expected_trades = Trades where orders were filled (from order history)
    recorded_trades = TradeHistory.Count

    IF recorded_trades < expected_trades * 0.9:
        FLAG "Trade history potentially incomplete"
        USE equity_snapshot_based_calculation
        ALERT operator
```

### Edge Case 5: Price Manipulation / Exchange Anomaly

**Scenario:** Exchange reports a flash crash to $0.01 then recovery to $100,000.

**Handling:**
```
RULE: Implement sanity bounds on individual trades.

MAX_SINGLE_TRADE_PNL = +50%   // No single trade can show > 50% gain
MIN_SINGLE_TRADE_PNL = -50%   // No single trade can show > 50% loss

IF trade.PnlPercent > MAX_SINGLE_TRADE_PNL OR < MIN_SINGLE_TRADE_PNL:
    FLAG "Anomalous trade P&L detected"
    CLAMP trade to bounds for limit calculation
    ALERT operator for manual review
    LOG full trade details for investigation
```

---

## Recovery Logic Specification

### Current Behavior
- `HaltUntil` expires after fixed time
- Manual `ClearHalt()` method available

### New Behavior: Graduated Recovery

**Principle:** Do NOT auto-clear halt just because rolling loss improves. Require both time AND improved metrics.

#### Recovery Requirements by Breach Type

| Breach Type | Minimum Wait | Additional Requirements | Manual Override |
|-------------|--------------|-------------------------|-----------------|
| 24h Limit | 4 hours | Rolling 24h > 50% of limit | Yes |
| 7d Limit | 24 hours | Rolling 7d > 50% of limit | Yes |
| 30d Limit | 72 hours | Rolling 30d > 50% of limit | Yes |
| Max Drawdown | No auto-clear | Must recover above 75% of HWM | Yes |

#### Recovery State Machine

```
STATES:
    Halted          -> Awaiting minimum wait period
    RecoveryPhase1  -> Minimum wait passed, checking metrics
    RecoveryPhase2  -> Metrics improving, reduced capacity (50%)
    RecoveryPhase3  -> Stability confirmed, 75% capacity
    Normal          -> Full trading restored

TRANSITIONS:

Halted -> RecoveryPhase1:
    WHEN elapsed >= minimum_wait_for_breach_type

RecoveryPhase1 -> RecoveryPhase2:
    WHEN rolling_pnl > (breach_limit * 0.5)  // e.g., > -6% if limit is -12%
    AND no new breaches for 1 hour

RecoveryPhase2 -> RecoveryPhase3:
    WHEN rolling_pnl > (breach_limit * 0.25)  // e.g., > -3% if limit is -12%
    AND no new breaches for 2 hours
    AND position_multiplier = 0.5 for duration

RecoveryPhase3 -> Normal:
    WHEN rolling_pnl > (breach_limit * 0.1)  // e.g., > -1.2% if limit is -12%
    AND no new breaches for 4 hours
    AND position_multiplier = 0.75 for duration

ANY -> Halted:
    WHEN new breach detected
```

#### Recovery After Max Drawdown

Max drawdown is special - it measures distance from ATH, not rolling performance.

```
RULE: Max drawdown halt clears ONLY when:
    1. Equity recovers to 75% of HWM, OR
    2. Manual operator override

NEVER auto-clear just because time passed.

RATIONALE: -35% drawdown means you need +53.8% gain to recover.
           Auto-clearing after 24h accomplishes nothing.
           Only clear when actual recovery is demonstrated.
```

#### Manual Override Protocol

```
FUNCTION ManualClearHalt(marketId: int, operatorId: string, reason: string):
    LOG "Manual halt clear by {operatorId}: {reason}"

    IF current_breach == MaxDrawdown:
        WARN "Clearing max drawdown halt manually. Equity has NOT recovered."
        SET HaltReason = "ManualOverride_DrawdownNotRecovered"

    SET HaltUntil = null
    SET RecoveryState = RecoveryPhase2  // Start at 50% capacity, not full

    ALERT "Manual halt clear initiated. Trading at 50% capacity."
```

---

## Data Retention and Storage

### Retention Periods

| Data Type | Retention | Rationale |
|-----------|-----------|-----------|
| Trade Records | 35 days | 30-day window + 5-day buffer for edge cases |
| Equity Snapshots | 35 days | Same as trade records |
| Risk Events | 90 days | Audit and pattern analysis |
| Halt History | 180 days | Compliance and review |

### Storage Strategy

#### Primary Storage: Redis (Recommended)

```
KEY STRUCTURE:
    gridbot:{marketId}:trades:{YYYYMMDD}      -> List<TradeRecord>
    gridbot:{marketId}:equity:{YYYYMMDD}      -> List<EquitySnapshot>
    gridbot:{marketId}:loss_state             -> RollingLossState
    gridbot:{marketId}:halt_history           -> List<HaltEvent>
```

**Pros:**
- Already in use for other state
- Fast reads for frequent window calculations
- Built-in TTL for automatic cleanup

**Configuration:**
```json
{
  "Redis": {
    "TradeRecordTTLDays": 35,
    "EquitySnapshotTTLDays": 35,
    "RiskEventTTLDays": 90,
    "HaltHistoryTTLDays": 180
  }
}
```

#### Backup Storage: File System

For disaster recovery, also persist to local JSON files:

```
/data/loss_monitor/
    /trades/
        2024-12-06.json
        2024-12-05.json
        ...
    /equity/
        2024-12-06.json
        ...
    /state/
        loss_state_market_1.json
```

### Cleanup Frequency

```
SCHEDULE: Daily at 04:00 UTC (low trading volume period)

PROCESS:
    1. Identify trade records older than 35 days
    2. Archive to cold storage if needed
    3. Delete from Redis
    4. Same for equity snapshots
    5. Compact state files
```

---

## Interface Changes

### ILossMonitor Updates

```csharp
public interface ILossMonitor
{
    // EXISTING (modified return type)
    Task<RollingLossStatus> GetCurrentLossStatusAsync(int marketId, CancellationToken ct = default);

    // EXISTING (unchanged)
    Task RecordTradeResultAsync(int marketId, decimal pnlPercent, CancellationToken ct = default);
    Task RecordEquityAsync(int marketId, decimal currentEquity, CancellationToken ct = default);
    Task<bool> CheckLossLimitsAsync(int marketId, CancellationToken ct = default);

    // REMOVED - no longer needed
    // void ResetDailyLimits();
    // void ResetWeeklyLimits();
    // void ResetMonthlyLimits();

    // EXISTING (unchanged)
    void ClearHalt(int marketId);
    Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default);

    // NEW - for trade-level recording
    Task RecordTradeAsync(int marketId, TradeRecord trade, CancellationToken ct = default);

    // NEW - for window queries
    Task<decimal> GetRollingPnlAsync(int marketId, TimeSpan window, CancellationToken ct = default);

    // NEW - for recovery state
    RecoveryState GetRecoveryState(int marketId);

    // NEW - for equity snapshot management
    Task RecordEquitySnapshotAsync(int marketId, CancellationToken ct = default);
}
```

### RollingLossStatus (New Model)

```csharp
public sealed class RollingLossStatus
{
    /// <summary>
    /// P&L over the rolling 24-hour window as percentage.
    /// </summary>
    public decimal Rolling24hPnlPercent { get; init; }

    /// <summary>
    /// P&L over the rolling 7-day window as percentage.
    /// </summary>
    public decimal Rolling7dPnlPercent { get; init; }

    /// <summary>
    /// P&L over the rolling 30-day window as percentage.
    /// </summary>
    public decimal Rolling30dPnlPercent { get; init; }

    /// <summary>
    /// Current drawdown from all-time high as percentage.
    /// </summary>
    public decimal DrawdownFromAthPercent { get; init; }

    /// <summary>
    /// All-time high equity value.
    /// </summary>
    public decimal EquityHighWaterMark { get; init; }

    /// <summary>
    /// Current equity value.
    /// </summary>
    public decimal CurrentEquity { get; init; }

    /// <summary>
    /// Number of trades in the 24h window.
    /// </summary>
    public int TradesIn24h { get; init; }

    /// <summary>
    /// Number of trades in the 7d window.
    /// </summary>
    public int TradesIn7d { get; init; }

    /// <summary>
    /// Whether 24h rolling limit has been breached.
    /// </summary>
    public bool Rolling24hBreached { get; init; }

    /// <summary>
    /// Whether 7d rolling limit has been breached.
    /// </summary>
    public bool Rolling7dBreached { get; init; }

    /// <summary>
    /// Whether 30d rolling limit has been breached.
    /// </summary>
    public bool Rolling30dBreached { get; init; }

    /// <summary>
    /// Whether max drawdown limit has been breached.
    /// </summary>
    public bool DrawdownBreached { get; init; }

    /// <summary>
    /// Current recovery state if in recovery.
    /// </summary>
    public RecoveryState RecoveryState { get; init; }

    /// <summary>
    /// When trading halt expires, if currently halted.
    /// </summary>
    public DateTimeOffset? HaltUntil { get; init; }

    /// <summary>
    /// Reason for current halt, if any.
    /// </summary>
    public string? HaltReason { get; init; }

    /// <summary>
    /// Whether any rolling limit has been breached.
    /// </summary>
    public bool AnyLimitBreached =>
        Rolling24hBreached || Rolling7dBreached || Rolling30dBreached || DrawdownBreached;
}
```

### Configuration Updates

```csharp
public sealed class LossLimitOptions
{
    // REMOVED
    // public decimal DailyLossPercent { get; set; }
    // public decimal WeeklyLossPercent { get; set; }
    // public decimal MonthlyLossPercent { get; set; }

    // NEW - Rolling window limits
    /// <summary>
    /// Rolling 24-hour loss limit as percentage (default -12%).
    /// </summary>
    public decimal Rolling24HourLossPercent { get; set; } = -12m;

    /// <summary>
    /// Rolling 7-day loss limit as percentage (default -20%).
    /// </summary>
    public decimal Rolling7DayLossPercent { get; set; } = -20m;

    /// <summary>
    /// Rolling 30-day loss limit as percentage (default -30%).
    /// </summary>
    public decimal Rolling30DayLossPercent { get; set; } = -30m;

    // EXISTING (unchanged)
    public decimal MaxDrawdownPercent { get; set; } = -35m;
    public decimal SingleTradeLossPercent { get; set; } = -3m;
    public decimal DrawdownPositionReductionPercent { get; set; } = 75m;

    // NEW - Recovery configuration
    /// <summary>
    /// Minimum hours to wait before entering recovery for 24h breach (default 4).
    /// </summary>
    public int Rolling24HourRecoveryWaitHours { get; set; } = 4;

    /// <summary>
    /// Minimum hours to wait before entering recovery for 7d breach (default 24).
    /// </summary>
    public int Rolling7DayRecoveryWaitHours { get; set; } = 24;

    /// <summary>
    /// Minimum hours to wait before entering recovery for 30d breach (default 72).
    /// </summary>
    public int Rolling30DayRecoveryWaitHours { get; set; } = 72;

    // NEW - Data retention
    /// <summary>
    /// Days to retain trade records (default 35).
    /// </summary>
    public int TradeRecordRetentionDays { get; set; } = 35;

    /// <summary>
    /// Interval in minutes for equity snapshots (default 60).
    /// </summary>
    public int EquitySnapshotIntervalMinutes { get; set; } = 60;
}
```

---

## Migration Plan

### Phase 1: Data Structure Migration (No Behavior Change)

1. Add new `TradeRecord` and `EquitySnapshot` models
2. Add new Redis key structures alongside existing
3. Modify `RecordTradeResultAsync` to also create `TradeRecord`
4. Add hourly equity snapshot job
5. Deploy and verify data collection for 7+ days

### Phase 2: Parallel Calculation

1. Implement rolling window calculations
2. Run both old and new limit checks in parallel
3. Log discrepancies between systems
4. Do NOT trigger halts from new system yet
5. Tune thresholds based on real data

### Phase 3: Cutover

1. Switch breach detection to rolling windows
2. Remove `Reset*Limits()` methods
3. Update UI to show rolling metrics
4. Keep old counters for 30 days (backward compatibility)

### Phase 4: Cleanup

1. Remove old `DailyPnl`, `WeeklyPnl`, `MonthlyPnl` fields
2. Remove reset methods
3. Update documentation
4. Archive migration code

---

## Risk Rule Summary

### Rule LOSS-R001: Rolling 24-Hour Breach
```
IF Rolling24hPnlPercent <= -12%
THEN:
    - Enter Degraded_ProtectiveMode
    - Pause grid operations
    - Set HaltUntil = now + 4 hours minimum
    - Alert: CRITICAL "24-hour rolling loss limit breached"
    - Recovery: Graduated (50% -> 75% -> 100%) over 8+ hours
```

### Rule LOSS-R002: Rolling 7-Day Breach
```
IF Rolling7dPnlPercent <= -20%
THEN:
    - Enter Degraded_ProtectiveMode
    - Cancel all open orders
    - Set HaltUntil = now + 24 hours minimum
    - Alert: CRITICAL "7-day rolling loss limit breached"
    - Recovery: Graduated over 48+ hours
    - Notify operator for review
```

### Rule LOSS-R003: Rolling 30-Day Breach
```
IF Rolling30dPnlPercent <= -30%
THEN:
    - Enter Degraded_ProtectiveMode
    - Cancel all open orders
    - Set HaltUntil = now + 72 hours minimum
    - Alert: CRITICAL "30-day rolling loss limit breached - strategy review required"
    - Recovery: Requires operator review + graduated recovery
    - Recommend: Full strategy assessment before resuming
```

### Rule LOSS-R004: Max Drawdown Breach
```
IF DrawdownFromAthPercent <= -35%
THEN:
    - Enter Degraded_ProtectiveMode
    - Reduce position sizes by 75%
    - Alert: CRITICAL "Maximum drawdown breached - capital preservation mode"
    - NO automatic recovery
    - Recovery: Only when equity > 75% of HWM OR manual override
```

---

## Testing Requirements

### Unit Tests
1. Rolling window calculation with various data patterns
2. Edge case: Empty window returns 0%
3. Edge case: Partial history (< 30 days)
4. Edge case: Gap in trading data
5. Recovery state transitions
6. Threshold breach detection

### Integration Tests
1. Full cycle: Trade -> Record -> Calculate -> Breach -> Halt -> Recovery
2. Redis persistence and retrieval
3. Concurrent access to loss state
4. Data cleanup job

### Simulation Tests
1. Replay historical BTC data through new system
2. Compare breach frequency: old vs new thresholds
3. Validate recovery timing is appropriate
4. Stress test with high trade volume

---

## Appendix: Comparison of Threshold Sets

### Scenario Analysis: $500 Starting Capital

| Scenario | Loss | Old (3%/7%/10%) | Recommended (12%/20%/30%) | User Proposed (15%/25%/35%) |
|----------|------|-----------------|---------------------------|----------------------------|
| Bad day (-$50) | -10% | Daily breach | Within limits | Within limits |
| Bad week (-$80) | -16% | Weekly breach | Within limits | Within limits |
| Flash crash (-$70) | -14% | Daily + Weekly breach | 24h breach | Within limits |
| Bear week (-$100) | -20% | All breached | 7d breach | Within limits |
| Month of losses (-$140) | -28% | All breached | 7d + 30d breach | Within limits |

**Analysis:**
- Old thresholds: Too tight for grid bot, would halt frequently
- Recommended: Balanced protection, allows for volatility
- User proposed: May allow too much loss before intervention

---

## Document Metadata

- **Version**: 1.0
- **Created**: 2024-12-06
- **Author**: Trading Risk Manager Agent
- **Status**: Specification Complete
- **Next Steps**: Pass to dotnet-feature-builder for implementation
