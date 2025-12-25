# GridBot.Core

## Purpose

This is the **minimal, simple grid trading bot**. It contains only what is essential for grid trading on Lighter DEX with basic safety features.

Target: ~3,000 lines of code, ~15 services, ~20 configuration parameters.

## What This Module Does

### Core Grid Trading Loop

Every 5 seconds:
1. Fetch current price and account data
2. Check basic risk limits (flash crash, daily loss)
3. If safe: calculate and place/adjust grid orders
4. Log metrics and state

Thats it. No trend detection. No moon bags. No multi-phase recovery.

## Components

### 1. SimpleTradingEngine (~400 lines)

The main orchestrator with minimal dependencies:

```csharp
public class SimpleTradingEngine
{
    // Only 5 dependencies (vs 17 in current TradingDecisionEngine)
    private readonly IMarketDataService _marketData;
    private readonly IGridManager _gridManager;
    private readonly IBasicRiskMonitor _riskMonitor;
    private readonly ITradingMetrics _metrics;
    private readonly ILogger _logger;
    
    public async Task RunCycleAsync()
    {
        var price = await _marketData.GetPriceAsync();
        var riskStatus = _riskMonitor.Check(price);
        
        if (riskStatus.IsSafe)
            await _gridManager.UpdateGridAsync(price);
        else
            await _gridManager.PauseAsync(riskStatus.Reason);
    }
}
```

### 2. GridManager (~500 lines combined)

Simplified grid operations:

**GridCalculator:**
- Calculate grid levels based on fixed spacing
- Determine order sizes
- No ATR-based dynamic adjustment (thats advanced)

**GridOrderManager:**
- Place limit orders at grid levels
- Track fills
- Shift grid when fills occur
- Cancel stale orders

### 3. BasicRiskMonitor (~200 lines)

Essential safety only:

```csharp
public class BasicRiskMonitor
{
    public RiskStatus Check(decimal currentPrice)
    {
        // 1. Flash crash check (1-minute window only)
        if (PriceDroppedMoreThan(8, TimeSpan.FromMinutes(1)))
            return RiskStatus.Pause("Flash crash detected");
            
        // 2. Daily loss limit
        if (TodayLoss > MaxDailyLossPercent)
            return RiskStatus.Pause("Daily loss limit reached");
            
        return RiskStatus.Safe;
    }
}
```

### 4. MarketDataService (~300 lines)

Simple REST-based data fetching:

- Get current price
- Get account balance/positions
- Get active orders
- No WebSocket complexity (add later if needed)

### 5. Configuration (~20 parameters)

```csharp
public class SimpleGridConfig
{
    // Grid
    public decimal GridSpacingPercent { get; set; } = 0.5m;
    public int BuyLevels { get; set; } = 10;
    public int SellLevels { get; set; } = 10;
    public decimal OrderSizeUsdc { get; set; } = 100m;
    
    // Risk
    public decimal MaxDailyLossPercent { get; set; } = 5m;
    public decimal FlashCrashThresholdPercent { get; set; } = 8m;
    public int PauseCooldownMinutes { get; set; } = 15;
    
    // Position
    public decimal MaxPositionPercent { get; set; } = 10m;
    public decimal MaxOrderPercent { get; set; } = 2m;
    
    // Exchange
    public string Market { get; set; } = "BTC-USDC";
    public int MarketIndex { get; set; } = 4;
    public int Leverage { get; set; } = 3;
    
    // Timing
    public int LoopIntervalSeconds { get; set; } = 5;
    public int MaxApiRetries { get; set; } = 3;
    public int ApiTimeoutSeconds { get; set; } = 10;
}
```

## Trading States: Just 2

```csharp
public enum TradingState
{
    Active,     // Grid is trading
    Paused      // Grid is paused (manual resume or cooldown)
}
```

No recovery phases. No degraded modes. Just Active or Paused.

## What This Module Does NOT Include

Intentionally excluded (available in other modules):

| Feature | Module |
|---------|--------|
| Trend detection | GridBot.TrendIntelligence |
| Inventory rebalancing | GridBot.TrendIntelligence |
| Moon bag protection | GridBot.MoonBag |
| Trailing stops | GridBot.MoonBag |
| Multi-phase recovery | GridBot.AdvancedRisk |
| Flash pump detection | GridBot.AdvancedRisk |
| Liquidity monitoring | GridBot.AdvancedRisk |
| External notifications | GridBot.AdvancedRisk |

## File Structure

```
GridBot.Core/
+-- Services/
|   +-- Engine/
|   |   +-- SimpleTradingEngine.cs      # Main loop (~400 lines)
|   |   +-- TradingBotHostedService.cs  # Background service
|   +-- Grid/
|   |   +-- GridCalculator.cs           # Level calculations
|   |   +-- GridManager.cs              # Order management
|   +-- Risk/
|   |   +-- BasicRiskMonitor.cs         # Flash crash + loss limit
|   +-- MarketData/
|       +-- MarketDataService.cs        # Price/account data
+-- Models/
|   +-- TradingState.cs                 # Active, Paused
|   +-- GridLevel.cs                    # Price, size, order ID
|   +-- GridState.cs                    # Current grid status
|   +-- RiskStatus.cs                   # Safe or pause reason
+-- Configuration/
|   +-- SimpleGridConfig.cs             # 20 parameters
+-- Extensions/
    +-- CoreServiceExtensions.cs        # DI registration
```

## Integration

```csharp
// Minimal setup
builder.Services.AddLighterClient(configuration);
builder.Services.AddGridBotCore(configuration);

// Thats it. No other modules required.
```

## Adding Advanced Features

Each advanced module is opt-in:

```csharp
// Basic grid only
builder.Services.AddGridBotCore(configuration);

// Add trend following
builder.Services.AddTrendIntelligence(configuration);

// Add moon bag protection
builder.Services.AddMoonBag(configuration);

// Add advanced risk
builder.Services.AddAdvancedRisk(configuration);
```

## Philosophy

This module follows KISS:

1. **Simple over clever** - Fixed grid spacing, not dynamic ATR
2. **Essential only** - Flash crash + loss limit, nothing more
3. **Easy to understand** - Read the code in 30 minutes
4. **Easy to debug** - 2 states, not 16
5. **Easy to extend** - Add modules when you need them

## Comparison

| Metric | Current ApiService | GridBot.Core |
|--------|-------------------|--------------|
| Service files | 75 | ~10 |
| Lines of code | 15,000+ | ~3,000 |
| Config params | 160 | 20 |
| Engine dependencies | 17 | 5 |
| Trading states | 16 combined | 2 |
| Time to understand | Hours | 30 min |
