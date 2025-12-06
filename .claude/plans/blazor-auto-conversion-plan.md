# Blazor Auto (Interactive WebAssembly) Conversion Plan

## Overview

This document outlines the plan to convert GridBot.Web from Blazor Server to Blazor Web Auto (Interactive WebAssembly) mode.

**Current State:** Blazor Server with `@rendermode InteractiveServer`
**Target State:** Blazor Web Auto with `@rendermode InteractiveAuto`

## Rationale

1. **Dashboard only makes HTTP API calls** - TradingApiClient communicates via HTTP to GridBot.ApiService
2. **No server-side service dependencies** - All data fetching is already done via HTTP
3. **Reduces server load** - Components run in the browser via WebAssembly
4. **Better scalability** - Server resources freed for API processing
5. **Offline capability** - Client can render even if server connection drops momentarily

## Architecture Analysis

### Components to Move to Client Project

| Component | Location | Reason |
|-----------|----------|--------|
| Dashboard.razor | Pages/ | Main interactive page |
| TradingStateCard.razor | Components/Dashboard/ | Interactive card |
| PricePositionCard.razor | Components/Dashboard/ | Interactive card |
| OperationalCapacityCard.razor | Components/Dashboard/ | Interactive card |
| GridVisualization.razor | Components/Dashboard/ | Interactive visualization |
| RiskAssessmentCard.razor | Components/Dashboard/ | Interactive card |
| TrendIndicatorCard.razor | Components/Dashboard/ | Interactive card |
| MoonBagStatusCard.razor | Components/Dashboard/ | Interactive card |
| DecisionCycleMetrics.razor | Components/Dashboard/ | Interactive card |
| RecoveryProgressCard.razor | Components/Dashboard/ | Interactive card |
| AlertsPanel.razor | Components/Dashboard/ | Interactive panel |

### Models to Move to Client Project

| Model | Location | Notes |
|-------|----------|-------|
| DashboardState.cs | Models/ | Full state model |
| AlertItem.cs | Models/ | Alert model |
| DashboardApiResponse + DTOs | Models/DashboardState.cs | API response DTOs |

### Services to Refactor

| Service | Current Location | Target Location | Notes |
|---------|------------------|-----------------|-------|
| TradingApiClient | GridBot.Web | GridBot.Web.Client | HTTP client, works in WASM |
| DashboardStateService | GridBot.Web | GridBot.Web.Client | Polling service, runs in browser |
| IDashboardStateService | GridBot.Web | GridBot.Web.Client | Interface |
| WebhookNotificationService | GridBot.Web | **GridBot.Web (Server)** | Server-side only |
| IWebhookNotificationService | GridBot.Web | **GridBot.Web (Server)** | Server-side only |
| WebhookOptions | GridBot.Web | **GridBot.Web (Server)** | Server config |
| WebhookPayload | GridBot.Web | **GridBot.Web (Server)** | Server-side model |

### Components to Keep on Server

| Component | Location | Reason |
|-----------|----------|--------|
| App.razor | Components/ | Entry point, hosts client |
| MainLayout.razor | Components/Layout/ | Could be shared or client |
| Routes.razor | Components/ | Routing |

## Implementation Steps

### Step 1: Create GridBot.Web.Client Project

Create new Blazor WebAssembly client project:

```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="10.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0" />
    <PackageReference Include="MudBlazor" Version="8.5.0" />
  </ItemGroup>
</Project>
```

### Step 2: Create Project Structure

```
GridBot.Web.Client/
  Components/
    Dashboard/
      TradingStateCard.razor
      PricePositionCard.razor
      OperationalCapacityCard.razor
      GridVisualization.razor
      RiskAssessmentCard.razor
      TrendIndicatorCard.razor
      MoonBagStatusCard.razor
      DecisionCycleMetrics.razor
      RecoveryProgressCard.razor
      AlertsPanel.razor
  Models/
    DashboardState.cs
    AlertItem.cs
  Pages/
    Dashboard.razor
  Services/
    IDashboardStateService.cs
    DashboardStateService.cs
  TradingApiClient.cs
  _Imports.razor
  Program.cs
```

### Step 3: Update Server Project (GridBot.Web)

1. Add reference to Client project
2. Update Program.cs for Interactive WebAssembly components
3. Update App.razor for InteractiveAuto render mode
4. Keep webhook service for server-side notifications

**Updated Program.cs:**
```csharp
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// ... keep webhook services ...

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(GridBot.Web.Client._Imports).Assembly);
```

**Updated App.razor:**
```razor
<HeadOutlet @rendermode="InteractiveAuto" />
<Routes @rendermode="InteractiveAuto" />
```

### Step 4: Create Client Program.cs

```csharp
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using GridBot.Web.Client;
using GridBot.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Configure HttpClient for API calls
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register TradingApiClient
builder.Services.AddScoped<TradingApiClient>();

// Register DashboardStateService as scoped (per-circuit in WASM = singleton)
builder.Services.AddScoped<IDashboardStateService, DashboardStateService>();

await builder.Build().RunAsync();
```

### Step 5: Update Solution File

Add GridBot.Web.Client to GridBot.slnx:
```xml
<Project Path="GridBot.Web.Client/GridBot.Web.Client.csproj" />
```

### Step 6: Update AppHost (Optional)

The AppHost should not need changes since the client is bundled with the server project.

## Key Considerations

### API Base URL

The client runs in the browser and needs to call the API. Options:
1. **Relative URLs (Preferred)** - If API is proxied through the web server
2. **Absolute URLs** - If API is on a different origin (requires CORS)

Current setup uses Aspire service discovery (`https+http://apiservice`), which won't work in the browser. The web server should proxy API calls or CORS should be configured.

**Recommendation:** Configure the web server to proxy `/api/*` requests to the apiservice, so the client can use relative URLs.

### CORS Configuration

If not using proxy, add CORS to GridBot.ApiService:
```csharp
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("https://localhost:xxxx") // Web frontend URL
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});
```

### Webhook Service Architecture

The WebhookNotificationService must remain on the server because:
1. It makes HTTP calls to Home Assistant (internal network)
2. It should not be accessible from the browser
3. State change detection should be centralized

**Architecture Change:**
- DashboardStateService on the client polls the API and manages UI state
- Webhook notifications should be triggered by the API service itself, not the web frontend
- Consider moving webhook logic to GridBot.ApiService

### Service Lifetime Changes

| Service | Server (Blazor Server) | Client (WASM) |
|---------|------------------------|---------------|
| DashboardStateService | Singleton | Scoped (per circuit) |
| TradingApiClient | Scoped (per circuit) | Scoped |

In WASM, each browser tab is isolated, so Scoped services effectively behave as singletons within that tab.

## Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| API CORS issues | High | Configure proxy or CORS properly |
| Service discovery in browser | High | Use relative URLs with proxy |
| Webhook loss | Medium | Move webhook to API service |
| Larger initial load | Low | WebAssembly bundle size is acceptable |
| Browser compatibility | Low | Modern browsers all support WASM |

## Testing Plan

1. Verify dashboard loads in Auto mode
2. Verify API calls work from browser
3. Verify real-time updates via polling
4. Verify MudBlazor components render correctly
5. Verify alert management works
6. Test in multiple browsers (Chrome, Firefox, Edge)

## Rollback Plan

If issues arise:
1. Remove Client project reference from Server
2. Revert App.razor to InteractiveServer
3. Revert Program.cs to AddInteractiveServerComponents
4. Move components back to server project

## Files to Create

1. `GridBot.Web.Client/GridBot.Web.Client.csproj`
2. `GridBot.Web.Client/_Imports.razor`
3. `GridBot.Web.Client/Program.cs`
4. Move all Dashboard components
5. Move TradingApiClient
6. Move DashboardStateService

## Files to Modify

1. `GridBot.Web/GridBot.Web.csproj` - Add reference to Client
2. `GridBot.Web/Program.cs` - Configure WASM components
3. `GridBot.Web/Components/App.razor` - Use InteractiveAuto
4. `GridBot.slnx` - Add Client project

## Estimated Effort

- Project setup: 15 min
- File migration: 30 min
- Configuration updates: 15 min
- Testing: 30 min
- **Total: ~1.5 hours**
