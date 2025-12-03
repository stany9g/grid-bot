# ALTE Implementation Plan

## Project: Adaptive Liquidity & Trend Engine (Codename: Nexus)

### Document Purpose
This document outlines the phased implementation plan for the ALTE trading bot, tracking progress from foundation to full autonomous trading capability.

---

## Implementation Phases Overview

| Phase | Name | Description | Status |
|-------|------|-------------|--------|
| 1 | Foundation | Core infrastructure, data models, configuration | COMPLETE |
| 2 | Market Data | Real-time price feeds, indicator calculations | COMPLETE |
| 3 | Grid Engine | Dynamic grid geometry and order management | COMPLETE |
| 4 | Trend Intelligence | Trend detection and inventory management | COMPLETE |
| 5 | Risk Sentinel | Circuit breakers and safety systems | COMPLETE |
| 6 | Moon Bag Module | Trailing grid and position protection | COMPLETE |
| 7 | Integration | Full system integration and testing | COMPLETE |
| 8 | Deployment | Production deployment and monitoring | COMPLETE |

---

## Phase 1: Foundation

### Objectives
- Establish core domain models and configuration system
- Create trading state management infrastructure
- Set up background service framework

### Tasks

#### 1.1 Domain Models
- [ ] Create `TradingState` enum (ACTIVE, PAUSED, HALTED, RECOVERING)
- [ ] Create `TrendState` enum (STRONG_BULL, MILD_BULL, NEUTRAL, MILD_BEAR, STRONG_BEAR)
- [ ] Create `RiskEvent` model for logging risk triggers
- [ ] Create `GridConfiguration` model with all parameters from risk spec
- [ ] Create `InventoryState` model (current skew, target skew, rebalance needed)
- [ ] Create `MarketMetrics` model (ATR, volume, order book depth)

#### 1.2 Configuration System
- [ ] Create `TradingBotOptions` configuration class
- [ ] Define appsettings.json schema for all configurable parameters
- [ ] Implement configuration validation on startup
- [ ] Create `IRiskConfiguration` interface for runtime access

#### 1.3 State Management
- [ ] Create `ITradingStateService` interface
- [ ] Implement `TradingStateService` with thread-safe state transitions
- [ ] Create state change events for pub/sub
- [ ] Add state persistence (Redis or database)

#### 1.4 Background Service Infrastructure
- [ ] Create `TradingBotHostedService` as main orchestrator
- [ ] Implement graceful shutdown with position safety
- [ ] Create health check for trading bot status
- [ ] Add Aspire integration for the hosted service

### Deliverables
- Domain model classes in `GridBot.ApiService/Models/`
- Configuration classes in `GridBot.ApiService/Configuration/`
- State management service ready for Phase 2

---

## Phase 2: Market Data

### Objectives
- Implement real-time market data ingestion
- Build technical indicator calculation engine
- Create caching layer for market metrics

### Tasks

#### 2.1 Price Feed Service
- [ ] Create `IMarketDataService` interface
- [ ] Implement `MarketDataService` with Lighter API integration
- [ ] Add price history caching (in-memory + Redis)
- [ ] Implement OHLCV candle aggregation

#### 2.2 Technical Indicators
- [ ] Create `IIndicatorService` interface
- [ ] Implement ATR (Average True Range) calculation - 14 period
- [ ] Implement EMA (Exponential Moving Average) - 20, 50 periods
- [ ] Implement MACD (12, 26, 9)
- [ ] Implement ADX (Average Directional Index)
- [ ] Add indicator caching with configurable refresh

#### 2.3 Order Book Analysis
- [ ] Create `IOrderBookAnalyzer` interface
- [ ] Implement order book depth calculation
- [ ] Implement liquidity cluster detection
- [ ] Implement bid/ask imbalance calculation
- [ ] Add spread monitoring

#### 2.4 Market Metrics Aggregation
- [ ] Create `IMarketMetricsService` interface
- [ ] Aggregate all metrics into `MarketMetrics` model
- [ ] Implement volume tracking (24h, 7d average)
- [ ] Add funding rate monitoring

### Deliverables
- Market data services in `GridBot.ApiService/Services/MarketData/`
- Indicator library in `GridBot.ApiService/Services/Indicators/`
- All metrics available via dependency injection

---

## Phase 3: Grid Engine

### Objectives
- Implement dynamic grid geometry calculation
- Build order placement and management system
- Create grid visualization for UI

### Tasks

#### 3.1 Grid Calculator
- [ ] Create `IGridCalculator` interface
- [ ] Implement ATR-based grid spacing calculation (0.2% - 2.0%)
- [ ] Implement grid level calculation with liquidity bias
- [ ] Add grid width constraints (min 5%, max 30%)
- [ ] Implement order book awareness for level placement

#### 3.2 Grid Order Manager
- [ ] Create `IGridOrderManager` interface
- [ ] Implement grid order placement via `ILighterCommandClient`
- [ ] Implement order tracking (pending, filled, cancelled)
- [ ] Add order batching (up to 10 per transaction)
- [ ] Implement stale order detection and cleanup

#### 3.3 Grid Lifecycle
- [ ] Create `IGridLifecycleService` interface
- [ ] Implement grid initialization at current price
- [ ] Implement grid shifting (up/down with price)
- [ ] Implement grid rebuild after price gaps
- [ ] Add grid pause/resume functionality

#### 3.4 Grid State Persistence
- [ ] Persist current grid configuration
- [ ] Track order fill history
- [ ] Implement grid recovery after restart

### Deliverables
- Grid engine services in `GridBot.ApiService/Services/Grid/`
- Grid state models and persistence
- API endpoints for grid management

---

## Phase 4: Trend Intelligence

### Objectives
- Implement trend state detection
- Build inventory management system
- Create rebalancing logic

### Tasks

#### 4.1 Trend Detector
- [ ] Create `ITrendDetector` interface
- [ ] Implement trend state calculation using EMA/MACD/ADX
- [ ] Add trend confirmation delay (15 minutes)
- [ ] Implement trend flip cooldown (2 hours on rapid flips)
- [ ] Add conflicting indicator handling

#### 4.2 Inventory Manager
- [ ] Create `IInventoryManager` interface
- [ ] Calculate current inventory skew (crypto/USDT ratio)
- [ ] Determine target skew based on trend state
- [ ] Calculate rebalance delta
- [ ] Implement skew tolerance (5% band)

#### 4.3 Rebalancing Engine
- [ ] Create `IRebalancingService` interface
- [ ] Implement gradual rebalancing (max 10% per hour)
- [ ] Implement emergency rebalancing (when delta > 30%)
- [ ] Add rebalance order placement
- [ ] Track rebalancing progress

#### 4.4 Trend State Persistence
- [ ] Store trend state history
- [ ] Track trend flip events
- [ ] Log indicator values for analysis

### Deliverables
- Trend services in `GridBot.ApiService/Services/Trend/`
- Inventory management services
- Rebalancing logic integrated with grid

---

## Phase 5: Risk Sentinel

### Objectives
- Implement all circuit breakers from risk specification
- Build real-time risk monitoring
- Create alerting system

### Tasks

#### 5.1 Loss Limit Monitor
- [ ] Create `ILossMonitor` interface
- [ ] Track daily P&L
- [ ] Track weekly P&L
- [ ] Track monthly P&L
- [ ] Track drawdown from ATH
- [ ] Implement automatic trading halt on breach

#### 5.2 Flash Crash Detector
- [ ] Create `IFlashCrashDetector` interface
- [ ] Implement 1-minute drop detection (3%)
- [ ] Implement 5-minute drop detection (5%)
- [ ] Implement 15-minute drop detection (10%)
- [ ] Implement 1-hour drop detection (15%)
- [ ] Add automatic response actions

#### 5.3 Liquidity Monitor
- [ ] Create `ILiquidityMonitor` interface
- [ ] Monitor 24h volume vs 7d average
- [ ] Monitor order book depth
- [ ] Monitor bid/ask spread
- [ ] Monitor funding rate
- [ ] Implement automatic spread widening

#### 5.4 Risk Event Logger
- [ ] Create risk event storage
- [ ] Log all rule triggers with context
- [ ] Track rule actions taken
- [ ] Build risk event query API

#### 5.5 Alert System
- [ ] Create `IAlertService` interface
- [ ] Implement severity levels (CRITICAL, HIGH, MEDIUM, LOW)
- [ ] Add dashboard notification
- [ ] Add email alerts (optional)
- [ ] Add Telegram/Discord alerts (optional)

### Deliverables
- Risk monitoring services in `GridBot.ApiService/Services/Risk/`
- Alert system
- Risk dashboard endpoints

---

## Phase 6: Moon Bag Module

### Objectives
- Implement trailing grid mechanism
- Build moon bag protection
- Add trailing stop functionality

### Tasks

#### 6.1 Trailing Grid
- [ ] Create `ITrailingGridService` interface
- [ ] Detect price breakout above grid
- [ ] Implement grid shift upward
- [ ] Add shift cooldown (prevent rapid shifts)
- [ ] Track grid shift history

#### 6.2 Moon Bag Protection
- [ ] Create `IMoonBagManager` interface
- [ ] Calculate moon bag threshold (15% of position)
- [ ] Block sell orders for moon bag portion
- [ ] Implement moon bag release conditions
- [ ] Add operator override capability

#### 6.3 Trailing Stop
- [ ] Implement trailing stop calculation (15% below high)
- [ ] Add trail tightening on large profits (10% at 50%+ profit)
- [ ] Handle trailing stop trigger
- [ ] Protect moon bag on stop trigger

### Deliverables
- Moon bag services in `GridBot.ApiService/Services/MoonBag/`
- Position protection logic
- Trailing grid integration

---

## Phase 7: Integration

### Objectives
- Integrate all systems into cohesive trading loop
- Implement decision engine
- Comprehensive testing

### Tasks

#### 7.1 Decision Engine
- [ ] Create `ITradingDecisionEngine` interface
- [ ] Implement main decision loop (runs every few seconds)
- [ ] Integrate trend analysis
- [ ] Integrate inventory check
- [ ] Integrate risk checks
- [ ] Integrate grid optimization
- [ ] Implement rule priority hierarchy

#### 7.2 Recovery Procedures
- [ ] Implement post-loss-limit recovery
- [ ] Implement post-flash-crash recovery
- [ ] Implement post-API-failure recovery
- [ ] Add graceful degradation

#### 7.3 Testing
- [ ] Unit tests for all services
- [ ] Integration tests for decision engine
- [ ] Paper trading simulation
- [ ] Stress testing with historical data
- [ ] Edge case testing

#### 7.4 UI Integration
- [ ] Create Blazor dashboard for trading status
- [ ] Display current grid visualization
- [ ] Show trend state and inventory
- [ ] Show risk metrics and alerts
- [ ] Add manual override controls

### Deliverables
- Fully integrated trading bot
- Test suite
- Dashboard UI

---

## Phase 8: Deployment

### Objectives
- Production deployment
- Monitoring and observability
- Documentation

### Tasks

#### 8.1 State Persistence (Redis) - COMPLETE
- [x] Configure Redis distributed cache with Aspire
- [x] Create IStateRepository interface
- [x] Implement RedisStateRepository with JSON serialization
- [x] Persist recovery state (phase, trigger, timestamps)
- [x] Persist moon bag state (high watermark, locked quantity, state machine)
- [x] Persist trading state and trend state
- [x] Persist loss tracking (daily, weekly, monthly P&L)
- [x] Persist circuit breaker events with 24h TTL
- [x] State loading on startup in TradingBotHostedService

#### 8.2 Telemetry & Metrics - COMPLETE
- [x] Integrate with Aspire OpenTelemetry
- [x] Create TradingMetrics static class with all instruments
- [x] Implement decision loop execution time histogram
- [x] Implement data collection latency histogram
- [x] Implement orders placed/cancelled counters
- [x] Implement circuit breaker triggers counter by type
- [x] Implement state transitions counter by from/to
- [x] Implement observable gauges for multipliers (per-market)
- [x] Implement observable gauges for recovery phase
- [x] Implement observable gauges for price and equity
- [x] Thread-safe gauge storage with ConcurrentDictionary

#### 8.3 Minimal Dashboard (Blazor) - COMPLETE
- [x] Create dashboard API endpoints (`/api/trading/*`)
- [x] Create TradingApiClient for service-to-service calls
- [x] Create Trading Status Panel (state, trend, recovery, multipliers)
- [x] Create Moon Bag Status Panel (locked qty, high watermark, profit)
- [x] Create Risk Indicators Panel (P&L, drawdown, flash crash)
- [x] Create Manual Controls Panel (Pause/Resume/Halt)
- [x] Auto-refresh every 5 seconds with proper timer management
- [x] CancellationTokenSource pattern for graceful shutdown

#### 8.4 Documentation - COMPLETE
- [x] Update implementation-plan.md with Phase 8 details
- [x] Update progress-tracker.md with Phase 8 files
- [x] Create session context for Phase 8
- [x] Document all code review fixes applied

### Deliverables
- Production-ready deployment with state persistence
- Full telemetry and metrics via Aspire
- Minimal Blazor dashboard for monitoring
- Updated documentation

---

## Dependencies

### External Dependencies
- Lighter DEX API (already integrated via `GridBot.Lighter`)
- Redis (already configured via Aspire)
- Native signing library (signer-amd64.dll/so)

### Internal Dependencies
```
Phase 2 → depends on → Phase 1
Phase 3 → depends on → Phase 1, Phase 2
Phase 4 → depends on → Phase 1, Phase 2
Phase 5 → depends on → Phase 1, Phase 2
Phase 6 → depends on → Phase 3, Phase 4
Phase 7 → depends on → Phase 3, Phase 4, Phase 5, Phase 6
Phase 8 → depends on → Phase 7
```

---

## Risk Specification Reference

All numeric thresholds, rules, and edge cases are defined in:
- `.claude/doc/risk-management-specification.md` (detailed specification)
- `doc/risk-management-specification.md` (project documentation)

**Key Reference Sections:**
- Section 1: Global Risk Parameters
- Section 2: Dynamic Grid Geometry Rules
- Section 3: Inventory Management Rules
- Section 4: Moon Bag Rules
- Section 5: Sentinel Risk Protection
- Section 6: Lighter DEX Specifics
- Section 7: Rule Priority and Conflict Resolution

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-11-26 | Claude Code | Initial implementation plan |
| 2.0 | 2025-11-27 | Claude Code | All phases complete - Phase 8 Deployment finalized |
