# Session 4: Multi-DEX Abstraction Analysis

## Date: 2025-12-25
## Status: ANALYSIS COMPLETED

## Goal
Analyze current codebase architecture to determine readiness for adding new DEX integrations (e.g., Hyperliquid) alongside Lighter DEX.

## Executive Summary

**Current Abstraction Level: ~40-50%**

The codebase has **mixed abstraction quality**. While some core trading components (GridState, GridLevel, configuration) are reasonably well-isolated, there are significant **tight couplings to Lighter** that would require substantial refactoring to support new DEXes.

**Verdict**: Adding a new DEX is NOT a simple "add project + register services" task. It requires ~15-20 days of refactoring.

---

## Critical Tight Coupling Points

### 1. GridBot.Core → GridBot.Lighter (CRITICAL)
- File: `GridBot.Core.csproj`
- Issue: Core project directly references GridBot.Lighter
- Impact: Core is NOT DEX-agnostic

### 2. GridManager (CRITICAL)
- File: `GridBot.Core\Services\Grid\GridManager.cs:16-17`
```csharp
private readonly ILighterCommandClient _commandClient;
private readonly ILighterQueryClient _queryClient;
```
- Directly calls Lighter-specific: `CreateOrderBatchAsync()`, `CancelAllOrdersAsync()`, `GetActiveOrdersAsync()`

### 3. SimpleTradingEngine (CRITICAL)
- File: `GridBot.Core\Services\Engine\SimpleTradingEngine.cs:22`
```csharp
private readonly ILighterQueryClient _queryClient;
```
- Calls `GetOrderBookDetailsAsync()`, `GetAccountAsync()` directly

### 4. GridCalculator Scaling (CRITICAL)
- File: `GridBot.Core\Services\Grid\GridCalculator.cs`
```csharp
// Hardcoded Lighter scales
return (long)(price * OrderConstants.PriceScale); // 100
return (long)(amount * OrderConstants.BaseAssetScale); // 100_000_000
```
- Hyperliquid uses completely different decimal precision

### 5. API Endpoints (HIGH)
- File: `GridBot.ApiService\Program.cs:41-47`
- Routes hardcoded: `/api/lighter/*`
- Returns Lighter-specific models directly

### 6. Market Services (HIGH)
- `MarketResolver.cs` → ILighterQueryClient
- `MarketScalingService.cs` → ILighterQueryClient + Lighter decimals
- `DashboardStateService.cs` → ILighterQueryClient + ILighterRealtimeState

---

## Files Requiring Modification

| File | Coupling Type | Changes Needed |
|------|--------------|----------------|
| GridBot.Core.csproj | Project reference | Remove Lighter reference |
| GridManager.cs | Interface | Use abstract IOrderClient |
| SimpleTradingEngine.cs | Interface | Use abstract IMarketDataClient |
| GridCalculator.cs | Hardcoded scales | Use IScalingProvider |
| Program.cs (ApiService) | Routes/Models | DEX-agnostic routing |
| MarketResolver.cs | Interface | Abstract market resolution |
| MarketScalingService.cs | Interface + Logic | DEX adapter |
| DashboardStateService.cs | Interface | Abstract realtime state |
| AdaptiveParameterService.cs | Interface | Abstract market queries |

**Total: 16+ files directly impacted**

---

## What IS Well-Abstracted (Reusable)

✅ **ISimpleTradingEngine interface** - Generic contract
✅ **GridState model** - DEX-agnostic state tracking
✅ **GridLevel model** - Generic grid level representation
✅ **RuntimeGridConfig** - Generic configuration (except MarketIndex)
✅ **SimpleGridConfig** - Generic grid parameters
✅ **IMarketResolver interface** - Properly abstracted
✅ **IMarketScalingService interface** - Properly abstracted
✅ **DryRunCommandClient** - Good decorator pattern for testing

---

## Missing Abstractions Needed

### 1. IOrderClient (Replace ILighterCommandClient in Core)
```csharp
public interface IOrderClient
{
    Task<OrderResult> CreateOrderAsync(OrderRequest order, CancellationToken ct);
    Task<BatchResult> CreateOrderBatchAsync(IEnumerable<OrderRequest> orders, CancellationToken ct);
    Task<OrderResult> CancelOrderAsync(string orderId, CancellationToken ct);
    Task<OrderResult> CancelAllOrdersAsync(string marketId, CancellationToken ct);
}
```

### 2. IAccountClient (Abstract account queries)
```csharp
public interface IAccountClient
{
    Task<AccountInfo> GetAccountAsync(CancellationToken ct);
    Task<decimal> GetEquityAsync(CancellationToken ct);
    Task<Position> GetPositionAsync(string marketId, CancellationToken ct);
}
```

### 3. IMarketDataClient (Abstract market queries)
```csharp
public interface IMarketDataClient
{
    Task<MarketInfo> GetMarketAsync(string marketId, CancellationToken ct);
    Task<IReadOnlyList<MarketInfo>> GetAvailableMarketsAsync(CancellationToken ct);
    Task<OrderBookSnapshot> GetOrderBookAsync(string marketId, CancellationToken ct);
    Task<decimal> GetCurrentPriceAsync(string marketId, CancellationToken ct);
}
```

### 4. IScalingProvider (DEX-specific decimal handling)
```csharp
public interface IScalingProvider
{
    long ScalePrice(decimal price, string marketId);
    long ScaleAmount(decimal amount, string marketId);
    decimal UnscalePrice(long scaledPrice, string marketId);
    decimal UnscaleAmount(long scaledAmount, string marketId);
}
```

### 5. IAuthenticationProvider (DEX-specific auth)
```csharp
public interface IAuthenticationProvider
{
    Task<string> SignOrderAsync(OrderRequest order, CancellationToken ct);
    Task InitializeAsync(CancellationToken ct);
}
```

---

## Recommended Architecture

```
GridBot/
├── GridBot.Abstractions/           # NEW: DEX-agnostic interfaces
│   ├── Trading/
│   │   ├── IOrderClient.cs
│   │   ├── IAccountClient.cs
│   │   └── IMarketDataClient.cs
│   ├── Scaling/
│   │   └── IScalingProvider.cs
│   └── Auth/
│       └── IAuthenticationProvider.cs
│
├── GridBot.Core/                   # Trading engine (DEX-agnostic)
│   └── References: GridBot.Abstractions only
│
├── GridBot.Lighter/                # Lighter adapter
│   └── Implements: IOrderClient, IAccountClient, etc.
│
├── GridBot.Hyperliquid/            # NEW: Hyperliquid adapter
│   └── Implements: IOrderClient, IAccountClient, etc.
│
├── GridBot.ApiService/             # REST API with DEX factory
│   └── DexFactory pattern for runtime selection
│
└── GridBot.Web/                    # Blazor UI (unchanged)
```

---

## Effort Estimation

| Task | Days | Priority |
|------|------|----------|
| Create GridBot.Abstractions | 2-3 | P0 |
| Refactor GridManager | 1-2 | P0 |
| Refactor SimpleTradingEngine | 1 | P0 |
| Refactor GridCalculator | 1 | P0 |
| Create Lighter adapters | 1-2 | P0 |
| Refactor API endpoints | 2-3 | P1 |
| Create Hyperliquid project | 3-5 | P1 |
| Update configuration | 1 | P2 |
| **Total** | **15-20 days** | |

---

## Recommended Next Steps

### Phase A: Create Abstraction Layer (Priority 0)
1. Create `GridBot.Abstractions` project with DEX-agnostic interfaces
2. Define `IOrderClient`, `IMarketDataClient`, `IAccountClient`, `IScalingProvider`
3. Create generic Order, Account, Market models

### Phase B: Decouple Core (Priority 0)
1. Remove `GridBot.Lighter` reference from `GridBot.Core`
2. Refactor `GridManager` to use `IOrderClient`
3. Refactor `SimpleTradingEngine` to use `IMarketDataClient`
4. Move scaling logic to `IScalingProvider`

### Phase C: Create Lighter Adapter (Priority 0)
1. Implement `IOrderClient` → `LighterOrderClient`
2. Implement `IMarketDataClient` → `LighterMarketDataClient`
3. Implement `IScalingProvider` → `LighterScalingProvider`
4. Verify existing behavior unchanged

### Phase D: DEX Factory (Priority 1)
1. Create `IDexFactory` for runtime DEX selection
2. Update Program.cs with factory registration
3. Create DEX-agnostic API routes

### Phase E: Add Hyperliquid (Priority 1)
1. Create `GridBot.Hyperliquid` project
2. Implement all abstractions for Hyperliquid
3. Test parallel DEX support

---

## Conclusion

**Adding a new DEX today**: Requires modifying 16+ files, ~15-20 days work

**After implementing recommended architecture**: Add new project, implement interfaces, register in DI → ~3-5 days per new DEX

The investment in proper abstraction pays off significantly for each subsequent DEX integration.
