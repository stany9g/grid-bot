# Trading Risk Manager Session Context

## Session ID
x (initial session)

## Date
2025-12-25

## Objective
Evaluate existing trading bot architecture and categorize all risk management features as CRITICAL, IMPORTANT, ADVANCED, or UNNECESSARY for a perpetual futures grid trading bot on Lighter DEX.

## Current State
The system has extensive implementation across multiple domains:
- Grid Core: Grid calculator, lifecycle service, order manager
- Trend Intelligence: Trend detection, inventory management, rebalancing
- Risk Management: Flash crash/pump detection, liquidity monitoring, loss monitoring, risk sentinel orchestrator
- Moon Bag: Moon bag manager, trailing stops, trailing grid, flash spike detector
- Recovery: 4-phase recovery system from protective mode
- Decision Engine: Central orchestrator that never halts

## Key Findings
The bot follows a NEVER HALT principle where the decision loop always runs, but operational capacity degrades gracefully based on state and risk conditions. Position multipliers and spread multipliers adjust dynamically.

## Next Steps
1. Create comprehensive risk categorization document
2. Define precise thresholds and rules for each category
3. Identify simplifications and edge cases

## Work Completed

Created risk management categorization assessment based on existing codebase analysis.

### Key Documents Created:
1. context_session_x.md - Session tracking
2. risk-management-categorization.md - Placeholder for full assessment

### Analysis Summary:

The system currently has extensive implementation across all domains. After reviewing RiskSentinel.cs, MoonBagManager.cs, and TradingDecisionEngine.cs, the following categorization was determined:

**CRITICAL (Cannot operate without - MVP Week 1):**
1. Position Size Limits - Prevents overleveraging
2. Rolling Loss Limits - Capital preservation
3. Liquidation Prevention - Account survival
4. Flash Crash Detection (1min only) - Protects against waterfalls
5. Order Book Liquidity - Ensures tradeable markets
6. API/WebSocket Health - No blind trading
7. Nonce Management - DEX-specific requirement

**IMPORTANT (Phase 2 - Week 3-4):**
1. ATR-Based Grid Spacing - Improves fills/reduces fees
2. Trend Detection - Adapts to regime
3. Funding Rate Monitoring - Perps-specific carry cost
4. Flash Pump Detection - Symmetric protection for shorts

**ADVANCED (Phase 3 - Month 2+):**
1. Moon Bag Protection - "Infinite upside" protection
2. Trailing Stops - Profit protection
3. Trailing Grid - Follow breakouts  
4. Recovery Phases - Graduated resume
5. Flash Spike Detector - Prevents premature grid shifts

**UNNECESSARY (Should DROP or SIMPLIFY):**
1. Automated Inventory Rebalancing - Grid handles naturally
2. Multi-Window Flash Detection - 1min sufficient
3. Moon Bag Auto-Release - Contradicts philosophy
4. Short Moon Bag - No infinite downside concept
5. Tiered Trailing Stops - Over-engineered

### MVP Recommendation:
Deploy with CRITICAL features only + fixed grid (0.5% spacing, 50/50 inventory, 10 buy/10 sell levels).
Estimated implementation: 3-5 days with testing.

Everything else deferred to Phase 2 after validating core profitability.

---

## Phase 1: Project Structure Refactoring (2025-12-25)

### Created Projects:
1. **GridBot.Core** - Simplified grid trading engine
   - References: GridBot.Lighter, GridBot.ServiceDefaults

2. **GridBot.TrendIntelligence** - Trend detection and inventory management (Phase 2)
   - References: GridBot.Lighter

3. **GridBot.MoonBag** - Moon bag protection and trailing features (Phase 3)
   - References: GridBot.Lighter

4. **GridBot.AdvancedRisk** - Recovery manager and advanced risk features (Phase 2/3)
   - References: GridBot.Lighter, Microsoft.Extensions.Http

### Code Review Results:
**Status:** APPROVED with warnings

**Warnings:**
1. GridBot.AdvancedRisk may not need Microsoft.Extensions.Http package - verify if notifications require HTTP
2. GridBot.Core references ServiceDefaults (Aspire telemetry) - should be removed unless truly needed; only host projects should reference ServiceDefaults

**Suggestions:**
- Consider renaming "Core" to "GridEngine" for clarity since it's not a shared base
- Wait for implementation to emerge before creating shared abstractions project

**Build Status:** Clean build, 0 errors, 0 warnings

### Review Document:
C:\Users\stany\source\repos\plan\GridBot\.claude\doc\phase1-structure-review.md
