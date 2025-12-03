# Session 6: Phase 6 Moon Bag Module - Risk Specification Enrichment

## Date
2025-11-26

## Objective
Enrich Phase 6 Moon Bag Module requirements with comprehensive risk management rules, edge cases, and trading logic considerations.

## Current Status
**PHASE 6: MOON BAG MODULE - RISK SPECIFICATION COMPLETE**

## Previous Phases Complete
- Phase 1: Foundation - COMPLETE
- Phase 2: Market Data - COMPLETE
- Phase 3: Grid Engine - COMPLETE
- Phase 4: Trend Intelligence - COMPLETE
- Phase 5: Risk Sentinel - COMPLETE

## Work Completed This Session

### Risk Specification Document Created
**File**: `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\phase6-moonbag-risk-specification.md`

### Key Additions Beyond Original Requirements

#### 1. Enhanced Thresholds
- Added minimum moon bag size constraint ($50 USD OR 0.1% of portfolio)
- Added tiered trailing stop tightening (50%, 100%, 200% profit levels)
- Added maximum single shift cap (10%) and hourly cumulative shift limit (20%)

#### 2. Warm-Up Period Rules
- 30-minute default warm-up before moon bag activates
- Early activation override if price moves >5% in favor
- Position tracking timestamps for warm-up calculation

#### 3. Position Size Edge Cases
- Position decrease via grid fills triggering hold mode
- Max position size tracking across all trading activities
- Position increase handling while in moon bag mode (recalculate thresholds)

#### 4. Inventory Skew Integration
- Moon bag priority over inventory rebalancing sells
- Skew calculation adjustment for tradeable inventory
- Emergency rebalance exception conditions
- Buy-side operations unaffected

#### 5. Perpetual Futures Specific Rules
- Long positions only by default (optional short moon bag)
- Leverage cap to 3x in moon bag mode
- Liquidation distance monitoring (20% warning threshold)
- Funding rate impact analysis and alerts
- Software-managed trailing stops (Lighter has no native support)
- Reduce-only orders for stop execution

#### 6. Active Trailing Stop Risk Rules
- Order placement restrictions during active trailing
- Grid modification restrictions
- Flash crash interaction protocol
- Cascading event priority hierarchy

#### 7. State Machine Definition
- 7 states: INACTIVE, WARMING_UP, TRACKING, TRAILING, TRIGGERED, HOLD_MODE, RELEASED
- State transitions clearly defined
- Persistence requirements specified

#### 8. Flash Spike Handling
- Spike reversal detection (discard from high watermark)
- Wick filtering for manipulation prevention
- Graduated cooldown periods (10-30 minutes based on severity)

#### 9. Stop Execution Edge Cases
- Normal trigger with 3-tick confirmation
- Gap through stop handling
- Partial fill completion strategy
- Low liquidity execution adjustment (TWAP)

## Questions Answered

| Question | Section | Summary |
|----------|---------|---------|
| Additional edge cases | Section 3 | Flash spike reversal, wick filtering, partial fills, low liquidity execution |
| Position decrease via partial fills | EC-POS-001 | Immediate HOLD mode entry, cancel sell orders |
| Warm-up period | Section 3.4 | 30-minute default, early activation at 5% favorable move |
| Increase position in moon bag mode | EC-POS-003 | Recalculate threshold, update locked quantity, exit HOLD if above threshold |
| Perpetual futures considerations | Section 5 | Long-only default, leverage cap, liquidation monitoring, funding rate alerts |
| Risk rules while trailing active | Section 6 | Order restrictions, grid modification limits, flash crash override |
| Inventory skew interaction | Section 4 | Moon bag priority, adjusted skew calculation, emergency exception |

## Configuration Parameters Added

| Parameter | Default | Purpose |
|-----------|---------|---------|
| MoonBagPercentage | 15% | Position protection percentage |
| WarmUpPeriodMinutes | 30 | Time before activation |
| MinimumMoonBagUsd | $50 | Minimum moon bag value |
| TightenAtProfitPercent50 | 50% | First tightening trigger |
| TightenAtProfitPercent100 | 100% | Second tightening trigger |
| AggressiveStopPercent | 7% | Stop at 100%+ profit |
| EmergencyStopPercent | 5% | Stop at 200%+ profit |
| MaxShiftPercent | 10% | Maximum single grid shift |
| MaxCumulativeShift1h | 20% | Hourly shift limit |
| EnableShortMoonBag | false | Optional short support |

## Files Created/Modified

### Created
- `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\phase6-moonbag-risk-specification.md`
- `C:\Users\stany\source\repos\plan\GridBot\.claude\sessions\context_session_6.md`

## Next Steps
1. Pass risk specification to `dotnet-feature-builder` for implementation
2. Implementation should follow the state machine design
3. Ensure persistence layer handles restart scenarios
4. Code review with `csharp-code-reviewer`
5. Trading logic audit with `trading-bot-auditor`

## Important Implementation Notes

1. **State Machine First**: Implement the state machine before individual rules to ensure coherent behavior
2. **Persistence Critical**: High watermark and locked quantity MUST survive system restarts
3. **Lighter DEX**: No native trailing stops - must implement in software with order replacement strategy
4. **Order Update Frequency**: Respect 30-second minimum interval between trailing stop order updates
5. **Testing Priority**: Flash spike handling and partial fill scenarios need thorough testing
6. **Reduce-Only Flag**: All stop execution orders must use reduce-only to prevent accidental position increase

## Dependencies for Implementation

### From Phase 1 (Configuration)
- `TradingBotOptions` - Add `MoonBagOptions` section

### From Phase 3 (Grid Engine)
- `IGridLifecycleService` - Grid shift integration
- `GridState` - Current grid parameters

### From Phase 4 (Trend Intelligence)
- `ITrendDetector` - STRONG_BEAR detection for release conditions
- `IInventoryManager` - Position tracking and skew calculation

### From Phase 5 (Risk Sentinel)
- `IFlashCrashDetector` - Flash spike detection
- `IRiskSentinel` - Risk assessment integration

### From GridBot.Lighter
- `ILighterQueryClient` - Position and account queries
- `ILighterCommandClient` - Stop order placement
