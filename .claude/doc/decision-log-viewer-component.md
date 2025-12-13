# DecisionLogViewer Blazor Component

## Overview

The `DecisionLogViewer` is a MudBlazor component that displays decision cycle log entries in a compact, interactive table format. It provides real-time monitoring of the trading bot's decision cycles with the ability to browse historical data using a time slider.

## Location

`GridBot.ApiService/Components/Dashboard/DecisionLogViewer.razor`

## Dependencies

### Services
- `IDecisionCycleLogService` - Ring buffer service storing decision cycle entries

### Models
- `DecisionCycleLogEntry` - Record containing all decision cycle metrics

### Packages
- MudBlazor - UI component library

## Features

### Two Operating Modes

1. **Live Mode (Default)**
   - Automatically displays the latest 50 entries
   - Auto-refreshes when new entries are added via `EntryAdded` event
   - Ideal for real-time monitoring during trading

2. **History Mode**
   - Activated by toggling off the "Live" switch
   - Time slider appears to navigate through historical data
   - Shows entries within a +/- 30 second window around the selected time
   - Useful for reviewing past decisions and debugging

### Table Columns

| Column | Field | Format | Description |
|--------|-------|--------|-------------|
| Time | Timestamp | `HH:mm:ss` | Local time of decision cycle |
| Price | CurrentPrice | `$XX,XXX.XX` | Market price at time of decision |
| Position | PositionDirection + PositionSize | Icon + number | LONG (green up), SHORT (red down), FLAT (gray dash) |
| Trend | TrendState + skews | Text + subtext | Current trend + target vs actual skew deviation |
| Grid | ActiveBuyOrders + ActiveSellOrders | `B:N S:N` | Active orders on each side |
| Risk | RiskStatus | Colored chip | Risk level (OK=green, warnings=yellow, severe=red) |
| Block | BlockStatus | Colored chip | Order blocking status |
| Cap | Capacity | Colored % | Operational capacity percentage |
| Orders | OrdersPlaced + OrdersCancelled | `+N/-N` | Orders placed/cancelled this cycle |
| Dur | DurationMs | `XXms` | Cycle execution duration |

### Color Coding

#### Risk Status
- **Green (OK)**: No risk issues detected
- **Yellow**: Minor/Moderate flash crash/pump, low liquidity
- **Red**: Severe/Extreme events, loss limits, halted, critical liquidity

#### Block Status
- **Green (OK)**: No blocking active
- **Warning (Orange)**: BLOCKED:BUYS or BLOCKED:SELLS
- **Red**: BLOCKED:ALL

#### Capacity
- **Green**: >75% capacity
- **Yellow**: 25-75% capacity
- **Red**: <25% capacity

### Expandable Row Details

Click any row to expand and view:
- **Warnings**: List of warnings generated during the cycle
- **Actions Blocked**: List of actions that were blocked
- **Moon Bag Status**: Current moon bag state (if active)

## Component API

### Parameters
None - the component is self-contained and injects its dependencies.

### Events
None exposed - all events are internal.

## Usage

Add to a Blazor page:

```razor
@using GridBot.ApiService.Components.Dashboard

<DecisionLogViewer />
```

## Integration

The component is integrated into the main Dashboard at Row 7:

```razor
@* Row 7: Decision Cycle Log *@
<MudGrid>
    <MudItem xs="12">
        <DecisionLogViewer />
    </MudItem>
</MudGrid>
```

## Service Registration

The `IDecisionCycleLogService` must be registered as a singleton in DI:

```csharp
services.AddSingleton<IDecisionCycleLogService, DecisionCycleLogService>();
```

This is done automatically through `AddLoggingServices()` in `LoggingServiceExtensions.cs`.

## Thread Safety

- Uses `InvokeAsync(StateHasChanged)` for safe UI updates from event handlers
- The underlying `DecisionCycleLogService` is thread-safe using `ConcurrentQueue`
- Event subscription is cleaned up in `Dispose()` to prevent memory leaks

## Configuration Constants

```csharp
private const int MaxLiveEntries = 50;        // Max entries shown in live mode
private const int HistoryWindowSeconds = 30;  // Time window around slider position
```

## Service Configuration

The `DecisionCycleLogService` has a default capacity of 500 entries (configurable in constructor).

## Performance Considerations

- Ring buffer automatically trims oldest entries when capacity is reached
- LINQ queries are performed on enumeration each time entries are requested
- For large entry counts, consider pagination or virtualization in future iterations
