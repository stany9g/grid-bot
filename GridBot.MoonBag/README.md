# GridBot.MoonBag

## Purpose

This module implements the **Infinite Upside** protection strategy. It prevents the grid bot from selling its entire position during parabolic price moves, ensuring you always have exposure if the asset goes to the moon.

## The Problem This Solves

Traditional grid bots have a fatal flaw: they sell on the way up. If BTC goes from 40K to 100K, a grid bot will have sold all its BTC by 60K, missing the biggest gains.

This module solves that by:
1. Reserving a moon bag that NEVER gets sold
2. Using trailing stops instead of fixed sell levels
3. Shifting the entire grid upward during breakouts

## What This Module Does

### 1. Moon Bag Manager (MoonBagManager)

Maintains a protected reserve position:

**State Machine:**
```
Inactive -> Accumulating -> HoldMode -> Trailing -> Released
    |           |              |           |           |
    v           v              v           v           v
 No bag    Building up    Protecting   Profit run   Bag sold
           to 10-15%      the reserve  trailing up  on reversal
```

**Behavior:**
- **Inactive**: No moon bag, normal grid operation
- **Accumulating**: Building position to moon bag size (10-15% of max)
- **HoldMode**: Moon bag is protected, grid trades around it
- **Trailing**: Price broke out, trailing stop activated
- **Released**: Moon bag sold (only on major trend reversal)

### 2. Trailing Stop Service (TrailingStopService)

Implements tiered trailing stops based on profit level:

| Profit Level | Trailing Distance | Example |
|--------------|-------------------|---------|
| 0-25% | 15% | Entry 50K, peak 62.5K, stop at 53K |
| 25-50% | 10% | Entry 50K, peak 75K, stop at 67.5K |
| 50-100% | 7% | Entry 50K, peak 100K, stop at 93K |
| 100%+ | 5% | Entry 50K, peak 150K, stop at 142.5K |

The tighter stops at higher profits lock in more gains while still allowing for normal volatility.

### 3. Trailing Grid Service (TrailingGridService)

When price breaks above the grid:
1. Detects sustained breakout (not flash pump)
2. Shifts entire grid structure upward
3. Maintains relative spacing
4. Prevents grid from being left behind in rallies

### 4. Flash Spike Detector (FlashSpikeDetector)

Prevents premature grid shifts by detecting flash pumps:
- Measures rate of price change
- Compares to historical volatility
- Blocks grid shift during suspected flash spikes
- Waits for price stability before shifting

## When to Use This Module

**USE** if you:
- Are primarily LONG on crypto
- Want to capture 10x+ moves
- Believe in moon scenarios
- Accept reduced grid profits for upside exposure

**SKIP** if you:
- Are market neutral
- Want maximum grid efficiency
- Trade mostly sideways markets
- Prefer simpler strategies

## Integration

```csharp
// In Program.cs or DI setup
services.AddMoonBag(configuration);

// This registers:
// - IMoonBagManager
// - ITrailingStopService
// - ITrailingGridService
// - IFlashSpikeDetector
```

## Configuration

```json
{
  "MoonBag": {
    "Enabled": true,
    "ReservePercent": 15,
    "MinProfitToActivate": 20,
    "TrailingStopTiers": [
      { "ProfitThreshold": 0, "TrailPercent": 15 },
      { "ProfitThreshold": 25, "TrailPercent": 10 },
      { "ProfitThreshold": 50, "TrailPercent": 7 },
      { "ProfitThreshold": 100, "TrailPercent": 5 }
    ],
    "GridShiftThreshold": 5,
    "FlashSpikeMultiplier": 3.0
  }
}
```

## Files in This Module

```
GridBot.MoonBag/
+-- Services/
|   +-- MoonBagManager.cs         # State machine for moon bag
|   +-- TrailingStopService.cs    # Tiered trailing stops
|   +-- TrailingGridService.cs    # Grid shifting logic
|   +-- FlashSpikeDetector.cs     # Flash pump detection
+-- Models/
|   +-- MoonBagState.cs           # State enum
|   +-- TrailingStopState.cs      # Current stop level
+-- Extensions/
    +-- MoonBagServiceExtensions.cs
```

## Dependencies

- GridBot.Core (for position data, grid state)
- GridBot.Lighter (for executing stops)

## Important Notes

1. Moon bag is for LONG positions only - no short moon bags
2. Auto-release on major trend reversal (optional, can be manual)
3. This adds complexity - only use if you truly want upside exposure
