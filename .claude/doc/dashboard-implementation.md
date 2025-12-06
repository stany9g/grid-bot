# ALTE Grid Bot Monitoring Dashboard - Implementation Documentation

## Overview

The ALTE Grid Bot Monitoring Dashboard is a real-time web interface built with Blazor Server and MudBlazor that provides comprehensive visibility into all aspects of the trading system. It also integrates with Home Assistant for push notifications via webhooks.

## Architecture

```
GridBot.Web/
├── Models/
│   ├── DashboardState.cs      # Aggregated state model
│   ├── AlertItem.cs           # Alert model
│   └── WebhookPayload.cs      # Webhook payload for Home Assistant
├── Services/
│   ├── IWebhookNotificationService.cs
│   ├── WebhookNotificationService.cs
│   ├── IDashboardStateService.cs
│   └── DashboardStateService.cs
├── Components/
│   ├── Dashboard/
│   │   ├── TradingStateCard.razor
│   │   ├── PricePositionCard.razor
│   │   ├── OperationalCapacityCard.razor
│   │   ├── GridVisualization.razor
│   │   ├── RiskAssessmentCard.razor
│   │   ├── TrendIndicatorCard.razor
│   │   ├── MoonBagStatusCard.razor
│   │   ├── DecisionCycleMetrics.razor
│   │   ├── RecoveryProgressCard.razor
│   │   └── AlertsPanel.razor
│   └── Layout/
│       └── MainLayout.razor (MudBlazor layout)
└── Pages/
    └── Dashboard.razor         # Main dashboard page
```

## Components

### 1. TradingStateCard

Displays the current trading state with color-coded indicators:

| State | Color | Description |
|-------|-------|-------------|
| Active | Green | Full 100% capacity |
| Recovering | Blue | Returning to normal |
| Degraded_Bootstrap | Yellow | Building initial position |
| Degraded_SkewCorrection | Yellow | Correcting inventory skew |
| Degraded_HighVolatility | Yellow | High volatility detected |
| Degraded_LowLiquidity | Yellow | Low order book liquidity |
| Degraded_ProtectiveMode | Red | Loss limits near breach |

### 2. PricePositionCard

Shows current price, position size, equity, and unrealized PnL with trend arrows.

### 3. OperationalCapacityCard

Displays:
- Operational capacity percentage (0-100%)
- Position size multiplier (0.0-1.0)
- Spread width multiplier (1.0+)
- Consecutive timeout count

### 4. GridVisualization

Visual representation of the grid showing:
- Upper and lower bounds
- Ask (sell) levels with prices and sizes
- Current price indicator
- Bid (buy) levels with prices and sizes
- Grid statistics (spacing, width, fills, shifts, PnL)

### 5. RiskAssessmentCard

Risk monitoring including:
- Flash crash status with alert banner
- Daily/Weekly/Monthly PnL progress bars
- Max drawdown gauge
- Buy/Sell block indicators
- Active warnings list

### 6. TrendIndicatorCard

Trend intelligence display:
- Current trend state (StrongBull, MildBull, Neutral, MildBear, StrongBear)
- EMA20/EMA50 values
- ADX strength gauge
- Inventory skew gauge (current vs target)
- Rebalance needed indicator
- Cooldown status

### 7. MoonBagStatusCard

Moon bag protection status:
- Current state (Inactive, WarmingUp, Tracking, Trailing, Triggered, HoldMode, Released)
- Protected quantity
- Current profit percentage
- High watermark price
- Trailing stop price and distance
- Current tier
- Active stop order status

### 8. DecisionCycleMetrics

Trading loop health metrics:
- Cycle time (milliseconds)
- Cycles per minute
- Timeout count
- Error count
- Data collection time
- Orders placed/cancelled
- Position and spread multipliers

### 9. RecoveryProgressCard

Recovery phase progress (shown only during recovery):
- Phase stepper (1-4)
- Position size progress bar
- Spread width progress bar
- Phase description

### 10. AlertsPanel

Scrollable alert list with:
- Severity-colored indicators (Info, Warning, Critical)
- Timestamp and message
- Acknowledge button
- Clear all button
- Maximum 100 alerts (oldest auto-removed)

## Services

### WebhookNotificationService

Sends HTTP POST requests to Home Assistant webhook endpoint.

Configuration in `appsettings.json`:
```json
{
  "Webhook": {
    "Url": "http://192.168.1.175:8123/api/webhook/-Y-QjbVoWNw1UzxMqW2OhAOpP",
    "Enabled": true,
    "TimeoutSeconds": 10
  }
}
```

Webhook payload format:
```json
{
  "event_type": "state_change",
  "severity": "critical",
  "title": "ALTE Bot Alert",
  "message": "Trading state changed: Active -> Degraded_ProtectiveMode",
  "data": {
    "previous_state": "Active",
    "new_state": "Degraded_ProtectiveMode",
    "market_id": 1,
    "timestamp": "2024-12-05T14:32:15Z"
  }
}
```

### DashboardStateService

Singleton service that:
1. Polls TradingApiClient every 5 seconds
2. Aggregates data into DashboardState
3. Detects state changes and generates alerts
4. Sends webhook notifications for critical events
5. Manages alert queue (max 100 alerts)
6. Raises StateChanged event for UI updates

Auto-generated alerts:
- Trading state changes
- Recovery phase changes
- Flash crash detection
- Loss limit breaches
- Consecutive timeout warnings (3+)

## Webhook Event Types

| Event Type | Severity | Trigger |
|------------|----------|---------|
| `state_change` | varies | Trading state changes |
| `fill_detected` | info | Grid order filled |
| `flash_crash` | critical | Flash crash detected |
| `loss_limit` | critical | Loss limit breached |
| `timeout_warning` | warning | 3+ consecutive timeouts |
| `protective_mode` | critical | Entered protective mode |
| `recovery_phase` | info | Recovery phase changed |
| `grid_shift` | info | Grid shifted up/down |
| `moon_bag_triggered` | warning | Trailing stop triggered |
| `liquidation_risk` | critical | Potential liquidation detected |

## Home Assistant Automation Example

```yaml
automation:
  - alias: "ALTE Bot Critical Alert"
    trigger:
      - platform: webhook
        webhook_id: "-Y-QjbVoWNw1UzxMqW2OhAOpP"
    condition:
      - condition: template
        value_template: "{{ trigger.json.severity == 'critical' }}"
    action:
      - service: notify.mobile_app
        data:
          title: "{{ trigger.json.title }}"
          message: "{{ trigger.json.message }}"
          data:
            priority: high
            ttl: 0
```

## Dependencies

- MudBlazor 8.5.0
- Microsoft.Extensions.Http

## Future Enhancements

1. **SignalR Integration** - Push updates from ApiService instead of polling
2. **Grid State API** - Full grid level data for visualization
3. **Historical Charts** - PnL, price, and fill history
4. **Mobile Optimization** - Better responsive design for phones
5. **Settings Page** - Configure webhook URL, refresh interval
6. **Alert Filters** - Filter by severity, event type
7. **Export Logs** - Download alert history as CSV
