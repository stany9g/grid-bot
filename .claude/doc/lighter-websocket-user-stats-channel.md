# Lighter DEX WebSocket: user_stats Channel for Perps Trading Balance

## Executive Summary

The `account_all` channel does NOT include the perps collateral/available balance at the root level. Instead, Lighter DEX provides a **separate** `user_stats` channel that contains the actual trading-relevant balance information (collateral, available_balance, buying_power, etc.).

**Key Finding**: The current implementation incorrectly calculates `collateral` and `availableBalance` from `assets.3.balance` (USDC spot balance), which is the **spot** wallet balance, not the **perps trading** balance.

## The Problem

Current JSON received from `account_all` channel:
```json
{
  "account": 293,
  "assets": {
    "1": {"symbol": "ETH", "asset_id": 1, "balance": "2.25000000"},
    "3": {"symbol": "USDC", "asset_id": 3, "balance": "7.507500", "locked_balance": "0.000000"}
  },
  "channel": "account_all:293",
  "positions": {...}
}
```

What the UI shows:
- USDC/Spot: $7.50 (matches `assets.3.balance`)
- USDC/Perps: $4,955.30 (THIS is what we need - but it's NOT in `account_all`)

## Solution: Subscribe to `user_stats` Channel

The `user_stats` channel provides the perps trading balance information.

### Subscription Message

```json
{
  "type": "subscribe",
  "channel": "user_stats/{ACCOUNT_ID}",
  "auth": "<AUTH_TOKEN>"
}
```

### Response Structure

```json
{
  "channel": "user_stats:{ACCOUNT_ID}",
  "stats": {
    "collateral": "4955.30",
    "portfolio_value": "5123.45",
    "leverage": "2.50",
    "available_balance": "3500.00",
    "margin_usage": "0.35",
    "buying_power": "8750.00",
    "cross_stats": {
      "collateral": "4955.30",
      "portfolio_value": "5123.45",
      "leverage": "2.50",
      "available_balance": "3500.00",
      "margin_usage": "0.35",
      "buying_power": "8750.00"
    },
    "total_stats": {
      "collateral": "4955.30",
      "portfolio_value": "5123.45",
      "leverage": "2.50",
      "available_balance": "3500.00",
      "margin_usage": "0.35",
      "buying_power": "8750.00"
    }
  },
  "type": "update/user_stats"
}
```

### Field Definitions

| Field | Description |
|-------|-------------|
| `collateral` | Total perps collateral deposited (USDC) |
| `portfolio_value` | Collateral + Unrealized PnL |
| `available_balance` | Balance available for trading/withdrawals (after margin requirements) |
| `margin_usage` | Current margin utilization percentage (0.00-1.00) |
| `buying_power` | Available buying power based on leverage |
| `leverage` | Current account leverage |
| `cross_stats` | Statistics for cross-margin positions |
| `total_stats` | Aggregated total statistics |

## Spot vs Perps Balance Explanation

On Lighter DEX, there are **two separate balance pools**:

1. **Spot Balance** (`assets.3.balance` in `account_all`)
   - The USDC held in the spot wallet
   - Used for spot trading
   - Can be transferred to perps account

2. **Perps Collateral** (`stats.collateral` in `user_stats`)
   - The USDC deposited as collateral for perpetual futures trading
   - Used for margin calculations
   - Can be withdrawn back to spot (if not locked by positions)

These are separate! A user can have:
- $7.50 in spot wallet
- $4,955.30 in perps collateral

The trading bot needs the **perps collateral** for trading decisions.

## Implementation Plan

### Step 1: Add UserStats Models

Create new model classes in `GridBot.Lighter/Models/WebSocket/`:

```csharp
// UserStatsMessages.cs
public sealed record UserStatsMessage : WebSocketMessage
{
    [JsonPropertyName("stats")]
    public UserStatsData? Stats { get; init; }
}

public sealed record UserStatsData
{
    [JsonPropertyName("collateral")]
    public string Collateral { get; init; } = "0";

    [JsonPropertyName("portfolio_value")]
    public string PortfolioValue { get; init; } = "0";

    [JsonPropertyName("leverage")]
    public string Leverage { get; init; } = "0";

    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; init; } = "0";

    [JsonPropertyName("margin_usage")]
    public string MarginUsage { get; init; } = "0";

    [JsonPropertyName("buying_power")]
    public string BuyingPower { get; init; } = "0";

    [JsonPropertyName("cross_stats")]
    public UserStatsDetails? CrossStats { get; init; }

    [JsonPropertyName("total_stats")]
    public UserStatsDetails? TotalStats { get; init; }
}

public sealed record UserStatsDetails
{
    [JsonPropertyName("collateral")]
    public string Collateral { get; init; } = "0";

    [JsonPropertyName("portfolio_value")]
    public string PortfolioValue { get; init; } = "0";

    [JsonPropertyName("leverage")]
    public string Leverage { get; init; } = "0";

    [JsonPropertyName("available_balance")]
    public string AvailableBalance { get; init; } = "0";

    [JsonPropertyName("margin_usage")]
    public string MarginUsage { get; init; } = "0";

    [JsonPropertyName("buying_power")]
    public string BuyingPower { get; init; } = "0";
}
```

### Step 2: Add UserStatsUpdateEvent

Add to `ChannelEvents.cs`:

```csharp
public sealed record UserStatsUpdateEvent : ChannelEvent
{
    public required long AccountId { get; init; }
    public required decimal Collateral { get; init; }
    public required decimal PortfolioValue { get; init; }
    public required decimal AvailableBalance { get; init; }
    public required decimal BuyingPower { get; init; }
    public required decimal Leverage { get; init; }
    public required decimal MarginUsage { get; init; }
}
```

### Step 3: Update ILighterWebSocketClient

Add new channel reader and subscription method:

```csharp
ChannelReader<UserStatsUpdateEvent> UserStatsUpdates { get; }

Task SubscribeUserStatsAsync(CancellationToken cancellationToken = default);
```

### Step 4: Update LighterWebSocketClient

1. Add the channel field
2. Add subscription method
3. Add message routing for `user_stats` channel
4. Add handler method

### Step 5: Update AccountUpdateEvent Usage

The current `AccountUpdateEvent` gets collateral/availableBalance from spot balance. Options:

**Option A (Recommended)**: Keep `AccountUpdateEvent` for spot balances, use `UserStatsUpdateEvent` for perps balances
- Pro: Clear separation of concerns
- Pro: No breaking changes
- Con: Two events to monitor

**Option B**: Merge user_stats into AccountUpdateEvent
- Pro: Single event for all account data
- Con: Requires both subscriptions to get complete data
- Con: More complex correlation logic

### Step 6: Update LighterRealtimeStateService

Subscribe to `user_stats` channel during startup (alongside `account_all`).

## Code Changes Summary

| File | Change |
|------|--------|
| `Models/WebSocket/UserStatsMessages.cs` | NEW - Add UserStatsMessage and related records |
| `Models/WebSocket/ChannelEvents.cs` | Add UserStatsUpdateEvent |
| `ILighterWebSocketClient.cs` | Add UserStatsUpdates channel and SubscribeUserStatsAsync method |
| `LighterWebSocketClient.cs` | Add channel, subscription, routing, and handler |
| `LighterRealtimeStateService.cs` | Subscribe to user_stats channel |

## Important Notes

1. **Auth Required**: The `user_stats` channel requires authentication (same as `account_all`)

2. **Update Frequency**: `user_stats` updates in real-time as positions change, funding occurs, or mark prices update

3. **Which Stats to Use**:
   - For cross-margin trading: use `cross_stats`
   - For isolated margin: use main `stats` (isolated by market)
   - For overall account view: use `total_stats`

4. **Balance Calculation**:
   - `available_balance = collateral - (margin used by positions) - (margin reserved by open orders)`
   - `buying_power = available_balance * max_leverage`

## Sources

- [WebSocket Reference](https://apidocs.lighter.xyz/docs/websocket-reference)
- [Account Types](https://apidocs.lighter.xyz/docs/account-types)
- [API Documentation](https://docs.lighter.xyz/perpetual-futures/api)
