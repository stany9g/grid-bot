# Phase 6: Moon Bag Module - Risk Management Specification

## Document Purpose
This document provides comprehensive risk management rules, edge cases, and trading logic for the Moon Bag Module (Phase 6) of the ALTE trading bot. It enriches the existing requirements with additional considerations for perpetual futures trading on Lighter DEX.

---

## Section 1: Moon Bag Core Thresholds (Enhanced)

### 1.1 Base Thresholds

| Parameter | Value | Formula/Condition | Rationale |
|-----------|-------|-------------------|-----------|
| Moon Bag Percentage | 15% | `moon_bag_size = max_position * 0.15` | Preserves upside exposure during parabolic moves |
| Moon Bag Trigger | Price > grid_upper_bound | `current_price > grid_parameters.upper_bound` | Activates trailing grid mechanism |
| Trailing Grid Step | 2% | `price_move >= grid_upper_bound * 1.02` | Grid shifts up after 2% price increase |
| Maximum Trail Distance | 20% | `trailing_stop >= current_high * 0.80` | Prevents trailing stop from being too distant |
| Minimum Moon Bag Size | $50 USD OR 0.1% of portfolio | `MAX($50, portfolio * 0.001)` | Ensures meaningful position after fees |

### 1.2 Trailing Stop Thresholds

| Parameter | Default Value | Trigger Condition | Rationale |
|-----------|---------------|-------------------|-----------|
| Initial Trailing Stop | 15% below high | Price moved >10% above initial grid | Standard profit protection |
| Tightened Trailing Stop | 10% below high | Unrealized profit > 50% | Lock in larger profits |
| Aggressive Trailing Stop | 7% below high | Unrealized profit > 100% | Protect exceptional gains |
| Emergency Stop | 5% below high | Unrealized profit > 200% | Extreme profit protection |

### 1.3 Moon Bag Activation Requirements

```
Rule MB-ACT-001: Minimum Position for Moon Bag
IF position_size < minimum_moon_bag_threshold ($50 OR 0.1% portfolio)
THEN moon_bag_protection = DISABLED
Rationale: Tiny positions do not justify the complexity of moon bag management

Rule MB-ACT-002: Warm-Up Period
IF time_since_position_opened < 30 minutes
THEN moon_bag_protection = DISABLED (allow normal grid trading to establish position)
Rationale: Prevents premature locking before position properly established
Exception: If price moves > 5% in favor during warm-up, activate immediately

Rule MB-ACT-003: Profitable Position Requirement
IF unrealized_pnl <= 0
THEN moon_bag_protection = DISABLED (but track for later activation)
Rationale: Moon bag is designed to protect profits, not amplify losses
```

---

## Section 2: Trailing Grid Rules (Enhanced)

### 2.1 Grid Shift Rules

```
Rule TG-001: Grid Shift Trigger (Enhanced)
IF current_price > grid_upper_bound
AND time_since_last_shift >= shift_cooldown (minimum 60 seconds)
AND flash_spike_not_detected
THEN shift_grid_up(price_delta = current_price - grid_upper_bound)
Priority: MEDIUM

Rule TG-001a: Proportional Grid Shift
IF price_breakout_percentage > trailing_grid_step (2%)
THEN shift_amount = FLOOR(price_breakout_percentage / trailing_grid_step) * trailing_grid_step
Rationale: Shift in discrete steps to avoid constant micro-adjustments

Rule TG-001b: Grid Shift Ceiling
IF proposed_shift > 10% (single shift)
THEN cap_shift_at(10%) AND flag_for_operator_review
Rationale: Massive single shifts may indicate anomaly or manipulation

Rule TG-001c: Maximum Shift Velocity
IF cumulative_shifts_1h > 20%
THEN pause_shift_for(30_minutes) AND alert_operator
Rationale: Prevents runaway grid in parabolic but potentially manipulated markets
```

### 2.2 Moon Bag Lock Rules

```
Rule TG-002: Moon Bag Lock (Enhanced)
IF position_size <= moon_bag_threshold (15% of max position)
THEN block_all_automated_sell_orders = TRUE
AND set_mode = HOLD_MODE
Priority: HIGH

Rule TG-002a: Moon Bag Calculation Basis
moon_bag_threshold = max_position_size_achieved * moon_bag_percentage
NOT current_position * moon_bag_percentage
Rationale: Locks absolute quantity, not relative percentage of remaining position

Rule TG-002b: Moon Bag Quantity Lock
IF moon_bag_mode = ACTIVE
THEN locked_quantity = max(moon_bag_threshold, minimum_moon_bag_size)
AND all_sell_orders_blocked_for_locked_quantity
Rationale: Prevents eroding moon bag through small incremental sells

Rule TG-002c: Moon Bag Persistence
Moon bag threshold persists across:
- Grid rebuilds
- System restarts
- Trend state changes (except STRONG_BEAR with release conditions)
Rationale: Moon bag is a long-term position protection mechanism
```

### 2.3 Trailing Stop Rules

```
Rule TG-003: Trailing Stop Activation (Enhanced)
IF price > initial_grid_upper_bound * 1.10 (10% above)
AND unrealized_profit > 0
THEN activate_trailing_stop(distance = 15% below high)
Priority: MEDIUM

Rule TG-003a: High Watermark Tracking
Update highest_price_seen = MAX(highest_price_seen, current_price)
Frequency: Every price update
Persistence: Per-position, survives system restart

Rule TG-003b: Trailing Stop Calculation
trailing_stop_price = highest_price_seen * (1 - trailing_stop_percentage)
Where trailing_stop_percentage = 15% (or tightened value)

Rule TG-004: Trail Tightening (Enhanced)
| Unrealized Profit | Trailing Distance | Stop Type |
|-------------------|-------------------|-----------|
| 0% - 50%          | 15%               | Standard  |
| 50% - 100%        | 10%               | Tightened |
| 100% - 200%       | 7%                | Aggressive|
| > 200%            | 5%                | Emergency |

Rule TG-004a: One-Way Tightening
Trailing stop distance can ONLY tighten, NEVER widen
IF proposed_distance > current_distance
THEN reject_change
Rationale: Prevents losing protection during profit pullbacks

Rule TG-005: Moon Bag Release (Enhanced)
IF (trend_state == STRONG_BEAR
    AND price < 200_day_MA
    AND price < 50_day_MA
    AND manual_release_flag == TRUE)
THEN allow_moon_bag_sale
Priority: LOW (requires multiple confirmations)

Rule TG-005a: Partial Release Option
IF release_conditions_met
THEN allow_sale_of(moon_bag_quantity * 0.50) per 24 hours
Rationale: Gradual exit prevents panic selling entire moon bag
```

---

## Section 3: Edge Cases (Comprehensive)

### 3.1 Flash Spike Handling

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| Flash spike > 20% in 5 min | `(current_price - price_5_min_ago) / price_5_min_ago > 0.20` | Pause grid shift for 10 minutes, do NOT update high watermark | Resume after 10 min stability (< 2% movement) |
| Flash spike > 30% in 5 min | Same formula, higher threshold | Pause grid shift for 30 minutes, flag suspicious activity | Require operator acknowledgment before resume |
| Flash spike reversal | Price returns to within 5% of pre-spike level within 15 min | Discard spike from high watermark calculation | Reset high watermark to pre-spike level |

```
Rule EC-SPIKE-001: High Watermark Spike Protection
IF price_increase_5min > 20%
THEN suspend_high_watermark_updates(10_minutes)
AND set_provisional_high = current_price
AND IF price_stable_10_minutes
    THEN confirm_provisional_high_as_new_high
    ELSE discard_provisional_high

Rule EC-SPIKE-002: Wick Filter
IF candle_wick_percentage > 50% (wick vs body)
AND candle_duration < 1_minute
THEN use_candle_body_high (not wick high) for trailing stop
Rationale: Prevents manipulation via thin wick wicks
```

### 3.2 Trailing Stop Trigger Scenarios

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| Normal trailing stop trigger | `current_price <= trailing_stop_price` for 3 consecutive ticks | Sell 85% of position at market, keep 15% moon bag | Moon bag remains; rebuild grid if trend bullish |
| Gap through trailing stop | Price gaps below stop (no ticks at stop level) | Execute at first available price, accept slippage | Log slippage event, adjust future stop calculations |
| Partial fill on stop exit | Stop order partially filled | Complete remaining fill as aggressive limit order (-0.5% from current) | If still unfilled after 2 min, convert to market |
| Stop triggered during low liquidity | Stop triggers AND liquidity_status == LOW/CRITICAL | Reduce sell size to 50% of planned, use limit orders | Sell remaining 35% over next hour via TWAP |

```
Rule EC-STOP-001: Confirmation Before Execution
IF trailing_stop_triggered
THEN wait_for_confirmation(3_consecutive_ticks OR 10_seconds, whichever first)
THEN execute_stop
Rationale: Prevents false triggers from single bad ticks

Rule EC-STOP-002: Moon Bag Preservation on Stop
IF trailing_stop_executing
THEN max_sell_quantity = position_size - moon_bag_locked_quantity
AND protect_moon_bag = TRUE
Rationale: Never sell moon bag via trailing stop automation

Rule EC-STOP-003: Stop Order Type Selection
IF liquidity_status == NORMAL AND spread < 0.3%
THEN use_market_order_for_stop
ELSE use_aggressive_limit_order(slippage_tolerance = 1%)
```

### 3.3 Position Size Edge Cases

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| Position drops below moon bag via partial fills | `position_size < moon_bag_threshold` from normal grid fills | Convert to HOLD mode immediately, block all sells | Resume grid if position increased above threshold |
| Moon bag becomes entire position | All non-moon-bag portion sold (via stop or manual) | Convert to HOLD mode, cease active trading | Resume if: manual override OR position increased OR trend turns bullish |
| Position increases while in moon bag mode | New buys executed (manual or grid recovery) | Recalculate moon bag threshold based on new max position | Update locked quantity, exit HOLD mode if position > threshold |
| Insufficient balance for moon bag | Portfolio value drops, moon bag worth < minimum ($50) | Disable moon bag protection, alert operator | Re-enable if position value increases above minimum |

```
Rule EC-POS-001: Position Decrease During Grid Trading
IF position_decreases_via_grid_fill
AND new_position_size <= moon_bag_threshold
THEN immediately_enter_hold_mode
AND cancel_all_pending_sell_orders
AND log_event("Moon bag protection activated via grid fill")
Priority: HIGH

Rule EC-POS-002: Maximum Position Size Tracking
Track max_position_size_achieved across:
- Grid trading fills
- Manual position increases
- Rebalancing operations
Update: max_position_size_achieved = MAX(max_position_size_achieved, current_position_size)
Reset: Only on manual reset OR new trading cycle start

Rule EC-POS-003: Position Size Increase in Moon Bag Mode
IF moon_bag_mode == ACTIVE
AND position_increased (buy executed)
THEN new_max_position = MAX(max_position_size_achieved, current_position)
AND new_moon_bag_threshold = new_max_position * 0.15
AND IF current_position > new_moon_bag_threshold
    THEN exit_hold_mode, resume_grid_with_sell_orders
```

### 3.4 Warm-Up Period Edge Cases

```
Rule EC-WARMUP-001: Position Entry Tracking
Track position_opened_at timestamp when:
- New position opened from zero
- Position direction changes (long to short or vice versa)
- Position size increases by > 50% in single transaction

Rule EC-WARMUP-002: Early Activation Override
IF warm_up_period_active
AND price_move_in_favor > 5%
THEN activate_moon_bag_immediately
Rationale: Capture early momentum in fast-moving markets

Rule EC-WARMUP-003: Warm-Up Expiry
IF warm_up_period >= 30_minutes
THEN auto_activate_moon_bag_protection
AND begin_high_watermark_tracking
```

---

## Section 4: Inventory Skew Integration

### 4.1 Moon Bag vs Inventory Skew Interaction

```
Rule INV-MB-001: Moon Bag Priority Over Skew
IF moon_bag_mode == ACTIVE
THEN inventory_rebalancing_sells = BLOCKED
AND target_skew_calculation = SUSPENDED for sell side
Priority: HIGH > MEDIUM (moon bag overrides rebalancing)

Rule INV-MB-002: Skew Calculation with Moon Bag
IF calculating_current_skew
AND moon_bag_mode == ACTIVE
THEN available_for_sale = position_size - moon_bag_locked_quantity
AND effective_crypto_allocation = available_for_sale_value / portfolio_value
Rationale: Skew calculations should reflect actually tradeable inventory

Rule INV-MB-003: Emergency Rebalance Exception
IF emergency_rebalance_triggered (delta > 30%)
AND trend_state == STRONG_BEAR
AND moon_bag_release_conditions_met
THEN allow_moon_bag_participation_in_emergency_rebalance
Else: moon_bag_protected even during emergency

Rule INV-MB-004: Buy-Side Skew Unaffected
Moon bag mode does NOT affect:
- Buy order placement
- Buy-side rebalancing
- Position size increases
Rationale: Moon bag only protects against selling, not against accumulation
```

### 4.2 Trend State Interactions

| Trend State | Moon Bag Behavior | Inventory Adjustment |
|-------------|-------------------|----------------------|
| STRONG_BULL | Active protection, trailing stop engaged | Allow buys, block automated sells of moon bag |
| MILD_BULL | Active protection, monitoring | Allow buys, block automated sells of moon bag |
| NEUTRAL | Active protection, reduced trailing sensitivity | Normal grid, moon bag protected |
| MILD_BEAR | Active protection, alert operator | Reduce non-moon-bag position per skew target |
| STRONG_BEAR | Release conditions checkable, operator decision | If release approved, gradual exit; else hold |

```
Rule INV-MB-005: Trend Transition Handling
IF trend_state transitions TO STRONG_BEAR
AND moon_bag_mode == ACTIVE
THEN alert_operator("Moon bag release conditions may be met")
AND do NOT auto-sell (require manual confirmation)
```

---

## Section 5: Perpetual Futures Specific Considerations (Lighter DEX)

### 5.1 Long vs Short Position Handling

```
Rule PF-001: Moon Bag for Long Positions Only (Default)
IF position_direction == LONG
THEN moon_bag_protection = AVAILABLE
ELSE moon_bag_protection = DISABLED
Rationale: Moon bag concept assumes holding for upside; shorts have inverse exposure

Rule PF-001a: Optional Short Moon Bag (Inverse)
IF operator_enabled_short_moon_bag == TRUE
AND position_direction == SHORT
THEN apply_inverse_moon_bag_logic:
    - Protect 15% of short position from covering
    - Trailing stop activates BELOW initial grid
    - Grid shifts DOWN on price decline
Rationale: Allows "moon bag" behavior for bearish conviction plays
```

### 5.2 Leverage Considerations

```
Rule PF-LEV-001: Moon Bag Leverage Cap
IF moon_bag_mode == ACTIVE
THEN max_leverage_allowed = MIN(3x, configured_max_leverage)
Rationale: Reduced leverage for positions we're protecting long-term

Rule PF-LEV-002: Liquidation Distance Monitoring
IF moon_bag_mode == ACTIVE
THEN continuously_monitor_liquidation_price
AND IF (current_price - liquidation_price) / current_price < 20%
    THEN alert_operator("Moon bag at liquidation risk")
    AND consider_adding_margin OR reducing_non_moon_bag_position

Rule PF-LEV-003: Margin Requirement Priority
IF margin_call_warning
AND moon_bag_mode == ACTIVE
THEN add_margin_first (if available)
ELSE reduce_non_moon_bag_position
ELSE (last resort) reduce_moon_bag_to_prevent_liquidation
Priority: Capital preservation > moon bag protection
```

### 5.3 Funding Rate Impact

```
Rule PF-FUND-001: Funding Rate Monitoring for Moon Bag
IF funding_rate > 0.1% (8-hour)
AND moon_bag_mode == ACTIVE
AND position_direction == LONG
THEN alert_operator("High funding eroding moon bag value")
AND calculate_daily_funding_cost

Rule PF-FUND-002: Funding Rate Break-Even Analysis
Calculate: days_until_funding_erodes_profit = unrealized_profit / daily_funding_cost
IF days_until_funding_erodes_profit < 14
THEN alert_operator("Funding may consume moon bag profits within 2 weeks")

Rule PF-FUND-003: Extreme Funding Response
IF funding_rate > 0.3% (8-hour) for > 24 hours
THEN consider_tightening_trailing_stop by additional 5%
Rationale: High funding indicates crowded trade, potential for reversal
```

### 5.4 Order Types for Lighter DEX

```
Rule PF-ORD-001: Trailing Stop Implementation
Lighter DEX does not have native trailing stops
Implementation: Software-managed stop-loss orders
- Update stop-limit order price every time high watermark increases
- Use reduce-only flag for stop orders
- Cancel and replace (not modify) for order updates

Rule PF-ORD-002: Stop Order Parameters
For moon bag trailing stop orders:
- Order type: LIMIT (not market) with aggressive price
- Post-only: FALSE (we need guaranteed execution)
- Reduce-only: TRUE (prevent accidental position increase)
- Time-in-force: GTC (good till cancelled)
- Slippage tolerance: 1% below stop price

Rule PF-ORD-003: Order Update Frequency
Update trailing stop order:
- When high watermark increases by >= 0.5%
- When trailing distance tightens (profit milestones)
- Minimum interval: 30 seconds between updates
Rationale: Balance responsiveness with API rate limits
```

---

## Section 6: Risk Rules During Active Trailing Stop

### 6.1 Trading Restrictions with Active Trailing Stop

```
Rule TS-ACTIVE-001: Order Placement Restrictions
WHILE trailing_stop_active:
- BUY orders: ALLOWED (but reduce size by 50%)
- SELL orders (grid): BLOCKED for quantities that would breach moon bag
- SELL orders (rebalance): BLOCKED unless emergency
- SELL orders (manual): ALLOWED with confirmation

Rule TS-ACTIVE-002: Grid Modification Restrictions
WHILE trailing_stop_active:
- Grid shift UP: ALLOWED (follows price)
- Grid shift DOWN: BLOCKED (trailing stop handles downside)
- Grid rebuild: BLOCKED (maintain current structure)
- Grid spacing changes: ALLOWED (volatility-based)

Rule TS-ACTIVE-003: Risk Check Integration
WHILE trailing_stop_active:
- Flash crash detection: ACTIVE (may override trailing stop)
- Loss limit monitoring: ACTIVE (portfolio-level)
- Liquidity monitoring: ACTIVE (affects execution method)
```

### 6.2 Trailing Stop vs Flash Crash Interaction

```
Rule TS-FC-001: Flash Crash Override
IF flash_crash_detected (5-min drop > 5%)
AND trailing_stop_active
THEN pause_trailing_stop_execution(15_minutes)
AND let_flash_crash_protocol_handle
Rationale: Flash crash response is more conservative; let it stabilize first

Rule TS-FC-002: Post-Flash-Crash Trailing Stop
IF flash_crash_recovery_complete
AND trailing_stop_was_triggered_during_crash
THEN re-evaluate_stop_level using current high watermark
AND resume_trailing_stop if still below watermark
Rationale: Crash may have been temporary; don't force exit

Rule TS-FC-003: Cascading Event Priority
Priority order during simultaneous events:
1. CRITICAL loss limit (halt everything)
2. Severe flash crash (15-min > 10%)
3. Trailing stop trigger
4. Moderate flash crash (5-min > 5%)
5. Normal grid operations
```

---

## Section 7: State Machine Definition

### 7.1 Moon Bag States

```
States:
- INACTIVE: Moon bag protection not engaged
- WARMING_UP: Position opened, waiting for warm-up period
- TRACKING: Active tracking, high watermark updating
- TRAILING: Trailing stop active, protection engaged
- TRIGGERED: Trailing stop triggered, executing exit
- HOLD_MODE: Only moon bag remains, no automated trading
- RELEASED: Moon bag released, normal trading resumed

Transitions:
INACTIVE -> WARMING_UP: position_opened
WARMING_UP -> TRACKING: warm_up_complete OR early_activation_trigger
TRACKING -> TRAILING: price > initial_grid * 1.10
TRAILING -> TRIGGERED: price <= trailing_stop_price (confirmed)
TRIGGERED -> HOLD_MODE: non_moon_bag_sold
HOLD_MODE -> TRACKING: position_increased above threshold
HOLD_MODE -> RELEASED: release_conditions_met AND operator_approved
RELEASED -> INACTIVE: position_closed OR new_cycle_started
```

### 7.2 State Persistence Requirements

```
Persisted Data (survive restart):
- Current moon bag state
- Max position size achieved
- High watermark price
- Trailing stop price
- Warm-up start timestamp
- Position open timestamp
- Moon bag locked quantity
- Release approval flag

Calculated on Restart:
- Current position size (from exchange)
- Current price (from market data)
- Trailing stop trigger status (recalculate)
- Grid state (from grid service)
```

---

## Section 8: Configuration Parameters

### 8.1 MoonBagOptions Configuration

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| MoonBagPercentage | 15% | 10-25% | Percentage of max position to protect |
| TrailingGridStep | 2% | 1-5% | Price movement to trigger grid shift |
| MaxTrailDistance | 20% | 15-30% | Maximum distance for trailing stop |
| InitialTrailingStop | 15% | 10-20% | Initial trailing stop distance |
| TightenedStopPercent | 10% | 7-15% | Tightened stop at 50%+ profit |
| AggressiveStopPercent | 7% | 5-10% | Aggressive stop at 100%+ profit |
| EmergencyStopPercent | 5% | 3-7% | Emergency stop at 200%+ profit |
| TightenAtProfitPercent50 | 50% | 30-70% | Profit level for first tightening |
| TightenAtProfitPercent100 | 100% | 80-150% | Profit level for aggressive tightening |
| FlashSpikeThreshold | 20% | 15-30% | Price spike to pause grid shift |
| FlashSpikeCooldownMinutes | 10 | 5-30 | Cooldown after flash spike |
| WarmUpPeriodMinutes | 30 | 15-60 | Time before moon bag activates |
| MinimumMoonBagUsd | 50 | 20-200 | Minimum USD value for moon bag |
| ShiftCooldownSeconds | 60 | 30-300 | Minimum time between grid shifts |
| MaxShiftPercent | 10% | 5-20% | Maximum single grid shift |
| MaxCumulativeShift1h | 20% | 10-50% | Maximum cumulative shift per hour |
| EnableShortMoonBag | false | true/false | Enable moon bag for short positions |

---

## Section 9: Alerting and Logging

### 9.1 Alert Triggers

| Event | Severity | Alert Message |
|-------|----------|---------------|
| Moon bag activated | MEDIUM | "Moon bag protection activated at {quantity} {asset}" |
| Trailing stop activated | MEDIUM | "Trailing stop activated at {stop_price} ({distance}% below high)" |
| Trailing stop tightened | LOW | "Trailing stop tightened to {new_distance}% (profit: {profit}%)" |
| Trailing stop triggered | HIGH | "Trailing stop triggered - selling {quantity} (preserving moon bag)" |
| Moon bag release conditions met | HIGH | "Moon bag release conditions met - operator approval required" |
| Hold mode entered | MEDIUM | "Position reduced to moon bag only - entering HOLD mode" |
| Flash spike detected | MEDIUM | "Flash spike detected ({change}% in 5 min) - pausing grid shift" |
| High watermark updated | LOW | "New high watermark: {price} (previous: {old_price})" |
| Moon bag at liquidation risk | CRITICAL | "Moon bag position within 20% of liquidation price" |

### 9.2 Logging Requirements

```
Log every:
- State transition with timestamp and reason
- High watermark update with old and new values
- Trailing stop price update with calculation details
- Grid shift with shift amount and new bounds
- Order placement/cancellation for moon bag related orders
- Release condition checks (even if not met)
- Operator overrides with operator identifier
```

---

## Section 10: Summary of Answers to User Questions

### Q1: Additional edge cases for moon bag protection?
**Answer**: Added:
- Flash spike reversal (discard spike from high watermark)
- Wick filtering (prevent manipulation via thin wicks)
- Partial fill on stop exit scenarios
- Low liquidity stop execution handling
- Position decrease via normal grid trading triggering hold mode
- Warm-up period early activation override
- Multiple profit level tightening (50%, 100%, 200%)

### Q2: Position decreases below moon bag via partial fills?
**Answer**: Rule EC-POS-001 addresses this:
- Immediately enter HOLD mode
- Cancel all pending sell orders
- Log the event for auditing
- Recovery: Resume grid if position increases above threshold

### Q3: Warm-up period before moon bag activates?
**Answer**: Yes, implemented:
- Default 30-minute warm-up period (configurable)
- Early activation if price moves >5% in favor
- Tracks position_opened_at timestamp
- Resets on position direction change or 50%+ size increase

### Q4: Increasing position while in moon bag mode?
**Answer**: Rule EC-POS-003 handles this:
- Recalculate moon bag threshold based on new max position
- Update locked quantity accordingly
- Exit HOLD mode if position exceeds new threshold
- Resume grid trading with sell orders enabled

### Q5: Perpetual futures vs spot considerations?
**Answer**: Section 5 covers:
- Long positions only by default (optional short moon bag)
- Leverage capped to 3x in moon bag mode
- Liquidation distance monitoring with alerts
- Funding rate impact analysis and alerts
- Software-managed trailing stops (Lighter has no native trailing stops)
- Reduce-only orders for stop execution

### Q6: Risk rules while trailing stop active but not triggered?
**Answer**: Section 6 provides:
- Buy orders allowed but at 50% size
- Sell orders blocked that would breach moon bag
- Grid shift UP allowed, DOWN blocked
- Flash crash override protocol
- Loss limit and liquidity monitoring remain active

### Q7: Interaction with inventory skew system?
**Answer**: Section 4 details:
- Moon bag has priority over inventory rebalancing sells
- Skew calculations adjusted to reflect tradeable inventory
- Emergency rebalance exception (with release conditions)
- Buy-side operations unaffected by moon bag mode
- Trend state specific behaviors defined

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | trading-risk-manager | Initial Phase 6 risk specification |

---

## Implementation Notes

1. **State Machine First**: Implement the state machine (Section 7) before individual rules
2. **Persistence Critical**: High watermark and locked quantity must survive restarts
3. **Lighter DEX Specifics**: No native trailing stops - implement in software with order replacement
4. **Testing Priority**: Test flash spike handling and partial fill scenarios thoroughly
5. **Operator UX**: Provide clear dashboard visibility into moon bag state and release conditions
6. **Log Everything**: Moon bag decisions affect long-term P&L; audit trail essential
