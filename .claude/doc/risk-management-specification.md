# ALTE Risk Management Specification

## Document Purpose
This document defines comprehensive risk management rules, thresholds, and edge case handling for the ALTE (Adaptive Liquidity & Trend Engine) trading bot operating on Lighter DEX perpetual futures.

---

## Section 1: Global Risk Parameters

### 1.1 Capital Allocation Limits

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Maximum Portfolio Allocation per Market | 25% | Prevents single-market concentration risk |
| Maximum Total Deployed Capital | 80% | Maintains 20% reserve for margin calls and opportunities |
| Minimum Reserve Balance | 20% of total equity | Emergency buffer for liquidation prevention |
| Maximum Leverage (per position) | 5x | Conservative for automated systems; reduces liquidation risk |
| Maximum Aggregate Leverage | 3x | Portfolio-level leverage constraint |

### 1.2 Hard Stop Limits (Non-Negotiable Circuit Breakers)

| Metric | Threshold | Action |
|--------|-----------|--------|
| Daily Loss Limit | -5% of portfolio equity | Halt ALL trading for 24 hours |
| Weekly Loss Limit | -10% of portfolio equity | Halt ALL trading for 7 days |
| Monthly Loss Limit | -15% of portfolio equity | Halt ALL trading, require manual restart |
| Maximum Drawdown from ATH | -20% | Reduce all position sizes by 75%, alert operator |
| Single Trade Loss | -2% of portfolio equity | Cancel all pending orders in that market |

### 1.3 Position Size Limits

| Parameter | Value | Formula |
|-----------|-------|---------|
| Maximum Single Position Size | 10% of portfolio | `position_value <= portfolio_equity * 0.10` |
| Maximum Position per Market | 15% of portfolio | Accounts for multiple orders same direction |
| Minimum Order Size | 0.1% of portfolio OR $10 USD | Whichever is greater |
| Maximum Order Size | 5% of portfolio | Per individual order |

---

## Section 2: Dynamic Grid Geometry (Market Maker) Risk Rules

### 2.1 ATR-Based Grid Spacing Thresholds

| ATR Percentile | Grid Spacing | Orders per Side | Rationale |
|----------------|--------------|-----------------|-----------|
| ATR < 0.5% (14-period) | 0.2% | 10 | Low volatility - tight grid for frequent fills |
| 0.5% <= ATR < 1.0% | 0.5% | 8 | Normal volatility |
| 1.0% <= ATR < 2.0% | 1.0% | 6 | Elevated volatility |
| 2.0% <= ATR < 3.0% | 1.5% | 5 | High volatility |
| ATR >= 3.0% | 2.0% | 4 | Extreme volatility - wide grid, fewer orders |

### 2.2 Grid Spacing Rules

```
Rule GG-001: Grid Spacing Floor
IF grid_spacing < 0.15%
THEN set grid_spacing = 0.15%
Rationale: Minimum spacing must exceed 2x trading fees to ensure profitability

Rule GG-002: Grid Spacing Ceiling
IF grid_spacing > 3.0%
THEN set grid_spacing = 3.0%
Rationale: Prevents grid from becoming too sparse during extreme volatility

Rule GG-003: ATR Smoothing
IF ATR_change_rate > 50% in 1 hour
THEN use EMA(ATR, 6) instead of raw ATR
Rationale: Prevents whipsaw grid adjustments during sudden volatility spikes

Rule GG-004: Minimum Grid Width
IF total_grid_width < 5%
THEN expand grid to minimum 5% width
Rationale: Ensures adequate range coverage for mean reversion

Rule GG-005: Maximum Grid Width
IF total_grid_width > 30%
THEN contract grid to maximum 30% width
Rationale: Prevents capital from being spread too thin
```

### 2.3 Order Book Awareness Rules

```
Rule OB-001: Liquidity Cluster Detection
IF order_book_depth at price_level > 2x average_depth
THEN bias grid level toward that price (within 0.3% tolerance)
Rationale: Place orders at high-probability support/resistance levels

Rule OB-002: Thin Order Book Warning
IF total_bid_depth < $50,000 OR total_ask_depth < $50,000
THEN widen grid spacing by 50% AND reduce order sizes by 50%
Rationale: Thin books indicate low liquidity and higher slippage risk

Rule OB-003: Order Book Imbalance
IF (bid_depth / ask_depth) > 3.0 OR (ask_depth / bid_depth) > 3.0
THEN flag potential large move, increase grid spacing by 25%
Rationale: Severe imbalance often precedes directional moves

Rule OB-004: Spread Monitoring
IF bid_ask_spread > 0.5%
THEN pause new market-making orders, wait for spread normalization
Rationale: Wide spreads indicate stress or manipulation
```

### 2.4 Edge Cases - Dynamic Grid

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| ATR calculation failure | No valid ATR for 5 minutes | Use last valid ATR value, set grid to 1.0% spacing | Resume normal ATR calculation when data available |
| Price gap > 5% | Price moves > 5% between ticks | Cancel all grid orders, wait 5 minutes, rebuild grid at current price | Rebuild grid after 5-minute stability period |
| Rapid ATR oscillation | ATR changes direction 3+ times in 1 hour | Lock ATR at median of last 6 readings for 2 hours | Unlock after 2-hour stability period |
| Order book data stale | Order book age > 10 seconds | Widen spreads by 100%, reduce position sizes by 75% | Resume normal when fresh data received |

---

## Section 3: Smart Inventory Management (Trend Follower) Risk Rules

### 3.1 Trend State Definitions

| State | Conditions | Target Skew (Crypto/USDT) |
|-------|------------|---------------------------|
| STRONG_BULL | EMA(20) > EMA(50) AND MACD > Signal AND MACD > 0 AND ADX > 25 | 80/20 |
| MILD_BULL | EMA(20) > EMA(50) AND (MACD > Signal OR MACD > 0) | 70/30 |
| NEUTRAL | EMA(20) within 1% of EMA(50) OR ADX < 20 | 50/50 |
| MILD_BEAR | EMA(20) < EMA(50) AND (MACD < Signal OR MACD < 0) | 30/70 |
| STRONG_BEAR | EMA(20) < EMA(50) AND MACD < Signal AND MACD < 0 AND ADX > 25 | 20/80 |

### 3.2 Inventory Management Rules

```
Rule IM-001: Maximum Rebalance Rate
IF inventory_delta > target_delta
THEN rebalance at maximum 10% of portfolio per hour
Rationale: Prevents large market impact from rapid rebalancing

Rule IM-002: Trend Confirmation Delay
IF trend_state changes
THEN wait 15 minutes before adjusting target skew
Rationale: Prevents whipsaw from temporary trend signals

Rule IM-003: Rebalance Threshold
IF |current_skew - target_skew| < 5%
THEN do NOT rebalance (within tolerance)
Rationale: Avoids excessive trading fees for minor deviations

Rule IM-004: Emergency Rebalance
IF |current_skew - target_skew| > 30%
THEN force immediate rebalance to within 15% of target
Rationale: Large deviations indicate system drift or market dislocation

Rule IM-005: Trend Flip Cooldown
IF trend_state flips from BULL to BEAR (or vice versa) within 1 hour
THEN maintain NEUTRAL skew for 2 hours
Rationale: Prevents overtrading during choppy conditions

Rule IM-006: Maximum Inventory Skew
IF crypto_allocation > 90% OR usdt_allocation > 90%
THEN halt trading, alert operator
Rationale: Extreme positions indicate system malfunction or extreme market
```

### 3.3 Edge Cases - Inventory Management

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| Indicator calculation failure | MA/MACD returns null/NaN | Hold current trend state, set to NEUTRAL after 30 minutes | Resume when valid indicator data available |
| Conflicting indicators | EMA bullish but MACD bearish for > 2 hours | Use NEUTRAL skew (50/50) | Resume when indicators align |
| Rapid trend flips | Trend state changes > 4 times in 24 hours | Lock to NEUTRAL for 24 hours, alert operator | Manual override required to resume trend-following |
| Insufficient historical data | < 50 candles available for indicator calculation | Use NEUTRAL skew, widen grid by 50% | Resume when sufficient data accumulated |

---

## Section 4: Infinite Upside Module (Moon Bag) Risk Rules

### 4.1 Moon Bag Thresholds

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Moon Bag Percentage | 15% of max position | Reserved portion never sold automatically |
| Moon Bag Trigger | Price exits top of grid | Activates trailing grid mechanism |
| Trailing Grid Step | 2% price movement | Grid shifts up after 2% price increase |
| Maximum Trail Distance | 20% below current price | Prevents trailing stop from being too distant |

### 4.2 Trailing Grid Rules

```
Rule TG-001: Grid Shift Trigger
IF current_price > grid_upper_bound
THEN shift entire grid up by (current_price - grid_upper_bound)
Rationale: Follows price action upward during bull runs

Rule TG-002: Moon Bag Lock
IF position_size <= moon_bag_threshold (15%)
THEN block ALL sell orders for this position
Rationale: Preserves upside exposure during parabolic moves

Rule TG-003: Trailing Activation
IF price has moved > 10% above initial_grid_upper_bound
THEN activate trailing stop at 15% below current high
Rationale: Protects profits while allowing continued upside

Rule TG-004: Trail Tightening
IF unrealized_profit > 50%
THEN tighten trailing stop to 10% below current high
Rationale: Lock in larger profits as position becomes more profitable

Rule TG-005: Moon Bag Release Condition
IF trend_state == STRONG_BEAR AND price < 200_day_MA
THEN allow moon bag to be sold at operator discretion (manual flag)
Rationale: Provides exit mechanism during confirmed bear markets
```

### 4.3 Edge Cases - Moon Bag

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| Flash spike > 20% | Price increases > 20% in < 5 minutes | Pause grid shift, wait for 10-minute stabilization | Resume trailing after price stable for 10 minutes |
| Trailing stop triggered | Price drops to trailing stop level | Sell only NON-moon-bag portion (85% of position) | Moon bag remains; rebuild grid if trend turns bullish |
| Gap through trailing stop | Price gaps below trailing stop | Execute at first available price, accept slippage | Log slippage event, continue with remaining moon bag |
| Moon bag becomes entire position | Sells executed until only moon bag remains | Convert to HOLD mode, cease active trading | Resume if position increased or manual override |

---

## Section 5: Sentinel Risk Protection (Circuit Breakers)

### 5.1 Flash Crash Protection

| Metric | Threshold | Action | Duration |
|--------|-----------|--------|----------|
| 1-minute price drop | > 3% | Pause all BUY orders | 5 minutes |
| 5-minute price drop | > 5% | Pause ALL orders | 15 minutes |
| 15-minute price drop | > 10% | Cancel all orders, close 50% of long positions | 1 hour |
| 1-hour price drop | > 15% | Full trading halt, close all positions to 50% | 4 hours + manual review |

### 5.2 Flash Crash Rules

```
Rule FC-001: Rapid Decline Detection
IF price_change(1_minute) < -3%
THEN pause_buying = true for 5 minutes
Rationale: Prevents catching falling knife

Rule FC-002: Cascading Crash
IF price_change(5_minute) < -5% AND price_change(1_minute) < -2%
THEN cancel_all_orders() AND pause_all_trading(15_minutes)
Rationale: Cascading sells indicate panic; stay out

Rule FC-003: Recovery Confirmation
IF flash_crash_triggered AND price_stabilized_for(10_minutes, tolerance=1%)
THEN allow_gradual_resume(25%_capacity_per_15_minutes)
Rationale: Gradual re-entry prevents catching dead cat bounce

Rule FC-004: Consecutive Crash Events
IF flash_crash_triggered > 2 times in 24 hours
THEN halt_trading(24_hours) AND alert_operator
Rationale: Multiple crashes indicate unstable conditions
```

### 5.3 Liquidity/Volume Monitoring

| Metric | Warning Threshold | Critical Threshold | Action |
|--------|-------------------|-------------------|--------|
| 24h Volume | < 50% of 7-day average | < 25% of 7-day average | Warning: Widen spreads 25% / Critical: Widen spreads 100%, reduce sizes 50% |
| Order book depth | < $100K total | < $50K total | Warning: Alert / Critical: Halt trading |
| Funding rate | > 0.1% per 8h | > 0.3% per 8h | Warning: Reduce position 25% / Critical: Close position |

### 5.4 Liquidity Rules

```
Rule LQ-001: Volume Drought
IF volume_24h < (average_volume_7d * 0.5)
THEN widen_grid_spacing(25%) AND reduce_order_sizes(25%)
Rationale: Low volume = low liquidity = higher slippage risk

Rule LQ-002: Dead Market Detection
IF volume_24h < (average_volume_7d * 0.25) for > 4 hours
THEN halt_trading() AND alert_operator("Low liquidity warning")
Rationale: Extremely low volume indicates potential market issues

Rule LQ-003: Depth Evaporation
IF order_book_total_depth decreases > 50% in 5 minutes
THEN immediately_widen_spreads(100%) AND cancel_orders_near_market
Rationale: Sudden depth removal often precedes large moves

Rule LQ-004: Funding Rate Warning
IF abs(funding_rate) > 0.1%
THEN reduce_position_size_by(funding_rate * 100)%
Rationale: High funding rates indicate crowded positioning
```

### 5.5 API/Connectivity Failure Handling

| Failure Type | Detection | Response | Recovery |
|--------------|-----------|----------|----------|
| API timeout | No response > 10 seconds | Retry 3 times with exponential backoff | Log failure, continue with cached data |
| Consecutive API failures | > 5 failures in 5 minutes | Halt new orders, maintain existing positions | Resume when API stable for 2 minutes |
| WebSocket disconnect | Connection lost | Immediate reconnect attempt, hold all orders | Resume when reconnected |
| Rate limit hit | 429 response | Exponential backoff starting 1 second | Resume when rate limit window resets |
| Authentication failure | 401/403 response | Halt all operations, alert operator | Requires manual intervention |

### 5.6 Edge Cases - Sentinel Protection

| Scenario | Detection | Response | Recovery |
|----------|-----------|----------|----------|
| Exchange maintenance | Scheduled or detected downtime | Cancel all orders 5 minutes before, hold positions | Rebuild orders 5 minutes after confirmed uptime |
| Price feed divergence | Price differs > 2% from external oracle | Use average of available sources, widen spreads 50% | Resume when feeds converge within 0.5% |
| Nonce desynchronization | Transaction rejected for nonce | Fetch fresh nonce, retry with new nonce | Continue normal operation after sync |
| Partial fill timeout | Order partially filled, no activity for 5 minutes | Cancel remaining order, adjust inventory accounting | Place new order if needed based on current state |

---

## Section 6: Lighter DEX Specific Parameters

### 6.1 Recommended Settings for Lighter

| Parameter | Recommended Value | Notes |
|-----------|-------------------|-------|
| Order Expiry | 28 days (-1 flag) | Use default long expiry for grid orders |
| Post-Only Flag | true | Ensures maker fees (typically lower/rebates) |
| Reduce-Only for Closes | true | Prevents accidental position increase when closing |
| Leverage Mode | Isolated | Prevents cross-margin liquidation cascade |
| Tick Size Compliance | Always round to market tick size | Lighter rejects non-compliant prices |

### 6.2 Lighter-Specific Rules

```
Rule LD-001: USDC Scaling
ALWAYS multiply USDC amounts by 1,000,000 (USDC_TICKER_SCALE)
Rationale: Lighter uses 6-decimal precision for USDC

Rule LD-002: Nonce Management
BEFORE each order batch: fetch_fresh_nonce()
AFTER failed transaction: resync_nonce()
Rationale: Lighter requires sequential nonces

Rule LD-003: Order Batching
IF placing multiple orders
THEN batch into single transaction (up to 10 orders)
Rationale: Reduces gas costs and API calls

Rule LD-004: Market ID Validation
BEFORE trading: validate market_id exists and is active
Rationale: Prevent orders to inactive/delisted markets

Rule LD-005: Leverage Verification
AFTER setting leverage: verify_leverage_applied()
IF leverage != requested
THEN alert and halt trading for that market
Rationale: Incorrect leverage changes liquidation price
```

### 6.3 Lighter Fee Considerations

| Fee Type | Typical Rate | Impact on Strategy |
|----------|--------------|-------------------|
| Maker Fee | 0.02% (may have rebates) | Prefer limit orders, use post-only |
| Taker Fee | 0.05% | Avoid market orders except emergencies |
| Funding Rate | Variable (every 8h) | Factor into position holding cost |
| Gas Fees | Variable | Batch orders to reduce per-order cost |

---

## Section 7: Rule Priority and Conflict Resolution

### 7.1 Priority Hierarchy (Highest to Lowest)

1. **CRITICAL (Priority 1)**: Capital preservation rules - NEVER override
   - Daily/Weekly/Monthly loss limits
   - Maximum drawdown circuit breaker
   - Flash crash protection
   - Liquidation prevention

2. **HIGH (Priority 2)**: Position safety rules - Override only with manual approval
   - Maximum position sizes
   - Maximum leverage
   - Liquidity checks

3. **MEDIUM (Priority 3)**: Operational rules - Can be temporarily adjusted
   - Inventory skew targets
   - Grid spacing adjustments
   - Rebalancing rates

4. **LOW (Priority 4)**: Optimization rules - Flexible based on conditions
   - Order book awareness biasing
   - Trailing grid adjustments
   - Fee optimization

### 7.2 Conflict Resolution Rules

```
Rule CR-001: Loss Limit vs Trend Following
IF loss_limit_triggered AND trend_state == STRONG_BULL
THEN loss_limit WINS - halt trading regardless of trend
Priority: CRITICAL > MEDIUM

Rule CR-002: Moon Bag vs Flash Crash
IF flash_crash_triggered AND position == moon_bag_only
THEN moon_bag protected UNLESS 15-minute drop > 10%
THEN allow moon bag sale to preserve capital
Priority: CRITICAL overrides LOW during extreme events

Rule CR-003: Rebalancing vs Low Liquidity
IF rebalance_needed AND liquidity_warning_active
THEN reduce_rebalance_rate_by(50%), extend_rebalance_period
Priority: HIGH > MEDIUM

Rule CR-004: Grid Adjustment vs API Failure
IF grid_needs_update AND api_unreliable
THEN maintain_current_grid, do NOT cancel existing orders
Priority: System stability > optimization
```

---

## Section 8: Monitoring and Alerting

### 8.1 Alert Severity Levels

| Level | Response Time | Notification Method | Examples |
|-------|---------------|---------------------|----------|
| CRITICAL | Immediate (auto-halt) | All channels + auto-action | Loss limit hit, flash crash, API auth failure |
| HIGH | < 5 minutes | Push notification + email | Liquidity warning, leverage change failed |
| MEDIUM | < 1 hour | Email | Trend state change, large rebalance needed |
| LOW | Daily digest | Dashboard only | Grid adjustments, order fills, performance metrics |

### 8.2 Health Check Intervals

| Check | Interval | Failure Action |
|-------|----------|----------------|
| API connectivity | 10 seconds | Alert after 3 consecutive failures |
| Price feed freshness | 5 seconds | Widen spreads after 30 seconds stale |
| Position reconciliation | 1 minute | Alert on any discrepancy |
| Risk limit check | 30 seconds | Immediate action if breached |
| Indicator calculation | 1 minute | Use cached values, alert after 5 minutes |

---

## Section 9: Recovery Procedures

### 9.1 Post-Circuit-Breaker Recovery

```
Recovery Procedure RP-001: After Daily Loss Limit
1. Wait full 24-hour cooling period
2. Analyze trades that led to loss
3. Verify all risk parameters are at default
4. Reduce position sizes to 50% of normal for first 4 hours
5. Gradually increase to 75% after 4 hours of profitable/neutral trading
6. Resume 100% after 24 hours of stable operation

Recovery Procedure RP-002: After Flash Crash
1. Wait for price stabilization (10 minutes, < 1% volatility)
2. Verify order book has rebuilt (depth > $100K)
3. Resume at 25% capacity
4. Increase 25% every 15 minutes if stable
5. Full capacity after 1 hour of stable trading

Recovery Procedure RP-003: After API Failure
1. Verify API is responding consistently (5 successful calls)
2. Resync nonce
3. Reconcile positions with exchange
4. Cancel any stale orders
5. Rebuild grid at current price
6. Resume normal operation
```

### 9.2 Manual Override Conditions

The following require explicit operator approval:
- Selling moon bag position
- Exceeding maximum leverage
- Trading during liquidity warning
- Resuming after monthly loss limit
- Overriding trend state manually

---

## Section 10: Configuration Summary Table

| Category | Parameter | Default Value | Range | Unit |
|----------|-----------|---------------|-------|------|
| Capital | Max Position Size | 10% | 5-20% | % of portfolio |
| Capital | Reserve Balance | 20% | 15-30% | % of portfolio |
| Capital | Max Leverage | 5x | 1-10x | multiplier |
| Loss Limits | Daily Loss | -5% | -3% to -10% | % of portfolio |
| Loss Limits | Weekly Loss | -10% | -5% to -15% | % of portfolio |
| Loss Limits | Monthly Loss | -15% | -10% to -25% | % of portfolio |
| Loss Limits | Max Drawdown | -20% | -15% to -30% | % from ATH |
| Grid | Min Spacing | 0.15% | 0.1-0.3% | % price |
| Grid | Max Spacing | 3.0% | 2-5% | % price |
| Grid | Orders per Side | 4-10 | 3-15 | count |
| Trend | EMA Fast Period | 20 | 10-30 | candles |
| Trend | EMA Slow Period | 50 | 40-100 | candles |
| Trend | Confirmation Delay | 15 | 5-30 | minutes |
| Moon Bag | Reserve Percentage | 15% | 10-20% | % of position |
| Moon Bag | Trailing Stop | 15% | 10-20% | % below high |
| Flash Crash | 1-min Threshold | -3% | -2% to -5% | % price change |
| Flash Crash | 5-min Threshold | -5% | -3% to -8% | % price change |
| Liquidity | Min Book Depth | $50K | $25K-$100K | USD |
| Liquidity | Volume Warning | 50% | 30-70% | % of 7d avg |

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | trading-risk-manager | Initial specification |

---

## Notes for Implementation

1. **All thresholds are recommendations** - they should be configurable via application settings
2. **Test in paper trading first** - validate rules do not conflict before live deployment
3. **Log all rule triggers** - essential for debugging and optimization
4. **Atomic operations** - risk checks must be atomic with order placement
5. **Fail-safe defaults** - if any configuration missing, use most conservative setting
6. **Time synchronization** - ensure system clock is NTP-synchronized for accurate time-based rules
