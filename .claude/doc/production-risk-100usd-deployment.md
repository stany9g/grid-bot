# Production Deployment Risk Framework: $100 Capital

**Document Version:** 1.0
**Date:** 2025-12-13
**Status:** RECOMMENDATION
**Target Environment:** Lighter DEX Mainnet (ChainId: 304)
**Trading Pair:** BTC Perpetual Futures

---

## Executive Summary

Deploying an algorithmic trading bot with $100 capital on perpetual futures requires **extreme conservatism**. This document provides specific configuration recommendations, risk thresholds, and operational guidelines to maximize learning while minimizing the probability of total capital loss.

### CRITICAL WARNING

**$100 is a LEARNING budget, NOT a profit-seeking budget.**

At this capital level:
- A single bad trade can wipe out the account
- Transaction fees consume a disproportionate percentage of profits
- Minimum order sizes may conflict with position sizing rules
- Recovery from drawdowns is extremely difficult

**Primary Goal:** Survive long enough to validate the system, NOT to maximize returns.

---

## Section A: Capital and Leverage Configuration

### A.1 Leverage Recommendations

## Risk Category: Leverage Control

### Thresholds
- MaxLeverage: **1.5x** (Rationale: Even 2x doubles liquidation risk; 1.5x provides buffer)
- MaxAggregateLeverage: **1.0x** (Rationale: Effective no leverage across portfolio)
- Actual Recommended Practice: **1.0x** (Rationale: Zero leverage until system proves profitable)

### Rules
1. IF AccountEquity <= $100 THEN MaxLeverage = 1.0x (no leverage phase)
2. IF AccountEquity >= $250 AND ConsecutiveProfitableDays >= 14 THEN MaxLeverage = 1.5x (earned trust phase)
3. IF UnrealizedLoss >= 10% THEN reject any order that would increase leverage
4. IF FundingRate > 0.05% per 8h (against position) THEN reduce leverage to 1.0x

### Edge Cases
- Scenario: Market gaps overnight with 1.5x leverage
  Response: 1.5x on $100 means $150 exposure; 7% adverse move = $10.50 loss (10.5% of capital). Acceptable.
- Scenario: Liquidation price calculation
  Response: At 1.5x leverage with 50% maintenance margin, liquidation occurs at ~33% adverse move. Safe for BTC.

### Priority Level: CRITICAL

### Rationale for Conservative Leverage

| Leverage | $100 Exposure | 10% Adverse Move | % Capital Lost |
|----------|---------------|------------------|----------------|
| 1.0x     | $100          | $10              | 10%            |
| 1.5x     | $150          | $15              | 15%            |
| 2.0x     | $200          | $20              | 20%            |
| 3.0x     | $300          | $30              | 30%            |
| 5.0x     | $500          | $50              | 50%            |

**BTC daily volatility** averages 3-5%, with occasional 10-15% moves. At 2x leverage, a 15% move = 30% loss. At 5x leverage, a 10% move = 50% loss. With $100, any significant loss is psychologically and practically devastating.

---

## Section B: Position Sizing Configuration

### B.1 Position Size Limits

## Risk Category: Position Sizing

### Thresholds
- MaxPositionSizePercent: **20%** (= $20 max position per market)
- MaxOrderSizePercent: **10%** (= $10 max per order)
- MinOrderSizeUsd: **$5** (Rationale: Lighter minimum is ~$5-10, must be viable)
- ReserveBalancePercent: **50%** (= $50 minimum reserve)
- MaxDeployedCapitalPercent: **50%** (= $50 max at risk)

### Rules
1. IF single_order_size > $10 THEN reject order (too large for $100 account)
2. IF total_deployed_capital > $50 THEN reject new positions
3. IF reserve_balance < $50 THEN halt all new orders, only allow position reduction
4. IF single_position > $25 THEN force partial close to $20

### Edge Cases
- Scenario: Order size $10 but actual fill is $10.50 due to slippage
  Response: Accept 5% tolerance on fills; flag if > 10% deviation
- Scenario: Multiple small fills accumulate beyond limit
  Response: GridLifecycleService checks position after EACH fill; halts if exceeded

### Priority Level: CRITICAL

### Why 50% Reserve at $100?

With $100 capital:
- $50 in reserve = emergency buffer for:
  - Funding payments (can be $0.10-$0.50 per 8h at high rates)
  - Transaction fees (~0.02-0.05% per trade)
  - Unexpected losses during WebSocket disconnect
  - Black swan partial position reduction

- $50 deployed = realistic trading capital
  - 5 grid orders x $10 each = $50 total grid exposure
  - Allows meaningful grid structure while preserving emergency funds

---

## Section C: Loss Limits Configuration

### C.1 Daily and Rolling Loss Limits

## Risk Category: Loss Limits

### Thresholds
- Rolling24HourLossPercent: **-8%** (= -$8) (Rationale: Tighter than default -15%; recoverable)
- Rolling7DayLossPercent: **-15%** (= -$15) (Rationale: One bad week shouldn't destroy account)
- Rolling30DayLossPercent: **-20%** (= -$20) (Rationale: Total monthly risk cap)
- MaxDrawdownPercent: **-25%** (= -$25) (Rationale: Hard stop before half account is gone)
- SingleTradeLossPercent: **-3%** (= -$3) (Rationale: Alert early on individual bad trades)

### Rules
1. IF 24h_loss >= $8 THEN halt_trading_4_hours AND reduce_position_size_50%
2. IF 7d_loss >= $15 THEN halt_trading_24_hours AND require_manual_review
3. IF 30d_loss >= $20 THEN halt_trading_indefinite AND require_manual_restart
4. IF drawdown_from_peak >= $25 THEN emergency_close_all_positions
5. IF single_trade_loss >= $3 THEN alert_operator (continues trading)

### Edge Cases
- Scenario: Loss limit hit during active position
  Response: Do NOT market sell immediately (causes slippage). Place limit orders at current price, wait 30 seconds, then market sell remaining.
- Scenario: Loss limit hit while unrealized PnL is positive
  Response: Loss limits track REALIZED losses only. Unrealized positions continue but no new orders.
- Scenario: Flash crash triggers multiple loss limits simultaneously
  Response: Execute highest priority (MaxDrawdown > Rolling30Day > Rolling7Day > Rolling24Hour)

### Priority Level: CRITICAL

### Loss Limit Rationale for $100

| Metric | Default | Recommended | Dollar Impact | Recovery Difficulty |
|--------|---------|-------------|---------------|---------------------|
| Daily Loss | -15% | -8% | -$8 | Need 8.7% gain to recover |
| Weekly Loss | -25% | -15% | -$15 | Need 17.6% gain to recover |
| Monthly Loss | -30% | -20% | -$20 | Need 25% gain to recover |
| Max Drawdown | -35% | -25% | -$25 | Need 33.3% gain to recover |

**Recovery Math:** A 25% loss requires a 33.3% gain just to break even. At $75 remaining, you need to make $25 profit. This is extremely difficult with reduced capital.

---

## Section D: Grid Configuration

### D.1 Grid Geometry for $100 Capital

## Risk Category: Grid Configuration

### Thresholds
- MinSpacing: **0.3%** (Rationale: Below this, fees eat profits; BTC at $100k = $300 per level)
- DefaultSpacing: **0.6%** (Rationale: Balanced for BTC volatility; $600 per level at $100k)
- MaxSpacing: **1.5%** (Rationale: Too wide = missed opportunities with limited capital)
- MinOrdersPerSide: **2** (Rationale: Minimum viable grid structure)
- MaxOrdersPerSide: **3** (Rationale: $100 / 6 orders = ~$16/order max; 3 per side safer)
- DefaultOrdersPerSide: **3** (Rationale: Match max for simplicity)

### Rules
1. IF OrdersPerSide * 2 * MinOrderSize > DeployedCapital THEN reduce OrdersPerSide
2. IF Spacing < 0.3% THEN reject grid (fee drag too high)
3. IF GridWidth > 5% of price THEN alert (may indicate misconfiguration)
4. IF GridWidth < 1% of price THEN alert (too concentrated risk)

### Edge Cases
- Scenario: ATR suggests 0.2% spacing but minimum is 0.3%
  Response: Use 0.3% minimum; accept reduced fill frequency
- Scenario: 3 orders per side but only $8 available per order
  Response: Reduce to 2 orders per side at $10 each (better liquidity capture)
- Scenario: All grid orders filled on one side (all buys or all sells)
  Response: System enters "inventory rebalance" mode; waits for reversal before placing new orders

### Priority Level: HIGH

### Grid Math for $100

**Scenario:** BTC at $100,000, DefaultSpacing 0.6%

| Side | Level | Price | Order Size | Cumulative |
|------|-------|-------|------------|------------|
| Sell 3 | +1.8% | $101,800 | $10 | $30 |
| Sell 2 | +1.2% | $101,200 | $10 | $20 |
| Sell 1 | +0.6% | $100,600 | $10 | $10 |
| --- | Mid | $100,000 | --- | --- |
| Buy 1 | -0.6% | $99,400 | $10 | $10 |
| Buy 2 | -1.2% | $98,800 | $10 | $20 |
| Buy 3 | -1.8% | $98,200 | $10 | $30 |

**Total Grid Exposure:** $60 (6 orders x $10)
**Reserve:** $40 (40%)
**Grid Width:** 3.6% ($3,600 on $100k BTC)

---

## Section E: Flash Crash/Pump Protection

### E.1 Volatility Protection Thresholds

## Risk Category: Flash Crash Protection

### Thresholds (TIGHTENED for $100)
- OneMinuteDropPercent: **-2%** (Default: -3%; Earlier detection)
- FiveMinuteDropPercent: **-4%** (Default: -5%; Earlier pause)
- FifteenMinuteDropPercent: **-7%** (Default: -10%; More conservative)
- OneHourDropPercent: **-10%** (Default: -15%; Protect smaller capital)
- BlackSwanThresholdPercent: **-15%** (Default: -25%; Earlier emergency action)

### Rules
1. IF 1min_drop >= 2% THEN pause_buys_5_minutes (don't catch falling knife)
2. IF 5min_drop >= 4% THEN pause_all_orders_15_minutes
3. IF 15min_drop >= 7% THEN reduce_position_50% AND pause_60_minutes
4. IF 1hour_drop >= 10% THEN full_halt_4_hours
5. IF 1hour_drop >= 15% THEN emergency_close_50% AND halt_24_hours (black swan for small accounts)

### Edge Cases
- Scenario: Flash crash recovers within 1 minute
  Response: Pause still enforced for full duration; no early exit
- Scenario: Multiple flash crash events in 24 hours
  Response: After 2 events, halt for remainder of 24h period regardless of recovery
- Scenario: Flash crash during position reduction order
  Response: Complete the reduction order; do not pause protective actions

### Priority Level: CRITICAL

## Risk Category: Flash Pump Protection

### Thresholds (TIGHTENED for $100)
- OneMinuteGainPercent: **+2%** (Default: +3%; Earlier detection)
- FiveMinuteGainPercent: **+4%** (Default: +5%; Earlier pause)
- FifteenMinuteGainPercent: **+7%** (Default: +10%; More conservative)
- OneHourGainPercent: **+10%** (Default: +15%; Protect short positions)

### Rules
1. IF 1min_gain >= 2% THEN pause_sells_5_minutes (don't sell into pump)
2. IF 5min_gain >= 4% THEN pause_all_orders_15_minutes
3. IF 15min_gain >= 7% THEN cover_50%_shorts AND pause_60_minutes
4. IF 1hour_gain >= 10% THEN full_halt_4_hours

### Priority Level: CRITICAL

---

## Section F: Pre-Trade and Liquidity Validation

### F.1 Order Book Depth Requirements

## Risk Category: Pre-Trade Validation

### Thresholds (ADJUSTED for Lighter DEX)
- MinOrderBookDepthUsd: **$10,000** (Default: $25,000; Lighter has less depth than CEX)
- CriticalDepthThresholdUsd: **$5,000** (Default: $10,000; Adjusted for DEX reality)
- MaxOrderToDepthRatio: **5%** (Default: 10%; More conservative impact)
- MaxAcceptableSpreadPercent: **0.5%** (Default: 1%; Tighter spread requirement)
- MaxDataAgeSeconds: **3** (Default: 5; Fresher data for small account)

### Rules
1. IF total_depth < $5,000 THEN reject_ALL_orders (market too thin)
2. IF order_size > 5% of available_depth THEN reduce_order_size
3. IF spread > 0.5% THEN reject_order AND alert (unusual market condition)
4. IF depth_data_age > 3_seconds THEN reject_order (stale data risk)
5. IF bid_depth / ask_depth > 5.0 OR < 0.2 THEN alert_imbalance (potential manipulation)

### Edge Cases
- Scenario: Depth meets threshold but concentrated at one price level
  Response: Check depth at 5 price levels; if 80% at top level, treat as thin
- Scenario: Spread acceptable but widening rapidly
  Response: If spread doubled in last 5 minutes, halt and alert
- Scenario: Lighter DEX has lower liquidity during Asian hours
  Response: Widen spread tolerance to 0.8% during 00:00-08:00 UTC

### Priority Level: HIGH

### Lighter DEX Liquidity Reality

**IMPORTANT:** Lighter DEX is a newer perpetual DEX with lower liquidity than Binance/Bybit.

Observed characteristics:
- BTC perpetual depth: $50k-200k typical (vs $5M+ on Binance)
- Spread: 0.02%-0.1% typical (can widen to 0.5%+ during volatility)
- Volume: Lower, especially during Asian session

**Implications for $100:**
- Even $10 orders can move the market slightly
- Must respect depth constraints
- Avoid trading during low liquidity periods

---

## Section G: Moon Bag Configuration

### G.1 Moon Bag Adjustments for $100

## Risk Category: Moon Bag Protection

### Thresholds (DISABLED or MINIMAL)
- MoonBagPercentage: **0%** (Default: 15%; DISABLE for $100)
- MinimumMoonBagUsd: **$100** (Effectively disabled since account is $100)
- AutoReleaseEnabled: **true** (If ever activated, auto-release enabled)
- AutoReleaseUnrealizedLossPercent: **-10%** (Earlier release for small account)

### Rules
1. IF AccountEquity <= $150 THEN disable_moon_bag_entirely
2. IF moon_bag_activated AND unrealized_loss > 10% THEN auto_release_immediately
3. Moon bag only activates when AccountEquity >= $250 AND position_profit >= 20%

### Edge Cases
- Scenario: Moon bag activated then account drops below $150
  Response: Auto-release moon bag; too much capital locked
- Scenario: User manually enables moon bag at $100
  Response: System override; log warning; disable moon bag

### Priority Level: LOW (feature disabled at this capital level)

### Rationale for Disabling Moon Bag

Moon bag concept (holding 15% of winning position indefinitely) requires:
1. Sufficient position size to matter (15% of $10 = $1.50 - meaningless)
2. Ability to absorb opportunity cost of locked capital
3. Psychological comfort with reduced liquidity

At $100, every dollar matters. Locking even $5 in a "moon bag" reduces trading capital by 5%.

**Recommendation:** Enable moon bag when account reaches $500+.

---

## Section H: Complete Production Configuration

### H.1 Recommended appsettings.Production.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Lighter": {
    "DryRun": false,
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "ChainId": 304
  },
  "TradingBot": {
    "AutoStartTrading": false,
    "Symbol": "BTC",
    "DecisionLoopIntervalMs": 5000,

    "Capital": {
      "MaxPositionSizePercent": 20,
      "MaxOrderSizePercent": 10,
      "MinOrderSizeUsd": 5,
      "MaxLeverage": 1.5,
      "MaxAggregateLeverage": 1.0,
      "ReserveBalancePercent": 50,
      "MaxDeployedCapitalPercent": 50,
      "MaxPerMarketPercent": 50
    },

    "LossLimits": {
      "Rolling24HourLossPercent": -8,
      "Rolling7DayLossPercent": -15,
      "Rolling30DayLossPercent": -20,
      "MaxDrawdownPercent": -25,
      "SingleTradeLossPercent": -3,
      "DrawdownPositionReductionPercent": 75,
      "Rolling24HourRecoveryWaitHours": 4,
      "Rolling7DayRecoveryWaitHours": 24,
      "Rolling30DayRecoveryWaitHours": 72
    },

    "Grid": {
      "MinSpacing": 0.3,
      "MaxSpacing": 1.5,
      "DefaultSpacing": 0.6,
      "MinOrdersPerSide": 2,
      "MaxOrdersPerSide": 3,
      "DefaultOrdersPerSide": 3,
      "MinWidth": 2,
      "MaxWidth": 10
    },

    "FlashCrash": {
      "OneMinuteDropPercent": -2,
      "FiveMinuteDropPercent": -4,
      "FifteenMinuteDropPercent": -7,
      "OneHourDropPercent": -10,
      "BlackSwanThresholdPercent": -15,
      "BlackSwanPositionTargetPercent": 0.50,
      "BlackSwanHaltDurationHours": 24,
      "BlackSwanRequiresManualRestart": true,
      "MaxEventsIn24Hours": 2
    },

    "FlashPump": {
      "OneMinuteGainPercent": 2,
      "FiveMinuteGainPercent": 4,
      "FifteenMinuteGainPercent": 7,
      "OneHourGainPercent": 10,
      "MaxEventsIn24Hours": 2
    },

    "PreTrade": {
      "MinOrderBookDepthUsd": 10000,
      "CriticalDepthThresholdUsd": 5000,
      "MaxOrderToDepthRatio": 0.05,
      "MaxAcceptableSpreadPercent": 0.5,
      "MaxDataAgeSeconds": 3
    },

    "Liquidity": {
      "MinBookDepthUsd": 10000,
      "CriticalBookDepthUsd": 5000,
      "MaxBidAskSpreadPercent": 0.5,
      "VolumeWarningPercent": 50,
      "VolumeCriticalPercent": 25,
      "MaxFundingRatePercent": 0.05,
      "CriticalFundingRatePercent": 0.1
    },

    "MoonBag": {
      "MoonBagPercentage": 0,
      "MinimumMoonBagUsd": 100,
      "AutoReleaseEnabled": true,
      "AutoReleaseUnrealizedLossPercent": -0.10
    },

    "Trend": {
      "EmaFastPeriod": 20,
      "EmaSlowPeriod": 50,
      "ConfirmationDelayMinutes": 15,
      "RebalanceTolerancePercent": 10,
      "MaxRebalanceRatePercent": 5
    },

    "DecisionEngine": {
      "MaxWebSocketDataAgeSeconds": 10,
      "SilenceDetectionSeconds": 30,
      "ExtendedOutageMinutes": 5,
      "MaxReconnectCyclesIn5Min": 3
    },

    "Nonce": {
      "WarningThreshold": 2,
      "HaltThreshold": 3,
      "RecoverySuccessCount": 10
    }
  }
}
```

---

## Section I: Account Wipeout Scenarios

### I.1 Scenarios That Could Lose 100% of Capital

| Scenario | Probability | Mechanism | Prevention |
|----------|-------------|-----------|------------|
| Black Swan Crash | LOW | BTC drops 30%+ in hours | Tightened black swan at -15%; halt at -25% drawdown |
| Liquidation | LOW | Leverage + adverse move | Max 1.5x leverage; liquidation at ~33% move |
| Funding Rate Drain | MEDIUM | Persistent negative funding | Monitor funding; reduce position if > 0.05% |
| Cascading Losses | MEDIUM | Multiple small losses accumulate | Daily loss limit -8% halts trading |
| Technical Failure | MEDIUM | WebSocket disconnect during position | WebSocket health monitoring; pause on disconnect |
| Thin Market Slippage | HIGH | Order fills at bad price | Pre-trade depth validation; 5% max impact |
| Fee Drag | HIGH | Frequent trades eat capital | Min 0.3% grid spacing; reduce frequency |

### I.2 Critical Failure Modes

**1. WebSocket Disconnect During Black Swan**
- Risk: Cannot execute protective orders
- Mitigation: WebSocket health monitoring pauses grid; manual review required
- Residual Risk: Position exposed until reconnection; accept this at $100 level

**2. API Nonce Desync**
- Risk: Cannot place ANY orders including protective ones
- Mitigation: Nonce health monitoring; auto-resync; halt after 3 failures
- Residual Risk: Minutes of exposure during resync

**3. Lighter DEX Smart Contract Bug**
- Risk: Funds locked or lost
- Mitigation: NONE (systemic risk of DeFi)
- Residual Risk: Total loss possible; only deploy what you can afford to lose

**4. Market Gap Through Stop Loss**
- Risk: Stop loss at -10% but market gaps to -15%
- Mitigation: No stop loss orders (grid logic only); protective mode instead
- Residual Risk: Unrealized loss until fill

---

## Section J: Deployment Checklist

### J.1 Pre-Deployment (24-48 Hours Before)

- [ ] Run DryRun mode for 48+ hours on mainnet data
- [ ] Verify all WebSocket channels connected and receiving data
- [ ] Confirm account balance shows $100+ on dashboard
- [ ] Test manual order placement (single $5 order, then cancel)
- [ ] Verify nonce synchronization successful
- [ ] Review current BTC volatility (ATR 14-day)
- [ ] Check Lighter DEX order book depth (should be > $10k)
- [ ] Set up monitoring alerts (Discord/Telegram webhook for CRITICAL logs)

### J.2 Go-Live Sequence

1. **T-60 minutes:** Final dry run verification
2. **T-30 minutes:** Switch `DryRun: false` in config
3. **T-15 minutes:** Restart application; verify startup sequence
4. **T-5 minutes:** Verify WebSocket connected; account balance correct
5. **T-0:** Set `AutoStartTrading: true` OR manually trigger start
6. **T+5 minutes:** Verify first grid orders placed
7. **T+15 minutes:** Verify decision loop running (check logs)
8. **T+60 minutes:** First health check; review any warnings

### J.3 Observation Period Recommendations

| Phase | Duration | Capital at Risk | Actions |
|-------|----------|-----------------|---------|
| Phase 1: Passive | 7 days | 50% ($50) | Observe only; verify system operates correctly |
| Phase 2: Limited | 7-14 days | 50% ($50) | Trade but maintain 50% reserve; review daily |
| Phase 3: Active | 14-30 days | 60% ($60) | Increase deployment to 60%; reduce reserve to 40% |
| Phase 4: Full | 30+ days | 70% ($70) | If profitable, increase to 70% deployed |

### J.4 Daily Monitoring Checklist

- [ ] Check 24h P&L (should be within -8% to +8%)
- [ ] Verify WebSocket health status (no disconnects > 5 min)
- [ ] Review any CRITICAL or WARNING logs
- [ ] Check current drawdown from peak
- [ ] Verify grid orders active (correct number per side)
- [ ] Check funding rate paid/received
- [ ] Review any rejected orders (pre-trade validation)

---

## Section K: Key Metrics to Monitor

### K.1 Health Metrics (Check Every 4 Hours)

| Metric | Healthy Range | Warning | Critical |
|--------|---------------|---------|----------|
| WebSocket Connected | Yes | Reconnecting | Disconnected > 5min |
| Orders Active | 4-6 | 2-3 or 7-8 | 0-1 or > 10 |
| Data Age | < 10s | 10-30s | > 30s |
| Nonce Failures (24h) | 0 | 1-2 | 3+ |
| Spread | < 0.5% | 0.5-1.0% | > 1.0% |

### K.2 Performance Metrics (Daily Review)

| Metric | Target | Acceptable | Concerning |
|--------|--------|------------|------------|
| Daily P&L | +0.5% | -2% to +2% | < -5% |
| Win Rate | > 50% | 40-60% | < 35% |
| Avg Win/Loss Ratio | > 1.2 | 0.8-1.5 | < 0.7 |
| Orders Filled | 5-20/day | 2-30/day | 0 or > 50 |
| Grid Utilization | 50-80% | 30-90% | < 20% or > 95% |

### K.3 Risk Metrics (Weekly Review)

| Metric | Target | Acceptable | Action Required |
|--------|--------|------------|-----------------|
| Weekly Drawdown | < 5% | 5-10% | > 10%: Review strategy |
| Sharpe Ratio (annualized) | > 1.0 | 0.5-2.0 | < 0.3: Consider pause |
| Max Single Loss | < 3% | 3-5% | > 5%: Tighten limits |
| Recovery Factor | > 2.0 | 1.0-3.0 | < 1.0: System ineffective |

---

## Section L: Answers to Your Specific Questions

### L.1 Should I Use Leverage with $100?

**RECOMMENDATION: Start with 1.0x (no leverage), graduate to 1.5x after 2 weeks of profitable operation.**

Rationale:
- Leverage amplifies LOSSES more than gains psychologically
- At $100, the absolute dollar amounts are small anyway
- System validation is more important than profit maximization
- 1.5x leverage only makes sense after proving the system works

### L.2 What Position Sizes Make Sense for $100?

**RECOMMENDATION: $5-10 per order, $20 max per position, $50 max total deployed.**

Rationale:
- Lighter minimum order ~$5
- 6 grid orders x $10 = $60 total grid
- 50% reserve = $50 always available
- Single position max $20 = 20% of capital (manageable risk)

### L.3 Is 0.8% Grid Spacing Appropriate for BTC?

**RECOMMENDATION: Use 0.6% default spacing.**

Rationale:
- 0.8% is reasonable but slightly wide for current BTC volatility
- 0.6% captures more small moves while staying above fee threshold
- BTC at $100k: 0.6% = $600 per grid level
- Daily BTC range typically 2-4%, so 0.6% captures 3-6 fills per day

### L.4 Are Current Loss Limits Appropriate for $100?

**RECOMMENDATION: TIGHTEN all limits.**

| Limit | Your Current | Recommended | Reason |
|-------|--------------|-------------|--------|
| Daily | -15% | -8% | Faster halt preserves more capital |
| Weekly | -25% | -15% | $15 loss is recoverable |
| Monthly | -30% | -20% | $20 loss still allows operation |
| Drawdown | -35% | -25% | $25 loss = 25% gone = hard to recover |

### L.5 Is $10,000 Depth Threshold Appropriate?

**RECOMMENDATION: Reduce to $5,000 critical threshold.**

Rationale:
- Lighter DEX has lower liquidity than centralized exchanges
- $10k threshold may reject too many orders
- $5k critical threshold still protects against truly illiquid conditions
- $10k as warning threshold is appropriate

### L.6 How Many Grid Levels Can We Realistically Place?

**RECOMMENDATION: 3 orders per side (6 total).**

Math:
- $100 capital
- 50% reserve = $50 for grid
- Minimum order $5
- $50 / $5 = 10 orders maximum
- BUT: Want $10+ per order for meaningful fills
- $50 / $10 = 5 orders
- Round to 6 orders (3 per side) x $8.33 each

### L.7 Is 30% Reserve Too Much?

**RECOMMENDATION: Increase to 50% reserve for $100 account.**

Rationale:
- $100 is already tiny; 30% = $30 reserve
- Funding, fees, emergency actions need buffer
- 50% reserve = $50 deployed, $50 reserve
- Safer for learning phase

### L.8 Any BTC-Specific Recommendations?

**RECOMMENDATIONS:**

1. **Avoid major news events:** Fed meetings, halving, ETF announcements
2. **Weekend trading:** Lower liquidity; consider wider spread tolerance
3. **Funding rate awareness:** BTC perpetual funding can be 0.01-0.1% per 8h
4. **Correlation risk:** BTC moves often drag entire market; monitor ETH correlation
5. **Asian session:** 00:00-08:00 UTC typically lower volume on Lighter

---

## Section M: Final Recommendations Summary

### M.1 Configuration Changes from Current

| Setting | Current | Recommended | Change |
|---------|---------|-------------|--------|
| MaxPositionSizePercent | 5% | 20% | INCREASE (allow bigger positions) |
| MaxOrderSizePercent | 3% | 10% | INCREASE (meaningful order sizes) |
| MaxLeverage | 2x | 1.5x | DECREASE (more conservative) |
| MaxAggregateLeverage | 1.5x | 1.0x | DECREASE (no portfolio leverage) |
| ReserveBalancePercent | 30% | 50% | INCREASE (more buffer) |
| MinSpacing | 0.2% | 0.3% | INCREASE (fee protection) |
| DefaultSpacing | 0.8% | 0.6% | DECREASE (more fill opportunities) |
| MinOrdersPerSide | 4 | 2 | DECREASE (capital constraint) |
| MaxOrdersPerSide | 6 | 3 | DECREASE (capital constraint) |
| Rolling24HourLossPercent | -15% | -8% | TIGHTEN |
| MaxDrawdownPercent | -35% | -25% | TIGHTEN |
| CriticalDepthThresholdUsd | $10,000 | $5,000 | DECREASE (DEX reality) |
| Flash crash thresholds | Default | -2%/-4%/-7%/-10% | TIGHTEN ALL |
| MoonBagPercentage | 15% | 0% | DISABLE |

### M.2 Critical Success Factors

1. **Survive the first month:** Capital preservation is the only goal
2. **Learn the system:** Understand every log, every decision, every fill
3. **Validate assumptions:** Does grid trading work on Lighter DEX?
4. **Document everything:** What works, what doesn't, unexpected behaviors
5. **Graduate capital slowly:** Only increase deployment after proven success

### M.3 When to STOP Trading

**IMMEDIATE HALT TRIGGERS:**
- Single day loss > 10% of starting capital ($10)
- Cumulative loss > 25% of starting capital ($25)
- 3+ consecutive losing days
- WebSocket disconnects > 5 times in 24 hours
- Any CRITICAL error that isn't understood

---

## Appendix A: Glossary

- **ATR:** Average True Range - volatility measure
- **Drawdown:** Peak-to-trough decline in equity
- **Grid:** Network of limit orders at fixed price intervals
- **Leverage:** Borrowed capital multiplying exposure
- **Moon Bag:** Reserved portion never sold (disabled at $100)
- **Nonce:** Transaction counter for blockchain operations
- **Slippage:** Difference between expected and executed price
- **Spread:** Difference between best bid and best ask

---

## Appendix B: Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-12-13 | Trading Risk Manager Agent | Initial comprehensive framework |

---

**END OF DOCUMENT**
