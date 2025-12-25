# MVP Risk Management Plan - Quick Reference

## Deploy These 7 Features ONLY (Week 1-2)

1. **Position Limits:** Max 10% equity per position, 2% per order
2. **Loss Limits:** -5% (24h), -10% (7d), -15% (30d), -20% max drawdown
3. **Liquidation Prevention:** 15%/25%/40% margin thresholds  
4. **Flash Crash (1min):** -8%/-12%/-20%/-30% graduated response
5. **Liquidity:** 10k depth, 100k/hr volume, 0.5% max spread
6. **API Health:** 30sec WS tolerance, 5 failures max, 60sec staleness
7. **Nonce:** Serialize orders (no parallel), 3 retries, auto-resync

## Grid Config (MVP)
- Fixed 0.5% spacing
- 10 buy + 10 sell levels
- 50/50 crypto/cash (no trend adaptation)
- Instant resume (no recovery phases)

## DO NOT Build (MVP)
- ATR spacing (use fixed)
- Trend detection (use 50/50)
- Moon bag (sell full position)
- Trailing stops (use grid levels)
- Rebalancing (let grid handle)
- Multi-window flash (1min only)

## When to Add Features

**Phase 2 (Week 3-4):** ATR spacing, funding monitoring, flash pump (if shorting)
**Phase 3 (Month 2+):** Trend (EMA only), moon bag (manual, long only), 2-phase recovery
**Never:** Auto-rebalancing, multi-window flash, auto moon bag release, short moon bag, tiered trailing

## Critical Rules



## Testing Checklist
- Position limit rejects >10%
- Loss limit triggers at -5%
- Flash crash pauses at -12%
- Margin trigger at 20%
- WS disconnect at 35sec
- Nonce resync on desync
- Thin book widens spreads
- Partial fill at limit
- 5% price gap reset

## Success = Safety Validation (Not Profit)
1. No liquidations
2. Loss limits work
3. Grid operates normally
4. Crashes handled
5. API degrades gracefully

Start simple. Iterate on real data. Add only when pain points emerge.

See full plan: C:/Users/stany/source/repos/plan/GridBot/.claude/doc/mvp-risk-summary.md
