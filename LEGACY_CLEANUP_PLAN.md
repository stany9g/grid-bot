# Legacy Code Cleanup Plan

## Goal
Remove all deprecated legacy code from GridBot.ApiService, keeping only the new modular architecture.

## Current State
- 75 service files in Services/
- 42 model files in Models/
- 13 extension files in Extensions/
- 3 configuration files

## What to KEEP

### Must Keep (Required for new architecture)
```
GridBot.ApiService/
├── Adapters/                      # KEEP - Bridges to new modules
│   ├── AdvancedRiskMarketDataAdapter.cs
│   ├── MoonBagConfigurationAdapter.cs
│   ├── MoonBagMarketDataAdapter.cs
│   ├── TrendConfigurationAdapter.cs
│   └── TrendMarketDataAdapter.cs
│
├── Components/                    # KEEP - Blazor UI
│
├── Extensions/
│   └── TradingBotExtensions.cs    # KEEP - New consolidated DI
│
├── Services/
│   ├── Dashboard/                 # KEEP - Blazor dashboard
│   │   ├── DashboardStateService.cs
│   │   └── IDashboardStateService.cs
│   │
│   ├── MarketData/                # KEEP - Adapters depend on these
│   │   ├── IMarketDataService.cs
│   │   ├── MarketDataService.cs
│   │   ├── IMarketResolver.cs
│   │   ├── MarketResolver.cs
│   │   ├── IMarketScalingService.cs
│   │   └── MarketScalingService.cs
│   │
│   ├── Telemetry/                 # KEEP - OpenTelemetry metrics
│   │   └── TradingMetrics.cs
│   │
│   └── TradingBotHealthCheck.cs   # KEEP - Health endpoint
│
├── Models/
│   └── Dashboard/                 # KEEP - Dashboard DTOs
│       ├── AlertItem.cs
│       ├── DashboardDtos.cs
│       └── DashboardState.cs
│
├── Configuration/                 # SIMPLIFY - Keep minimal
│   └── (will create new simplified version)
│
└── Program.cs                     # UPDATE - Remove legacy registrations
```

## What to DELETE

### Services to DELETE (75 -> 8 files)
```
DELETE: Services/Capacity/           (2 files) - Not needed
DELETE: Services/Connectivity/       (4 files) - Basic retry in Core
DELETE: Services/DecisionEngine/     (4 files) - Replaced by Core.SimpleTradingEngine
DELETE: Services/Grid/               (6 files) - Replaced by Core.GridManager
DELETE: Services/Indicators/         (2 files) - In TrendIntelligence
DELETE: Services/Inventory/          (2 files) - In TrendIntelligence
DELETE: Services/Logging/            (2 files) - Use standard logging
DELETE: Services/Metrics/            (2 files) - Basic metrics in Core
DELETE: Services/MoonBag/            (8 files) - In GridBot.MoonBag
DELETE: Services/Notifications/      (2 files) - In AdvancedRisk
DELETE: Services/OrderBook/          (2 files) - In Core
DELETE: Services/Persistence/        (4 files) - Simplified in Core
DELETE: Services/Rebalancing/        (2 files) - In TrendIntelligence
DELETE: Services/Risk/               (10 files) - In Core + AdvancedRisk
DELETE: Services/State/              (4 files) - Simplified in Core
DELETE: Services/Trend/              (4 files) - In TrendIntelligence
DELETE: Services/Validation/         (2 files) - In Core
DELETE: Services/TradingBotHostedService.cs - Will create new one using Core
```

### Models to DELETE (42 -> 3 files)
```
DELETE: Models/Trading/              (39 files) - Now in modules
DELETE: Models/Logging/              (1 file) - Not needed
```

### Extensions to DELETE (13 -> 1 file)
```
DELETE: Extensions/GridServiceExtensions.cs
DELETE: Extensions/TrendServiceExtensions.cs
DELETE: Extensions/PersistenceServiceExtensions.cs
DELETE: Extensions/TelemetryServiceExtensions.cs
DELETE: Extensions/MoonBagServiceExtensions.cs
DELETE: Extensions/DecisionEngineServiceExtensions.cs
DELETE: Extensions/MarketDataServiceExtensions.cs
DELETE: Extensions/ValidationServiceExtensions.cs
DELETE: Extensions/ConnectivityServiceExtensions.cs
DELETE: Extensions/LoggingServiceExtensions.cs
DELETE: Extensions/TradingBotServiceExtensions.cs
DELETE: Extensions/RiskServiceExtensions.cs
KEEP:   Extensions/TradingBotExtensions.cs
```

### Configuration to DELETE/SIMPLIFY
```
DELETE: Configuration/TradingBotOptions.cs (883 lines!) - Use Core.SimpleGridConfig
DELETE: Configuration/IRiskConfiguration.cs
DELETE: Configuration/RiskConfiguration.cs
```

## New Files to CREATE

### 1. SimpleTradingBotHostedService.cs
New hosted service that uses GridBot.Core's SimpleTradingEngine instead of legacy.

### 2. Simplified Configuration
Map appsettings.json to Core.SimpleGridConfig (20 params, not 160).

### 3. Updated Program.cs
Remove all legacy service registrations, use only:
- AddGridBotCore()
- AddTrendIntelligence() (optional)
- AddMoonBag() (optional)
- AddAdvancedRisk() (optional)

## Execution Phases

### Phase 7: Delete Legacy Services
1. Delete Services/Capacity/
2. Delete Services/Connectivity/
3. Delete Services/DecisionEngine/
4. Delete Services/Grid/
5. Delete Services/Indicators/
6. Delete Services/Inventory/
7. Delete Services/Logging/
8. Delete Services/Metrics/
9. Delete Services/MoonBag/
10. Delete Services/Notifications/
11. Delete Services/OrderBook/
12. Delete Services/Persistence/
13. Delete Services/Rebalancing/
14. Delete Services/Risk/
15. Delete Services/State/
16. Delete Services/Trend/
17. Delete Services/Validation/
18. Delete Services/TradingBotHostedService.cs

### Phase 8: Delete Legacy Models
1. Delete Models/Trading/
2. Delete Models/Logging/

### Phase 9: Delete Legacy Extensions
1. Delete all Extensions/* except TradingBotExtensions.cs

### Phase 10: Delete Legacy Configuration
1. Delete Configuration/TradingBotOptions.cs
2. Delete Configuration/IRiskConfiguration.cs
3. Delete Configuration/RiskConfiguration.cs

### Phase 11: Create New Hosted Service
1. Create SimpleTradingBotHostedService.cs using Core.SimpleTradingEngine
2. Update TradingBotExtensions.cs

### Phase 12: Update Program.cs
1. Remove legacy service registrations
2. Simplify to use only new modules
3. Update appsettings.json

### Phase 13: Fix Adapters
1. Update adapters to work without legacy services
2. Move required types to appropriate modules if needed

### Phase 14: Final Cleanup
1. Build and fix any remaining issues
2. Update documentation
3. Commit

## Expected Result

### Before Cleanup
```
GridBot.ApiService/
├── Services/     (75 files, ~15,000 lines)
├── Models/       (42 files, ~3,000 lines)
├── Extensions/   (13 files, ~1,500 lines)
├── Configuration/ (3 files, ~900 lines)
Total: ~133 files, ~20,000+ lines
```

### After Cleanup
```
GridBot.ApiService/
├── Adapters/     (5 files, ~300 lines)
├── Services/     (8 files, ~600 lines)
├── Models/       (3 files, ~100 lines)
├── Extensions/   (1 file, ~200 lines)
├── Components/   (unchanged)
Total: ~17 files, ~1,200 lines
```

**Reduction: ~85% fewer files, ~94% less code!**

## Notes

- The Blazor dashboard will need updates to work with new data sources
- Some API endpoints in Program.cs may need adjustment
- Integration tests will need to be updated
