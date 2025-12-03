# Session 10: Fix Grid Initialization Exception & Add Auto-Start

## Date
2025-12-02

## Objective
Fix startup exception when grid bot starts in Paused state and add configurable auto-start option.

## Problem
```
System.InvalidOperationException
Message=Cannot initialize grid: trading state is Paused
Source=GridBot.ApiService
at GridBot.ApiService.Services.Grid.GridLifecycleService.InitializeGridAsync
```

## Root Cause Analysis
1. `TradingStateService` defaults to `TradingState.Paused` (line 16)
2. `TradingBotHostedService.StartAsync` calls `_decisionEngine.InitializeAsync()`
3. `TradingDecisionEngine.InitializeAsync` unconditionally calls `_gridLifecycle.InitializeGridAsync()`
4. `GridLifecycleService.InitializeGridAsync` throws if state is not `Active`

## Solution

### Part 1: Fix Grid Initialization
Modified `TradingDecisionEngine.cs`:

1. **`InitializeAsync` (line 438-454):** Now checks trading state before initializing grid
   - Only initializes grid if state is `Active`
   - Logs informational message when skipping due to Paused state

2. **STEP 6 in `ExecuteDecisionCycleAsync` (line 320-326):** Added lazy grid initialization
   - Checks if grid exists before update
   - Initializes grid on first active cycle if needed
   - Handles transition from `Paused` → `Active` gracefully

### Part 2: Add Auto-Start Configuration
Added `AutoStartTrading` option to control whether bot starts in Active state automatically.

**Files Modified:**
- `GridBot.ApiService/Configuration/TradingBotOptions.cs` - Added `AutoStartTrading` property (default: false)
- `GridBot.ApiService/Services/TradingBotHostedService.cs` - Added auto-start logic in `StartAsync`
- `GridBot.ApiService/appsettings.json` - Added `TradingBot.AutoStartTrading: false`
- `GridBot.ApiService/appsettings.Development.json` - Added `TradingBot.AutoStartTrading: true`
- `GridBot.ApiService/appsettings.Production.json` - Added `TradingBot.AutoStartTrading: false`

## New Startup Flow

### Development (AutoStartTrading: true)
1. Bot starts → State is `Paused` (default)
2. LoadPersistedStateAsync runs (may load saved state)
3. AutoStartTrading check → Transitions to `Active`
4. Decision engine initializes → Grid is initialized
5. Decision loop starts → Bot begins trading

### Production (AutoStartTrading: false)
1. Bot starts → State is `Paused` (default)
2. LoadPersistedStateAsync runs (may load saved state)
3. AutoStartTrading is false → State remains `Paused`
4. Decision engine initializes → Grid skipped (not Active)
5. Decision loop starts → Skips cycles until manually activated via `/api/trading/control/resume`

## Configuration Reference

```json
{
  "TradingBot": {
    "AutoStartTrading": true  // Development
    // "AutoStartTrading": false  // Production (default, safer)
  }
}
```

## Status
**COMPLETE**
