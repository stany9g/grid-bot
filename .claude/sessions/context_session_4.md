# Session 4: Dashboard Live Data Investigation

## Problem Statement
The Dashboard shows what appears to be mock/default data instead of live trading algorithm data:
- Grid Visualization: "No grid data available"
- Decision Cycle Metrics: "0ms Cycle Time", "0 Cycles/Min", "0 Errors"
- Trend Intelligence: EMAs show "-", ADX Strength "0.0 (Weak)"
- Risk Assessment: All PnL metrics show "+0.00%"

## Root Cause Analysis

### Architecture Flow
```
TradingBotHostedService (BackgroundService)
    ↓ calls every DecisionLoopIntervalMs
TradingDecisionEngine.ExecuteDecisionCycleAsync()
    ↓ stores results
_lastDecisionResults[marketId] = successResult;
    ↓
Dashboard API endpoint (/api/trading/dashboard)
    ↓ fetches from
decisionEngine.GetLastDecisionResult(marketId)
    ↓ returns null if no cycles have run
Dashboard shows empty/default values
```

### Key Findings

1. **`GetLastDecisionResult(marketId)` returns `null`**
   - Location: `TradingDecisionEngine.cs:667-669`
   - The decision engine only stores results AFTER a decision cycle completes
   - If no cycles have executed, the dictionary is empty → returns null
   - Dashboard checks `if (lastDecisionResult != null)` and skips populating DTOs

2. **Grid State is `null` until initialized**
   - Location: `GridLifecycleService.cs:193-196`
   - `_gridStates` dictionary only populated when `InitializeGridAsync()` is called
   - Grid initialization happens in `TradingDecisionEngine.ExecuteDecisionCycleAsync()` at line 350
   - If trading hasn't started, grid state is null → "No grid data available"

3. **Trend Info depends on decision result**
   - Location: `Program.cs:731-751`
   - `TrendDto` is only built if `lastDecisionResult?.TrendResult != null`
   - If no decision cycle ran, trend info is null

4. **Decision Cycle Info depends on decision result**
   - Location: `Program.cs:754-767`
   - `CycleDto` is only built if `lastDecisionResult != null`
   - Shows 0ms, 0 cycles if no decision results exist

### Why Data Appears "Mock"
The data is NOT mock - it's showing **default values because the trading algorithm hasn't executed any decision cycles yet**. This could happen because:
1. The `TradingBotHostedService` hasn't started or failed to start
2. The decision engine failed to initialize
3. API configuration is missing (Lighter API credentials)
4. The bot is in a state where it skips trading operations

## Services Data Flow

| Data Element | Source Service | Populated When |
|-------------|---------------|----------------|
| Grid State | `GridLifecycleService._gridStates` | `InitializeGridAsync()` called |
| Decision Cycle | `TradingDecisionEngine._lastDecisionResults` | After `ExecuteDecisionCycleAsync()` |
| Trend Info | `TradingDecisionEngine` → `_lastDecisionResults` | After decision cycle with trend analysis |
| Risk Info | `LossMonitor`, `FlashCrashDetector` | Always available (may show defaults) |
| Moon Bag | `MoonBagManager` | Always available (shows "Inactive") |
| Price/Position | `LighterQueryClient` | Real-time from Lighter API |

## Verified Working Components
- **Current Price**: $1,501.00 - This IS live data from Lighter API
- **Position**: 0.000000 - Real from Lighter (no open position)
- **Equity**: $8,970.93 - Real from Lighter API
- **Trading State**: "Skew Correction" - Real from `TradingStateService`
- **Uptime**: 0m 16s - Real from service start time

## Root Problem
The issue is NOT that data is "mock" - it's that **the decision loop hasn't run or failed to execute**. The dashboard correctly shows defaults when no data exists, but this looks like mock data to the user.

## Potential Fixes

### Option A: Show explicit "Not Running" indicators
- Change "0ms" to "Not available" or "No cycles yet"
- Change "No grid data" to "Grid not initialized - trading not started"

### Option B: Ensure trading loop starts correctly
- Check if `TradingBotHostedService` is registered and starting
- Verify `AutoStartTrading` configuration
- Check for errors in the decision engine initialization

### Option C: Both - Better UX AND ensure loop runs
- Improve dashboard messaging for empty states
- Debug why the trading loop isn't producing results

## Next Steps
1. Check if `TradingBotHostedService` is registered in DI
2. Check `AutoStartTrading` configuration setting
3. Look at application logs to see if decision loop is running
4. Verify Lighter API credentials are configured correctly

## ROOT CAUSE IDENTIFIED: Market ID Mismatch

**The Bug**: Dashboard API endpoint hardcodes `const int marketId = 0;` but the trading engine uses `_riskConfig.MarketId` from the `MarketResolver`.

**The MarketResolver** dynamically resolves the configured symbol (e.g., "BTC") to the actual market ID from Lighter DEX. If BTC resolves to market ID 1:
- Decision engine stores results in `_lastDecisionResults[1]`
- Dashboard queries `GetLastDecisionResult(0)` → returns null
- Dashboard shows empty/zero data that looks like mock data

## Fix Applied

### File: `GridBot.ApiService/Program.cs`

**Change 1: `/api/trading/dashboard` endpoint (line 623-638)**
```diff
- const int marketId = 0;
+ IRiskConfiguration riskConfig,  // Added parameter
+ ...
+ var marketId = riskConfig.MarketId;  // Use dynamic market ID
```

**Change 2: `/api/trading/status` endpoint (line 837-844)**
```diff
- const int marketId = 0; // Single market for now
+ IRiskConfiguration riskConfig,  // Added parameter
+ ...
+ var marketId = riskConfig.MarketId;  // Use dynamic market ID
```

## Pre-existing Build Errors (Unrelated)

There are build errors in `LossMonitor.cs` that pre-date this fix:
- `LossMonitor` doesn't implement `ILossMonitor` interface properly
- Missing methods: `RecordTradeAsync`, `RecordEquitySnapshotAsync`, `GetRollingPnlAsync`, etc.

These are unrelated to the dashboard fix and need separate attention.

## ADDITIONAL ISSUE: MarketResolver Using Wrong Matching

### The Bug
The `MarketResolver.cs` used `.Contains()` for symbol matching:
```csharp
// OLD (buggy) - matches ANY market containing "BTC"
var matchingOrderBook = orderBooks.FirstOrDefault(ob =>
    ob.Symbol.Contains(configuredSymbol, StringComparison.OrdinalIgnoreCase));
```

This could match:
- "ETH-**BTC**" ← Could match first! ($1,501 = ETH price)
- "**BTC**-USDC" ← Intended market (~$100k)

### Fix Applied: `MarketResolver.cs`
Changed to priority-based matching:
1. **Exact match**: "BTC-USDC" = "BTC-USDC"
2. **StartsWith + separator**: "BTC" matches "BTC-USDC" but NOT "ETH-BTC"
3. **StartsWith**: "BTC" matches "BTCUSDC"

Also added logging of all available markets for debugging.

## Explanation of Dashboard Terms

### Skew (Inventory Skew)
- **Definition**: Portfolio allocation ratio between crypto and cash
- **50% skew**: 50% crypto, 50% cash (neutral)
- **80% skew**: 80% crypto, 20% cash (bullish - holding more crypto)
- **20% skew**: 20% crypto, 80% cash (bearish - holding more cash)
- **Why showing 50%**: Default value because wrong market has no position

### Moon Bag "Inactive"
- Moon bag protection activates only when:
  1. Position value meets minimum ($50 default)
  2. `InitializeMoonBagAsync()` is called by trading engine
- **Why inactive**: Querying wrong market with no position

## Status
- [x] Identify data flow architecture
- [x] Identify root cause (decision loop not producing results)
- [x] Check TradingBotHostedService registration ✓ (properly registered)
- [x] Verify configuration settings ✓ (AutoStartTrading=true)
- [x] Identify actual root cause: **Market ID mismatch in dashboard API**
- [x] Fix #1: Use dynamic market ID from IRiskConfiguration in Program.cs
- [x] Identify second root cause: **MarketResolver using wrong symbol matching**
- [x] Fix #2: Change MarketResolver to use StartsWith instead of Contains
