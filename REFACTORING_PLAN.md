# GridBot Refactoring Plan: From Complex to Simple

## Executive Summary

The current GridBot.ApiService is massively overcomplicated for a grid trading bot:
- 75 service files (vs ~10 needed for simple grid)
- ~15,000-18,000 lines of service code (vs ~3,000 needed)
- 160+ configuration parameters (vs ~20 needed)
- 16 combined states (vs 2 needed: Active/Paused)

This plan simplifies the codebase by extracting advanced features into optional modules.

## Current Architecture (75 Services)

| Folder | Files | Purpose | Assessment |
|--------|-------|---------|------------|
| DecisionEngine | 4 | Main orchestrator + recovery | CRITICAL BLOAT |
| Grid | 6 | Core grid trading logic | OVERCOMPLICATED |
| MoonBag | 8 | Infinite upside protection | ADVANCED - EXTRACT |
| Trend | 4 | Trend detection | ADVANCED - EXTRACT |
| Risk | 10 | Flash crash, loss monitoring | PARTIALLY NEEDED |
| Inventory | 2 | Position analysis | ADVANCED - EXTRACT |
| Rebalancing | 2 | Position rebalancing | ADVANCED - EXTRACT |
| Connectivity | 4 | WebSocket/Nonce health | SIMPLIFY |
| MarketData | 6 | Price/orderbook data | KEEP |
| Persistence | 4 | Redis state storage | SIMPLIFY |
| Others | 25+ | Various support | REVIEW EACH |

## Complexity Hotspots

| File | Lines | Dependencies | Problem |
|------|-------|--------------|---------|
| TradingDecisionEngine.cs | 1408 | 17 services | Too many responsibilities |
| GridLifecycleService.cs | 1000+ | 11 services | Overcomplicated |
| GridOrderManager.cs | 900+ | 8 services | Embedded validation |
| MoonBagManager.cs | 1000+ | 7 services | Complex state machine |
| TrendIntelligenceService.cs | 400+ | 5 services | Not core to grid |

## Proposed New Architecture

```
GridBot/
+-- GridBot.Core/                 # NEW: Minimal grid bot (~3000 lines)
|   +-- Services/
|   |   +-- Grid/                 # GridCalculator, GridManager (simplified)
|   |   +-- Risk/                 # BasicRiskMonitor (flash crash + loss limit)
|   |   +-- MarketData/           # MarketDataService (REST only)
|   |   +-- Engine/               # SimpleTradingEngine (~400 lines)
|   +-- Models/                   # Essential models only (~15 types)
|   +-- Configuration/            # ~20 parameters
|
+-- GridBot.TrendIntelligence/    # EXTRACTED: Optional trend module
|   +-- README.md
|   +-- TrendDetector, InventoryManager, RebalancingService
|
+-- GridBot.MoonBag/              # EXTRACTED: Optional moon bag module
|   +-- README.md
|   +-- MoonBagManager, TrailingStopService, TrailingGridService
|
+-- GridBot.AdvancedRisk/         # EXTRACTED: Optional advanced risk
|   +-- README.md
|   +-- RecoveryManager, FlashPumpDetector, LiquidityMonitor
|
+-- GridBot.ApiService/           # SIMPLIFIED: Thin API layer
    +-- Program.cs                # ~200 lines (endpoints only)
```

## Components to Extract

### 1. GridBot.TrendIntelligence

What it does:
- Detects market trend using EMA/MACD/ADX
- Calculates target inventory skew (20/80 bear vs 80/20 bull)
- Triggers rebalancing trades to match trend

Why separate: A simple grid bot does not need trend following. It trades the range regardless of trend.

Files to move:
- Services/Trend/*
- Services/Inventory/*
- Services/Rebalancing/*
- Services/Indicators/IndicatorService.cs (EMA/MACD parts)

### 2. GridBot.MoonBag

What it does:
- Reserves 10-15% of position as moon bag during breakouts
- Implements tiered trailing stops (15% -> 10% -> 7% -> 5%)
- Shifts grid upward during sustained rallies

Why separate: Premium feature for long-only strategies. Complex state machine adds overhead.

Files to move:
- Services/MoonBag/*
- Models/Trading/MoonBagState.cs

### 3. GridBot.AdvancedRisk

What it does:
- Multi-phase recovery from protective mode
- Flash pump detection
- Advanced liquidity monitoring
- Risk event logging and webhooks

Why separate: Basic grid bot needs only flash crash pause + loss limits.

Files to move:
- Services/DecisionEngine/RecoveryManager.cs
- Services/Risk/FlashPumpDetector.cs
- Services/Risk/RiskEventLogger.cs
- Services/Notifications/*

## Core Simplifications

### TradingDecisionEngine: 1408 -> 400 lines

Remove:
- Moon bag checks (separate module)
- Trend intelligence (separate module)
- Recovery management (simple cooldown instead)
- Complex timeout escalation (simple retry)
- 9 concurrent dictionaries (1 simple state object)

Keep:
- Fetch price + account data
- Check basic risk limits
- Run grid logic
- Log metrics

### Trading States: 16 -> 2

Current: TradingState (7) + RecoveryPhase (4) + MoonBagState (5)
Simplified: Active, Paused

### Configuration: 160 -> 20 parameters

Essential config only:
- Grid: spacing, levels, order size
- Risk: max daily loss, flash crash threshold, cooldown
- Position: max position %, max order %
- Exchange: market, leverage
- Timing: loop interval, retries

## Success Metrics

| Metric | Current | Target |
|--------|---------|--------|
| Service files | 75 | ~15 |
| Lines of code | 15,000+ | ~3,000 |
| Config params | 160 | 20 |
| Dependencies per engine | 17 | 5 |
| Trading states | 16 | 2 |
| Time to understand | Hours | 30 min |

## Next Steps

1. Create GridBot.TrendIntelligence.csproj with README
2. Create GridBot.MoonBag.csproj with README
3. Create GridBot.AdvancedRisk.csproj with README
4. Create GridBot.Core.csproj with SimpleTradingEngine
5. Migrate and test each module
6. Simplify GridBot.ApiService to thin layer
