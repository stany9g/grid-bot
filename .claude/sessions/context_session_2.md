# Session 2: Blazor WebAssembly Architecture Fix

## Problem Statement
The GridBot web frontend was experiencing a critical error:
```
ManagedError: AggregateException_ctor_DefaultMessage (Root component type 'GridBot.Web.Components.Routes' could not be found in the assembly 'GridBot.Web'.)
```

Additionally, the dashboard data was reportedly displaying incorrectly.

## Root Cause Analysis

### Architecture Issue
The Blazor application was incorrectly configured to use **Interactive WebAssembly** mode for components that resided in the **server project**.

**Problematic Configuration (App.razor)**:
```html
<Routes @rendermode="InteractiveWebAssembly" />
```

**The Issue**:
1. `Routes.razor` was in `GridBot.Web/Components/Routes.razor` (server project)
2. When rendered with `InteractiveWebAssembly`, Blazor tries to load the component in the browser via WebAssembly
3. The browser cannot access the server-side assembly `GridBot.Web.dll`
4. Result: Component not found error

**Additional Routing Issue**:
The Router in `Routes.razor` only referenced the server assembly:
```razor
<Router AppAssembly="typeof(Program).Assembly">
```
This meant pages in `GridBot.Web.Client` (like `Dashboard.razor`) might not be found.

## Solution Implemented

### Changed from WebAssembly to Server Mode

**1. Modified `GridBot.Web/Components/App.razor`**:
- Changed all `@rendermode="InteractiveWebAssembly"` to `@rendermode="InteractiveServer"`
- Changed script src from fingerprinted asset to standard path

**2. Modified `GridBot.Web/Components/Routes.razor`**:
- Added `AdditionalAssemblies` to include the client project assembly
```razor
<Router AppAssembly="typeof(Program).Assembly"
        AdditionalAssemblies="[typeof(GridBot.Web.Client._Imports).Assembly]">
```

**3. Modified `GridBot.Web/Program.cs`**:
- Changed `.AddInteractiveWebAssemblyComponents()` to `.AddInteractiveServerComponents()`
- Changed `.AddInteractiveWebAssemblyRenderMode()` to `.AddInteractiveServerRenderMode()`

## Files Modified

| File | Change |
|------|--------|
| `GridBot.Web/Components/App.razor` | Switched render mode from WebAssembly to Server |
| `GridBot.Web/Components/Routes.razor` | Added client assembly to Router |
| `GridBot.Web/Program.cs` | Changed service registration to Server mode |

## Verification

1. **Build**: Successful with 0 warnings, 0 errors
2. **HTTP Test**: Dashboard returns HTTP 200
3. **HTML Inspection**: All MudBlazor components rendering correctly
   - ALTE Grid Bot Dashboard header
   - Trading State Card
   - Price Position Card
   - Operational Capacity Card
   - Grid Visualization
   - Risk Assessment Card
   - Trend Indicator Card
   - Moon Bag Status Card
   - Decision Cycle Metrics
   - Alerts Panel

## Architecture Decision: Server vs WebAssembly

**Why Server Mode is Better for This Application**:
1. The trading bot requires real-time data from backend services
2. Server mode provides direct access to backend services without API proxying complexity
3. Lower latency for SignalR-based updates
4. Simpler deployment (no WASM download overhead)
5. The application is internal/limited users, so server scaling is not a concern

**Trade-offs**:
- Requires persistent connection to server
- More server resources per concurrent user
- Not suitable for offline scenarios

## Additional Fix: Ambiguous Route Conflict

### Problem
After the initial fix, a new error appeared:
```
System.InvalidOperationException: The following routes are ambiguous:
'' in 'GridBot.Web.Components.Pages.Home'
'' in 'GridBot.Web.Client.Pages.Dashboard'
```

Both `Home.razor` and `Dashboard.razor` had `@page "/"` directive.

### Solution
1. **Removed `@page "/"` from `Dashboard.razor`** - now only has `@page "/dashboard"`
2. **Converted `Home.razor` to a redirect** - automatically redirects to `/dashboard`

### Files Modified (Additional)
| File | Change |
|------|--------|
| `GridBot.Web.Client/Pages/Dashboard.razor` | Removed `@page "/"`, kept only `@page "/dashboard"` |
| `GridBot.Web/Components/Pages/Home.razor` | Converted to redirect to `/dashboard` |

## Status
- [x] Root cause identified
- [x] Fix implemented
- [x] Build verified
- [x] Ambiguous route conflict resolved
- [x] Documentation updated
