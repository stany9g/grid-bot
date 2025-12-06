# BTC Live Testing Risk Framework - ALTE Grid Bot

## Executive Summary

This document provides comprehensive risk management recommendations for testing the ALTE Grid Bot with REAL MONEY on BTC (marketId = 1) on Lighter DEX.

**CRITICAL WARNING**: This is a transition from testnet to mainnet with real capital. All recommendations prioritize capital preservation over profit maximization during the initial testing phase.

---

## 1. Recommended Starting Capital

### Minimum Capital Analysis

| Factor | Calculation | Result |
|--------|-------------|--------|
| Minimum order size | $10 (configured MinOrderSizeUsd) | $10 |
| Orders per side | 4-10 (configured range) | Use 6 for calculation |
| Both sides (buy + sell) | 6 x 2 = 12 orders | 12 orders |
| Total order value | 12 x $10 minimum | $120 |
| Reserve buffer (20%) | $120 / 0.80 | $150 |
| Safety margin (2x) | $150 x 2 | $300 |

**Absolute Minimum: $300**

### Recommended Capital Tiers

| Tier | Capital | Rationale | Order Size Range |
|------|---------|-----------|------------------|
| **Micro Test** | $500 | Bare minimum for meaningful grid | $10-$25 per order |
| **Small Test** | $1,000 | Recommended starting point | $10-$50 per order |
| **Standard Test** | $2,500 | Comfortable testing with flexibility | $25-$125 per order |
| **Full Test** | $5,000 | Complete feature validation | $50-$250 per order |
| **Production Ready** | $10,000+ | After successful phased testing | $100-$500 per order |

### Capital Recommendation

**START WITH: $1,000**

Rationale:
- Provides meaningful grid depth (12-20 orders)
- Order sizes ($10-$50) are realistic but not painful if lost
- Sufficient to test all bot features including moon bag ($150 minimum value threshold met)
- 20% reserve ($200) provides adequate buffer
- Psychological comfort zone for first live test
- Allows 10-20 "lessons" (losses) before capital depletion

---

## 2. Settings Adjustments for Initial Live Testing

### Risk Category: Capital & Position Sizing

#### Thresholds (RECOMMENDED CHANGES)

| Setting | Default | Recommended | Rationale |
|---------|---------|-------------|-----------|
| MaxPositionSizePercent | 10% | **5%** | Halved for initial caution |
| MaxOrderSizePercent | 5% | **3%** | Smaller individual orders |
| MaxLeverage | 5x | **2x** | Significantly reduced leverage |
| MaxAggregateLeverage | 3x | **1.5x** | Conservative aggregate exposure |
| ReserveBalancePercent | 20% | **30%** | Increased reserve buffer |

#### Rules
1. IF single order > 3% of portfolio THEN reject order
2. IF total position > 5% of portfolio THEN block new entries
3. IF effective leverage > 2x THEN reduce position by 25%
4. IF reserve balance < 30% THEN halt new orders

#### Edge Cases
- **Insufficient balance**: Proportionally reduce order sizes, minimum $10
- **Leverage breach during price movement**: Gradual reduction, not panic liquidation

#### Priority Level: CRITICAL

---

### Risk Category: Loss Limits (TIGHTENED)

#### Thresholds (RECOMMENDED CHANGES)

| Setting | Default | Recommended | Rationale |
|---------|---------|-------------|-----------|
| DailyLossPercent | -5% | **-3%** | Tighter daily limit |
| WeeklyLossPercent | -10% | **-7%** | Tighter weekly limit |
| MonthlyLossPercent | -15% | **-10%** | Tighter monthly limit |
| MaxDrawdownPercent | -20% | **-15%** | Tighter drawdown limit |
| SingleTradeLossPercent | -2% | **-1.5%** | Tighter per-trade limit |

#### Rules
1. IF daily loss > 3% THEN halt all trading for remainder of day
2. IF weekly loss > 7% THEN halt trading until manual review
3. IF drawdown from ATH > 10% THEN reduce position sizes by 50%
4. IF drawdown from ATH > 15% THEN halt trading completely
5. IF single trade loss > 1.5% THEN flag for investigation

#### Edge Cases
- **Multiple flash crashes in one day**: After 2nd trigger, halt for 24 hours
- **Loss limit hit near weekend**: Reduce positions before low-liquidity period

#### Priority Level: CRITICAL

---

### Risk Category: Grid Geometry (BTC-SPECIFIC)

#### Thresholds (RECOMMENDED CHANGES)

| Setting | Default | Recommended | Rationale |
|---------|---------|-------------|-----------|
| MinSpacing | 0.15% | **0.25%** | BTC has lower volatility than altcoins |
| MaxSpacing | 3.0% | **2.5%** | Tighter maximum for BTC stability |
| DefaultSpacing | 1.0% | **0.8%** | BTC typical ATR-based spacing |
| MinOrdersPerSide | 4 | **4** | Keep minimum (capital constraint) |
| MaxOrdersPerSide | 10 | **8** | Reduced for capital efficiency |

#### Rules
1. IF BTC ATR(14) < 1.5% THEN use 0.5% grid spacing
2. IF BTC ATR(14) > 3.0% THEN use 1.5% grid spacing
3. IF grid fill rate < 20% in 4 hours THEN widen spacing by 25%
4. IF grid fill rate > 80% in 1 hour THEN tighten spacing by 20%

#### Edge Cases
- **Weekend low volatility**: Widen grid spacing by 50%
- **Major news event expected**: Pause grid, wait for ATR normalization
- **Grid entirely one-sided**: Trigger inventory rebalance check

#### Priority Level: HIGH

---

### Risk Category: Flash Crash Protection (TIGHTENED)

#### Thresholds (RECOMMENDED CHANGES)

| Setting | Default | Recommended | Rationale |
|---------|---------|-------------|-----------|
| OneMinuteDropPercent | -3% | **-2%** | Earlier trigger for BTC |
| FiveMinuteDropPercent | -5% | **-4%** | Tighter 5-min threshold |
| FifteenMinuteDropPercent | -10% | **-7%** | Tighter 15-min threshold |
| OneHourDropPercent | -15% | **-10%** | Tighter 1-hour threshold |
| MaxEventsIn24Hours | 2 | **1** | More conservative event limit |

#### Rules
1. IF 1-min drop > 2% THEN pause buying for 10 minutes (extended from 5)
2. IF 5-min drop > 4% THEN cancel all pending orders, pause 30 minutes
3. IF 15-min drop > 7% THEN reduce position by 30%, pause 2 hours
4. IF 1-hour drop > 10% THEN full trading halt, require manual restart
5. IF 2nd flash crash in 24 hours THEN halt for 48 hours

#### Edge Cases
- **V-shaped recovery during pause**: Do NOT auto-resume, wait full pause duration
- **Flash crash during recovery phase**: Reset recovery to Phase 1
- **Cascade of multiple timeframe triggers**: Use longest pause duration

#### Priority Level: CRITICAL

---

### Risk Category: Moon Bag Protection

#### Thresholds (KEEP DEFAULTS WITH MINOR ADJUSTMENTS)

| Setting | Default | Recommended | Rationale |
|---------|---------|-------------|-----------|
| MoonBagPercentage | 15% | **15%** | Keep default |
| TrailingGridStep | 2% | **2%** | Keep default |
| MinimumMoonBagUsd | $50 | **$100** | Higher threshold for activation |
| WarmUpPeriodMinutes | 30 | **60** | Longer warmup before protection |

#### Rules
1. IF position value > $100 AND warmup complete THEN enable moon bag protection
2. IF price > 10% above grid top THEN activate trailing stop
3. IF trailing stop triggered THEN sell 85% only (keep 15% moon bag)
4. IF price gaps below trailing stop THEN execute at best available

#### Edge Cases
- **Flash spike (>20% in 5 min)**: Pause grid shift for 10 minutes (default)
- **Moon bag value < $100 after partial fill**: Disable protection until threshold met
- **Bot restart during trailing stop active**: Restore from persisted state

#### Priority Level: HIGH

---

## 3. BTC-Specific Considerations

### Market Characteristics

| Characteristic | BTC Reality | Impact on Settings |
|----------------|-------------|-------------------|
| **Liquidity** | Highest in crypto ($1B+ daily on major DEXs) | Lighter DEX may have lower depth - monitor |
| **Volatility** | 1.5-3% daily ATR typical | Wider grid spacing than altcoins |
| **Spread** | Usually 0.01-0.05% on major venues | Watch Lighter spread closely |
| **Funding** | Typically -0.01% to +0.03% | Monitor funding rate impact |
| **Correlation** | All crypto follows BTC | Single asset testing is representative |

### BTC-Specific Monitoring Thresholds

| Metric | Warning | Critical | Action |
|--------|---------|----------|--------|
| Order book depth | < $500K | < $100K | Widen spreads / halt |
| Bid-ask spread | > 0.1% | > 0.3% | Pause new orders |
| Funding rate | > 0.05% | > 0.1% | Review position size |
| 24h volume | < $5M | < $1M | Reduce position 50% |

### Time-Based Considerations

| Period | Risk Level | Recommendation |
|--------|------------|----------------|
| **Weekdays (UTC 13:00-21:00)** | Low | Normal trading |
| **Weekdays (Asia/Europe overlap)** | Medium | Normal trading |
| **Weekends** | High | Widen grid 25%, reduce position 25% |
| **Major news events** | Very High | Pause or manual mode |
| **FOMC announcements** | Very High | Pause 2 hours before/after |

---

## 4. Phased Testing Approach

### Phase 1: Micro Test (Days 1-3)

**Capital**: $500
**Duration**: 3 days minimum, 7 days recommended
**Objective**: Verify basic functionality with real money

#### Settings Override
```json
{
  "TradingBot": {
    "MarketId": 1,
    "AutoStartTrading": false,
    "Capital": {
      "MaxPositionSizePercent": 3,
      "MaxOrderSizePercent": 2,
      "MaxLeverage": 1.5,
      "MaxAggregateLeverage": 1,
      "ReserveBalancePercent": 40
    },
    "LossLimits": {
      "DailyLossPercent": -2,
      "WeeklyLossPercent": -5,
      "MaxDrawdownPercent": -10
    },
    "Grid": {
      "MinOrdersPerSide": 4,
      "MaxOrdersPerSide": 6,
      "DefaultSpacing": 1.0
    }
  }
}
```

#### Success Criteria to Advance
- [ ] 72 hours continuous operation without crashes
- [ ] At least 10 successful order fills (buy OR sell)
- [ ] No loss limit triggers
- [ ] No flash crash triggers (unless market actually crashed)
- [ ] Fill detection accuracy > 95%
- [ ] All orders placed at expected prices
- [ ] Moon bag logic correctly identifies protected quantity

#### Metrics to Monitor
| Metric | Target | Minimum |
|--------|--------|---------|
| Uptime | 99% | 95% |
| Fill detection accuracy | 99% | 95% |
| Order placement success rate | 98% | 90% |
| P&L (absolute) | Any positive | > -$25 |
| P&L (vs hold) | Within 1% | Within 3% |

---

### Phase 2: Small Test (Days 4-14)

**Capital**: $1,000
**Duration**: 10 days minimum
**Objective**: Validate full feature set under normal conditions

#### Settings Override
```json
{
  "TradingBot": {
    "Capital": {
      "MaxPositionSizePercent": 5,
      "MaxOrderSizePercent": 3,
      "MaxLeverage": 2,
      "MaxAggregateLeverage": 1.5,
      "ReserveBalancePercent": 30
    },
    "LossLimits": {
      "DailyLossPercent": -3,
      "WeeklyLossPercent": -7,
      "MaxDrawdownPercent": -15
    },
    "Grid": {
      "MinOrdersPerSide": 4,
      "MaxOrdersPerSide": 8
    }
  }
}
```

#### Success Criteria to Advance
- [ ] 10 days continuous operation
- [ ] At least 50 successful fills
- [ ] At least 3 complete "buy low, sell high" cycles
- [ ] Grid spacing auto-adjusted at least 5 times
- [ ] Trend detection changed state at least once
- [ ] No loss limit triggers (or justified by market conditions)
- [ ] Recovery from at least 1 degraded state
- [ ] P&L neutral or positive

#### Metrics to Monitor
| Metric | Target | Minimum |
|--------|--------|---------|
| Win rate (fills) | > 55% | > 45% |
| Avg profit per trade | > $0.50 | > -$1.00 |
| Sharpe ratio | > 0.5 | > 0 |
| Max drawdown | < 5% | < 10% |

---

### Phase 3: Standard Test (Days 15-30)

**Capital**: $2,500
**Duration**: 15 days minimum
**Objective**: Stress test under varying market conditions

#### Settings Override
```json
{
  "TradingBot": {
    "Capital": {
      "MaxPositionSizePercent": 7,
      "MaxOrderSizePercent": 4,
      "MaxLeverage": 3,
      "MaxAggregateLeverage": 2,
      "ReserveBalancePercent": 25
    },
    "LossLimits": {
      "DailyLossPercent": -4,
      "WeeklyLossPercent": -8,
      "MaxDrawdownPercent": -17
    }
  }
}
```

#### Success Criteria to Advance
- [ ] 15 days continuous operation
- [ ] Survived at least one 5%+ daily move (up or down)
- [ ] Flash crash protection triggered correctly (if applicable)
- [ ] Moon bag protection triggered correctly (if price rallied)
- [ ] Inventory skew adjusted correctly to trend changes
- [ ] P&L positive relative to buy-and-hold

---

### Phase 4: Full Test (Days 31-60)

**Capital**: $5,000
**Duration**: 30 days minimum
**Objective**: Production readiness validation

#### Settings: Near-production defaults

#### Success Criteria for Production
- [ ] 30 days with < 2 hours total downtime
- [ ] Survived multiple market conditions (trending, ranging, volatile)
- [ ] All protective mechanisms validated
- [ ] Positive P&L in absolute terms
- [ ] Better than -3% vs buy-and-hold in bull market
- [ ] Better than +5% vs buy-and-hold in bear/sideways market

---

## 5. Critical Metrics to Watch

### Real-Time Dashboard Monitoring

| Category | Metric | Warning | Critical |
|----------|--------|---------|----------|
| **System** | Decision loop latency | > 3s | > 10s |
| **System** | Consecutive timeouts | > 3 | > 5 |
| **System** | Cache staleness | > 30s | > 60s |
| **Trading** | Daily P&L | < -1% | < -3% |
| **Trading** | Drawdown | > 5% | > 10% |
| **Trading** | Fill detection misses | > 2 | > 5 |
| **Grid** | One-sided fills | > 5 consecutive | > 10 consecutive |
| **Grid** | Fill rate | < 5% in 2h | < 1% in 4h |
| **Risk** | Flash crash triggers | 1 | 2 |
| **Risk** | Loss limit proximity | > 50% of limit | > 75% of limit |
| **Liquidity** | Order book depth | < $200K | < $100K |
| **Liquidity** | Spread | > 0.15% | > 0.3% |

### Webhook Alert Configuration

Ensure Home Assistant webhook receives alerts for:

| Event | Severity | Action Required |
|-------|----------|-----------------|
| `flash_crash` | CRITICAL | Check market, may need manual intervention |
| `loss_limit` | CRITICAL | Investigate immediately |
| `liquidation_risk` | CRITICAL | Emergency action required |
| `protective_mode` | WARNING | Monitor closely |
| `state_change` | INFO | Log for review |
| `fill_detected` | INFO | Confirm grid is working |
| `timeout_warning` | WARNING | Check connectivity |
| `grid_shift` | INFO | Verify moon bag logic |

---

## 6. Emergency Procedures

### Manual Intervention Triggers

| Condition | Immediate Action |
|-----------|------------------|
| Bot unresponsive > 15 min | SSH/RDP and restart service |
| Loss > 5% in 1 hour | Manual halt via API or dashboard |
| Lighter DEX issues | Pause bot, DO NOT panic sell |
| Unexpected position size | Verify, then manually adjust if needed |
| Exchange maintenance | Pause 1 hour before, resume 1 hour after |

### Position Liquidation Procedure

IF manual liquidation required:

1. Pause bot trading via dashboard
2. Cancel all pending orders via dashboard
3. Assess current position size and P&L
4. Execute market order to close (if urgent) OR
5. Place limit order at reasonable price (if not urgent)
6. Verify position is flat
7. Review logs to understand what happened
8. Fix issue before restarting

### Recovery After Incident

1. Wait for market stabilization (1-4 hours minimum)
2. Review logs and identify root cause
3. Adjust settings if needed
4. Restart bot in Phase 1 settings
5. Monitor for 24 hours before returning to previous phase

---

## 7. Configuration Summary for Phase 1

### appsettings.Production.json (Initial Live Test)

```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "ChainId": 304,
    "ApiKeyIndex": 0,
    "AccountIndex": 0
  },
  "TradingBot": {
    "MarketId": 1,
    "AutoStartTrading": false,
    "DecisionLoopIntervalMs": 5000,
    "Capital": {
      "MaxPositionSizePercent": 3,
      "MaxOrderSizePercent": 2,
      "MaxLeverage": 1.5,
      "MaxAggregateLeverage": 1,
      "ReserveBalancePercent": 40,
      "MinOrderSizeUsd": 10
    },
    "LossLimits": {
      "DailyLossPercent": -2,
      "WeeklyLossPercent": -5,
      "MonthlyLossPercent": -8,
      "MaxDrawdownPercent": -10,
      "SingleTradeLossPercent": -1
    },
    "Grid": {
      "MinSpacing": 0.25,
      "MaxSpacing": 2.5,
      "DefaultSpacing": 0.8,
      "MinOrdersPerSide": 4,
      "MaxOrdersPerSide": 6
    },
    "FlashCrash": {
      "OneMinuteDropPercent": -2,
      "FiveMinuteDropPercent": -4,
      "FifteenMinuteDropPercent": -7,
      "OneHourDropPercent": -10,
      "MaxEventsIn24Hours": 1
    },
    "MoonBag": {
      "MoonBagPercentage": 0.15,
      "TrailingGridStep": 0.02,
      "MinimumMoonBagUsd": 100,
      "WarmUpPeriodMinutes": 60
    }
  }
}
```

---

## 8. Risk Hierarchy

### Priority Order (Highest to Lowest)

1. **Capital Preservation** - ABSOLUTE, cannot be overridden
   - Max drawdown limits
   - Flash crash protection
   - Liquidation risk alerts

2. **System Stability** - CRITICAL, minimal override
   - Never halt philosophy
   - Timeout handling
   - Recovery state machine

3. **Position Safety** - HIGH, context-dependent
   - Loss limits
   - Leverage constraints
   - Moon bag protection

4. **Profit Optimization** - MEDIUM, can be overridden
   - Grid spacing optimization
   - Trend-based skewing
   - Fill rate optimization

5. **Feature Activation** - LOW, user discretion
   - Moon bag activation thresholds
   - Trailing stop tightening levels
   - Warmup periods

---

## 9. Checklist Before Going Live

### Pre-Flight Checklist

- [ ] Mainnet API credentials configured and tested
- [ ] Account funded with $500-$1,000 USDT/USDC
- [ ] Phase 1 conservative settings applied
- [ ] Dashboard accessible and showing correct data
- [ ] Webhook notifications tested (send test alert)
- [ ] Manual kill procedure documented and tested
- [ ] Backup connectivity plan (mobile hotspot if home internet)
- [ ] Emergency contact method for alerts (phone notifications)
- [ ] Bot in PAUSED state, not auto-starting
- [ ] Market conditions reviewed (no major news imminent)

### Launch Sequence

1. Verify all checklist items complete
2. Start bot service
3. Verify bot connects to exchange
4. Verify dashboard shows correct balance and market data
5. Enable trading via dashboard
6. Verify first grid orders placed
7. Monitor for first 30 minutes actively
8. Set up periodic check reminders (every 2-4 hours)

---

## Document Metadata

| Field | Value |
|-------|-------|
| Version | 1.0 |
| Created | 2025-12-06 |
| Author | trading-risk-manager agent |
| Target Asset | BTC (marketId = 1) |
| Target Platform | Lighter DEX Mainnet |
| Review Status | Ready for user review |

---

## Important Notes

1. **This is NOT financial advice** - These are technical recommendations for system testing
2. **Only risk what you can afford to lose** - All trading involves risk
3. **Start smaller than you think** - You can always add capital later
4. **Monitor actively in Phase 1** - Do not "set and forget" initially
5. **Document everything** - Keep logs of all anomalies for analysis
6. **Be patient** - 60 days of testing is minimal for production confidence
