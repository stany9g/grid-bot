# ALTE Grid Bot Monitoring Dashboard - Implementation Plan

## Overview

Create a comprehensive real-time monitoring dashboard for the ALTE Grid Bot trading system using Blazor Server with MudBlazor components. The dashboard provides visibility into all aspects of the trading system and integrates with Home Assistant for alerts via webhook.

## Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Blazor Server (GridBot.Web)                   │
├─────────────────────────────────────────────────────────────────────┤
│  Pages/Dashboard.razor                                               │
│  ├── Components/                                                     │
│  │   ├── TradingStateCard.razor      (Current state + transitions)  │
│  │   ├── PricePositionCard.razor     (Price, position, PnL)         │
│  │   ├── GridVisualization.razor     (Visual grid levels)           │
│  │   ├── RiskAssessmentCard.razor    (Flash crash, loss limits)     │
│  │   ├── DecisionCycleMetrics.razor  (Loop health, timeouts)        │
│  │   ├── MoonBagStatusCard.razor     (Moon bag protection status)   │
│  │   ├── TrendIndicatorCard.razor    (Trend state, EMAs, ADX)       │
│  │   ├── AlertsPanel.razor           (Recent alerts, notifications) │
│  │   └── RecoveryProgressCard.razor  (Recovery phase progress)      │
│  └── Services/                                                       │
│      ├── DashboardStateService.cs    (Real-time state aggregation)  │
│      └── WebhookNotificationService.cs (Home Assistant alerts)      │
└─────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Home Assistant Webhook                            │
│  POST http://192.168.1.175:8123/api/webhook/-Y-QjbVoWNw1UzxMqW2...  │
│                                                                      │
│  Payload: {                                                          │
│    "event_type": "state_change|alert|fill|error",                   │
│    "severity": "info|warning|critical",                              │
│    "title": "ALTE Bot Alert",                                        │
│    "message": "Trading state changed to Degraded_ProtectiveMode",   │
│    "data": { ... event-specific data ... }                          │
│  }                                                                   │
└─────────────────────────────────────────────────────────────────────┘
```

## Dashboard Layout (MudBlazor Grid)

```
┌────────────────────────────────────────────────────────────────────────────┐
│ ALTE Grid Bot Dashboard                                    [Auto-Refresh] │
├────────────────────────────────────────────────────────────────────────────┤
│                                                                            │
│  ┌─────────────────────┐  ┌─────────────────────┐  ┌────────────────────┐ │
│  │ TRADING STATE       │  │ PRICE & POSITION    │  │ OPERATIONAL        │ │
│  │ ● Active            │  │ BTC: $97,450.25     │  │ CAPACITY           │ │
│  │ Since: 2h 15m       │  │ Position: 0.125 BTC │  │ ████████░░ 80%     │ │
│  │ Market: BTC-USDC    │  │ Equity: $15,234.50  │  │ State: Normal      │ │
│  │ [State History ▼]   │  │ PnL: +$234.50 (1.5%)│  │ Timeouts: 0        │ │
│  └─────────────────────┘  └─────────────────────┘  └────────────────────┘ │
│                                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │ GRID VISUALIZATION                                                   │  │
│  │ ═══════════════════════════════════════════════════════════════════ │  │
│  │ $98,500 ─────────────────────────────────●──── Ask Level 4 (0.02)  │  │
│  │ $98,200 ─────────────────────────────●──────── Ask Level 3 (0.02)  │  │
│  │ $97,900 ─────────────────────────●──────────── Ask Level 2 (0.02)  │  │
│  │ $97,600 ─────────────────────●──────────────── Ask Level 1 (0.02)  │  │
│  │ $97,450 ════════════════●════════════════════ CURRENT PRICE         │  │
│  │ $97,200 ─────────────●──────────────────────── Bid Level 1 (0.02)  │  │
│  │ $96,900 ─────────●──────────────────────────── Bid Level 2 (0.02)  │  │
│  │ $96,600 ─────●──────────────────────────────── Bid Level 3 (0.02)  │  │
│  │ $96,300 ─●──────────────────────────────────── Bid Level 4 (0.02)  │  │
│  │ ═══════════════════════════════════════════════════════════════════ │  │
│  │ Grid Spacing: 0.8% | Width: 4.5% | Fills Today: 12 | Shifts: 3     │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                            │
│  ┌──────────────────────┐  ┌──────────────────────┐  ┌─────────────────┐  │
│  │ RISK ASSESSMENT      │  │ TREND INTELLIGENCE   │  │ MOON BAG        │  │
│  │ Flash Crash: None    │  │ Trend: Bullish ▲     │  │ Status: Active  │  │
│  │ Daily Loss: -0.5%    │  │ EMA20 > EMA50        │  │ Protected: 15%  │  │
│  │ Weekly: -1.2%        │  │ ADX: 32 (Strong)     │  │ Trail: -12%     │  │
│  │ Max DD: -3.4%        │  │ Target Skew: 70%     │  │ High: $98,200   │  │
│  │ Buys: ✓ Sells: ✓    │  │ Current: 65%         │  │ Stop: $86,416   │  │
│  └──────────────────────┘  └──────────────────────┘  └─────────────────┘  │
│                                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │ DECISION CYCLE METRICS                                               │  │
│  │ Last Cycle: 2024-12-05 14:32:15 (245ms) | Next: 4.8s                │  │
│  │ ──────────────────────────────────────────────────────────────────── │  │
│  │ Cycles/min: 12 | Skipped: 0 | Timeouts: 0 | Errors: 0               │  │
│  │ Data Collection: 180ms | Orders Placed: 2 | Cancelled: 1            │  │
│  │ Position Mult: 1.0x | Spread Mult: 1.0x                             │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐  │
│  │ RECENT ALERTS                                                        │  │
│  │ ──────────────────────────────────────────────────────────────────── │  │
│  │ [14:30] INFO    Grid shifted up by 0.8% (price breakout)            │  │
│  │ [14:25] INFO    Fill detected: Bid Level 2 @ $97,100                │  │
│  │ [14:20] WARNING Consecutive timeouts: 2 (widening spreads)          │  │
│  │ [14:15] INFO    Trend changed: Neutral → Bullish                    │  │
│  └─────────────────────────────────────────────────────────────────────┘  │
│                                                                            │
└────────────────────────────────────────────────────────────────────────────┘
```

## Webhook Notification Events

### Event Types to Send to Home Assistant

| Event Type | Severity | Trigger Condition | Webhook Payload |
|------------|----------|-------------------|-----------------|
| `state_change` | varies | Trading state transitions | state, previous_state, reason |
| `fill_detected` | info | Grid order filled | side, price, size, level |
| `flash_crash` | critical | Flash crash detected | severity, drop_percent, duration |
| `loss_limit` | critical | Loss limit breached | type, current_percent, limit |
| `timeout_warning` | warning | 3+ consecutive timeouts | count, last_success |
| `protective_mode` | critical | Entered protective mode | reason, position_size |
| `recovery_phase` | info | Recovery phase change | phase, time_remaining |
| `grid_shift` | info | Grid shifted | direction, percent, reason |
| `moon_bag_triggered` | warning | Trailing stop triggered | price, stop_price, profit |
| `liquidation_risk` | critical | EC-001 detected | position_before, position_after |

### Sample Webhook Payloads

```json
// State Change
{
  "event_type": "state_change",
  "severity": "critical",
  "title": "ALTE Bot State Change",
  "message": "Trading state changed: Active → Degraded_ProtectiveMode",
  "data": {
    "previous_state": "Active",
    "new_state": "Degraded_ProtectiveMode",
    "reason": "Flash crash: 5.2% drop in 5 minutes",
    "market_id": 1,
    "timestamp": "2024-12-05T14:32:15Z"
  }
}

// Fill Detected
{
  "event_type": "fill_detected",
  "severity": "info",
  "title": "Grid Order Filled",
  "message": "Buy order filled at $97,100 (0.02 BTC)",
  "data": {
    "side": "buy",
    "price": 97100,
    "size": 0.02,
    "level_index": 2,
    "total_fills_today": 13,
    "timestamp": "2024-12-05T14:25:30Z"
  }
}

// Critical Alert
{
  "event_type": "flash_crash",
  "severity": "critical",
  "title": "Flash Crash Detected",
  "message": "SEVERE flash crash: -7.2% in 15 minutes. Protective mode activated.",
  "data": {
    "severity": "Severe",
    "drop_percent": -7.2,
    "time_window_minutes": 15,
    "current_price": 90500,
    "position_size": 0.125,
    "action_taken": "Entered protective mode, buys blocked",
    "timestamp": "2024-12-05T14:32:15Z"
  }
}
```

## Components Specification

### 1. TradingStateCard.razor
- Shows current TradingState with color-coded indicator
- State duration timer
- Market ID display
- Collapsible state history timeline
- MudBlazor: MudCard, MudChip, MudTimeline

### 2. PricePositionCard.razor
- Current price with trend arrow
- Position size in base and USD
- Total equity
- Unrealized PnL (absolute and percentage)
- Daily PnL sparkline
- MudBlazor: MudCard, MudText, MudIcon, custom sparkline

### 3. GridVisualization.razor
- Visual representation of grid levels
- Current price indicator
- Bid/Ask level markers with sizes
- Filled orders highlighted
- Grid parameters (spacing, width)
- MudBlazor: Custom SVG/Canvas, MudChip for status

### 4. RiskAssessmentCard.razor
- Flash crash status indicator
- Loss limits progress bars (daily, weekly, monthly)
- Max drawdown gauge
- Buy/Sell block indicators
- Liquidity status
- MudBlazor: MudCard, MudProgressLinear, MudAlert

### 5. DecisionCycleMetrics.razor
- Last cycle timestamp and duration
- Countdown to next cycle
- Cycles per minute
- Timeout/Error counters
- Position/Spread multipliers
- MudBlazor: MudCard, MudChip, MudProgressCircular

### 6. TrendIndicatorCard.razor
- Current trend state with arrow
- EMA relationship (fast vs slow)
- ADX value with strength indicator
- Target vs Current inventory skew
- Trend confirmation status
- MudBlazor: MudCard, MudChip, custom gauge

### 7. MoonBagStatusCard.razor
- Moon bag state (Inactive/Active/Trailing/HoldMode)
- Protected percentage
- Trailing stop distance
- High watermark price
- Current stop price
- MudBlazor: MudCard, MudProgressLinear, MudAlert

### 8. AlertsPanel.razor
- Scrollable alert list
- Color-coded severity (info, warning, critical)
- Timestamp and message
- Filterable by type
- Clear all button
- MudBlazor: MudTable, MudChip, MudAlert

### 9. RecoveryProgressCard.razor (shown when in recovery)
- Current recovery phase
- Phase progress bar
- Estimated time remaining
- Phase multipliers display
- Stability window status
- MudBlazor: MudCard, MudStepper, MudProgressLinear

## Services

### DashboardStateService.cs
```csharp
public interface IDashboardStateService
{
    event EventHandler<DashboardState>? StateChanged;
    DashboardState CurrentState { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public record DashboardState
{
    public TradingState TradingState { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal? Position { get; init; }
    public decimal? Equity { get; init; }
    public decimal? UnrealizedPnl { get; init; }
    public GridState? GridState { get; init; }
    public RiskAssessment? RiskAssessment { get; init; }
    public DecisionResult? LastDecisionResult { get; init; }
    public TrendIntelligenceResult? TrendResult { get; init; }
    public MoonBagStatus? MoonBagStatus { get; init; }
    public RecoveryPhase RecoveryPhase { get; init; }
    public int OperationalCapacity { get; init; }
    public List<AlertItem> RecentAlerts { get; init; }
    public DateTimeOffset LastUpdated { get; init; }
}
```

### WebhookNotificationService.cs
```csharp
public interface IWebhookNotificationService
{
    Task SendAlertAsync(WebhookAlert alert, CancellationToken ct = default);
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
}

public record WebhookAlert
{
    public required string EventType { get; init; }
    public required string Severity { get; init; }  // info, warning, critical
    public required string Title { get; init; }
    public required string Message { get; init; }
    public Dictionary<string, object>? Data { get; init; }
}
```

## File Structure

```
GridBot.Web/
├── Pages/
│   └── Dashboard.razor           # Main dashboard page
├── Components/
│   ├── Dashboard/
│   │   ├── TradingStateCard.razor
│   │   ├── PricePositionCard.razor
│   │   ├── GridVisualization.razor
│   │   ├── RiskAssessmentCard.razor
│   │   ├── DecisionCycleMetrics.razor
│   │   ├── TrendIndicatorCard.razor
│   │   ├── MoonBagStatusCard.razor
│   │   ├── AlertsPanel.razor
│   │   └── RecoveryProgressCard.razor
│   └── Shared/
│       ├── StatusIndicator.razor  # Reusable status dot
│       └── MetricCard.razor       # Reusable metric display
├── Services/
│   ├── DashboardStateService.cs
│   └── WebhookNotificationService.cs
└── Models/
    ├── DashboardState.cs
    ├── AlertItem.cs
    └── WebhookPayload.cs
```

## SignalR Integration

Use SignalR hub for real-time updates from ApiService to Web:

```csharp
// In GridBot.ApiService
public class TradingHub : Hub
{
    public async Task SendStateUpdate(DashboardState state)
        => await Clients.All.SendAsync("StateUpdated", state);

    public async Task SendAlert(AlertItem alert)
        => await Clients.All.SendAsync("AlertReceived", alert);
}

// Hook into TradingDecisionEngine to broadcast updates
```

## Implementation Order

1. **Phase 1: Core Infrastructure**
   - WebhookNotificationService with Home Assistant integration
   - DashboardStateService skeleton
   - Dashboard.razor layout with MudBlazor grid

2. **Phase 2: State Display Components**
   - TradingStateCard
   - PricePositionCard
   - OperationalCapacity display

3. **Phase 3: Trading Visualization**
   - GridVisualization
   - RiskAssessmentCard
   - DecisionCycleMetrics

4. **Phase 4: Advanced Components**
   - TrendIndicatorCard
   - MoonBagStatusCard
   - RecoveryProgressCard

5. **Phase 5: Alerts & Polish**
   - AlertsPanel with webhook integration
   - Real-time updates via SignalR
   - Auto-refresh toggle
   - Mobile responsiveness

## Home Assistant Automation Example

```yaml
# automation.yaml
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

- alias: "ALTE Bot State Change Notification"
  trigger:
    - platform: webhook
      webhook_id: "-Y-QjbVoWNw1UzxMqW2OhAOpP"
  condition:
    - condition: template
      value_template: "{{ trigger.json.event_type == 'state_change' }}"
  action:
    - service: notify.persistent_notification
      data:
        title: "ALTE Bot: {{ trigger.json.data.new_state }}"
        message: "{{ trigger.json.message }}"
```
