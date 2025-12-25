# GridBot.AdvancedRisk

## Purpose

This module provides **enterprise-grade risk management** features beyond the basic flash crash and loss limits. Use this for production systems that need sophisticated recovery, monitoring, and alerting.

## What This Module Does

### 1. Recovery Manager (RecoveryManager)

Multi-phase recovery from protective mode:

**The Problem:** After a flash crash, you cannot just resume full trading immediately - the market may still be unstable.

**Solution:** 4-phase graduated recovery:

| Phase | Capacity | Duration | Requirements |
|-------|----------|----------|--------------|
| None | 0% | - | Protective mode active |
| Phase1 | 25% | 15 min | Price stable for 5 min |
| Phase2 | 50% | 15 min | No new risk triggers |
| Phase3 | 75% | 15 min | Continued stability |
| Full | 100% | - | All phases passed |

Each phase can regress if conditions deteriorate.

### 2. Flash Pump Detector (FlashPumpDetector)

Symmetric detection for rapid price INCREASES:

**Why it matters for shorts:** If you have short positions, a flash pump is just as dangerous as a flash crash for longs.

**Detection Windows:**
- 1 minute: +8% = Warning, +12% = Alert, +20% = Critical
- 5 minute: +12% = Warning, +18% = Alert, +25% = Critical

### 3. Liquidity Monitor (LiquidityMonitor)

Advanced order book and volume analysis:

**Monitors:**
- Bid/ask depth at multiple price levels
- Volume trends (is liquidity drying up?)
- Spread widening (market stress indicator)
- Order book imbalance (directional pressure)

**Alerts when:**
- Depth < $10K within 1% of price
- Volume < $100K/hour
- Spread > 0.5%
- Imbalance > 70% (one-sided book)

### 4. Risk Event Logger (RiskEventLogger)

Structured logging and external notifications:

**Logs:**
- All risk threshold breaches
- State transitions
- Recovery phase changes
- Trading decisions

**Integrations:**
- Structured JSON logs
- Webhook notifications (Discord, Telegram, Home Assistant)
- Metrics for dashboards

### 5. Webhook Notifier (WebhookNotifier)

Send alerts to external systems:

**Supported:**
- Generic webhook POST
- Discord webhooks
- Telegram bots
- Home Assistant

**Alert Levels:**
- Info: State changes, phase transitions
- Warning: Threshold approached
- Critical: Trading paused, recovery started

## When to Use This Module

**USE** if you:
- Run significant capital (>$10K)
- Need audit trails for risk events
- Want external alerting
- Trade shorts (need flash pump detection)
- Require graduated recovery

**SKIP** if you:
- Running small test capital
- Prefer simple on/off protection
- Dont need external notifications
- Only trade long (basic flash crash sufficient)

## Integration

```csharp
// In Program.cs or DI setup
services.AddAdvancedRisk(configuration);

// This registers:
// - IRecoveryManager
// - IFlashPumpDetector
// - ILiquidityMonitor
// - IRiskEventLogger
// - IWebhookNotifier
```

## Configuration

```json
{
  "AdvancedRisk": {
    "Recovery": {
      "Phase1DurationMinutes": 15,
      "Phase2DurationMinutes": 15,
      "Phase3DurationMinutes": 15,
      "StabilityRequiredMinutes": 5
    },
    "FlashPump": {
      "Enabled": true,
      "Thresholds": {
        "1min": { "Warning": 8, "Alert": 12, "Critical": 20 },
        "5min": { "Warning": 12, "Alert": 18, "Critical": 25 }
      }
    },
    "Liquidity": {
      "MinDepthUsd": 10000,
      "MinHourlyVolumeUsd": 100000,
      "MaxSpreadPercent": 0.5,
      "MaxImbalancePercent": 70
    },
    "Webhooks": {
      "Discord": "https://discord.com/api/webhooks/...",
      "Telegram": "https://api.telegram.org/bot.../sendMessage"
    }
  }
}
```

## Files in This Module

```
GridBot.AdvancedRisk/
+-- Services/
|   +-- RecoveryManager.cs        # Multi-phase recovery
|   +-- FlashPumpDetector.cs      # Rapid price increase detection
|   +-- LiquidityMonitor.cs       # Order book depth monitoring
|   +-- RiskEventLogger.cs        # Structured risk logging
|   +-- WebhookNotifier.cs        # External notifications
+-- Models/
|   +-- RecoveryPhase.cs          # None, Phase1, Phase2, Phase3
|   +-- RiskEvent.cs              # Structured risk event
|   +-- LiquidityStatus.cs        # Current liquidity assessment
+-- Extensions/
    +-- AdvancedRiskServiceExtensions.cs
```

## Dependencies

- GridBot.Core (for market data, state)
- Microsoft.Extensions.Http (for webhooks)

## Note on Basic vs Advanced Risk

**GridBot.Core includes basic risk:**
- Flash crash detection (1-minute window only)
- Daily loss limit
- Simple cooldown pause

**This module adds:**
- Multi-phase recovery (vs simple cooldown)
- Flash pump detection (vs flash crash only)
- Liquidity monitoring (vs none)
- External notifications (vs logging only)
