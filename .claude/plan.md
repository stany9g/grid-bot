# Migration Plan: Consolidate Web Projects into ApiService

## Summary
Merge `GridBot.Web` and `GridBot.Web.Client` into `GridBot.ApiService` to create a single unified project that serves both the trading API and the Blazor Server dashboard.

## Benefits
1. **Simpler architecture** - One project instead of three
2. **Direct service injection** - Dashboard can inject trading services directly (no HTTP polling)
3. **Real-time updates** - Services can push updates to dashboard via events
4. **Reduced latency** - No HTTP round-trips for dashboard data
5. **Easier deployment** - Single container/process

---

## Phase 1: Update ApiService Project File

**File:** `GridBot.ApiService/GridBot.ApiService.csproj`

Add Blazor Server and MudBlazor dependencies:
```xml
<PackageReference Include="MudBlazor" Version="8.5.0" />
```

Change SDK to Web (already is) and ensure Razor components are enabled.

---

## Phase 2: Add Blazor Infrastructure to ApiService

### 2.1 Create Components folder structure
```
GridBot.ApiService/
├── Components/
│   ├── App.razor              (from GridBot.Web)
│   ├── Routes.razor           (from GridBot.Web)
│   ├── _Imports.razor         (merged from both)
│   ├── Layout/
│   │   └── MainLayout.razor   (from GridBot.Web)
│   ├── Pages/
│   │   ├── Home.razor         (redirect to Dashboard)
│   │   ├── Dashboard.razor    (from GridBot.Web.Client)
│   │   └── Error.razor        (from GridBot.Web)
│   └── Dashboard/
│       ├── TradingStateCard.razor
│       ├── PricePositionCard.razor
│       ├── OperationalCapacityCard.razor
│       ├── GridVisualization.razor
│       ├── RiskAssessmentCard.razor
│       ├── TrendIndicatorCard.razor
│       ├── MoonBagStatusCard.razor
│       ├── DecisionCycleMetrics.razor
│       ├── RecoveryProgressCard.razor
│       └── AlertsPanel.razor
```

### 2.2 Create _Imports.razor
```razor
@using System.Net.Http
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using static Microsoft.AspNetCore.Components.Web.RenderMode
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.JSInterop
@using MudBlazor
@using GridBot.ApiService
@using GridBot.ApiService.Components
@using GridBot.ApiService.Components.Layout
@using GridBot.ApiService.Components.Dashboard
@using GridBot.ApiService.Models.Dashboard
@using GridBot.ApiService.Services.Dashboard
```

---

## Phase 3: Create New Dashboard Service (Direct Injection)

### 3.1 Create `Services/Dashboard/IDashboardStateService.cs`

Instead of HTTP polling, this service will:
- Directly inject trading services
- Subscribe to service events for real-time updates
- Aggregate state from multiple services on demand

```csharp
public interface IDashboardStateService : IDisposable
{
    event EventHandler<DashboardState>? StateChanged;
    DashboardState CurrentState { get; }
    Task RefreshAsync(CancellationToken ct = default);
    Task AddAlertAsync(AlertItem alert, CancellationToken ct = default);
    void ClearAlerts();
    void AcknowledgeAlert(Guid alertId);
}
```

### 3.2 Create `Services/Dashboard/DashboardStateService.cs`

This service will inject:
- `ITradingStateService`
- `ITradingDecisionEngine`
- `IMoonBagManager`
- `ILossMonitor`
- `IFlashCrashDetector`
- `IGridLifecycleService`
- `IMarketDataService`
- `ILighterQueryClient`
- `IRiskConfiguration`

And aggregate their data into `DashboardState`.

### 3.3 Move models to `Models/Dashboard/`

Files to create (some already exist):
- `DashboardState.cs` - Main state record
- `AlertItem.cs` - Alert model
- `GridStateInfo.cs` - Grid display model
- `RiskInfo.cs` - Risk display model
- `TrendInfo.cs` - Trend display model
- `MoonBagInfo.cs` - Moon bag display model
- `DecisionCycleInfo.cs` - Cycle metrics model

---

## Phase 4: Update Program.cs

### 4.1 Add Blazor Server configuration

```csharp
// Add MudBlazor services
builder.Services.AddMudServices();

// Add Razor Components with Interactive Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register dashboard state service
builder.Services.AddScoped<IDashboardStateService, DashboardStateService>();
```

### 4.2 Add Blazor middleware

```csharp
app.UseAntiforgery();
app.MapStaticAssets();

// Map Razor components
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
```

---

## Phase 5: Refactor Dashboard.razor

Change from HTTP-based state to direct service injection:

**Before:**
```razor
@inject IDashboardStateService DashboardStateService
// DashboardStateService polls HTTP API
```

**After:**
```razor
@inject IDashboardStateService DashboardStateService
// DashboardStateService directly injects trading services
```

The interface stays the same, only the implementation changes.

---

## Phase 6: Update AppHost

### 6.1 Remove web project reference

**Before:**
```csharp
var apiService = builder.AddProject<Projects.GridBot_ApiService>("apiservice");

builder.AddProject<Projects.GridBot_Web>("webfrontend")
    .WithReference(apiService);
```

**After:**
```csharp
builder.AddProject<Projects.GridBot_ApiService>("apiservice")
    .WithExternalHttpEndpoints();
```

### 6.2 Update AppHost.csproj

Remove reference to GridBot.Web project.

---

## Phase 7: Remove Old Projects

1. Delete `GridBot.Web/` directory
2. Delete `GridBot.Web.Client/` directory
3. Remove from solution file (`GridBot.slnx`)
4. Update any remaining references

---

## Phase 8: Add wwwroot Assets

Copy from GridBot.Web:
- `wwwroot/app.css`
- `wwwroot/favicon.png`
- Any other static assets

---

## File Migration Mapping

| Source | Destination |
|--------|-------------|
| `GridBot.Web.Client/Pages/Dashboard.razor` | `GridBot.ApiService/Components/Pages/Dashboard.razor` |
| `GridBot.Web.Client/Components/Dashboard/*.razor` | `GridBot.ApiService/Components/Dashboard/*.razor` |
| `GridBot.Web/Components/App.razor` | `GridBot.ApiService/Components/App.razor` |
| `GridBot.Web/Components/Routes.razor` | `GridBot.ApiService/Components/Routes.razor` |
| `GridBot.Web/Components/Layout/MainLayout.razor` | `GridBot.ApiService/Components/Layout/MainLayout.razor` |
| `GridBot.Web/Components/Pages/Error.razor` | `GridBot.ApiService/Components/Pages/Error.razor` |
| `GridBot.Web.Client/Models/AlertItem.cs` | `GridBot.ApiService/Models/Dashboard/AlertItem.cs` |
| `GridBot.Web.Client/Models/DashboardState.cs` | `GridBot.ApiService/Models/Dashboard/DashboardState.cs` |

---

## Namespace Changes

| Old Namespace | New Namespace |
|---------------|---------------|
| `GridBot.Web.Client.Models` | `GridBot.ApiService.Models.Dashboard` |
| `GridBot.Web.Client.Services` | `GridBot.ApiService.Services.Dashboard` |
| `GridBot.Web.Client.Components.Dashboard` | `GridBot.ApiService.Components.Dashboard` |
| `GridBot.Web.Services` | `GridBot.ApiService.Services.Dashboard` |
| `GridBot.Web.Components` | `GridBot.ApiService.Components` |

---

## Key Implementation Notes

1. **DashboardStateService** - The new implementation will be a `BackgroundService` that:
   - Runs a polling loop (every 5 seconds)
   - Directly calls trading services instead of HTTP
   - Builds `DashboardState` from service responses
   - Raises `StateChanged` event for Blazor components

2. **Real-time potential** - Future enhancement could use SignalR or service events for true real-time updates

3. **Remove TradingApiClient** - No longer needed since we inject services directly

4. **Keep API endpoints** - The REST API remains for external integrations

---

## Execution Order

1. Update `GridBot.ApiService.csproj` (add MudBlazor)
2. Create Components folder structure
3. Copy Blazor files (App, Routes, Layout, _Imports)
4. Copy Dashboard components
5. Create/move model files
6. Create `DashboardStateService` implementation
7. Update `Program.cs` with Blazor configuration
8. Add wwwroot assets
9. Update AppHost
10. Build and test
11. Remove old projects
12. Final cleanup
