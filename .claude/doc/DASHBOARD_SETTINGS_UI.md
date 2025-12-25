# Dashboard Settings UI - Implementation Documentation

## Overview

This document describes the implementation of the Dashboard Settings UI for GridBot's adaptive runtime configuration. The UI provides a settings panel where users can configure grid trading parameters with hybrid auto-tuning support.

## Components Created

### 1. ConfigSlider.razor

**Location:** `GridBot.ApiService/Components/Dashboard/ConfigSlider.razor`

A reusable component for auto-tunable decimal values.

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| Label | string | Yes | Field label text |
| Value | decimal | No | Current value |
| ValueChanged | EventCallback<decimal> | No | Value change callback |
| SuggestedValue | decimal? | No | Auto-tuned suggestion |
| IsAuto | bool | No | Whether auto-tuning is enabled |
| IsAutoChanged | EventCallback<bool> | No | Auto toggle callback |
| Min | decimal | No | Minimum allowed value |
| Max | decimal | No | Maximum allowed value |
| Step | decimal | No | Slider/input step (default 0.01) |
| Unit | string | No | Unit suffix (e.g., "%", " USDC") |
| UseSlider | bool | No | Show slider (default true) |
| DangerouslyLowThreshold | decimal? | No | Danger warning threshold |
| DangerouslyHighThreshold | decimal? | No | Danger warning threshold |

**Features:**
- MudSwitch for Auto toggle
- MudSlider + MudNumericField for value input
- Disabled when IsAuto=true
- Shows suggested value as chip when available
- Warning alerts based on thresholds

**Warning Levels:**
1. **Danger** - Value below DangerouslyLowThreshold or above DangerouslyHighThreshold
2. **Warning** - Value <50% or >200% of suggestion
3. **Info** - Value <80% or >125% of suggestion

### 2. ConfigLevelsInput.razor

**Location:** `GridBot.ApiService/Components/Dashboard/ConfigLevelsInput.razor`

Similar to ConfigSlider but optimized for integer level inputs.

**Parameters:**
| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| Label | string | Yes | Field label text |
| Value | int | No | Current value |
| ValueChanged | EventCallback<int> | No | Value change callback |
| SuggestedValue | int? | No | Auto-tuned suggestion |
| IsAuto | bool | No | Whether auto-tuning is enabled |
| IsAutoChanged | EventCallback<bool> | No | Auto toggle callback |
| Min | int | No | Minimum allowed (default 2) |
| Max | int | No | Maximum allowed (default 30) |

**Features:**
- MudSwitch for Auto toggle
- MudNumericField with "levels" adornment
- Warning for <3 or >25 levels
- Suggestion deviation warnings

### 3. SettingsPanel.razor

**Location:** `GridBot.ApiService/Components/Dashboard/SettingsPanel.razor`

Main settings panel containing 4 expansion panels.

**Dependencies Injected:**
- `IGridConfigurationService` - Runtime configuration
- `IMarketResolver` - Market data
- `IAdaptiveParameterService` - Suggestion calculation
- `ISnackbar` - User feedback

**Panel Structure:**

#### Grid Strategy Panel (Expanded by Default)
| Setting | Component | Range | Thresholds |
|---------|-----------|-------|------------|
| Grid Spacing | ConfigSlider | 0.15% - 5.0% | Dangerous: <0.25%, >3.0% |
| Buy Levels | ConfigLevelsInput | 2 - 30 | Warning: <3, >25 |
| Sell Levels | ConfigLevelsInput | 2 - 30 | Warning: <3, >25 |
| Order Size | ConfigSlider | 5 - 5000 USDC | Dangerous: <15 |

#### Risk Management Panel
| Setting | Component | Range |
|---------|-----------|-------|
| Max Daily Loss | MudSlider | 1% - 20% |
| Flash Crash Threshold | MudSlider | 3% - 15% |
| Pause Cooldown | MudNumericField | 1 - 120 minutes |
| Max Position Size | MudSlider | 1% - 50% |

#### Exchange Panel
| Setting | Component | Range |
|---------|-----------|-------|
| Market | MudSelect | Dynamic (from API) |
| Leverage | MudSlider | 1x - 10x |

#### Timing Panel
| Setting | Component | Range |
|---------|-----------|-------|
| Loop Interval | MudNumericField | 1 - 60 seconds |
| Post-Only Orders | MudSwitch | On/Off |

**Features:**
- Validation against hard limits with error display
- "Refresh Suggestions" button for on-demand ATR calculation
- "Save Configuration" button (disabled if validation fails)
- "Reset to Defaults" button
- Reactive updates via ConfigChanged event subscription

## Dashboard Integration

**File Modified:** `GridBot.ApiService/Components/Pages/Dashboard.razor`

**Changes:**
1. Added MudDrawer anchored on right side (500px width)
2. Added Settings icon button in header
3. Drawer contains SettingsPanel component
4. Temporary variant (overlay, closes on outside click)

## Configuration Flow

```
User opens Settings drawer
        |
        v
SettingsPanel loads current config from IGridConfigurationService
        |
        v
User modifies values (auto or manual)
        |
        v
Local validation runs (hard limits)
        |
        v
User clicks "Save Configuration"
        |
        v
IGridConfigurationService.UpdateAsync() called
        |
        v
Config persisted to Redis
        |
        v
ConfigChanged event fires
        |
        v
All subscribers (including SimpleTradingEngine) update
```

## Warning Thresholds Reference

From `AUTO_TUNING_RISK_ASSESSMENT.md`:

### Grid Spacing
- **DangerouslyTight:** < 0.25% (High fee drag, flash crash exposure)
- **TooTight:** < suggested * 0.5
- **TooWide:** > suggested * 2.0
- **DangerouslyWide:** > 3.0% (Low fill frequency)

### Order Size
- **DangerouslySmall:** < 15 USDC (May not cover minimum trade)
- **DangerouslyLarge:** > equity * 20% / totalLevels

### Levels
- **TooFew:** < 3 per side (Insufficient coverage)
- **TooMany:** > 25 per side (Order management overhead)

### Leverage
- **Warning:** > 5x (Increased liquidation risk)

## Hard Limits (Non-Negotiable)

These cannot be overridden and are enforced in `RuntimeGridConfig.Validate()`:

| Parameter | Min | Max |
|-----------|-----|-----|
| GridSpacingPercent | 0.15% | 5.0% |
| MaxDailyLossPercent | 1% | 20% |
| FlashCrashThresholdPercent | 3% | 15% |
| Leverage | 1x | 10x |
| MinOrderSizeUsdc | 5 | - |
| TotalLevels | 4 | 60 |

## Build Status

All 8 projects compile with 0 warnings, 0 errors:
- GridBot.Core
- GridBot.TrendIntelligence
- GridBot.MoonBag
- GridBot.AdvancedRisk
- GridBot.Lighter
- GridBot.ServiceDefaults
- GridBot.ApiService
- GridBot.AppHost

## Future Enhancements

1. **Equity Integration** - Currently uses placeholder (1000 USDC) for suggestion calculation. Should fetch actual equity from account service.
2. **Visual Verification** - Use Playwright MCP for browser automation testing
3. **Keyboard Navigation** - Add keyboard shortcuts for common actions
4. **Undo/Redo** - Track configuration history for undo capability
5. **Profiles** - Save/load named configuration profiles
