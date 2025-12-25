# GridBot.TrendIntelligence

## Purpose

This module adds **Trend Following** capabilities to the basic grid bot. It transforms the grid from a pure range-trading strategy into an adaptive system that adjusts inventory based on market direction.

## What This Module Does

### 1. Trend Detection (TrendDetector)
Analyzes market trend using technical indicators:
- **EMA Crossover**: 20-period vs 50-period Exponential Moving Average
- **MACD Confirmation**: 12/26/9 MACD histogram direction
- **ADX Strength Filter**: Only acts on trends with ADX > 25

Outputs one of 5 trend states:
- StrongBull (EMA up + MACD up + ADX > 40)
- WeakBull (EMA up)
- Neutral
- WeakBear (EMA down)
- StrongBear (EMA down + MACD down + ADX > 40)

### 2. Inventory Management (InventoryManager)
Calculates target inventory skew based on trend:

| Trend State | Target Inventory | Behavior |
|-------------|------------------|----------|
| StrongBull | 80% crypto / 20% cash | Hold more, sell less |
| WeakBull | 65% crypto / 35% cash | Slight long bias |
| Neutral | 50% crypto / 50% cash | Balanced grid |
| WeakBear | 35% crypto / 65% cash | Slight short bias |
| StrongBear | 20% crypto / 80% cash | Hold cash, buy dips |

### 3. Rebalancing (RebalancingService)
When actual inventory deviates from target by >10%, executes market orders to rebalance:
- Uses TWAP (Time-Weighted Average Price) for large rebalances
- Respects slippage limits
- Only rebalances during low-volatility periods

### 4. Technical Indicators (IndicatorService)
Calculates required indicators:
- EMA (Exponential Moving Average)
- MACD (Moving Average Convergence Divergence)
- ADX (Average Directional Index)

## When to Use This Module

**USE** if you want:
- To outperform buy-and-hold in sideways/bear markets
- Automatic position sizing based on trend
- Protection against holding too much during downtrends

**SKIP** if you want:
- Simple range trading without trend awareness
- Minimal complexity
- Pure mean-reversion strategy

## Integration

```csharp
// In Program.cs or DI setup
services.AddTrendIntelligence(configuration);

// This registers:
// - ITrendDetector
// - ITrendIntelligenceService
// - IInventoryManager
// - IRebalancingService
// - IIndicatorService
```

## Configuration

```json
{
  "TrendIntelligence": {
    "EmaPeriodShort": 20,
    "EmaPeriodLong": 50,
    "MacdFast": 12,
    "MacdSlow": 26,
    "MacdSignal": 9,
    "AdxPeriod": 14,
    "AdxThreshold": 25,
    "RebalanceThreshold": 10,
    "MaxSlippagePercent": 0.5
  }
}
```

## Files in This Module

```
GridBot.TrendIntelligence/
+-- Services/
|   +-- TrendDetector.cs              # Trend analysis logic
|   +-- TrendIntelligenceService.cs   # Orchestrator
|   +-- InventoryManager.cs           # Skew calculations
|   +-- RebalancingService.cs         # Position adjustments
|   +-- IndicatorService.cs           # EMA, MACD, ADX
+-- Models/
|   +-- TrendState.cs                 # StrongBull..StrongBear
|   +-- InventoryAnalysis.cs          # Current vs target skew
+-- Extensions/
    +-- TrendServiceExtensions.cs     # DI registration
```

## Dependencies

- GridBot.Core (for market data, position info)
- GridBot.Lighter (for executing rebalance orders)
