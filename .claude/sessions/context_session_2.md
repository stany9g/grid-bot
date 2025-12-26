# Session 2: Grid Bot UI Controls & DEX Selection

## Objective
Add UI controls to operate the Grid Bot through the dashboard:
1. Start/Stop/Pause/Resume buttons
2. DEX selector (Lighter vs Extended)
3. Real-time status display

## Current Architecture Analysis

### Existing Components
- **SimpleTradingBotHostedService**: BackgroundService that runs the trading loop
- **ISimpleTradingEngine**: Interface with Start/Stop/Pause/Resume methods
- **IExchangeRegistry**: Registry for managing multiple exchange clients
- **ExchangeType enum**: Lighter = 1, Hyperliquid = 2, Extended = 3
- **Dashboard.razor**: Main dashboard with Settings drawer
- **SettingsPanel.razor**: Configuration panel in the drawer

### Current Limitations
1. Bot starts automatically with the application - no manual control
2. No way to switch DEX at runtime
3. No visual indication of which DEX is active
4. Pause/Resume exists but not exposed in UI

## Implementation Plan

### Phase 1: Backend Services

#### 1.1 GridBotControlService
Location: `GridBot.ApiService/Services/Bot/IGridBotControlService.cs`

```csharp
public interface IGridBotControlService
{
    BotStatus Status { get; }
    bool IsRunning { get; }
    string? CurrentExchangeId { get; }
    ExchangeType? CurrentExchangeType { get; }

    event EventHandler<BotStatusChangedEventArgs>? StatusChanged;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
    Task PauseAsync(string reason, CancellationToken ct = default);
    Task ResumeAsync(CancellationToken ct = default);
    Task SwitchExchangeAsync(ExchangeType exchangeType, CancellationToken ct = default);
}

public enum BotStatus
{
    Stopped,
    Starting,
    Running,
    Paused,
    Stopping,
    Error
}
```

#### 1.2 ExchangeSelectionService
Location: `GridBot.ApiService/Services/Exchange/IExchangeSelectionService.cs`

```csharp
public interface IExchangeSelectionService
{
    ExchangeType CurrentExchange { get; }
    IReadOnlyList<ExchangeInfo> AvailableExchanges { get; }

    event EventHandler<ExchangeType>? ExchangeChanged;

    Task SelectExchangeAsync(ExchangeType exchangeType, CancellationToken ct = default);
    IExchangeClient GetCurrentClient();
}

public record ExchangeInfo(ExchangeType Type, string DisplayName, bool IsAvailable, string? StatusMessage);
```

### Phase 2: API Endpoints

Add to Program.cs under `/api/trading` group:
```
POST /api/trading/control/start    - Start the bot
POST /api/trading/control/stop     - Stop the bot
POST /api/trading/control/pause    - Pause the bot
POST /api/trading/control/resume   - Resume the bot
GET  /api/trading/status           - Get bot status (exists, enhance)
GET  /api/exchange/available       - List available exchanges
GET  /api/exchange/current         - Get current exchange
POST /api/exchange/select          - Switch exchange
```

### Phase 3: Blazor UI Components

#### 3.1 BotControlPanel Component
Location: `GridBot.ApiService/Components/Dashboard/BotControlPanel.razor`

Features:
- Large Start/Stop button with color indication
- Pause/Resume toggle when running
- Status indicator (Stopped/Starting/Running/Paused/Error)
- Error message display

#### 3.2 ExchangeSelector Component
Location: `GridBot.ApiService/Components/Dashboard/ExchangeSelector.razor`

Features:
- Dropdown to select DEX (Lighter/Extended)
- Disabled when bot is running (must stop to switch)
- Status badge for each exchange (connected/disconnected)
- Connection health indicator

#### 3.3 Dashboard Updates
- Add BotControlPanel above the grid
- Add ExchangeSelector in header or settings panel
- Show active DEX in header
- Real-time status updates via event subscription

## Files to Create

### Backend
1. `GridBot.ApiService/Services/Bot/IGridBotControlService.cs`
2. `GridBot.ApiService/Services/Bot/GridBotControlService.cs`
3. `GridBot.ApiService/Services/Bot/BotStatus.cs`
4. `GridBot.ApiService/Services/Exchange/IExchangeSelectionService.cs`
5. `GridBot.ApiService/Services/Exchange/ExchangeSelectionService.cs`
6. `GridBot.ApiService/Services/Exchange/ExchangeInfo.cs`

### Frontend
1. `GridBot.ApiService/Components/Dashboard/BotControlPanel.razor`
2. `GridBot.ApiService/Components/Dashboard/ExchangeSelector.razor`

### Modified Files
1. `GridBot.ApiService/Program.cs` - Add new endpoints and services
2. `GridBot.ApiService/Components/Pages/Dashboard.razor` - Add new components
3. `GridBot.ApiService/Components/Dashboard/SettingsPanel.razor` - Add DEX selector
4. `GridBot.ApiService/Services/Dashboard/DashboardStateService.cs` - Add bot status
5. `GridBot.ApiService/Models/Dashboard/DashboardState.cs` - Add bot status fields

## Key Design Decisions

### 1. Bot Control Flow
- Bot should NOT auto-start on application startup
- User explicitly starts/stops via UI
- Pause preserves grid state, Stop clears orders
- Exchange switch requires stop first (safety)

### 2. Exchange Selection
- Must stop bot before switching DEX
- Validate exchange is configured and healthy before allowing selection
- Store selected exchange in configuration

### 3. State Management
- Use events for real-time UI updates
- DashboardStateService aggregates bot status
- Optimistic UI updates with error rollback

### 4. Error Handling
- Display connection errors prominently
- Auto-retry for transient failures
- Manual intervention for configuration errors

## Risk Considerations

1. **Race conditions**: Lock around state transitions
2. **Order orphans**: Cancel all orders before DEX switch
3. **Connection loss**: Detect and surface to user
4. **Config validation**: Prevent invalid DEX selection

## Progress Log

### Session Start (2025-12-26)
- Analyzed existing architecture
- Created implementation plan
- Ready to implement

### Phase 1 Complete (2025-12-26)
Backend services and API endpoints implemented by `dotnet-feature-builder-navsystem`:

**Files Created:**
1. `GridBot.ApiService/Services/Bot/BotStatus.cs` - Enum with Stopped, Starting, Running, Paused, Stopping, Error states
2. `GridBot.ApiService/Services/Bot/BotStatusChangedEventArgs.cs` - Event args for status changes
3. `GridBot.ApiService/Services/Bot/IGridBotControlService.cs` - Interface for bot control
4. `GridBot.ApiService/Services/Bot/GridBotControlService.cs` - Thread-safe implementation with lock-based synchronization
5. `GridBot.ApiService/Services/Exchange/ExchangeInfo.cs` - Record for exchange info
6. `GridBot.ApiService/Services/Exchange/IExchangeSelectionService.cs` - Interface for exchange selection
7. `GridBot.ApiService/Services/Exchange/ExchangeSelectionService.cs` - Implementation with validation
8. `GridBot.ApiService/Services/Exchange/ExchangeSelectRequest.cs` - Request DTO for API

**Files Modified:**
1. `GridBot.ApiService/Services/SimpleTradingBotHostedService.cs` - No longer auto-starts; waits for IGridBotControlService
2. `GridBot.ApiService/Extensions/TradingBotExtensions.cs` - Registered new services as singletons
3. `GridBot.ApiService/Program.cs` - Added new API endpoints

**New API Endpoints:**
- `GET /api/trading/bot-status` - Enhanced status including exchange info and errors
- `POST /api/trading/control/start` - Start the trading bot
- `POST /api/trading/control/stop` - Stop the trading bot
- `POST /api/trading/control/pause` - Pause (updated to use IGridBotControlService)
- `POST /api/trading/control/resume` - Resume (updated to use IGridBotControlService)
- `GET /api/exchange/available` - List available DEXes with status
- `GET /api/exchange/current` - Get current DEX selection
- `POST /api/exchange/select` - Switch DEX (requires bot to be stopped)

**Key Implementation Details:**
- GridBotControlService uses `lock` for thread-safe status transitions
- Bot no longer auto-starts - must call StartAsync explicitly
- Exchange switching blocked while bot is running (safety feature)
- StatusChanged event raised on all state transitions for UI integration

**Build Status:** SUCCESS (0 warnings, 0 errors)

### Phase 2 Complete (2025-12-26)
Blazor UI components implemented by `blazor-mudblazor-developer`:

**Files Created:**
1. `GridBot.ApiService/Components/Dashboard/BotControlPanel.razor` - Main bot control component
   - Large Start button (green, filled) when bot is stopped
   - Large Stop button (red, filled) when bot is running
   - Pause/Resume toggle buttons next to main button when running
   - Status chip with color coding:
     - Stopped = Default/Grey
     - Starting = Info/Blue (with spinner)
     - Running = Success/Green
     - Paused = Warning/Orange
     - Stopping = Info/Blue (with spinner)
     - Error = Error/Red
   - Error message display via MudAlert when in Error state
   - Selected DEX name shown as chip in card header
   - Subscribes to StatusChanged and ExchangeChanged events
   - Implements IDisposable for proper cleanup
   - Shows warning when no exchange selected

2. `GridBot.ApiService/Components/Dashboard/ExchangeSelector.razor` - DEX selection component
   - MudSelect dropdown to choose DEX
   - Shows each exchange with Connected/Disconnected badge
   - Disabled when bot is running with tooltip explanation
   - Calls SelectExchangeAsync on selection change
   - Shows errors via Snackbar
   - Refresh button to refresh exchange status
   - Subscribes to StatusChanged and ExchangeChanged events
   - Implements IDisposable for proper cleanup

**Files Modified:**
1. `GridBot.ApiService/Components/Pages/Dashboard.razor`
   - Added BotControlPanel as Row 0 at the TOP of dashboard (before Row 1)
   - Full width (xs="12") MudGrid layout

2. `GridBot.ApiService/Components/Dashboard/SettingsPanel.razor`
   - Added ExchangeSelector at the TOP of settings panel
   - Wrapped in MudPaper with subtle background

**Build Status:** SUCCESS (0 warnings, 0 errors)

**Component Design Patterns Used:**
- MudCard with MudCardHeader/MudCardContent structure
- MudChip for status badges with appropriate colors
- MudButton with Color, Variant, Disabled, StartIcon
- MudProgressCircular for loading states in buttons
- MudTooltip for disabled element explanations
- MudAlert for error/warning messages
- MudSelect for dropdown selection
- ISnackbar for user feedback messages
- Event subscription in OnInitialized, unsubscription in Dispose
- InvokeAsync(StateHasChanged) for thread-safe UI updates from events

### Code Review Complete (2025-12-26)
Reviewed by `csharp-code-reviewer`:

**Verdict:** APPROVED with fixes applied

**HIGH Priority Issues Found and Fixed:**

1. **Race Condition in PauseAsync/ResumeAsync** (GridBotControlService.cs)
   - Problem: State validation happened inside lock, but async engine operations ran outside
   - Fix: Added `BotStatus.Pausing` and `BotStatus.Resuming` transitional states
   - Files modified:
     - `GridBot.ApiService/Services/Bot/BotStatus.cs` - Added Pausing, Resuming states
     - `GridBot.ApiService/Services/Bot/GridBotControlService.cs` - Use transitional states with rollback on error
     - `GridBot.ApiService/Components/Dashboard/BotControlPanel.razor` - Updated UI to show new states

2. **TOCTOU Race in SelectExchangeAsync** (ExchangeSelectionService.cs)
   - Problem: `_botControlService.IsRunning` check was outside the lock
   - Fix: Moved bot status check inside the lock block

**Good Patterns Confirmed:**
- Blazor components correctly implement IDisposable and unsubscribe from events
- `InvokeAsync(StateHasChanged)` properly used for thread-safe UI updates
- Consistent null checking with `ArgumentNullException.ThrowIfNull()`
- Proper cancellation token propagation throughout

**Build Status:** SUCCESS (0 warnings, 0 errors)

## Implementation Complete

### Summary
Grid Bot UI controls feature is complete:

1. **Backend Services:**
   - `IGridBotControlService` / `GridBotControlService` - Bot lifecycle control with 8 states
   - `IExchangeSelectionService` / `ExchangeSelectionService` - DEX selection and switching

2. **Blazor Components:**
   - `BotControlPanel.razor` - Start/Stop/Pause/Resume buttons with real-time status
   - `ExchangeSelector.razor` - DEX dropdown with connection status

3. **Key Features:**
   - Bot no longer auto-starts (user must click Start)
   - DEX switching requires stopping bot first (safety)
   - Real-time UI updates via StatusChanged events
   - Thread-safe state transitions with transitional states (Starting, Stopping, Pausing, Resuming)
   - Error display when bot enters Error state
   - Connection status badges for each DEX

### Next Steps (Optional)
1. Trading bot auditor review for trading-specific concerns
2. Visual verification when running locally
