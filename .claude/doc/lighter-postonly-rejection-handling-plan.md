# Lighter DEX PostOnly Order Rejection Handling Plan

## Document Information
- **Date**: 2025-12-14
- **Session**: 6
- **Agent**: lighter-api-specialist
- **Status**: RESEARCH COMPLETE - Ready for Implementation

---

## 1. Problem Statement

The trading bot experiences PostOnly order rejections with the error:
> "Order canceled due to crossing with the top order in the book, violating the post-only rule."

**Affected Components:**
1. `GridOrderManager.cs:150` - Grid orders use `TimeInForce.PostOnly`
2. `RebalancingService.cs:179` - Rebalancing orders use `TimeInForce.PostOnly` with 0.03% spread

**Root Cause:**
PostOnly orders are automatically cancelled when the limit price would result in immediate execution (crossing the spread). This is expected behavior on Lighter DEX, NOT an error in the API implementation.

---

## 2. Lighter DEX PostOnly Behavior (Official Documentation)

From [Lighter Docs - Orders and Matching](https://docs.lighter.xyz/perpetual-futures/orders-and-matching):

> "The Post-Only option ensures that a limit order is placed only as a maker order. If there are crossing orders on the opposite side of the order book at the time of placement, the exchange automatically cancels the post-only order to prevent it from becoming a taker order."

**Key Points:**
1. PostOnly guarantees maker behavior (and maker fees)
2. If the order would cross the BBO (best bid/offer), it is CANCELLED entirely
3. This is not an error - it is intended behavior to protect maker status
4. No partial fills occur - the entire order is rejected if any part would cross

---

## 3. Crossing Conditions Explained

### For BUY (Bid) Orders
| Condition | Result |
|-----------|--------|
| `price < best_ask` | Order rests in book (MAKER) |
| `price >= best_ask` | Order CANCELLED (would cross) |

### For SELL (Ask) Orders
| Condition | Result |
|-----------|--------|
| `price > best_bid` | Order rests in book (MAKER) |
| `price <= best_bid` | Order CANCELLED (would cross) |

### Example Scenario
```
Current Order Book:
  Best Ask: $3,340.00
  Best Bid: $3,338.00
  Spread: $2.00 (0.06%)

Grid Order Placement:
  Buy at $3,339.50 -> CANCELLED (>= best_ask $3,340.00? NO, but >= best_ask? YES if best_ask dropped to $3,339.00)

Rebalancing (0.03% spread):
  Mid price: $3,339.00
  Buy at $3,339.00 * 1.0003 = $3,340.00 -> CANCELLED (>= best_ask)
```

---

## 4. Error Codes (Important Finding)

**No Specific Error Code for PostOnly Rejection**

Based on research of the [Lighter API Error Codes](https://apidocs.lighter.xyz/docs/data-structures-constants-and-errors), there is NO dedicated error code for PostOnly crossing rejection. The order is simply cancelled without an error response - it's treated as a successful "submission that resulted in cancellation."

**Relevant Order Error Codes (21700-21740 range):**
| Code | Name | Description |
|------|------|-------------|
| 21700 | `AppErrInvalidOrderIndex` | Invalid order index |
| 21701 | `AppErrInvalidBaseAmount` | Invalid base amount |
| 21702 | `AppErrInvalidPrice` | Invalid price |
| 21705 | `AppErrInvalidOrderTimeInForce` | Invalid TimeInForce value |
| 21734 | `AppErrPriceTooFarFromMarkPrice` | Price protection triggered |
| 21739 | `AppErrNotEnoughOrderMargin` | Insufficient margin |

**Note:** The placeholder codes in `GridOrderManager.cs:43` (`4001, 4002, 4003`) need to be verified or removed as they do not appear in official documentation.

---

## 5. Available Data for Order Book Awareness

The codebase already has the infrastructure to check best bid/ask BEFORE placing orders:

### Interface: `ILighterRealtimeState`
```csharp
// Get order book with best bid/ask (real-time from WebSocket)
OrderBookSnapshot? GetOrderBook(int marketId);
```

### Model: `OrderBookSnapshot`
```csharp
public sealed record OrderBookSnapshot
{
    public required decimal BestBidPrice { get; init; }
    public required decimal BestAskPrice { get; init; }
    public required decimal BestBidSize { get; init; }
    public required decimal BestAskSize { get; init; }
    // Plus full order book Bids and Asks lists
}
```

### Data Freshness Check
```csharp
bool IsOrderBookReady(int marketId);
TimeSpan? OldestDataAge { get; }
bool IsConnected { get; }
```

---

## 6. Recommended Solutions

### Solution A: Pre-Flight Price Validation (RECOMMENDED - Primary Fix)

Before placing any PostOnly order, validate that the price will not cross the spread.

**Implementation Location:** `GridOrderManager.PlaceGridOrdersAsync()` and `RebalancingService.ExecuteRebalanceAsync()`

**Logic:**
```csharp
// Before creating order request:
var orderBook = _realtimeState.GetOrderBook(marketId);
if (orderBook != null)
{
    if (isBid && price >= orderBook.BestAskPrice)
    {
        // Would cross - adjust price or skip
        price = orderBook.BestAskPrice - tickSize; // Place just below best ask
    }
    else if (isAsk && price <= orderBook.BestBidPrice)
    {
        // Would cross - adjust price or skip
        price = orderBook.BestBidPrice + tickSize; // Place just above best bid
    }
}
```

**Advantages:**
- Prevents rejection before it happens
- No wasted API calls
- Maintains maker fee benefit

**Considerations:**
- Requires fresh order book data
- Price may move between check and submission (race condition)
- Need to decide: adjust price vs skip order vs use different TimeInForce

---

### Solution B: Aggressive Maker Spread (Increase Safety Buffer)

Increase the spread buffer from 0.03% to a larger value (e.g., 0.10-0.15%) to reduce crossing probability.

**Current (RebalancingService):**
```csharp
const decimal aggressiveMakerSpread = 0.0003m; // 0.03%
```

**Recommended:**
```csharp
const decimal aggressiveMakerSpread = 0.0010m; // 0.10%
// OR make configurable
var spread = _config.RebalancingSpreadPercent / 100m;
```

**Advantages:**
- Simple change
- Reduces rejection frequency

**Disadvantages:**
- May reduce fill rate
- Doesn't guarantee no crossing (market can move)

---

### Solution C: Fallback TimeInForce Strategy (Hybrid Approach)

On PostOnly rejection, retry with a different TimeInForce:

**Strategy Options:**
1. **PostOnly -> GoodTillTime**: Accept taker fee if maker fails
2. **PostOnly -> IOC**: Execute immediately or cancel remainder
3. **Skip**: Simply don't place the order (safest for rebalancing)

**Implementation:**
```csharp
// First attempt with PostOnly
var request = new CreateOrderRequest
{
    TimeInForce = TimeInForce.PostOnly,
    // ...
};

var result = await _commandClient.CreateOrderAsync(request);

// Check if order was cancelled due to crossing (need to detect this)
if (OrderWasCancelledDueToCrossing(result))
{
    // Fallback option 1: Retry with GoodTillTime (accept taker fee)
    request = request with { TimeInForce = TimeInForce.GoodTillTime };
    result = await _commandClient.CreateOrderAsync(request);

    // OR Fallback option 2: Skip this order
    _logger.LogWarning("PostOnly rejected, skipping order at price {Price}", price);
    continue;
}
```

**Challenge:** No specific error code for PostOnly rejection - need to detect from order status or response message.

---

### Solution D: Dynamic TimeInForce Selection

Choose TimeInForce based on order purpose:

| Order Type | Recommended TimeInForce | Rationale |
|------------|------------------------|-----------|
| Grid Orders (regular) | PostOnly | Must be maker for profitability |
| Grid Orders (urgent refill) | GoodTillTime | Refill position quickly |
| Rebalancing (trend shift) | IOC | Execute immediately or skip |
| Rebalancing (routine) | PostOnly | Maker fee preferred |
| Trailing Stop Exit | IOC | Must execute for protection |
| Moon Bag Sell | GoodTillTime | Needs to fill but not urgent |

---

## 7. Recommended Implementation Plan

### Phase 1: Pre-Flight Validation (CRITICAL)

**Files to Modify:**
1. `GridBot.ApiService/Services/Grid/GridOrderManager.cs`
2. `GridBot.ApiService/Services/Rebalancing/RebalancingService.cs`

**Changes:**

1. Add `ILighterRealtimeState` dependency to both services
2. Before placing PostOnly orders, check against current best bid/ask
3. Implement one of these strategies:
   - **ADJUST**: Move price to safe side of spread
   - **SKIP**: Don't place order that would cross
   - **WARN**: Log and proceed (current behavior with detection)

**Example Implementation for GridOrderManager:**
```csharp
// In PlaceGridOrdersAsync, before creating request:

var orderBook = _realtimeState.GetOrderBook(marketId);
if (orderBook == null)
{
    _logger.LogWarning("Order book not available, proceeding without pre-flight check");
}
else
{
    // Validate price won't cross
    var tickSize = await _scalingService.GetTickSizeAsync(marketId, ct);

    if (level.IsBid && level.Price >= orderBook.BestAskPrice)
    {
        // Option A: Adjust price
        var adjustedPrice = orderBook.BestAskPrice - tickSize;
        _logger.LogDebug(
            "Adjusted buy price from {Original} to {Adjusted} to avoid PostOnly crossing (best ask: {BestAsk})",
            level.Price, adjustedPrice, orderBook.BestAskPrice);
        level.Price = adjustedPrice;

        // Option B: Skip (alternative)
        // errors.Add(...); continue;
    }
    else if (!level.IsBid && level.Price <= orderBook.BestBidPrice)
    {
        // Similar logic for sells
    }
}
```

### Phase 2: Configuration (HIGH)

**Add to `TradingBotOptions.cs`:**
```csharp
public class OrderPlacementOptions
{
    /// <summary>
    /// Minimum distance from best bid/ask as percentage (default 0.01% = 1 bps)
    /// </summary>
    public decimal MinSpreadBufferPercent { get; set; } = 0.01m;

    /// <summary>
    /// Strategy when PostOnly would cross: Adjust, Skip, or UseTaker
    /// </summary>
    public PostOnlyCrossingStrategy CrossingStrategy { get; set; } = PostOnlyCrossingStrategy.Adjust;

    /// <summary>
    /// Whether rebalancing orders should use PostOnly (true) or IOC (false)
    /// </summary>
    public bool RebalancingUsePostOnly { get; set; } = true;
}

public enum PostOnlyCrossingStrategy
{
    /// <summary>Adjust price to avoid crossing</summary>
    Adjust,
    /// <summary>Skip the order entirely</summary>
    Skip,
    /// <summary>Retry with GoodTillTime (accept taker fee)</summary>
    UseTaker
}
```

### Phase 3: Rebalancing Service Fix (HIGH)

**Change RebalancingService to use IOC instead of PostOnly:**

Rebalancing orders are for position management, not market making. Using IOC makes more sense:
- If price is favorable, execute immediately
- If price moved, cancel and retry next cycle
- Avoids the PostOnly rejection problem entirely

```csharp
// RebalancingService.cs
var orderRequest = new CreateOrderRequest
{
    // ...
    TimeInForce = TimeInForce.ImmediateOrCancel, // Changed from PostOnly
    OrderExpiry = OrderConstants.DefaultIocExpiry, // 0 for IOC
};
```

**Trade-off:** Pays taker fee (0.04%) instead of maker fee (0.02%). For infrequent rebalancing, this is acceptable.

### Phase 4: Clean Up Placeholder Codes (LOW)

**Remove or verify the placeholder error codes:**
```csharp
// GridOrderManager.cs:43
// Current (unverified):
private static readonly HashSet<int> PostOnlyRejectionCodes = [4001, 4002, 4003];

// Recommended: Remove until verified codes are found
// private static readonly HashSet<int> PostOnlyRejectionCodes = [];
```

---

## 8. Price Protection Parameter

The current implementation uses `priceProtection: false`:
```csharp
var response = await _commandClient.CreateOrderAsync(orderRequest, priceProtection: false, ct);
```

**What is Price Protection?**
Lighter DEX has error code `21734: AppErrPriceTooFarFromMarkPrice` which triggers when an order price is too far from the mark price.

**Recommendation:** Keep `priceProtection: false` for grid orders since:
1. Grid levels may intentionally be far from current price
2. The bot has its own risk checks (trailing stop, flash crash detector)
3. Enabling it could reject valid grid orders during high volatility

---

## 9. Detecting PostOnly Rejection

Since there's no specific error code, detection must be done by:

1. **Order Status Tracking**: After submission, check if order appears in active orders
2. **WebSocket Notifications**: Subscribe to `notifications` or `orders` channel for order status updates
3. **Transaction Hash Verification**: Check transaction result for order creation vs cancellation

**Current Order Notification Flow:**
```
WebSocket -> notifications channel -> order_cancelled event
```

This is already implemented in `LighterWebSocketClient.cs`. The bot can detect PostOnly rejections by monitoring order status changes.

---

## 10. Summary of Recommendations

| Priority | Action | Impact | Effort |
|----------|--------|--------|--------|
| 1 | Add pre-flight price validation in GridOrderManager | HIGH | MEDIUM |
| 2 | Change RebalancingService to use IOC instead of PostOnly | HIGH | LOW |
| 3 | Add configurable spread buffer | MEDIUM | LOW |
| 4 | Add crossing strategy configuration | MEDIUM | MEDIUM |
| 5 | Remove unverified error code placeholders | LOW | LOW |

---

## 11. References

- [Lighter Docs - Orders and Matching](https://docs.lighter.xyz/perpetual-futures/orders-and-matching)
- [Lighter API Documentation](https://apidocs.lighter.xyz)
- [Lighter API Error Codes](https://apidocs.lighter.xyz/docs/data-structures-constants-and-errors)
- [Lighter WebSocket Reference](https://apidocs.lighter.xyz/docs/websocket-reference)
- [Lighter Python SDK](https://github.com/elliottech/lighter-python)

---

## 12. Implementation Checklist

- [ ] Add `ILighterRealtimeState` dependency to `GridOrderManager`
- [ ] Implement pre-flight price validation in `PlaceGridOrdersAsync`
- [ ] Add `ILighterRealtimeState` dependency to `RebalancingService`
- [ ] Change RebalancingService TimeInForce from PostOnly to IOC
- [ ] Add `OrderPlacementOptions` configuration class
- [ ] Add `PostOnlyCrossingStrategy` enum
- [ ] Update `appsettings.json` with new options
- [ ] Remove placeholder error codes `[4001, 4002, 4003]`
- [ ] Add unit tests for crossing detection logic
- [ ] Add integration test for PostOnly behavior
