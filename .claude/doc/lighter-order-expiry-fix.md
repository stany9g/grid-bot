# Lighter DEX Order Expiry Fix

## Problem Statement

When implementing a market order function in C# for Lighter DEX, the error "OrderExpiry is invalid" is returned when signing orders with:
- OrderExpiry = -1 (Default28DayOrderExpiry)
- TimeInForce = ImmediateOrCancel (0)
- OrderType = Market (1)

## Root Cause

**CRITICAL FINDING**: The Python SDK uses DIFFERENT expiry values for different order types:

| Order Type | TimeInForce | OrderExpiry Value | Constant Name |
|------------|-------------|-------------------|---------------|
| Limit (GTT) | GoodTillTime (1) | -1 | DEFAULT_28_DAY_ORDER_EXPIRY |
| Market | ImmediateOrCancel (0) | **0** | DEFAULT_IOC_EXPIRY |
| IOC orders | ImmediateOrCancel (0) | **0** | DEFAULT_IOC_EXPIRY |

The issue is that **IOC (Immediate-Or-Cancel) and Market orders require `order_expiry = 0`, NOT -1**.

## Evidence from Python SDK

From the official Lighter Python SDK (`signer_client.py`):

```python
# For regular limit orders:
async def create_order(
    self,
    ...
    order_expiry=DEFAULT_28_DAY_ORDER_EXPIRY,  # equals -1
    ...
)

# For market orders - EXPLICITLY overrides to 0:
async def create_market_order(
    self,
    ...
):
    return await self.create_order(
        ...
        order_type=self.ORDER_TYPE_MARKET,
        time_in_force=self.ORDER_TIME_IN_FORCE_IMMEDIATE_OR_CANCEL,
        order_expiry=self.DEFAULT_IOC_EXPIRY,  # equals 0
        ...
    )
```

## The Fix

### 1. Add new constant in `Enums.cs`

```csharp
public static class OrderConstants
{
    // Existing constants...
    public const long Default28DayOrderExpiry = -1;

    // ADD THIS:
    /// <summary>
    /// Expiry for IOC (Immediate-Or-Cancel) and Market orders.
    /// These orders execute immediately and do not persist in the order book,
    /// so they require expiry = 0.
    /// </summary>
    public const long DefaultIocExpiry = 0;
}
```

NOTE: Looking at the current `Enums.cs`, the constant `DefaultIocExpiry = 0` already exists at line 137!

### 2. Update `CreateMarketOrderAsync` in `LighterCommandClient.cs`

Change line 115 from:
```csharp
OrderExpiry = OrderConstants.Default28DayOrderExpiry
```
to:
```csharp
OrderExpiry = OrderConstants.DefaultIocExpiry
```

### 3. Update validation in `CreateOrderRequest.Validate()`

The current validation at line 144-145 is:
```csharp
if (OrderExpiry != OrderConstants.Default28DayOrderExpiry &&
    OrderExpiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    return "OrderExpiry must be in the future";
```

This should also allow `DefaultIocExpiry (0)`:
```csharp
if (OrderExpiry != OrderConstants.Default28DayOrderExpiry &&
    OrderExpiry != OrderConstants.DefaultIocExpiry &&
    OrderExpiry < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    return "OrderExpiry must be in the future";
```

## Order Expiry Behavior Summary

| TimeInForce | OrderExpiry | Behavior |
|-------------|-------------|----------|
| GoodTillTime (1) | -1 | Signer calculates 28 days from now |
| GoodTillTime (1) | Unix timestamp | Order expires at that timestamp |
| ImmediateOrCancel (0) | 0 | Order executes immediately or cancels |
| PostOnly (2) | -1 | Signer calculates 28 days from now |

**Key Rule**: When `TimeInForce = ImmediateOrCancel (0)`, you MUST use `OrderExpiry = 0`.

## Why the Signer Rejects -1 for IOC Orders

The native signer (Go-based) validates that IOC orders use expiry=0 because:
1. IOC orders never enter the order book - they execute immediately or are cancelled
2. An expiry timestamp makes no semantic sense for IOC orders
3. Using -1 (which means "calculate 28 days from now") for an order that must execute NOW is contradictory

## Implementation Checklist

- [ ] Verify `OrderConstants.DefaultIocExpiry = 0` exists in `Enums.cs`
- [ ] Update `LighterCommandClient.CreateMarketOrderAsync` to use `DefaultIocExpiry`
- [ ] Update `CreateOrderRequest.Validate()` to allow expiry=0 for IOC orders
- [ ] Update any other code creating IOC orders to use `DefaultIocExpiry`
- [ ] Test market order creation

## Files to Modify

1. `GridBot.Lighter/LighterCommandClient.cs` - Line 115
2. `GridBot.Lighter/Models/OrderRequest.cs` - Lines 144-145

## References

- [Lighter Docs - Orders and Matching](https://docs.lighter.xyz/perpetual-futures/orders-and-matching)
- [Lighter API Documentation](https://apidocs.lighter.xyz)
- [Lighter Python SDK](https://github.com/elliottech/lighter-python)
- [Lighter Go SDK (signer)](https://github.com/elliottech/lighter-go)
