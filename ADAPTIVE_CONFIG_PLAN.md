# Adaptive Runtime Configuration Plan

## Overview
Transform GridBot from static config to runtime-editable with adaptive auto-tuning via Blazor dashboard.

## User Requirements
- Hybrid Auto-Tuning: Engine suggests values, user can override, "Auto" toggle per setting
- Redis Persistence: Config survives restarts
- Single Market: Trade BTC or ETH (not both), resolve index from API
- Dashboard UI: MudBlazor settings panel with grouped settings

---

## Phase 1: Configuration Model & Service

### New Files

**GridBot.Core/Configuration/RuntimeGridConfig.cs**
- ConfigValue<T> wrapper with: Value, SuggestedValue, IsAuto, EffectiveValue
- Grid Strategy bucket: GridSpacingPercent, BuyLevels, SellLevels, OrderSizeUsdc
- Risk bucket: MaxDailyLossPercent, FlashCrashThresholdPercent, PauseCooldownMinutes
- Exchange bucket: Market (just "BTC" or "ETH"), ResolvedMarketIndex, Leverage
- Timing bucket: LoopIntervalSeconds, UsePostOnlyOrders

**GridBot.Core/Services/Configuration/IGridConfigurationService.cs**
- Current property (RuntimeGridConfig)
- ConfigChanged event
- LoadAsync, SaveAsync, UpdateAsync, ResetToDefaultsAsync methods

**GridBot.Core/Services/Configuration/GridConfigurationService.cs**
- Uses IDistributedCache (Redis) for persistence
- Fires ConfigChanged on updates
- Loads defaults from appsettings on first run

---

## Phase 2: Adaptive Parameter Service

### New Files

**GridBot.Core/Services/Adaptive/IAdaptiveParameterService.cs**
- CalculateSuggestionsAsync(marketId) returns AdaptiveSuggestions
- AdaptiveSuggestions record: SuggestedSpacing, SuggestedOrderSize, SuggestedLevels, Reasoning

**GridBot.Core/Services/Adaptive/AdaptiveParameterService.cs**
- Uses IIndicatorService from TrendIntelligence for ATR
- Calculates: spacing = clamp(ATR% * 0.5, 0.2%, 2.0%)
- Calculates: orderSize = (equity * maxPosition%) / totalLevels
- Gets candlestick data for ATR calculation

---

## Phase 3: Market Resolution

### Modify: GridBot.ApiService/Services/MarketData/MarketResolver.cs
- Add ResolveMarketIndexAsync(symbol) method
- Calls GetOrderBooksAsync(), finds market by symbol prefix
- Returns MarketId for "BTC" -> 4, "ETH" -> 2, etc.

### Modify: GridBot.ApiService/Services/MarketData/IMarketResolver.cs
- Add ResolveMarketIndexAsync signature
- Add GetAvailableMarketsAsync for dropdown

---

## Phase 4: Dashboard UI

### New: GridBot.ApiService/Components/Dashboard/SettingsPanel.razor
- MudExpansionPanels with 4 buckets
- Grid Strategy: spacing slider, levels numeric, order size
- Risk Management: loss limits, flash crash threshold
- Exchange: Market dropdown (BTC/ETH), leverage
- Save/Reset buttons

### New: GridBot.ApiService/Components/Dashboard/ConfigSlider.razor
- Reusable component with Auto toggle switch
- Shows suggested value chip when Auto is on
- Disables manual input when Auto is on

### Modify: GridBot.ApiService/Components/Pages/Dashboard.razor
- Add settings drawer or collapsible panel
- Settings icon button to open

---

## Phase 5: API Endpoints

### Modify: GridBot.ApiService/Program.cs
- GET /api/config - Get current config
- PUT /api/config - Update config
- POST /api/config/reset - Reset to defaults
- GET /api/config/suggestions - Get adaptive suggestions

---

## Phase 6: Engine Integration

### Modify: GridBot.Core/Services/Engine/SimpleTradingEngine.cs
- Change from IOptions<SimpleGridConfig> to IGridConfigurationService
- Update suggestions each cycle if HasAutoSettings
- Use EffectiveValue for all config reads

### Modify: GridBot.Core/Services/Grid/GridCalculator.cs
- Use IGridConfigurationService instead of IOptions
- Use EffectiveValue for spacing, levels, order size

### Modify: GridBot.Core/Services/Grid/GridManager.cs
- React to ConfigChanged events
- Rebuild grid when config changes

---

## File Summary

### New Files (7)
- GridBot.Core/Configuration/RuntimeGridConfig.cs
- GridBot.Core/Services/Configuration/IGridConfigurationService.cs
- GridBot.Core/Services/Configuration/GridConfigurationService.cs
- GridBot.Core/Services/Adaptive/IAdaptiveParameterService.cs
- GridBot.Core/Services/Adaptive/AdaptiveParameterService.cs
- GridBot.ApiService/Components/Dashboard/SettingsPanel.razor
- GridBot.ApiService/Components/Dashboard/ConfigSlider.razor

### Modified Files (7)
- GridBot.Core/Services/Engine/SimpleTradingEngine.cs
- GridBot.Core/Services/Grid/GridCalculator.cs
- GridBot.Core/Services/Grid/GridManager.cs
- GridBot.ApiService/Services/MarketData/MarketResolver.cs
- GridBot.ApiService/Services/MarketData/IMarketResolver.cs
- GridBot.ApiService/Components/Pages/Dashboard.razor
- GridBot.ApiService/Program.cs

---

## Config Buckets

| Bucket | Settings | Auto-Tunable |
|--------|----------|--------------|
| Grid Strategy | Spacing, Levels, Order Size | Yes (ATR/equity) |
| Risk Management | Loss limits, Flash crash | No (safety) |
| Exchange | Market, Leverage | No (user choice) |
| Timing | Loop interval | No (system) |

---

## Execution Order

1. Phase 1: RuntimeGridConfig + ConfigurationService
2. Phase 2: AdaptiveParameterService
3. Phase 3: MarketResolver update
4. Phase 4: Dashboard UI components
5. Phase 5: API endpoints
6. Phase 6: Engine integration
7. Test and commit
