# Phase 7: Decision Engine Integration Specification

## Document Purpose
This document defines the comprehensive risk management rules, decision loop architecture, state coordination, and recovery procedures for the ALTE Decision Engine - the central orchestrator that integrates all trading subsystems into a cohesive, safe, and profitable trading system.

---

## Section 1: Decision Loop Architecture

### 1.1 Loop Sequence Overview

The Decision Engine executes a strict, ordered sequence of checks on each iteration. The order is NOT arbitrary - it follows a "fail-fast, safety-first" philosophy where capital preservation checks occur before profit-seeking operations.

```
DECISION LOOP ITERATION (runs every DecisionLoopIntervalMs, typically 5-10 seconds)

Phase A: DATA COLLECTION (Parallel - max 2 seconds timeout)
  |
  v
Phase B: RISK ASSESSMENT (Sequential - Critical checks first)
  |
  v
Phase C: STATE EVALUATION (Determine if action is allowed)
  |
  v
Phase D: TRADING OPERATIONS (Only if state permits)
  |
  v
Phase E: HOUSEKEEPING (Logging, metrics, state updates)
```

### 1.2 Detailed Decision Loop Sequence

```
STEP 1: DATA COLLECTION (Parallel Execution)
========================================================
Execute these in parallel with 2-second timeout:
  1a. Fetch current price via IMarketDataService
  1b. Fetch account/position data via ILighterQueryClient
  1c. Get order book snapshot via IOrderBookAnalyzer
  1d. Get current grid state via IGridLifecycleService

IF any fetch fails:
  - Log warning
  - Use cached value if available and < 30 seconds old
  - If no valid cached value, skip to housekeeping with stale data flag


STEP 2: RISK SENTINEL CHECK (CRITICAL - First Priority)
========================================================
Execute IRiskSentinel.AssessRiskAsync()

Internally this aggregates:
  2a. ILossMonitor.CheckLossLimitsAsync()
      - Daily loss limit (-5%)
      - Weekly loss limit (-10%)
      - Monthly loss limit (-15%)
      - Max drawdown (-20%)

  2b. IFlashCrashDetector.CheckForFlashCrashAsync()
      - 1-min drop > 3% -> PauseBuys
      - 5-min drop > 5% -> PauseAll
      - 15-min drop > 10% -> CancelAndReduceHalf
      - 1-hour drop > 15% -> FullHalt

  2c. ILiquidityMonitor.CheckLiquidityAsync()
      - Volume drought (< 50% of 7d avg)
      - Book depth (< $50K)
      - Spread warning (> 0.5%)
      - Funding rate (> 0.1% per 8h)

IF RiskAssessment.RequiresImmediateAction:
  - Execute emergency response (see Section 4)
  - Skip remaining steps
  - Transition state to Halted if severity = Critical

IF !RiskAssessment.TradingAllowed:
  - Log reason
  - Skip trading operations (Step 4-6)
  - Proceed to housekeeping


STEP 3: MOON BAG STATUS CHECK (HIGH - Second Priority)
========================================================
Execute IMoonBagManager.GetMoonBagStatusAsync()

3a. Check current state:
    - Inactive: No special handling
    - Accumulating: Normal grid operations
    - TrailingActive: Enable trailing grid shifts
    - StopTriggered: Sell non-moon-bag portion
    - MoonBagOnly: Block all grid sells
    - ReleaseApproved: Allow moon bag sale

3b. IF TrailingActive:
    - Check ITrailingStopService.IsTrailingStopTriggeredAsync()
    - IF triggered 3 consecutive times:
      - Execute ITrailingStopService.ExecuteTrailingStopAsync()
      - Transition to StopTriggered state

3c. Store moon bag constraints for use in Step 5/6


STEP 4: TREND INTELLIGENCE CYCLE (MEDIUM - Third Priority)
========================================================
Execute ITrendIntelligenceService.ProcessTrendCycleAsync()

This internally:
  4a. ITrendDetector.AnalyzeTrendAsync()
      - EMA(20)/EMA(50) crossover
      - MACD signal analysis
      - ADX trend strength
      - Apply 15-minute confirmation delay

  4b. IInventoryManager.AnalyzeInventoryAsync()
      - Calculate current skew
      - Determine target skew based on trend
      - Check if rebalance needed (> 5% deviation)

  4c. IRebalancingService.ExecuteRebalanceAsync() IF needed
      - Gradual: max 10% per hour
      - Emergency: if deviation > 30%

IF TrendIntelligenceResult.HaltRequired:
  - Inventory skew > 90% indicates system malfunction
  - Transition to Halted state
  - Skip remaining steps


STEP 5: TRAILING GRID CHECK (Only if MoonBag.TrailingActive)
========================================================
IF moon bag state is TrailingActive:
  5a. ITrailingGridService.DetectBreakoutAsync()
  5b. IF breakout detected AND no flash spike:
      - Check cumulative shift limit (< 50% in 1 hour)
      - Execute ITrailingGridService.ShiftGridUpwardAsync()
  5c. Update trailing stop level via ITrailingStopService


STEP 6: GRID OPERATIONS (Only if trading allowed)
========================================================
Skip if:
  - RiskAssessment.TradingAllowed == false
  - TradingState != Active (and != Recovering with capacity)
  - MoonBagState == MoonBagOnly (no grid operations)

6a. Apply risk multipliers to grid:
    - Position multiplier from RiskAssessment
    - Spread multiplier from RiskAssessment/LiquidityMonitor

6b. Execute IGridLifecycleService.UpdateGridAsync()
    - Sync order status
    - Detect filled orders
    - Place replacement orders (respecting moon bag blocks)

6c. Handle grid adjustment needs:
    - IF ATR changed significantly -> recalculate spacing
    - IF price moved significantly -> consider grid shift
    - Apply liquidity cluster bias from IOrderBookAnalyzer


STEP 7: HOUSEKEEPING
========================================================
7a. Record metrics:
    - IRiskSentinel.RecordPriceUpdateAsync()
    - IRiskSentinel.RecordEquityUpdateAsync()
    - ITrailingGridService.RecordPriceAsync() (if moon bag active)

7b. Log decision summary

7c. Update ITradingStateService with any state changes

7d. Trigger daily/weekly/monthly resets if time boundary crossed
```

---

## Section 2: Circuit Breaker Priority Ordering

### 2.1 Priority Hierarchy (Highest = 1)

When multiple circuit breakers trigger simultaneously, they are processed in this strict order:

| Priority | Circuit Breaker | Scope | Override Possible |
|----------|----------------|-------|-------------------|
| 1 | Monthly Loss Limit (-15%) | Global | Manual only |
| 2 | Max Drawdown (-20%) | Global | No |
| 3 | Weekly Loss Limit (-10%) | Global | Manual only |
| 4 | 1-Hour Flash Crash (> 15%) | Market | No |
| 5 | Daily Loss Limit (-5%) | Global | After 24h |
| 6 | 15-Min Flash Crash (> 10%) | Market | After 1h |
| 7 | 5-Min Flash Crash (> 5%) | Market | After 15m |
| 8 | Dead Market (volume < 25%) | Market | Manual only |
| 9 | 1-Min Flash Crash (> 3%) | Market | After 5m |
| 10 | Inventory Skew > 90% | Market | Manual only |
| 11 | Book Depth < $50K | Market | Auto when depth recovers |
| 12 | Volume Drought < 50% | Market | Auto when volume recovers |
| 13 | Wide Spread > 0.5% | Market | Auto when spread normalizes |
| 14 | High Funding Rate > 0.1% | Market | Auto when rate normalizes |

### 2.2 Simultaneous Trigger Resolution Rules

```
Rule CBR-001: Higher Priority Wins
IF multiple circuit breakers trigger simultaneously
THEN apply the action of the HIGHEST priority breaker
AND log all triggered breakers for audit

Rule CBR-002: State Transition Dominance
IF any breaker requires Halted state
THEN transition to Halted regardless of other breakers
THEN set halt duration to MAXIMUM of all triggered durations

Rule CBR-003: Position Action Cascade
IF one breaker requires CancelAndReduceHalf
AND another requires FullHalt
THEN execute FullHalt (which includes position reduction)
DO NOT execute position reduction twice

Rule CBR-004: Buy/Sell Block Aggregation
IF one breaker blocks buys
AND another blocks sells
THEN block BOTH (equivalent to PauseAll)

Rule CBR-005: Multiplier Stacking
Position size multipliers DO stack multiplicatively:
  Example: Loss limit 0.75 * Liquidity 0.75 = 0.5625 (43.75% reduction)
  Minimum multiplier floor: 0.10 (90% reduction max)

Spread multipliers DO stack additively:
  Example: Volume 1.25 + Book depth 1.25 - 1.0 = 1.50 (50% wider)
  Maximum multiplier ceiling: 3.0 (200% wider max)
```

---

## Section 3: Service Conflict Resolution Matrix

### 3.1 Grid vs Risk Sentinel Conflicts

| Grid Request | Risk Sentinel State | Resolution | Rationale |
|--------------|---------------------|------------|-----------|
| Place buy order | BuysBlocked | BLOCK | Safety first |
| Place sell order | SellsBlocked | BLOCK unless trailing stop | Capital protection |
| Rebuild grid | Position multiplier < 0.5 | ALLOW with reduced sizes | Maintain market presence |
| Shift grid up | Flash spike active | BLOCK for 10 minutes | Prevent chasing spikes |
| Widen grid | Liquidity spread > 1.5x | ALLOW and ENFORCE | Risk sentinel takes precedence |
| Tighten grid | Volume drought active | BLOCK | Prevent trapped orders |

### 3.2 Grid vs Moon Bag Conflicts

| Grid Request | Moon Bag State | Resolution | Rationale |
|--------------|----------------|------------|-----------|
| Place sell order | MoonBagOnly | BLOCK all sells | Moon bag protection |
| Place sell order | TrailingActive | ALLOW until moon bag threshold | Normal operation |
| Rebuild grid | StopTriggered | PAUSE grid rebuild | Let stop execute first |
| Shift grid up | TrailingActive | ALLOW via TrailingGridService | Coordinated shift |
| Shift grid down | MoonBagOnly | BLOCK | No grid operations in this state |
| Cancel all orders | ReleaseApproved | ALLOW | Operator approved liquidation |

### 3.3 Trend Intelligence vs Moon Bag Conflicts

| Rebalance Request | Moon Bag State | Resolution | Rationale |
|-------------------|----------------|------------|-----------|
| Sell to rebalance | MoonBagOnly | BLOCK | Moon bag takes precedence |
| Sell to rebalance | TrailingActive | ALLOW up to 85% of position | Keep 15% moon bag |
| Sell emergency (30%+) | TrailingActive | ALLOW up to 85% of position | Emergency still respects moon bag |
| Sell emergency (30%+) | MoonBagOnly | BLOCK unless ReleaseApproved | Requires operator approval |
| Buy to rebalance | Any state | ALLOW if risk permits | No moon bag restriction on buys |

### 3.4 Trend Intelligence vs Risk Sentinel Conflicts

| Rebalance Request | Risk State | Resolution | Rationale |
|-------------------|------------|------------|-----------|
| Sell to rebalance | SellsBlocked | DEFER until unblocked | Risk trumps rebalancing |
| Buy to rebalance | BuysBlocked | DEFER until unblocked | Risk trumps rebalancing |
| Emergency rebalance | Halted state | BLOCK | No trading during halt |
| Gradual rebalance | Recovering state | ALLOW at recovery capacity | Controlled re-entry |
| Any rebalance | Position multiplier < 0.25 | REDUCE rebalance amount | Scale with risk level |

### 3.5 Trailing Stop vs Flash Crash Conflicts

| Trailing Stop Event | Flash Crash State | Resolution | Rationale |
|--------------------|-------------------|------------|-----------|
| Stop triggered | BuysBlocked | ALLOW sell execution | Trailing stop protects profits |
| Stop triggered | SellsBlocked | ALLOW sell execution | Exception: profit protection |
| Stop triggered | CancelAndReduceHalf | EXECUTE stop FIRST, then reduce | Stop is more targeted |
| Stop triggered | FullHalt | EXECUTE stop, then enter halt | Protect profits before halt |
| Stop not triggered | Any crash state | NORMAL flash crash handling | No trailing stop override |

```
Rule TSF-001: Trailing Stop Exception to Sell Block
IF ITrailingStopService.IsTrailingStopTriggeredAsync() == true
AND FlashCrashStatus.RequiredAction == PauseBuys OR PauseAll
THEN ALLOW trailing stop sell execution
BECAUSE trailing stop protects existing profits, which aligns with capital preservation

Rule TSF-002: Trailing Stop Priority Over Position Reduction
IF trailing stop triggered
AND flash crash requires CancelAndReduceHalf
THEN execute trailing stop sell (85% of position)
THEN calculate if additional reduction needed
THEN execute additional reduction only if position still too large
```

---

## Section 4: Emergency Response Procedures

### 4.1 Flash Crash Response Sequence

```
PROCEDURE: Flash Crash Emergency Response

TRIGGER: FlashCrashStatus.CrashDetected == true

STEP 1: Determine Severity
  - Minor (1-min > 3%): PauseBuys for 5 minutes
  - Moderate (5-min > 5%): PauseAll for 15 minutes
  - Severe (15-min > 10%): CancelAndReduceHalf for 1 hour
  - Extreme (1-hour > 15%): FullHalt for 4+ hours

STEP 2: Execute Immediate Action
  IF severity == Minor:
    - Set BuysBlocked = true
    - Allow existing buy orders to remain
    - Allow sell orders normally
    - Schedule unblock after 5 minutes

  IF severity == Moderate:
    - Set BuysBlocked = true, SellsBlocked = true
    - DO NOT cancel existing orders (avoid slippage during crash)
    - Schedule unblock after 15 minutes

  IF severity == Severe:
    - Cancel ALL pending grid orders
    - Calculate 50% of current long position
    - Place market sell for 50% reduction (respecting moon bag if active)
    - Transition state to Halted
    - Set recovery timer for 1 hour

  IF severity == Extreme:
    - Cancel ALL pending orders
    - Calculate position reduction to 50%
    - Place market sell for reduction (respecting moon bag)
    - Transition state to Halted
    - REQUIRE manual operator intervention to resume
    - Send CRITICAL alert

STEP 3: Check Cascade
  IF CrashCount24h(marketId) > 2:
    - Override to Extreme severity
    - Halt for 24 hours regardless of individual crash severity
```

### 4.2 Loss Limit Breach Response Sequence

```
PROCEDURE: Loss Limit Emergency Response

TRIGGER: LossStatus.AnyLimitBreached == true

STEP 1: Identify Breached Limit
  - Check in order: Monthly -> Weekly -> Daily -> Drawdown

STEP 2: Execute Response by Type

  IF DailyLossBreached:
    - Cancel all pending grid orders
    - DO NOT close existing positions
    - Transition to Halted state
    - Set halt duration to 24 hours
    - Send HIGH alert

  IF WeeklyLossBreached:
    - Cancel all pending orders
    - DO NOT close existing positions
    - Transition to Halted state
    - Set halt duration to 7 days
    - Send CRITICAL alert

  IF MonthlyLossBreached:
    - Cancel all pending orders
    - DO NOT close existing positions (operator decides)
    - Transition to Halted state
    - REQUIRE manual intervention to resume
    - Send CRITICAL alert

  IF MaxDrawdownBreached (-20% from ATH):
    - Cancel all pending orders
    - Reduce ALL positions by 75%
    - Set position multiplier to 0.25
    - Transition to Recovering state (not Halted)
    - Begin recovery procedure at 25% capacity
    - Send CRITICAL alert
```

---

## Section 5: Recovery State Machine

### 5.1 Recovery State Definitions

```
STATE: Halted
  Entry Conditions:
    - Any CRITICAL circuit breaker triggered
    - Operator manual halt

  Allowed Operations:
    - Read-only data collection
    - Monitoring and logging
    - NO trading operations

  Exit Conditions:
    - Halt duration expired AND stability check passed
    - Operator manual override (for monthly loss)

  Transitions To: Recovering


STATE: Recovering
  Entry Conditions:
    - Transition from Halted after cooldown
    - Max drawdown triggered (direct entry)

  Allowed Operations:
    - Trading at reduced capacity
    - Grid operations with position multiplier applied
    - Gradual capacity increase

  Exit Conditions:
    - Recovery phases complete
    - OR new circuit breaker triggered -> back to Halted

  Transitions To: Active OR Halted


STATE: Active
  Entry Conditions:
    - Recovery complete (4 phases passed)
    - OR clean startup with no halt history

  Allowed Operations:
    - Full trading at 100% capacity
    - All subsystems operational

  Exit Conditions:
    - Circuit breaker triggered -> Halted/Recovering
    - Operator pause -> Paused

  Transitions To: Halted, Recovering, or Paused
```

### 5.2 Recovery Capacity Scaling

```
RECOVERY PHASES (after Daily Loss Limit or Flash Crash):

Phase 1: Initial Re-Entry (0-15 minutes)
  - Position Multiplier: 0.25 (75% size reduction)
  - Grid Orders: 25% of normal count (e.g., 2-3 orders instead of 8-10)
  - Spread Multiplier: 1.5 (50% wider spreads)
  - Rebalancing: DISABLED
  - Moon Bag: Normal protection active

Phase 2: Cautious Trading (15-30 minutes from recovery start)
  - Position Multiplier: 0.50 (50% size reduction)
  - Grid Orders: 50% of normal count
  - Spread Multiplier: 1.25 (25% wider spreads)
  - Rebalancing: DISABLED
  - Moon Bag: Normal protection active

Phase 3: Stabilization (30-60 minutes from recovery start)
  - Position Multiplier: 0.75 (25% size reduction)
  - Grid Orders: 75% of normal count
  - Spread Multiplier: 1.0 (normal spreads)
  - Rebalancing: ENABLED at 50% rate (5% per hour max)
  - Moon Bag: Normal protection active

Phase 4: Return to Normal (60+ minutes from recovery start)
  - Position Multiplier: 1.0 (full size)
  - Grid Orders: 100% of normal count
  - Spread Multiplier: 1.0 (normal spreads)
  - Rebalancing: ENABLED at full rate
  - Moon Bag: Normal protection active
  - Transition to Active state


RECOVERY PHASE ADVANCEMENT CRITERIA:

To advance from Phase N to Phase N+1:
  1. Minimum time in current phase elapsed (15 minutes)
  2. No new circuit breakers triggered
  3. Price stability: < 2% volatility in last 15 minutes
  4. P&L stability: no additional losses > 0.5% in current phase
  5. Order book depth > $100K recovered
  6. No API errors in last 5 minutes

IF any criteria fails:
  - Remain in current phase
  - Reset phase timer
  - After 3 failed advancements, require operator review
```

### 5.3 Circuit Breaker During Recovery

```
Rule RCB-001: New Circuit Breaker in Recovery
IF new circuit breaker triggers during Recovering state
THEN immediately transition to Halted state
AND apply halt duration of new trigger
AND reset recovery progress to Phase 1

Rule RCB-002: Same Trigger Recurrence
IF the SAME trigger type that caused initial halt occurs during recovery
THEN transition to Halted
AND DOUBLE the halt duration (up to 24 hour maximum)
AND send CRITICAL alert for repeated failure

Rule RCB-003: Minor Risk Events in Recovery
IF non-critical risk event occurs (e.g., volume drought, wide spread)
THEN DO NOT transition to Halted
BUT pause phase advancement until resolved
AND apply additional multipliers as normal

Rule RCB-004: Recovery Capacity Floor
During recovery, position multiplier NEVER exceeds phase maximum:
  - Phase 1: max 0.25 even if risk says 1.0
  - Phase 2: max 0.50 even if risk says 1.0
  - Phase 3: max 0.75 even if risk says 1.0
  - Phase 4: max 1.0 (full recovery complete)
```

---

## Section 6: Timing and Performance Specifications

### 6.1 Decision Loop Timing

| Parameter | Recommended Value | Range | Rationale |
|-----------|-------------------|-------|-----------|
| Decision Loop Interval | 5000ms | 3000-10000ms | Balance between responsiveness and API load |
| Data Collection Timeout | 2000ms | 1000-3000ms | Prevent blocking on slow API |
| Risk Assessment Timeout | 1000ms | 500-2000ms | Critical path - must be fast |
| Order Placement Timeout | 5000ms | 3000-10000ms | Allow for blockchain confirmation |
| Recovery Phase Duration | 900000ms (15 min) | 600000-1800000ms | Balance safety vs opportunity cost |

### 6.2 Check Frequency by Type

| Check Type | Frequency | Rationale |
|------------|-----------|-----------|
| Price/Data Collection | Every loop iteration | Real-time awareness |
| Flash Crash Detection | Every loop iteration | Critical safety |
| Loss Limit Check | Every loop iteration | Critical safety |
| Liquidity Check | Every loop iteration | Affects spreads |
| Trend Analysis | Every loop iteration | Trend changes slowly |
| Grid Update | Every loop iteration | Order sync |
| Moon Bag Threshold | Every loop iteration | Position protection |
| Trailing Stop Check | Every loop iteration (if active) | Time-sensitive |
| Recovery Phase Advancement | Every loop iteration (if recovering) | Progress tracking |
| Daily/Weekly/Monthly Reset | Once at boundary | Administrative |

### 6.3 Slow API Response Handling

```
Rule SAR-001: Timeout Handling
IF data collection exceeds 2-second timeout:
  - Use last valid cached value if < 30 seconds old
  - IF no valid cache, skip this iteration with warning
  - DO NOT block decision loop indefinitely

Rule SAR-002: Consecutive Timeout Escalation
IF 3 consecutive timeouts occur:
  - Widen spreads by 50% (stale data penalty)
  - Reduce position multiplier to 0.5
  - Log WARNING level alert

IF 5 consecutive timeouts occur:
  - Cancel all pending orders (safety)
  - Pause new order placement
  - Transition to Paused state
  - Log HIGH level alert

IF 10 consecutive timeouts occur:
  - Transition to Halted state
  - Require manual intervention
  - Log CRITICAL alert (potential API outage)

Rule SAR-003: Recovery from Timeouts
IF API becomes responsive after timeouts:
  - First successful response: Reset timeout counter
  - Wait 5 successful consecutive responses
  - Gradually restore normal spreads and multipliers
  - Resume normal trading operations
```

---

## Section 7: Decision Engine Interface Specification

### 7.1 ITradingDecisionEngine Interface

```
Interface: ITradingDecisionEngine
Purpose: Central orchestrator for all trading decisions

Methods:
  Task<DecisionResult> ExecuteDecisionCycleAsync(int marketId, CancellationToken ct)
    - Executes one complete decision loop iteration
    - Returns comprehensive result with all actions taken

  Task<bool> InitializeAsync(int marketId, CancellationToken ct)
    - Initializes all subsystems for a market
    - Must be called before first decision cycle

  Task ShutdownAsync(int marketId, CancellationToken ct)
    - Gracefully shuts down trading for a market
    - Cancels pending orders, preserves positions

  RecoveryPhase GetCurrentRecoveryPhase(int marketId)
    - Returns current recovery phase (or None if Active)

  decimal GetEffectivePositionMultiplier(int marketId)
    - Returns combined position multiplier from all sources

  decimal GetEffectiveSpreadMultiplier(int marketId)
    - Returns combined spread multiplier from all sources

  bool CanPlaceOrder(int marketId, OrderSide side)
    - Quick check if order placement is allowed
    - Considers all blocks and states
```

### 7.2 DecisionResult Model

```
Class: DecisionResult
Purpose: Capture all outcomes from a decision cycle

Properties:
  int MarketId
  DateTimeOffset Timestamp
  TradingState PreviousState
  TradingState CurrentState

  RiskAssessment RiskAssessment
  MoonBagStatus MoonBagStatus
  TrendIntelligenceResult TrendResult
  GridUpdateResult GridResult

  List<OrderAction> OrdersPlaced
  List<OrderAction> OrdersCancelled

  decimal EffectivePositionMultiplier
  decimal EffectiveSpreadMultiplier

  RecoveryPhase RecoveryPhase
  TimeSpan RecoveryTimeRemaining

  List<string> Warnings
  List<string> ActionsBlocked  // For audit: what was blocked and why

  bool Success
  string ErrorMessage  // If Success == false
  TimeSpan ExecutionDuration
```

---

## Section 8: Configuration Parameters

### 8.1 Decision Engine Configuration

| Parameter | Default | Range | Unit | Description |
|-----------|---------|-------|------|-------------|
| DecisionLoopIntervalMs | 5000 | 3000-10000 | ms | Main loop interval |
| DataCollectionTimeoutMs | 2000 | 1000-3000 | ms | Timeout for parallel data fetch |
| MaxConsecutiveTimeouts | 5 | 3-10 | count | Before pause state |
| CriticalTimeoutThreshold | 10 | 5-20 | count | Before halt state |
| CacheValidityMs | 30000 | 10000-60000 | ms | Max age for cached data |
| RecoveryPhase1DurationMs | 900000 | 600000-1800000 | ms | 15 minutes |
| RecoveryPhase2DurationMs | 900000 | 600000-1800000 | ms | 15 minutes |
| RecoveryPhase3DurationMs | 1800000 | 900000-3600000 | ms | 30 minutes |
| RecoveryStabilityWindowMs | 900000 | 600000-1800000 | ms | 15 min stability check |
| RecoveryVolatilityThreshold | 0.02 | 0.01-0.05 | decimal | Max 2% volatility |
| RecoveryLossThreshold | 0.005 | 0.002-0.01 | decimal | Max 0.5% loss in phase |
| RecoveryDepthMinimum | 100000 | 50000-200000 | USD | Min book depth for advancement |

### 8.2 Multiplier Bounds

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| PositionMultiplierFloor | 0.10 | 0.05-0.25 | Minimum position multiplier |
| SpreadMultiplierCeiling | 3.0 | 2.0-5.0 | Maximum spread multiplier |
| RecoveryPhase1MaxMultiplier | 0.25 | 0.1-0.5 | Phase 1 position cap |
| RecoveryPhase2MaxMultiplier | 0.50 | 0.25-0.75 | Phase 2 position cap |
| RecoveryPhase3MaxMultiplier | 0.75 | 0.5-1.0 | Phase 3 position cap |

---

## Section 9: Operator Approval Requirements

### 9.1 Actions Requiring Manual Approval

| Action | Trigger Condition | Approval Method |
|--------|-------------------|-----------------|
| Resume after Monthly Loss | Monthly limit breach | API endpoint call |
| Release Moon Bag | MoonBag in MoonBagOnly state | ApproveReleaseAsync() |
| Clear 24-Hour Halt | Consecutive crash events | ClearProtection() + TransitionToAsync() |
| Override Extreme Flash Crash | 1-hour drop > 15% | ClearProtection() + explicit resume |
| Resume after Dead Market | Volume < 25% for 4+ hours | ILiquidityMonitor override |
| Increase Leverage Beyond Max | Any request > 5x | Not supported (hard limit) |

### 9.2 Automatic Recovery (No Approval Needed)

| Event | Recovery Condition | Auto-Resume Criteria |
|-------|-------------------|---------------------|
| Daily Loss Limit | 24 hours elapsed | AND no new breaches |
| Weekly Loss Limit | 7 days elapsed | AND operator acknowledged |
| 1-Min Flash Crash | 5 minutes elapsed | AND price stable |
| 5-Min Flash Crash | 15 minutes elapsed | AND price stable |
| 15-Min Flash Crash | 1 hour elapsed | AND price stable |
| Volume Drought | Volume > 50% of 7d avg | Automatic |
| Wide Spread | Spread < 0.5% | Automatic |
| Low Book Depth | Depth > $100K | Automatic |
| High Funding Rate | Rate < 0.1% | Automatic |

---

## Section 10: Audit and Logging Requirements

### 10.1 Required Log Events (Structured Logging)

```
Every Decision Cycle:
{
  "event": "DecisionCycle",
  "marketId": 1,
  "timestamp": "2025-11-26T10:00:00Z",
  "state": "Active",
  "trendState": "MildBull",
  "positionMultiplier": 0.75,
  "spreadMultiplier": 1.25,
  "ordersPlaced": 2,
  "ordersCancelled": 1,
  "executionMs": 342
}

Risk Event Triggered:
{
  "event": "RiskTrigger",
  "marketId": 1,
  "timestamp": "2025-11-26T10:00:00Z",
  "triggerType": "FlashCrash",
  "severity": "Moderate",
  "threshold": -0.05,
  "actualValue": -0.062,
  "action": "PauseAll",
  "protectionDurationMinutes": 15
}

State Transition:
{
  "event": "StateTransition",
  "marketId": 1,
  "timestamp": "2025-11-26T10:00:00Z",
  "fromState": "Active",
  "toState": "Halted",
  "reason": "Daily loss limit breached (-5.2%)",
  "haltDurationHours": 24
}

Recovery Phase Advancement:
{
  "event": "RecoveryAdvancement",
  "marketId": 1,
  "timestamp": "2025-11-26T10:00:00Z",
  "fromPhase": 1,
  "toPhase": 2,
  "positionMultiplier": 0.50,
  "criteriaResults": {
    "timeElapsed": true,
    "noNewBreakers": true,
    "priceStable": true,
    "pnlStable": true,
    "depthRecovered": true,
    "noApiErrors": true
  }
}

Action Blocked:
{
  "event": "ActionBlocked",
  "marketId": 1,
  "timestamp": "2025-11-26T10:00:00Z",
  "requestedAction": "PlaceBuyOrder",
  "blockedBy": "FlashCrashDetector",
  "reason": "BuysBlocked due to 1-min crash protection",
  "protectionRemaining": "3m 42s"
}
```

### 10.2 Metrics to Track

| Metric | Type | Description |
|--------|------|-------------|
| DecisionLoopExecutionTime | Histogram | Duration of each decision cycle |
| DataCollectionLatency | Histogram | Time to fetch market data |
| OrdersPlacedPerCycle | Counter | Orders placed each iteration |
| OrdersCancelledPerCycle | Counter | Orders cancelled each iteration |
| CircuitBreakerTriggers | Counter | By trigger type |
| StateTransitions | Counter | By from/to state pair |
| RecoveryPhaseTime | Gauge | Time spent in each recovery phase |
| EffectivePositionMultiplier | Gauge | Current position multiplier |
| EffectiveSpreadMultiplier | Gauge | Current spread multiplier |
| TimeoutsTotal | Counter | API timeout count |
| ActionsBlocked | Counter | By action type and blocker |

---

## Section 11: Edge Case Matrix

### 11.1 System Startup Scenarios

| Scenario | Detection | Response |
|----------|-----------|----------|
| Clean startup, no halt history | No halt record in state | Start in Paused state, wait for activation |
| Startup during active halt | Halt record with future expiry | Remain in Halted, honor remaining duration |
| Startup with expired halt | Halt record with past expiry | Transition to Recovering, begin Phase 1 |
| Startup with open positions | Position data from Lighter | Rebuild grid around existing positions |
| Startup with no positions | Empty account | Initialize fresh grid at market price |
| Startup during market hours | Exchange open | Normal initialization |
| Startup during maintenance | Exchange closed | Wait for exchange, poll every 60s |

### 11.2 Mid-Cycle Failures

| Scenario | Detection | Response |
|----------|-----------|----------|
| API fails mid-order-placement | Exception or timeout | Reconcile state on next cycle, do not retry immediately |
| Partial order batch success | Some orders succeed, some fail | Track succeeded orders, retry failed on next cycle |
| Position sync mismatch | Local vs Lighter position differs | Trust Lighter, update local state, log WARNING |
| Price gap during cycle | Price moved > 5% during execution | Cancel any pending orders placed at stale prices |
| Balance insufficient for order | Order rejected by Lighter | Reduce order size, try again at 50% |

### 11.3 Concurrent Event Handling

| Scenario | Events | Resolution |
|----------|--------|------------|
| Flash crash + Trailing stop triggered | Both in same cycle | Execute trailing stop first (profit protection), then apply flash crash pause |
| Rebalance in progress + Circuit breaker | Loss limit breached mid-rebalance | Cancel rebalance orders immediately, do not complete rebalance |
| Grid update + Moon bag threshold reached | Position sold down to moon bag | Block remaining grid sells, transition to MoonBagOnly |
| Recovery phase advance + New risk event | Risk event during advancement check | Block advancement, stay in current phase |
| Multiple markets, one halts | Market A halts, Market B ok | Independent handling - A halts, B continues |

---

## Section 12: Implementation Checklist

### 12.1 Phase 7 Implementation Tasks

```
[ ] 1. Create ITradingDecisionEngine interface
[ ] 2. Create TradingDecisionEngine implementation
[ ] 3. Implement decision loop sequence (Steps 1-7)
[ ] 4. Implement circuit breaker priority handler
[ ] 5. Implement conflict resolution logic
[ ] 6. Implement recovery state machine
[ ] 7. Implement capacity scaling during recovery
[ ] 8. Implement timeout handling and degradation
[ ] 9. Add structured logging for all events
[ ] 10. Add metrics collection
[ ] 11. Update TradingBotHostedService to use DecisionEngine
[ ] 12. Create unit tests for decision logic
[ ] 13. Create integration tests for state transitions
[ ] 14. Create stress tests with simulated failures
```

### 12.2 Testing Scenarios Required

```
[ ] Normal operation cycle with all services returning healthy
[ ] Flash crash triggers at each severity level
[ ] Loss limit triggers at each threshold
[ ] Simultaneous circuit breaker triggers (priority test)
[ ] Moon bag state transitions through all states
[ ] Trailing stop triggers during flash crash
[ ] Recovery phase advancement through all 4 phases
[ ] Recovery interrupted by new circuit breaker
[ ] API timeout handling at various thresholds
[ ] Graceful shutdown during active trading
[ ] Startup with existing positions
[ ] Startup during active halt
```

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | trading-risk-manager | Initial specification |

---

## Key Implementation Notes

1. **Fail-Fast Philosophy**: The decision loop is designed to check safety conditions before any trading operations. If any CRITICAL check fails, remaining steps are skipped.

2. **Idempotency**: Each decision cycle should be idempotent - running the same cycle twice with the same inputs should produce the same outputs (aside from timestamps).

3. **State Consistency**: The decision engine is the ONLY component that should transition trading states. Subsystems should REPORT conditions but not directly change state.

4. **Audit Trail**: Every blocked action must be logged with the specific blocker and reason. This is essential for debugging and compliance.

5. **Recovery Design**: Recovery is intentionally slow and conservative. The cost of a false-positive (staying halted too long) is much lower than a false-negative (resuming too early into another loss).

6. **Moon Bag Sanctity**: The 15% moon bag protection is nearly inviolable. Only explicit operator approval via ApproveReleaseAsync() can sell the moon bag, and even then only in STRONG_BEAR + below 200 MA conditions.

7. **Multiplier Stacking**: Position multipliers are multiplicative (compound reductions), spread multipliers are additive (linear widening). This means position sizes can get very small (10% of normal) but spreads have a ceiling (3x normal).
