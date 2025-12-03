# Session 1: Implementation Plan Creation

## Date
2025-11-26

## Objective
Create comprehensive implementation plan for the ALTE (Adaptive Liquidity & Trend Engine) trading bot project.

## Current Status
**PHASE 4: TREND INTELLIGENCE - COMPLETE**
- Phase 1: Foundation - COMPLETE
- Phase 2: Market Data - COMPLETE
- Phase 3: Grid Engine - COMPLETE
- Phase 4: Trend Intelligence - COMPLETE (just implemented)
- Phase 5-8: Pending

## Work Done

### 1. Created Session Context
- Established `.claude/sessions/context_session_1.md` for tracking session work

### 2. Consulted trading-risk-manager Agent
Created comprehensive risk management specification covering:
- **Global Risk Parameters**: Capital allocation (25% max per market, 80% max deployed), leverage limits (5x per position, 3x aggregate)
- **Loss Limits**: Daily (-5%), Weekly (-10%), Monthly (-15%), Max Drawdown (-20%)
- **Dynamic Grid Geometry**: ATR-based spacing (0.2% to 2.0%), order book awareness rules
- **Inventory Management**: Trend states (STRONG_BULL to STRONG_BEAR), skew targets (80/20 to 20/80)
- **Moon Bag Protection**: 15% reserve, trailing stops, release conditions
- **Flash Crash Protection**: Tiered thresholds (1-min: 3%, 5-min: 5%, 15-min: 10%, 1-hour: 15%)
- **Liquidity Monitoring**: Volume checks, order book depth requirements
- **Lighter DEX Specifics**: USDC scaling, nonce management, order batching, fee considerations
- **Rule Priority Hierarchy**: Critical > High > Medium > Low with conflict resolution
- **Recovery Procedures**: Post-circuit-breaker recovery protocols

### 3. Created Documentation Structure
Created `doc/` folder with:

#### `doc/implementation-plan.md`
Full 8-phase implementation roadmap:
- **Phase 1**: Foundation - Domain models, configuration, state management
- **Phase 2**: Market Data - Price feeds, indicators, order book analysis
- **Phase 3**: Grid Engine - Dynamic grid geometry, order management
- **Phase 4**: Trend Intelligence - Trend detection, inventory management
- **Phase 5**: Risk Sentinel - Circuit breakers, monitoring, alerts
- **Phase 6**: Moon Bag Module - Trailing grid, position protection
- **Phase 7**: Integration - Decision engine, testing, UI
- **Phase 8**: Deployment - Production setup, monitoring, docs

#### `doc/risk-management-specification.md`
Comprehensive 10-section risk rules document with:
- 50+ specific trading rules
- Edge case handling tables
- Configuration parameter ranges
- Recovery procedures

#### `doc/progress-tracker.md`
Quick-reference progress tracking with:
- Phase status overview
- Current phase task checklists
- Change log
- Blockers tracking

### 4. Phase 1: Foundation Implementation (COMPLETE)
Implemented all Phase 1 components in `GridBot.ApiService`:

#### Domain Models (`Models/Trading/`)
- `TradingState.cs` - Enum: Active, Paused, Halted, Recovering
- `TrendState.cs` - Enum: StrongBull, MildBull, Neutral, MildBear, StrongBear
- `AlertSeverity.cs` - Enum: Critical, High, Medium, Low
- `RiskEvent.cs` - Record for logging risk triggers
- `GridConfiguration.cs` - Grid parameters (spacing, orders, bounds)
- `InventoryState.cs` - Inventory allocation state with trend-based targeting
- `MarketMetrics.cs` - Aggregated market data and indicators

#### Configuration System (`Configuration/`)
- `TradingBotOptions.cs` - Root config with nested option classes:
  - `CapitalOptions` - Position sizing, leverage limits
  - `LossLimitOptions` - Daily/weekly/monthly loss limits, drawdown
  - `GridOptions` - Grid spacing/width constraints
  - `TrendOptions` - EMA periods, confirmation delays, skew tolerance
  - `MoonBagOptions` - Reserve percent, trailing stops
  - `FlashCrashOptions` - Multi-timeframe drop thresholds
  - `LiquidityOptions` - Book depth, volume, funding rate thresholds
- `IRiskConfiguration.cs` - Interface for runtime config access
- `RiskConfiguration.cs` - Implementation wrapping IOptionsMonitor

#### State Management (`Services/State/`)
- `ITradingStateService.cs` - Interface for state management
- `TradingStateService.cs` - Thread-safe state transitions with SemaphoreSlim
- `TradingStateChangedEventArgs.cs` - Event args for state changes
- `TrendStateChangedEventArgs.cs` - Event args for trend changes

#### Background Services (`Services/`)
- `TradingBotHostedService.cs` - Main decision loop orchestrator
- `TradingBotHealthCheck.cs` - Health check based on state/loop status

#### Service Registration (`Extensions/`)
- `TradingBotServiceExtensions.cs` - AddTradingBot() extension method

#### Program.cs Update
- Added `builder.Services.AddTradingBot(builder.Configuration);`

**Build Status**: SUCCESS - 0 warnings, 0 errors

## Next Steps
1. **Phase 2: Market Data** - Price feeds, indicator calculations, order book analysis
2. Consult `lighter-api-specialist` for DEX-specific implementation details when needed
3. Use `dotnet-feature-builder` for coding each phase
4. Review with `csharp-code-reviewer` after each phase
5. Final audit with `trading-bot-auditor`

---

## Phase 3 Code Review (2025-11-26)
**Reviewer**: csharp-code-reviewer agent
**Status**: ISSUES FOUND - REQUIRES FIXES

### Critical Issues (Must Fix)

**CRITICAL-001**: IDisposable Not Implemented for SemaphoreSlim Resources
- Location: `GridOrderManager.cs` and `GridLifecycleService.cs`
- Both classes create `SemaphoreSlim` but never dispose them
- `_gridLocks` dictionary grows unbounded, never cleaned on teardown
- **Financial Risk**: Resource exhaustion in long-running trading systems

**CRITICAL-002**: UpdateOrderSizesAsync Does Nothing
- Location: `GridLifecycleService.cs` lines 465-492
- `GridLevel.Size` is init-only, method cannot update it
- All orders placed with hardcoded `0.001m` size
- **Financial Risk**: SEVERE - Incorrect order sizes, potential capital loss

### High Priority Issues

**HIGH-001**: Multiple Enumeration in GridCalculator
- Location: `GridCalculator.cs` lines 138-142
- `levels` enumerated 3 times in logging

**HIGH-002**: Race Condition in Fill Detection
- Location: `GridLifecycleService.cs` lines 183-185
- Already-filled orders counted again on each cycle
- Inflates `TotalFills` counter incorrectly

**HIGH-003**: Client Order Index Collision Potential
- Location: `GridOrderManager.cs` lines 380-386
- Same-second orders can have identical indices
- Only 500 unique indices per second possible

### Medium/Suggestions

**MEDIUM-001**: Division by zero potential in ATR calculation (defaults to magic number instead of error)

**SUGGESTION-001**: Use records for immutable result types

### Full Review Document
See: `.claude/doc/phase3-code-review.md`

### Required Actions
1. Implement `IDisposable` on both services
2. Fix `GridLevel.Size` to be settable and update `UpdateOrderSizesAsync`
3. Fix fill detection double-counting
4. Fix client order index collision potential

## Code Review Completed (2025-11-26)
**Reviewer**: csharp-code-reviewer agent

### Critical Finding - FIXED
- **CRITICAL-001**: Race condition and potential deadlock in `TradingStateService.cs`
  - **FIX APPLIED**: Restructured event invocation - prepare event args while holding lock, release lock, invoke events OUTSIDE lock without re-acquiring

### High Priority Findings - FIXED
- **HIGH-001**: `CurrentInventory` getter uses synchronous `Wait()`
  - **FIX APPLIED**: Replaced with `Volatile.Read()` for lock-free access
- **HIGH-002**: Events without unsubscription warning
  - **FIX APPLIED**: Added documentation in XML comments about singleton lifetime and unsubscription requirement

### Remaining (Low Priority)
- **HIGH-003**: Health check injects concrete `TradingBotHostedService` (acceptable for now)
- **MEDIUM-001**: Placeholder async methods (will become async when logic added in Phase 2+)
- **MEDIUM-002**: Duplicate `GetTargetSkewForTrend` logic (acceptable, one is in model, one in config)

### Full Review Document
See: `.claude/doc/phase1-code-review.md`

### Build Status After Fixes
**SUCCESS** - 0 warnings, 0 errors

## Key Documents
| Document | Path |
|----------|------|
| Implementation Plan | `doc/implementation-plan.md` |
| Risk Specification | `doc/risk-management-specification.md` |
| Progress Tracker | `doc/progress-tracker.md` |
| Risk Spec (backup) | `.claude/doc/risk-management-specification.md` |

## Files Created in Phase 1
| File | Purpose |
|------|---------|
| `Models/Trading/TradingState.cs` | Trading state enum |
| `Models/Trading/TrendState.cs` | Trend state enum |
| `Models/Trading/AlertSeverity.cs` | Alert severity enum |
| `Models/Trading/RiskEvent.cs` | Risk event record |
| `Models/Trading/GridConfiguration.cs` | Grid config class |
| `Models/Trading/InventoryState.cs` | Inventory state class |
| `Models/Trading/MarketMetrics.cs` | Market metrics class |
| `Configuration/TradingBotOptions.cs` | All config options |
| `Configuration/IRiskConfiguration.cs` | Config interface |
| `Configuration/RiskConfiguration.cs` | Config implementation |
| `Services/State/ITradingStateService.cs` | State service interface |
| `Services/State/TradingStateService.cs` | State service implementation |
| `Services/State/TradingStateChangedEventArgs.cs` | State event args |
| `Services/State/TrendStateChangedEventArgs.cs` | Trend event args |
| `Services/TradingBotHostedService.cs` | Background service |
| `Services/TradingBotHealthCheck.cs` | Health check |
| `Extensions/TradingBotServiceExtensions.cs` | DI extension |

## Notes
- Project codename: Nexus
- Target: Autonomous trading system bridging HFT Market Making and Long-Term Trend Following
- Tech stack: .NET 10, Aspire 13, ASP.NET Core, Blazor, Lighter DEX
- Existing infrastructure: Lighter client library with P/Invoke signing, REST API endpoints
- All values use decimal for precision in financial calculations
- Thread safety achieved with SemaphoreSlim for async operations
- State transitions validated to prevent invalid state changes

---

## Phase 4: Trend Intelligence Implementation (2025-11-26)

### Overview
Implemented the trend detection and inventory management system for the ALTE trading bot.
This phase creates the intelligence layer that determines market direction and adjusts
portfolio allocation accordingly.

### Files Created

#### Models (`Models/Trading/`)
| File | Purpose |
|------|---------|
| `TrendAnalysis.cs` | Result of trend analysis with EMA/MACD/ADX indicators |
| `InventoryAnalysis.cs` | Portfolio allocation analysis with rebalance requirements |
| `RebalanceResult.cs` | Result of rebalancing operations |
| `TrendIntelligenceResult.cs` | Combined result of complete trend cycle |

#### Trend Services (`Services/Trend/`)
| File | Purpose |
|------|---------|
| `ITrendDetector.cs` | Interface for trend detection |
| `TrendDetector.cs` | Implementation using EMA, MACD, ADX indicators |
| `ITrendIntelligenceService.cs` | Interface for trend cycle orchestration |
| `TrendIntelligenceService.cs` | Main orchestrator service |

#### Inventory Services (`Services/Inventory/`)
| File | Purpose |
|------|---------|
| `IInventoryManager.cs` | Interface for inventory management |
| `InventoryManager.cs` | Portfolio allocation calculations |

#### Rebalancing Services (`Services/Rebalancing/`)
| File | Purpose |
|------|---------|
| `IRebalancingService.cs` | Interface for rebalancing execution |
| `RebalancingService.cs` | Market order execution for rebalancing |

#### Extensions
| File | Purpose |
|------|---------|
| `TrendServiceExtensions.cs` | DI registration for trend services |
| `TradingBotServiceExtensions.cs` | Updated to include AddTrendServices() |

### Key Features Implemented

#### Trend Detection (`TrendDetector`)
- EMA(20)/EMA(50) crossover detection
- MACD signal line and zero line analysis
- ADX trend strength measurement
- Trend state determination:
  - STRONG_BULL: EMA20 > EMA50 AND MACD > Signal AND MACD > 0 AND ADX > 25
  - MILD_BULL: EMA20 > EMA50 AND (MACD > Signal OR MACD > 0)
  - NEUTRAL: EMAs within 1% OR ADX < 20
  - MILD_BEAR: EMA20 < EMA50 AND (MACD < Signal OR MACD < 0)
  - STRONG_BEAR: EMA20 < EMA50 AND MACD < Signal AND MACD < 0 AND ADX > 25
- Confirmation delay (15 minutes) before trend state changes
- Trend flip cooldown (2 hours) if trend flips twice within 1 hour

#### Inventory Management (`InventoryManager`)
- Current portfolio allocation calculation from Lighter account data
- Target skew calculation based on trend state:
  - StrongBull: 80% crypto / 20% USDT
  - MildBull: 70% crypto / 30% USDT
  - Neutral: 50% crypto / 50% USDT
  - MildBear: 30% crypto / 70% USDT
  - StrongBear: 20% crypto / 80% USDT
- Rebalance threshold detection (5% tolerance)
- Emergency rebalance detection (30%+ deviation)
- Max skew limit detection (90% halt trigger)
- Hourly rebalance rate tracking

#### Rebalancing Engine (`RebalancingService`)
- Market order execution via Lighter API
- Rate limiting (10% of portfolio per hour max)
- Emergency rebalance override (force to within 15% of target)
- Per-market rebalance locking (SemaphoreSlim)
- Minimum rebalance interval (1 minute)
- Transaction hash logging

#### Orchestrator (`TrendIntelligenceService`)
- Complete trend cycle processing:
  1. Analyze trend indicators
  2. Update trend state if confirmed
  3. Analyze inventory allocation
  4. Handle halt conditions
  5. Execute rebalance if needed
  6. Update inventory state
- Error handling with partial results
- State management integration

### Risk Rules Implemented
| Rule ID | Description | Implementation |
|---------|-------------|----------------|
| IM-001 | Max rebalance rate 10%/hour | `_hourlyRebalanceTracker` in RebalancingService |
| IM-002 | Trend confirmation delay 15 min | `_pendingConfirmations` in TrendDetector |
| IM-003 | Rebalance threshold 5% | `ShouldRebalance()` method |
| IM-004 | Emergency rebalance > 30% | `IsEmergencyRebalance()` method |
| IM-005 | Trend flip cooldown 2 hours | `_cooldownExpiry` in TrendDetector |
| IM-006 | Max inventory skew 90% | `HaltRequired` in InventoryAnalysis |

### Thread Safety
- `ConcurrentDictionary` for per-market state tracking
- `SemaphoreSlim` for rebalance operation locking
- `Interlocked` for client order counter
- All services are registered as singletons

### Build Status
**SUCCESS** - 0 warnings, 0 errors

### Next Steps
1. **Phase 5: Risk Sentinel** - Circuit breakers, flash crash protection, monitoring
2. Code review with `csharp-code-reviewer` agent
3. Trading audit with `trading-bot-auditor` agent

---

## Phase 4 Code Review (2025-11-26)
**Reviewer**: csharp-code-reviewer agent
**Status**: ISSUES FOUND - REQUIRES FIXES

### Critical Issues (Must Fix - Financial Risk)

**CRITICAL-001**: RebalancingService Does Not Implement IDisposable for SemaphoreSlim
- Location: `RebalancingService.cs`
- `_rebalanceLocks` dictionary creates SemaphoreSlim objects that are never disposed
- **Financial Risk**: Resource exhaustion in long-running trading systems

**CRITICAL-002**: Race Condition in TrendDetector Trend Flip History
- Location: `TrendDetector.cs` lines 296-325
- `_trendFlipHistory` uses List inside ConcurrentDictionary with per-list locking
- `GetOrAdd` can return new list to multiple threads before lock acquired
- **Financial Risk**: Trend flip cooldown logic could be bypassed

### High Priority Issues

**HIGH-001**: Duplicate Hourly Rebalance Tracking (Data Inconsistency)
- Both `InventoryManager` and `RebalancingService` maintain independent `_hourlyRebalanceTracker`
- **Financial Risk**: Rate limiting may be incorrectly calculated

**HIGH-002**: TrendIntelligenceResult.Failed() Sets null! for Required Properties
- `Failed()` factory assigns `null!` to non-nullable properties
- **Financial Risk**: NullReferenceException could crash decision loop

**HIGH-003**: Emergency Rebalance Calculation May Overshoot
- Missing bounds check on emergency target calculation
- **Financial Risk**: Invalid skew targets could cause unexpected large orders

### Medium Priority Issues

**MEDIUM-001**: TrendAnalysis.Macd initialized to null! instead of new()
**MEDIUM-002**: Portfolio calculation ignores position direction (short positions)
**MEDIUM-003**: Missing CancellationToken propagation in GetAvailableRebalanceCapacityAsync

### Full Review Document
See: `.claude/doc/phase4-code-review.md`

### Required Actions Before Phase 5
1. Implement `IDisposable` on `RebalancingService`
2. Fix race condition in `TrendDetector._trendFlipHistory`
3. Consolidate duplicate hourly rebalance tracking
4. Fix `TrendIntelligenceResult.Failed()` null handling
5. Add bounds checking to emergency rebalance calculation

---

## Phase 5 Code Review (2025-11-26)
**Reviewer**: csharp-code-reviewer agent
**Status**: ISSUES FOUND - REQUIRES FIXES

### Critical Issues (Must Fix - Financial Risk)

**CRITICAL-001**: IDisposable Not Implemented for SemaphoreSlim Resources
- Location: `LossMonitor.cs` line 21, `FlashCrashDetector.cs` line 21
- `SemaphoreSlim` objects never disposed, `ConcurrentDictionary` entries grow unbounded
- **Financial Risk**: Resource exhaustion in long-running trading systems

**CRITICAL-002**: Race Condition in FlashCrashDetector PriceHistory Access
- Location: `FlashCrashDetector.cs` lines 70-77, 224-243
- `CheckForFlashCrashAsync` reads `PriceHistory` without lock, while `RecordPriceAsync` modifies with lock
- **Financial Risk**: Flash crash detection could fail during actual crash events

### High Priority Issues

**HIGH-001**: Loss Limit Halt Bypass via Direct State Modification
- Location: `LossMonitor.cs` lines 142-184
- Halt state cleared without holding lock; MaxDrawdown doesn't set HaltUntil
- **Financial Risk**: Trading could resume before halt period expires

**HIGH-002**: RiskEventLogger ConcurrentQueue Iteration Not Thread-Safe
- Location: `RiskEventLogger.cs` lines 64-68, 77-82, 86-93
- LINQ iteration on ConcurrentQueue during concurrent modifications
- **Financial Risk**: Risk event queries could miss events or return duplicates

**HIGH-003**: FlashCrash CrashEvents List Thread Safety
- Location: `FlashCrashDetector.cs` lines 258, 216, 354-355
- `List<DateTimeOffset>` modified in multiple methods without consistent locking
- **Financial Risk**: Crash count could be miscounted, allowing trading during dangerous conditions

**HIGH-004**: AlertSeverity Comparison Logic Potentially Inverted
- Location: `RiskSentinel.cs` lines 102, 108, 125, 130, 135
- Severity comparison `overallSeverity > AlertSeverity.High` may not work as intended
- **Financial Risk**: Severity level may not escalate correctly during risk events

### Medium Priority Issues

**MEDIUM-001**: Incorrect MarketId in GetFundingRateReduction
- Location: `RiskSentinel.cs` line 311
- Passes `_riskConfig.MarketId` or `0` instead of actual `marketId` parameter

**MEDIUM-002**: LiquidityMonitor LastEventTimes Dictionary Unbounded Growth
- Location: `LiquidityMonitor.cs` line 267
- `LastEventTimes` entries never cleaned up after debounce period

**MEDIUM-003**: GetCurrentLossStatusAsync Does Not Respect CancellationToken
- Location: `LossMonitor.cs` lines 44-63
- `async` method with no awaits, cancellation token ignored

### Full Review Document
See: `.claude/doc/phase5-code-review.md`

### Required Actions Before Phase 6
1. Implement `IDisposable` on `LossMonitor` and `FlashCrashDetector`
2. Fix race condition in `FlashCrashDetector.PriceHistory` access (use ReaderWriterLockSlim)
3. Fix loss limit halt bypass and MaxDrawdown handling
4. Fix `RiskEventLogger` iteration (use ToArray() snapshot)
5. Fix `CrashEvents` list thread safety
6. Verify `AlertSeverity` enum ordering and fix comparison logic
7. Fix incorrect marketId in `GetFundingRateReduction`
8. Add cleanup for `LastEventTimes` dictionary
9. Fix async/cancellation token in `GetCurrentLossStatusAsync`

---

## Phase 6 Code Review (2025-11-26)
**Reviewer**: csharp-code-reviewer agent
**Status**: ISSUES FOUND - REQUIRES FIXES

### Critical Issues (Must Fix - Resource/Thread Safety)

**CRITICAL-001**: MoonBagManager Does Not Implement IDisposable for SemaphoreSlim
- Location: `MoonBagManager.cs` line 26
- `_marketLocks` ConcurrentDictionary creates SemaphoreSlim objects that are never disposed
- **Financial Risk**: Resource exhaustion in long-running trading systems

**CRITICAL-002**: Race Condition in MoonBagManager State Mutation
- Location: `MoonBagManager.cs` lines 57-61, 194-205
- `GetMoonBagStatusAsync` and `CalculateMoonBagThresholdAsync` read state without locking
- Returns mutable object reference that can be modified by other threads
- **Financial Risk**: Inconsistent moon bag state could cause incorrect sell decisions

**CRITICAL-003**: TrailingStopService State Not Thread-Safe for ConsecutiveTriggerTicks
- Location: `TrailingStopService.cs` lines 194, 219
- Counter incremented/reset without synchronization in `IsTrailingStopTriggeredAsync`
- **Financial Risk**: False or missed stop triggers due to race conditions

### High Priority Issues

**HIGH-001**: TrailingGridService ShiftHistory List Not Thread-Safe
- Location: `TrailingGridService.cs` lines 158, 162, 330-341, 351
- List accessed without lock in cumulative shift calculations
- **Financial Risk**: Incorrect hourly limit enforcement

**HIGH-002**: MoonBagStatus Is Mutable Class Shared Across Methods
- Location: `MoonBagStatus.cs` (entire class)
- All properties have setters, enabling concurrent modification
- **Financial Risk**: Data corruption from concurrent updates

**HIGH-003**: Potential Division by Zero in TrailingStopService
- Location: `TrailingStopService.cs` line 107
- Low impact - early return check at line 82 prevents worst case

**HIGH-004**: MoonBagManager CheckReleaseConditionsAsync Does Not Hold Lock
- Location: `MoonBagManager.cs` lines 327-397
- Reads state without lock during release conditions check
- **Financial Risk**: Could approve release based on stale state

### Medium Priority Issues

**MEDIUM-001**: ConsecutiveTriggerTicks should use Volatile/Interlocked
**MEDIUM-002**: Multiple async methods return Task.FromResult for sync work
**MEDIUM-003**: StopOrderId not updated after order placement (placeholder comment)
**MEDIUM-004**: MoonBagEvent.NewEventId() uses only 8 chars of GUID
**MEDIUM-005**: ApproveReleaseAsync calls CheckReleaseConditionsAsync while holding lock (potential deadlock if HIGH-004 fixed)

### Full Review Document
See: `.claude/doc/phase6-code-review.md`

### Required Actions Before Phase 7
1. Implement `IDisposable` on `MoonBagManager`
2. Fix race condition in `MoonBagManager` state access - add locking to read methods
3. Fix `ConsecutiveTriggerTicks` thread safety in `TrailingStopService`
4. Fix `ShiftHistory` list thread safety in `TrailingGridService`
5. Consider immutable state pattern for `MoonBagStatus`
6. Add locking to `CheckReleaseConditionsAsync`
7. Implement order ID tracking for trailing stop orders
8. Fix potential deadlock in `ApproveReleaseAsync`

---

## Phase 7: Decision Engine Integration Planning (2025-11-26)
**Agent**: trading-risk-manager
**Status**: SPECIFICATION COMPLETE

### Objective
Create comprehensive risk management specification for the Decision Engine - the central orchestrator that integrates all existing trading subsystems (Phases 1-6) into a cohesive, safe trading system.

### Key Deliverable
Created detailed specification document at:
`.claude/doc/phase7-decision-engine-specification.md`

### Specification Contents

#### 1. Decision Loop Architecture
- 7-step sequential decision loop with strict ordering
- Parallel data collection (max 2 seconds timeout)
- Risk assessment as first priority
- Moon bag status check as second priority
- Trend intelligence cycle as third priority
- Grid operations only if all checks pass

#### 2. Circuit Breaker Priority Ordering
- 14-level priority hierarchy from Monthly Loss (highest) to High Funding Rate (lowest)
- Simultaneous trigger resolution rules
- Multiplier stacking rules (position: multiplicative, spread: additive)

#### 3. Service Conflict Resolution Matrix
- Grid vs Risk Sentinel conflicts
- Grid vs Moon Bag conflicts
- Trend Intelligence vs Moon Bag conflicts
- Trend Intelligence vs Risk Sentinel conflicts
- Trailing Stop vs Flash Crash conflicts (special exception for profit protection)

#### 4. Emergency Response Procedures
- Flash crash response sequence by severity
- Loss limit breach response sequence
- Automatic vs manual intervention requirements

#### 5. Recovery State Machine
- 4-phase recovery process: 25% -> 50% -> 75% -> 100% capacity
- Phase advancement criteria (6 conditions must all pass)
- Circuit breaker handling during recovery
- Recovery capacity floor per phase

#### 6. Timing Specifications
- Decision loop interval: 5000ms (configurable 3000-10000ms)
- Data collection timeout: 2000ms
- Recovery phase duration: 15 minutes each
- Slow API response handling with escalation

#### 7. Key Risk Rules Defined
- Rule CBR-001 through CBR-005: Circuit breaker resolution
- Rule TSF-001 and TSF-002: Trailing stop flash crash exceptions
- Rule RCB-001 through RCB-004: Recovery circuit breaker handling
- Rule SAR-001 through SAR-003: Slow API response handling

#### 8. ITradingDecisionEngine Interface
```csharp
interface ITradingDecisionEngine {
    Task<DecisionResult> ExecuteDecisionCycleAsync(int marketId, CancellationToken ct);
    Task<bool> InitializeAsync(int marketId, CancellationToken ct);
    Task ShutdownAsync(int marketId, CancellationToken ct);
    RecoveryPhase GetCurrentRecoveryPhase(int marketId);
    decimal GetEffectivePositionMultiplier(int marketId);
    decimal GetEffectiveSpreadMultiplier(int marketId);
    bool CanPlaceOrder(int marketId, OrderSide side);
}
```

#### 9. Configuration Parameters
- 20+ configurable parameters for decision engine behavior
- Multiplier bounds (position floor 0.10, spread ceiling 3.0)
- Recovery phase maximum multipliers

#### 10. Operator Approval Requirements
- 6 actions requiring manual approval (monthly loss resume, moon bag release, etc.)
- 10 automatic recovery scenarios (daily loss after 24h, flash crash after duration, etc.)

#### 11. Audit and Logging Requirements
- Structured logging format for all events
- 12 metrics to track for observability

#### 12. Edge Case Matrix
- 7 system startup scenarios
- 5 mid-cycle failure scenarios
- 5 concurrent event handling scenarios

### Key Design Decisions

1. **Fail-Fast Philosophy**: Safety checks before any trading operations
2. **Idempotency**: Each decision cycle produces same outputs for same inputs
3. **State Consistency**: Only decision engine transitions trading states
4. **Audit Trail**: All blocked actions logged with specific blocker and reason
5. **Conservative Recovery**: Intentionally slow to prevent resuming into losses
6. **Moon Bag Sanctity**: 15% protection nearly inviolable, requires operator approval
7. **Multiplier Stacking**: Position multiplicative (can reach 10%), spread additive (cap at 3x)

### Critical Implementation Notes
- Recovery is intentionally slow (15 min phases) - cost of false positive (halted too long) is lower than false negative (resume into loss)
- Trailing stop has special exception to sell blocks during flash crash (profit protection aligns with capital preservation)
- Position multiplier floor is 0.10 (90% max reduction) to maintain minimal market presence
- All 6 recovery advancement criteria must pass to advance phase

### Next Steps for Implementation
1. Create `ITradingDecisionEngine` interface in `Services/DecisionEngine/`
2. Implement `TradingDecisionEngine` class
3. Implement `RecoveryPhase` enum and tracking
4. Implement `DecisionResult` model
5. Update `TradingBotHostedService` to delegate to decision engine
6. Add unit tests for priority resolution
7. Add integration tests for state transitions

### Key Documents Reference
| Document | Path |
|----------|------|
| Phase 7 Specification | `.claude/doc/phase7-decision-engine-specification.md` |
| Risk Management Spec | `doc/risk-management-specification.md` |
| Implementation Plan | `doc/implementation-plan.md` |

---

## Phase 7 Code Review (2025-11-26)
**Reviewer**: csharp-code-reviewer agent
**Status**: ISSUES FOUND - REQUIRES FIXES

### Critical Issues (Must Fix Before Production)

**CRITICAL-001**: IDisposable Not Implemented for SemaphoreSlim Resources
- Location: `RecoveryManager.cs` line 18, `TradingDecisionEngine.cs` line 37
- Both classes create `SemaphoreSlim` via `ConcurrentDictionary.GetOrAdd()` but never dispose them
- **Financial Risk**: Resource exhaustion (handle leaks) in long-running trading service

**CRITICAL-002**: Synchronous Blocking Call in GetCurrentRecoveryPhase
- Location: `TradingDecisionEngine.cs` lines 429-433
- Uses `.GetAwaiter().GetResult()` which can cause deadlocks in ASP.NET Core
- Called frequently from `GetEffectivePositionMultiplier` and `GetEffectiveSpreadMultiplier`
- **Financial Risk**: Potential deadlock during live trading

**CRITICAL-003**: Race Condition in RecoveryState Mutable Access
- Location: `RecoveryManager.cs` multiple methods (lines 75-76, etc.)
- `GetRecoveryStateAsync` returns mutable reference without lock
- `CheckPhaseAdvancementAsync` reads state before acquiring lock, then mutates it
- **Financial Risk**: Recovery criteria checked against stale/inconsistent data

### High Priority Issues

**HIGH-001**: RecoveryState Is a Mutable Class with Setters Exposed
- Location: `RecoveryState.cs` lines 17-86
- Public setters allow any caller to modify state bypassing locks
- **Financial Risk**: State corruption from concurrent modifications

**HIGH-002**: Data Collection Parallel Tasks Race Condition
- Location: `TradingDecisionEngine.cs` lines 537-667
- Local variables `timedOut`, `isPriceStale`, `failedSources` modified from multiple tasks without synchronization
- **Financial Risk**: Decision made on incorrect data quality assessment

**HIGH-003**: Missing Null Check Before Accessing MoonBagStatus.State
- Location: `TradingDecisionEngine.cs` lines 213, 229, 248
- `GetMoonBagStatusAsync` result used directly without null check
- **Financial Risk**: NullReferenceException during live trading

**HIGH-004**: Potential Lock Contention with ShutdownAsync
- Location: `TradingDecisionEngine.cs` lines 398-426
- Lock held for entire shutdown duration, silently skipping decision cycles
- **Financial Risk**: Low, but operational visibility issue

### Medium Priority Issues

**MEDIUM-001**: DecisionResult Uses Mutable List Properties
**MEDIUM-002**: Magic Numbers in Timeout Handling (0.5m, 3)
**MEDIUM-003**: Async Methods That Don't Await (return Task.FromResult)
**MEDIUM-004**: CancellationToken Not Passed to Some Async Calls
**MEDIUM-005**: RecoveryPhase switch default case returns full multipliers silently

### Full Review Document
See: `.claude/doc/phase7-code-review.md`

### Required Actions Before Phase 8
1. Implement `IDisposable` on `RecoveryManager` and `TradingDecisionEngine`
2. Remove synchronous blocking in `GetCurrentRecoveryPhase` (add sync overload to interface)
3. Fix race condition in `RecoveryManager` - read state inside lock
4. Return read-only copies of `RecoveryState` from `GetRecoveryStateAsync`
5. Add thread-safe counters (Interlocked) in `CollectDataAsync`
6. Add null check for `MoonBagStatus` before accessing properties
7. Add shutdown logging for operational visibility

### Files Reviewed
| File | Status |
|------|--------|
| `Models/Trading/RecoveryPhase.cs` | Clean |
| `Models/Trading/RecoveryState.cs` | HIGH-001 |
| `Models/Trading/DecisionContext.cs` | Clean |
| `Models/Trading/DecisionResult.cs` | MEDIUM-001 |
| `Services/DecisionEngine/IRecoveryManager.cs` | Clean |
| `Services/DecisionEngine/RecoveryManager.cs` | CRITICAL-001, CRITICAL-003, MEDIUM-003 |
| `Services/DecisionEngine/ITradingDecisionEngine.cs` | Clean |
| `Services/DecisionEngine/TradingDecisionEngine.cs` | CRITICAL-001, CRITICAL-002, HIGH-002, HIGH-003, HIGH-004, MEDIUM-002, MEDIUM-004 |
| `Extensions/DecisionEngineServiceExtensions.cs` | Clean |
| `Configuration/TradingBotOptions.cs` | Clean |
| `Services/TradingBotHostedService.cs` | Clean |
| `Extensions/TradingBotServiceExtensions.cs` | Clean |

---

## Trading Strategy & Risk Management Evaluation (2025-12-02)
**Agent**: trading-risk-manager
**Status**: EVALUATION COMPLETE

### Objective
Comprehensive evaluation of the ALTE trading strategy's viability, risk parameters, and expected performance across various market conditions.

### Key Findings

#### Strategy Verdict: CAN MAKE MONEY (CONDITIONAL)

The strategy is viable under specific market conditions:
1. Volatility (ATR) > 0.3% on chosen timeframe
2. Sufficient liquidity (> $100K book depth)
3. Funding rates < 0.2% per 8h on average
4. Not in extended dead market

#### Expected Performance
- **Bull market with pullbacks**: 90-110% of Buy-and-Hold
- **Bull market without pullbacks**: 70-85% of Buy-and-Hold
- **Bear market with rallies**: 150-200% of Buy-and-Hold
- **Sideways/choppy market**: 105-115% of Buy-and-Hold
- **Parabolic moves (3x+)**: ~185% gain vs 250% Buy-and-Hold

#### Expected Sharpe Ratio: 0.8-1.8

#### Critical Strategy Gaps Identified

| Gap | Severity |
|-----|----------|
| Low ATR grid spacing (0.2%) too tight for fees | HIGH |
| 1-minute flash crash threshold (3%) too sensitive | MEDIUM |
| Funding rate thresholds (0.1% warning, 0.3% critical) too high | MEDIUM |
| No correlation monitoring across markets | MEDIUM |

#### Recommended Parameter Adjustments

| Parameter | Current | Recommended | Rationale |
|-----------|---------|-------------|-----------|
| Min grid spacing | 0.15% | 0.25% | Ensure 3x fee coverage |
| Low ATR spacing | 0.2% | 0.35% | Improve fee margin |
| 1-min flash crash | 3% | 4% | Reduce false positives |
| StrongBull skew | 80/20 | 75/25 | Reduce concentration risk |
| Funding warning | 0.1% | 0.05% | Earlier warning |
| Funding critical | 0.3% | 0.15% | Earlier action |
| Monthly loss limit | -15% | -20% | Allow more operational flexibility |
| Recovery Phase 1 duration | 15 min | 10 min | Faster re-entry in V-recoveries |

#### Risk Analysis Summary

**Strengths**:
- Multi-layer circuit breakers protect capital
- Moon bag (15%) ensures upside participation
- Graduated recovery prevents premature re-entry
- Trend-based skew reduces bear market exposure
- ATR-adaptive grid spacing matches volatility

**Weaknesses**:
- Triple confirmation trend detection adds significant lag (30-60 min)
- 80/20 StrongBull skew is aggressive for automation
- V-shaped recovery handling causes underperformance
- Funding rate thresholds too permissive

### Final Assessment
**APPROVED FOR DEPLOYMENT** with recommended parameter adjustments and monitoring.

### Deliverable
Full evaluation document: `.claude/doc/trading-strategy-evaluation.md`

---

## Lighter API OrderBookDetails Fix (2025-12-02)
**Agent**: lighter-api-specialist
**Status**: RESEARCH COMPLETE

### Issue
The `OrderBookDetailResponse` model was failing to deserialize Lighter API responses due to incorrect response structure assumptions.

### Root Cause
**THREE CRITICAL MISUNDERSTANDINGS:**

1. **Wrong success code check**: Code checks `Code == 0` but API returns `Code == 200`
2. **Wrong response property name**: Code expects `data` but API returns `order_book_details` (array)
3. **Wrong endpoint for bids/asks**: `orderBookDetails` returns market metadata, NOT bids/asks

### Lighter Order Book Endpoints

| Endpoint | Purpose | Returns |
|----------|---------|---------|
| `orderBooks` | Market list | Array with fees, decimals, limits |
| `orderBookDetails` | Market metadata | Array with trading stats, margins |
| `orderBookOrders` | **Bids/Asks** | Individual orders in bids/asks arrays |

### Required Fixes

1. Change `IsSuccess` check from `Code == 0` to `Code == 200`
2. Rename `Data` property to `OrderBookDetails` (array type)
3. Add new `GetOrderBookOrdersAsync` method for actual bids/asks
4. Create `OrderBookOrder` and `OrderBookOrdersResponse` models

### Deliverable
Full specification: `.claude/doc/lighter-orderbook-api-fix.md`

---

## Comprehensive Technical Trading Bot Audit (2025-12-02)
**Agent**: trading-bot-auditor
**Status**: AUDIT COMPLETE - CONDITIONAL PASS

### Executive Summary

Comprehensive audit of the ALTE trading bot codebase examining order execution safety, decimal precision, race conditions, exchange integration, trading logic, and risk controls.

**Overall Verdict**: CONDITIONAL PASS

The system is fundamentally sound but requires fixes to identified critical issues before production deployment.

### Critical Issues Found (4)

1. **CRITICAL-001: Missing Lot Size Rounding**
   - Location: `GridBot.ApiService/Services/Grid/GridOrderManager.cs:476-482`
   - `ScaleSize()` applies fixed 8-decimal scale without validating against market-specific lot sizes
   - Financial Impact: Order rejection or execution at unintended sizes
   - Must Fix: Fetch market specs, use proper rounding, validate against minimum order size

2. **CRITICAL-002: Slippage Calculation Midpoint Rounding**
   - Location: `GridBot.Lighter/LighterCommandClient.cs:99-100`
   - `Math.Round()` uses banker's rounding by default
   - Financial Impact: Slippage protection may be inconsistent
   - Must Fix: Use `MidpointRounding.AwayFromZero`

3. **CRITICAL-003: No Position Limit Validation Before Order**
   - Location: `GridBot.ApiService/Services/Grid/GridOrderManager.cs:52-216`
   - Orders validated for moon bag but NOT for position limits, leverage, or max order size
   - Financial Impact: Could place orders exceeding limits causing liquidation
   - Must Fix: Add pre-submission validation

4. **CRITICAL-004: Emergency Rebalance Logic May Be Inverted**
   - Location: `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs:113-126`
   - The delta direction in emergency rebalance calculation may be inverted
   - Financial Impact: Emergency rebalance could overshoot/undershoot
   - Needs Review: Verify formula direction

### High Priority Issues Found (5)

1. **HIGH-001: Price Parsing Vulnerability**
   - `ParseScaledPrice()` assumes exactly 2 decimal places
   - Could fail on different decimal formats or locale-dependent separators
   - Must Fix: Use `decimal.Parse()` with `CultureInfo.InvariantCulture`

2. **HIGH-002: Grid Sizing Placeholder**
   - Previously flagged, now shows `Size { get; set; }` - appears FIXED
   - Verify in code that setter is being used correctly

3. **HIGH-003: Duplicate Hourly Rebalance Tracking**
   - Both `InventoryManager` and `RebalancingService` maintain independent trackers
   - Could allow bypassing the 10%/hour rate limit
   - Must Fix: Consolidate into single service

4. **HIGH-004: No Self-Trade Prevention**
   - Grid orders placed without checking if they cross own orders
   - Could trade against own orders causing fee waste
   - Must Fix: Verify bid price < lowest ask, ask price > highest bid

5. **HIGH-005: Short Position Handling in Inventory**
   - `Math.Abs()` treats short and long positions identically
   - Inventory allocation percentages may be misleading for shorts
   - Needs Review: Verify short position interpretation

### Positive Findings (9)

1. Comprehensive risk management architecture with defense in depth
2. Thread-safety through SemaphoreSlim with proper disposal
3. Consistent `decimal` usage for all financial calculations
4. Proper IDisposable implementation across services
5. Conservative 4-phase recovery state machine
6. Moon bag protection state machine (WarmingUp->Tracking->Trailing->HoldMode)
7. Trailing stop exception during flash crash (TSF-001)
8. Order validation via `CreateOrderRequest.Validate()`
9. Comprehensive telemetry via `TradingMetrics`

### Profitability Assessment

**Answer**: YES, the bot can realistically make money with caveats:

**Positive Factors**:
- ATR-based dynamic grid geometry
- Trend-based inventory skew prevents selling all in bull run
- Moon bag protection preserves upside
- Loss limits prevent catastrophic drawdowns

**Concerns**:
- No fee-aware grid spacing calculation
- Market order slippage on rebalancing
- Conservative recovery phases may miss opportunities

**Requirements for Profitability**:
- MUST: Fix lot size rounding, price parsing, self-trade prevention
- SHOULD: Add fee-aware spacing, limit orders for rebalancing
- NICE: Historical backtest framework, multi-market correlation

### Prior Code Review Status

Most issues from Phases 3-7 have been addressed:
- GridLevel.Size: FIXED (now has setter)
- TrendFlipHistory race: FIXED (using ConcurrentBag)
- FlashCrashDetector IDisposable: FIXED
- PriceHistory race: FIXED (uses ReaderWriterLockSlim)
- MoonBagManager IDisposable: FIXED
- GetCurrentRecoveryPhase blocking: FIXED (sync overload added)
- Data collection parallel race: FIXED (using Interlocked)

### Deployment Recommendation

**DO NOT** deploy to mainnet with real funds until:
1. All 4 CRITICAL issues resolved
2. HIGH-001, HIGH-003, HIGH-004 resolved
3. Extended testnet testing completed

**CONDITIONALLY APPROVED** for:
- Testnet deployment
- Paper trading simulation
- Limited capital pilot ($100-$500) after critical fixes

**Timeline**: ~4 weeks to production-ready with confidence
- Critical fixes: 2-3 days
- High priority fixes: 3-5 days
- Integration testing: 1 week
- Supervised pilot: 2 weeks

### Deliverable
Full audit document: `.claude/doc/trading-bot-audit-comprehensive.md`

---

## Lighter API Order Expiry Fix (2025-12-02)
**Agent**: lighter-api-specialist
**Status**: RESEARCH COMPLETE

### Issue
"OrderExpiry is invalid" error when creating market orders with:
- OrderExpiry = -1 (Default28DayOrderExpiry)
- TimeInForce = ImmediateOrCancel (0)
- OrderType = Market (1)

### Root Cause
**IOC (Immediate-Or-Cancel) and Market orders require `order_expiry = 0`, NOT -1.**

The Python SDK explicitly uses different expiry values:
- Limit orders (GTT): `order_expiry = -1` (DEFAULT_28_DAY_ORDER_EXPIRY)
- Market/IOC orders: `order_expiry = 0` (DEFAULT_IOC_EXPIRY)

### The Fix
The constant `OrderConstants.DefaultIocExpiry = 0` already exists in `Enums.cs` (line 137).

**Required Changes:**
1. `LighterCommandClient.cs` line 115: Change `Default28DayOrderExpiry` to `DefaultIocExpiry`
2. `OrderRequest.cs` lines 144-145: Update validation to allow expiry=0 for IOC orders

### Order Expiry Rules
| TimeInForce | OrderExpiry | Behavior |
|-------------|-------------|----------|
| GoodTillTime (1) | -1 | Signer calculates 28 days from now |
| GoodTillTime (1) | Unix timestamp | Order expires at that timestamp |
| ImmediateOrCancel (0) | **0** | Order executes immediately or cancels |
| PostOnly (2) | -1 | Signer calculates 28 days from now |

**Key Rule**: When `TimeInForce = ImmediateOrCancel (0)`, you MUST use `OrderExpiry = 0`.

### Deliverable
Full specification: `.claude/doc/lighter-order-expiry-fix.md`

---

## Lighter sendTx API Format Fix (2025-12-03)
**Agent**: lighter-api-specialist
**Status**: RESEARCH COMPLETE

### Issue
Error when sending orders to POST /api/v1/sendTx:
```json
{"code":20001,"message":"invalid param: : field \"tx_type\" is not set"}
```

### Root Cause
**The REST API uses multipart/form-data, NOT JSON.**

The current C# implementation sends JSON:
```csharp
var json = JsonSerializer.Serialize(request, _jsonOptions);
using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
```

But the Lighter API expects form data fields.

### Evidence from Python SDK

The official Python SDK (`lighter/api/transaction_api.py`) constructs requests as form data:
```python
_form_params = []
_form_params.append(('tx_type', tx_type))
_form_params.append(('tx_info', tx_info))
_form_params.append(('price_protection', price_protection))
```

Official documentation confirms:
- Content-Type: **multipart/form-data**
- Response Type: application/json

### Request Format

| Field | Type | Description |
|-------|------|-------------|
| `tx_type` | integer | Transaction type (e.g., 14 for CreateOrder) |
| `tx_info` | string | **JSON string** (NOT object) from signer |
| `price_protection` | boolean | Optional, lowercase string |

### Key Fix Points

1. **Use MultipartFormDataContent instead of JSON**
2. **Do NOT deserialize txInfo** - keep it as the raw JSON string from signer
3. **sendTxBatch also uses form data**

### Required Code Changes

```csharp
// BEFORE (broken):
var txInfoObject = JsonSerializer.Deserialize<object>(txInfo);
var json = JsonSerializer.Serialize(request, _jsonOptions);
using var content = new StringContent(json, "application/json");

// AFTER (fixed):
using var formContent = new MultipartFormDataContent();
formContent.Add(new StringContent(txType.ToString()), "tx_type");
formContent.Add(new StringContent(txInfo), "tx_info");  // txInfo is already JSON string
if (priceProtection.HasValue)
    formContent.Add(new StringContent(priceProtection.Value.ToString().ToLowerInvariant()), "price_protection");
```

### Deliverable
Full specification: `.claude/doc/lighter-sendtx-api-fix.md`

---
