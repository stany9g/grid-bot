# Auto-Tuning Risk Assessment for Adaptive Grid Configuration

## Document Purpose
This document provides a comprehensive risk analysis of the proposed auto-tuning formulas and strategies for the GridBot adaptive configuration feature. It addresses specific questions raised during the implementation planning phase.

---

## Question 1: ATR Multiplier Analysis

### Proposed Formula
```
spacing = clamp(ATR% * 0.5, 0.2%, 2.0%)
```

### Risk Assessment

**The 0.5x multiplier is CONSERVATIVE and APPROPRIATE for grid trading.**

**Rationale:**
- ATR measures the average price movement range over a period
- Grid spacing at 0.5x ATR means orders are placed at roughly half the expected daily movement
- This creates approximately 2 potential fill opportunities per ATR unit of movement

**Risks of 0.5x Multiplier:**

| Scenario | Risk | Mitigation |
|----------|------|------------|
| Very low ATR (< 0.4%) | 0.5 * 0.4% = 0.2% spacing, may not cover fees | Clamp minimum at 0.2% handles this |
| Very high ATR (> 4%) | 0.5 * 4% = 2% spacing, reduced fill frequency | Clamp maximum at 2.0% handles this |
| Sudden ATR spike | Grid may widen too fast, missing fills | Add smoothing (see Q4) |
| ATR calculation lag | Using stale ATR in fast markets | Use shorter ATR period (14 candles, 1h) |

**Recommendation: APPROVED with modifications**

1. Use 14-period ATR on 1-hour candles (not daily) for crypto markets
2. Consider dynamic multiplier: 0.4x in low-vol, 0.6x in high-vol regimes
3. Add minimum ATR threshold before auto-tuning activates

### Alternative Multiplier Options

| Multiplier | Style | Use Case |
|------------|-------|----------|
| 0.3x | Aggressive | High-frequency, tight spreads |
| 0.5x | Balanced | Default recommendation |
| 0.7x | Conservative | Lower frequency, wider margins |

---

## Question 2: Spacing Clamp Bounds Analysis

### Proposed Bounds
- Minimum: 0.2%
- Maximum: 2.0%

### Risk Assessment

**0.2% Minimum - NEEDS ADJUSTMENT**

| Concern | Analysis |
|---------|----------|
| Fee coverage | Lighter DEX maker fee ~0.01-0.02%. A 0.2% round-trip covers fees with ~10x margin |
| Slippage | Minimal on limit orders, 0.2% provides buffer |
| Flash crash exposure | Very tight spacing = many orders in crash zone |

**Recommendation: Increase minimum to 0.3% for safety**

**2.0% Maximum - APPROPRIATE**

| Concern | Analysis |
|---------|----------|
| Fill frequency | At 2% spacing with 10 levels, covers 20% price range |
| Capital efficiency | Acceptable for volatile markets |
| Opportunity cost | May miss small moves, but preserves capital |

**Recommendation: APPROVED - 2.0% maximum is reasonable**

### Revised Clamp Bounds

```
spacing = clamp(ATR% * 0.5, 0.3%, 2.0%)
```

**Boundary Behavior Specification:**

```
## Risk Category: Grid Spacing Clamps

### Thresholds
- MinimumSpacing: 0.3% (Fee coverage + slippage buffer)
- MaximumSpacing: 2.0% (Capital efficiency limit)
- ATRFloor: 0.5% (Below this, use fixed spacing)

### Rules
1. IF ATR% < 0.5% THEN use MinimumSpacing (0.3%)
2. IF ATR% > 4.0% THEN use MaximumSpacing (2.0%)
3. IF calculated spacing < MinimumSpacing THEN log warning "Spacing clamped to minimum - low volatility mode"
4. IF calculated spacing > MaximumSpacing THEN log warning "Spacing clamped to maximum - high volatility mode"

### Edge Cases
- Scenario: ATR calculation returns 0 or NaN (no data)
  Response: Use last valid spacing, log error, do not change grid

- Scenario: ATR suddenly jumps 3x in one period
  Response: Apply smoothing (see hysteresis), cap single-period change at 30%

### Priority Level: High
```

---

## Question 3: Order Size Formula Safety Analysis

### Proposed Formula
```
orderSize = (equity * maxPosition%) / totalLevels
```

### Risk Assessment

**FORMULA IS FUNDAMENTALLY SOUND but needs guardrails**

**Current Implementation:**
- equity = total account equity in USDC
- maxPosition% = configured max position percentage (e.g., 10%)
- totalLevels = buyLevels + sellLevels

**Risk Scenarios:**

| Scenario | Risk | Consequence |
|----------|------|-------------|
| totalLevels = 0 | Division by zero | System crash |
| totalLevels = 1 | Entire position in one order | Excessive concentration |
| equity = 0 | Zero order sizes | No trades |
| maxPosition% = 100% | Full account at risk | Total loss possible |
| Rapid equity decline | Shrinking orders don't close existing exposure | Stuck in losing position |

### Recommended Guardrails

```
## Risk Category: Order Size Calculation

### Thresholds
- MinimumTotalLevels: 4 (At least 2 buy + 2 sell)
- MaximumTotalLevels: 40 (Prevent order spam)
- MinimumOrderSizeUsdc: 10 USDC (Exchange minimum + buffer)
- MaximumOrderSizeUsdc: MIN(equity * 5%, 5000 USDC) (Single order cap)
- MinimumEquityForTrading: 100 USDC (Below this, halt trading)

### Rules
1. IF totalLevels < MinimumTotalLevels THEN use MinimumTotalLevels
2. IF calculated orderSize < MinimumOrderSizeUsdc THEN skip order placement, log warning
3. IF calculated orderSize > MaximumOrderSizeUsdc THEN cap at MaximumOrderSizeUsdc
4. IF equity < MinimumEquityForTrading THEN pause trading, emit "Low Balance" alert
5. IF equity drops > 20% since last check THEN pause trading, emit "Rapid Equity Decline" alert

### Edge Cases
- Scenario: Equity is NaN or negative (data error)
  Response: Pause trading immediately, use last known valid equity, require manual restart

- Scenario: All levels filled on one side (all buys filled, no sells)
  Response: Do not auto-increase order size; this indicates trending market, not rebalancing opportunity

- Scenario: Partial fills leave fragmented positions
  Response: Track cumulative position, not per-order; enforce total position limit

### Priority Level: Critical
```

### Enhanced Formula with Caps

```
effectiveTotalLevels = max(totalLevels, 4)
rawOrderSize = (equity * maxPosition%) / effectiveTotalLevels
orderSize = clamp(rawOrderSize, MinOrderSize, MaxOrderSize)
```

---

## Question 4: Auto-Tuning Cadence and Smoothing

### Current Proposal
Update suggestions each trading cycle (every 5 seconds)

### Risk Assessment

**TOO FREQUENT - Creates grid instability**

**Problems with per-cycle updates:**

| Issue | Impact |
|-------|--------|
| Grid churn | Constant order cancellations/placements increase fees |
| Whipsaw behavior | Rapid ATR changes cause grid to expand/contract repeatedly |
| API rate limits | Excessive order modifications may hit exchange limits |
| User confusion | Dashboard values change too fast to comprehend |

### Recommended Cadence

```
## Risk Category: Auto-Tuning Timing

### Thresholds
- SuggestionUpdateInterval: 60 seconds (Calculate new suggestions)
- GridRebuildCooldown: 300 seconds (Minimum time between grid restructures)
- ATRSmoothingPeriod: 3 periods (Use EMA of last 3 ATR values)
- MaxSpacingChangePerUpdate: 20% (Prevent sudden jumps)

### Rules
1. IF timeSinceLastSuggestion < 60s THEN skip calculation, use cached values
2. IF timeSinceLastGridRebuild < 300s THEN queue changes, do not rebuild
3. IF newSpacing differs from currentSpacing by > 20% THEN apply 20% change, queue remainder
4. IF user is viewing settings panel THEN update suggestions but do not auto-apply

### Edge Cases
- Scenario: Market gaps overnight (crypto 24/7, but low weekend volume)
  Response: Use longer ATR period (4h candles) during detected low-volume periods

- Scenario: ATR spikes during news event then normalizes
  Response: EMA smoothing automatically dampens spike effect

- Scenario: User enables Auto after manual override
  Response: Transition gradually over 3 update cycles, not instant

### Priority Level: High
```

### Hysteresis Implementation

```
// Smoothed ATR calculation
smoothedATR = (currentATR * 0.4) + (previousSmoothedATR * 0.6)

// Change limiting
maxChange = currentSpacing * 0.20
proposedChange = newSpacing - currentSpacing
actualChange = clamp(proposedChange, -maxChange, +maxChange)
finalSpacing = currentSpacing + actualChange
```

---

## Question 5: User Override Warning System

### Question
If user overrides, should the system warn if their value is "dangerous"?

### Risk Assessment

**YES - Warnings are essential but must not block**

### Warning Threshold Definitions

```
## Risk Category: User Override Warnings

### Thresholds (Grid Spacing)
- DangerouslyTight: < 0.25% (High fee drag, flash crash exposure)
- TooTight: < suggested * 0.5 (50% below suggestion)
- TooWide: > suggested * 2.0 (200% above suggestion)
- DangerouslyWide: > 3.0% (Low fill frequency, poor capital efficiency)

### Thresholds (Order Size)
- DangerouslySmall: < 15 USDC (May not cover minimum trade requirements)
- DangerouslyLarge: > equity * 20% / totalLevels (Over-concentrated)

### Thresholds (Levels)
- TooFewLevels: < 3 per side (Insufficient grid coverage)
- TooManyLevels: > 25 per side (Order management overhead)

### Rules
1. IF user sets spacing < DangerouslyTight THEN show warning "Spacing may not cover trading fees in volatile conditions"
2. IF user sets spacing < suggested * 0.5 THEN show warning "Spacing tighter than ATR suggests - increased flash crash exposure"
3. IF user sets spacing > suggested * 2.0 THEN show info "Spacing wider than ATR suggests - may miss trading opportunities"
4. IF user sets orderSize > DangerouslyLarge THEN show warning "Order size exceeds recommended concentration limit"
5. IF warnings exist THEN require confirmation checkbox before save (not block)

### Edge Cases
- Scenario: User explicitly wants aggressive settings for scalping
  Response: Allow with acknowledgment, log "User override with warning acknowledged"

- Scenario: ATR data unavailable (new market)
  Response: Warn "No historical data for suggestions - using defaults"

- Scenario: User sets maxDailyLoss to 50%
  Response: Block completely (hard limit), show error "Maximum allowed daily loss is 20%"

### Priority Level: Medium (warnings) / Critical (hard limits)
```

### Warning Categories

| Category | Behavior | UI Treatment |
|----------|----------|--------------|
| Info | Suggestion differs from user value | Gray info icon |
| Warning | User value may cause problems | Yellow warning icon |
| Danger | User value likely to cause losses | Red warning with confirmation |
| Block | User value violates hard limits | Red error, save disabled |

---

## Question 6: Missing Risk Scenarios

### Identified Gaps in Current Plan

**A. Low Liquidity Conditions**

```
## Risk Category: Liquidity Risk

### Thresholds
- MinimumOrderBookDepth: 10,000 USDC within 1% of mid-price
- HealthySpread: < 0.1% bid-ask spread
- MaxAcceptableSpread: 0.5% bid-ask spread
- CriticalSpread: > 1.0% bid-ask spread

### Rules
1. IF orderBookDepth < MinimumOrderBookDepth THEN widen spacing by 50%, emit "Low Liquidity" warning
2. IF spread > MaxAcceptableSpread THEN pause new order placement, keep existing
3. IF spread > CriticalSpread THEN cancel all orders, emit "Critical Liquidity" alert
4. IF liquidity normalizes for > 5 minutes THEN resume normal operations

### Edge Cases
- Scenario: Order book data stale (> 30 seconds old)
  Response: Treat as low liquidity, do not place new orders

- Scenario: Liquidity drops suddenly during our order placement
  Response: If fill fails with "insufficient liquidity", pause for 60 seconds

### Priority Level: Critical
```

**B. Extreme ATR Periods**

```
## Risk Category: Extreme Volatility

### Thresholds
- ATRNormalRange: 0.5% to 3.0% (typical crypto daily)
- ATRHighAlert: > 5.0% (elevated volatility)
- ATRExtreme: > 10.0% (crisis/black swan)
- ATRFloor: < 0.3% (suspiciously low, possible data issue)

### Rules
1. IF ATR > ATRHighAlert THEN reduce position sizes by 30%, widen spacing to maximum
2. IF ATR > ATRExtreme THEN pause trading, emit "Extreme Volatility" alert
3. IF ATR < ATRFloor THEN verify data source, if valid emit "Unusually Low Volatility" info
4. IF ATR transitions from Normal to Extreme within 1 period THEN immediate pause

### Edge Cases
- Scenario: ATR calculation uses weekend data with gaps
  Response: Exclude candles with > 4h gaps from ATR calculation

- Scenario: Exchange maintenance causes artificial low ATR
  Response: Detect via API status, do not update ATR during maintenance

### Priority Level: Critical
```

**C. Rapid Market Regime Changes**

```
## Risk Category: Regime Change Detection

### Thresholds
- VolumeSpike: > 300% of 24h average volume in 1 hour
- PriceVelocity: > 2% move in < 5 minutes
- DirectionReversal: Price crosses ATR band in opposite direction within 1 hour

### Rules
1. IF VolumeSpike detected THEN pause auto-tuning for 30 minutes
2. IF PriceVelocity triggered THEN invoke flash crash protection
3. IF DirectionReversal detected THEN do not rebuild grid for 15 minutes
4. IF multiple regime signals within 1 hour THEN reduce position to 50% of normal

### Edge Cases
- Scenario: Major news event causes sustained trend (not reversal)
  Response: After 4 hours of consistent direction, allow grid rebuild aligned to trend

- Scenario: Flash pump followed by dump (scam wick)
  Response: Flash crash protection handles dump; pump protection (FlashPumpDetector in AdvancedRisk module) handles pump

### Priority Level: High
```

**D. Data Staleness**

```
## Risk Category: Data Quality

### Thresholds
- MaxPriceAge: 30 seconds (beyond this, data considered stale)
- MaxATRAge: 5 minutes (ATR updates less frequently)
- MaxOrderBookAge: 15 seconds

### Rules
1. IF priceData.age > MaxPriceAge THEN pause order placement, use last known price for risk checks
2. IF atrData.age > MaxATRAge THEN use last valid ATR, emit "Stale ATR Data" warning
3. IF orderBook.age > MaxOrderBookAge THEN treat as low liquidity condition
4. IF all data sources stale > 60 seconds THEN pause trading, emit "Data Feed Failure" alert

### Edge Cases
- Scenario: API rate limited, data updates delayed
  Response: Exponential backoff, use cached data, do not assume market conditions changed

- Scenario: Clock drift between bot and exchange
  Response: Use exchange timestamp for staleness check, not local time

### Priority Level: Critical
```

**E. Cascading Risk Events**

```
## Risk Category: Cascade Protection

### Thresholds
- MaxRiskEventsPerHour: 3 (more than this suggests systemic issue)
- CooldownStackingLimit: 3 (don't stack more than 3 cooldown periods)

### Rules
1. IF riskEventsThisHour >= MaxRiskEventsPerHour THEN pause trading for 1 hour, require manual review
2. IF in cooldown AND new risk event occurs THEN extend cooldown by 50%, not restart
3. IF flash crash + low liquidity + stale data simultaneously THEN immediate shutdown, require manual restart

### Edge Cases
- Scenario: Bot caught in loop of pause/resume due to threshold boundary oscillation
  Response: After 3 cycles at boundary, extend cooldown to 30 minutes minimum

### Priority Level: Critical
```

---

## Summary: Recommended Formula Adjustments

### Grid Spacing
```
// Original
spacing = clamp(ATR% * 0.5, 0.2%, 2.0%)

// Revised
smoothedATR = EMA(ATR, 3 periods)
rawSpacing = smoothedATR * 0.5
clampedSpacing = clamp(rawSpacing, 0.3%, 2.0%)
changeLimit = currentSpacing * 0.20
spacing = currentSpacing + clamp(clampedSpacing - currentSpacing, -changeLimit, +changeLimit)
```

### Order Size
```
// Original
orderSize = (equity * maxPosition%) / totalLevels

// Revised
effectiveLevels = clamp(totalLevels, 4, 40)
rawOrderSize = (equity * maxPosition%) / effectiveLevels
orderSize = clamp(rawOrderSize, 10, min(equity * 0.05, 5000))
```

### Additional Required Checks
1. Liquidity validation before any order placement
2. Data freshness check at start of each cycle
3. Cascade detection for multiple simultaneous risk events
4. Regime change detection to pause auto-tuning temporarily

---

## Hard Limits (Non-Negotiable)

These values cannot be overridden by user, even with warnings:

| Parameter | Hard Minimum | Hard Maximum | Rationale |
|-----------|--------------|--------------|-----------|
| GridSpacingPercent | 0.15% | 5.0% | Fee coverage / capital efficiency |
| MaxDailyLossPercent | 1% | 20% | Capital preservation |
| FlashCrashThresholdPercent | 3% | 15% | False positive / real crash balance |
| Leverage | 1x | 10x | Risk of liquidation |
| MinimumOrderSizeUsdc | 5 | - | Exchange minimums |
| TotalLevels | 4 | 60 | Grid manageability |

---

## Implementation Checklist

Before implementing auto-tuning, ensure:

- [ ] ATR calculation service exists or is created
- [ ] Smoothing/hysteresis logic implemented
- [ ] Data staleness checks in place
- [ ] Liquidity monitoring active
- [ ] Warning UI components ready
- [ ] Hard limit validation in configuration service
- [ ] Logging for all auto-tuning decisions
- [ ] Manual override audit trail

---

## Document Metadata

- Created: 2025-12-25
- Author: trading-risk-manager agent
- Status: REVIEW COMPLETE
- Next Action: Implementation by dotnet-feature-builder agent
