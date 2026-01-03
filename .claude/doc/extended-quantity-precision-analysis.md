# Extended DEX Quantity Precision Analysis

## Date: 2026-01-02

## Problem Statement
Extended DEX is returning error: `{"status":"ERROR","error":{"code":1123,"message":"Invalid quantity precision"}}`

Sent quantity: `"0.000125"` for BTC-USD.

## Root Cause

The quantity `0.000125` violates the market's **step size** (`min_order_size_change`). The Extended DEX requires quantities to be multiples of the step size.

## Key Concepts from Python SDK

### 1. Trading Config Parameters

From `x10/perpetual/markets.py` (lines 35-46):

```python
class TradingConfigModel(X10BaseModel):
    min_order_size: Decimal          # Minimum order size (e.g., 0.001)
    min_order_size_change: Decimal   # Step size / quantity precision (e.g., 0.001)
    min_price_change: Decimal        # Price tick size (e.g., 0.1)
    max_market_order_value: Decimal
    max_limit_order_value: Decimal
    max_position_value: Decimal
    max_leverage: Decimal
    max_num_orders: int
    limit_price_cap: Decimal
    limit_price_floor: Decimal
    risk_factor_config: List[RiskFactorConfig]
```

### 2. Quantity Precision Calculation

From `x10/perpetual/markets.py` (lines 53-54):

```python
@cached_property
def quantity_precision(self) -> int:
    return abs(int(self.min_order_size_change.log10().to_integral_exact(ROUND_CEILING)))
```

**Example:**
- `min_order_size_change = 0.001` => `log10(0.001) = -3` => precision = `3` decimal places
- `min_order_size_change = 0.0001` => `log10(0.0001) = -4` => precision = `4` decimal places

### 3. Order Size Rounding

From `x10/perpetual/markets.py` (lines 64-68):

```python
def round_order_size(self, order_size: Decimal, rounding_direction: str = ROUND_CEILING) -> Decimal:
    order_size = (order_size / self.min_order_size_change).to_integral_exact(
        rounding_direction
    ) * self.min_order_size_change
    return order_size
```

**This is the critical function!** It ensures quantities are exact multiples of `min_order_size_change`.

### 4. Calculate Order Size from Value

From `x10/perpetual/markets.py` (lines 70-77):

```python
def calculate_order_size_from_value(
    self, order_value: Decimal, order_price: Decimal, rounding_direction: str = ROUND_CEILING
) -> Decimal:
    order_size = order_value / order_price
    if order_size > 0:
        return self.round_order_size(order_size, rounding_direction=rounding_direction)
    else:
        return Decimal(0)
```

## Analysis of the Error

**Sent:** `0.000125`

For BTC-USD, the typical `min_order_size_change` is `0.001` (3 decimal places).

| Value | Is Multiple of 0.001? | Valid? |
|-------|----------------------|--------|
| 0.001 | Yes (1 * 0.001) | Valid |
| 0.002 | Yes (2 * 0.001) | Valid |
| 0.0015 | No (1.5 * 0.001) | INVALID |
| 0.000125 | No (0.125 * 0.001) | INVALID |

`0.000125 / 0.001 = 0.125` - NOT an integer, so it's rejected.

## Typical BTC-USD Market Parameters

From actual Extended DEX API (subject to change):

```json
{
  "name": "BTC-USD",
  "assetName": "BTC",
  "assetPrecision": 8,
  "collateralAssetName": "USD",
  "collateralAssetPrecision": 6,
  "tradingConfig": {
    "minOrderSize": "0.001",
    "minOrderSizeChange": "0.001",
    "minPriceChange": "0.1",
    "maxLeverage": "50"
  }
}
```

**Key values for BTC-USD:**
- `minOrderSize`: 0.001 BTC (minimum order size)
- `minOrderSizeChange`: 0.001 BTC (step size)
- `minPriceChange`: 0.1 USD (price tick size)
- Quantity precision: 3 decimal places

## C# Implementation Fix

### 1. Add Quantity Rounding Method

```csharp
public static class QuantityRounding
{
    /// <summary>
    /// Rounds quantity to valid step size (min_order_size_change).
    /// Matches Python SDK's TradingConfigModel.round_order_size()
    /// </summary>
    public static decimal RoundToStepSize(
        decimal quantity,
        decimal stepSize,
        MidpointRounding rounding = MidpointRounding.ToPositiveInfinity)
    {
        if (stepSize <= 0)
            throw new ArgumentException("Step size must be positive", nameof(stepSize));

        // Round to nearest multiple of stepSize
        // quantity / stepSize => round to integer => multiply back
        var steps = quantity / stepSize;
        var roundedSteps = Math.Round(steps, 0, rounding);
        return roundedSteps * stepSize;
    }

    /// <summary>
    /// Calculates the number of decimal places from step size.
    /// Matches Python SDK's TradingConfigModel.quantity_precision
    /// </summary>
    public static int GetPrecisionFromStepSize(decimal stepSize)
    {
        if (stepSize >= 1)
            return 0;

        // Count decimal places: log10(0.001) = -3 => 3 places
        return (int)Math.Ceiling(-Math.Log10((double)stepSize));
    }

    /// <summary>
    /// Validates that quantity is a valid multiple of step size.
    /// </summary>
    public static bool IsValidQuantity(decimal quantity, decimal stepSize)
    {
        var remainder = quantity % stepSize;
        // Allow for floating-point tolerance
        return remainder < 0.0000001m || Math.Abs(remainder - stepSize) < 0.0000001m;
    }
}
```

### 2. Usage Before Order Creation

```csharp
// Get market info first
var markets = await _httpClient.GetMarketsAsync(ct);
var btcMarket = markets.FirstOrDefault(m => m.Name == "BTC-USD");
var stepSize = btcMarket?.TradingConfig?.MinOrderSizeChange ?? 0.001m;
var minOrderSize = btcMarket?.TradingConfig?.MinOrderSize ?? 0.001m;

// Round quantity to valid step size
var rawQuantity = 0.000125m;  // From user input or calculation
var validQuantity = QuantityRounding.RoundToStepSize(rawQuantity, stepSize);

// Ensure meets minimum
if (validQuantity < minOrderSize)
{
    validQuantity = minOrderSize;
}

// Now use validQuantity for order creation
```

### 3. Validation Check

```csharp
// Before sending order, validate:
if (!QuantityRounding.IsValidQuantity(quantity, stepSize))
{
    throw new InvalidOperationException(
        $"Quantity {quantity} is not a valid multiple of step size {stepSize}");
}

if (quantity < minOrderSize)
{
    throw new InvalidOperationException(
        $"Quantity {quantity} is below minimum order size {minOrderSize}");
}
```

## Summary Table

| Parameter | Source | BTC-USD Typical Value |
|-----------|--------|----------------------|
| `min_order_size` | `trading_config.min_order_size` | 0.001 BTC |
| `min_order_size_change` | `trading_config.min_order_size_change` | 0.001 BTC |
| Quantity Precision | `log10(step_size)` | 3 decimal places |
| Valid Quantities | Multiples of step size | 0.001, 0.002, 0.003, ... |
| Invalid Quantities | Non-multiples | 0.0015, 0.000125, 0.00123 |

## Python SDK Files Referenced

| File | Path | Purpose |
|------|------|---------|
| markets.py | `x10/perpetual/markets.py` | TradingConfigModel, round_order_size() |
| amounts.py | `x10/perpetual/amounts.py` | Rounding contexts |
| order_object.py | `x10/perpetual/order_object.py` | Order creation using market data |
| order_object_settlement.py | `x10/perpetual/order_object_settlement.py` | Stark amount conversion |

## Key Insight

The Python SDK does NOT automatically round quantities in `create_order_object()`. The caller is expected to:

1. Use `trading_config.round_order_size()` before passing to order creation
2. Or use `trading_config.calculate_order_size_from_value()` which includes rounding
3. Ensure quantity >= `min_order_size`

The C# implementation should:
1. Always fetch market data first
2. Round quantities using `min_order_size_change` as step size
3. Validate quantity >= `min_order_size`
4. Send the rounded, validated quantity to the API
