# Lighter DEX Order Book API - Correct Response Format

## Date
2025-12-02

## Summary
The current `OrderBookDetailResponse` model is incorrect. The Lighter API has a **different response structure** than what the code expects.

## Key Findings

### 1. CRITICAL: There are THREE different order book endpoints

| Endpoint | Purpose | Returns |
|----------|---------|---------|
| `orderBooks` | Market list with metadata | Array of markets with fees, decimals, limits |
| `orderBookDetails` | Detailed market metadata | Array of markets with trading stats, margin info |
| `orderBookOrders` | **Actual bids/asks** | Arrays of individual orders |

### 2. The Current Code is Calling the WRONG Endpoint

The current `GetOrderBookDetailsAsync` method calls `orderBookDetails` expecting to get bids/asks, but:
- `orderBookDetails` returns **market metadata** (fees, margins, stats) - NOT bids/asks
- **To get bids/asks you need `orderBookOrders`**

### 3. Correct API Response Structures

#### orderBookDetails Response (Market Metadata)
```json
{
  "code": 200,
  "order_book_details": [
    {
      "symbol": "BTC",
      "market_id": 1,
      "status": "active",
      "taker_fee": "0.0000",
      "maker_fee": "0.0000",
      "liquidation_fee": "1.0000",
      "min_base_amount": "0.00020",
      "min_quote_amount": "10.000000",
      "order_quote_limit": "",
      "supported_size_decimals": 5,
      "supported_price_decimals": 1,
      "supported_quote_decimals": 6,
      "size_decimals": 5,
      "price_decimals": 1,
      "quote_multiplier": 1,
      "default_initial_margin_fraction": 500,
      "min_initial_margin_fraction": 200,
      "maintenance_margin_fraction": 120,
      "closeout_margin_fraction": 80,
      "last_trade_price": 91229,
      "daily_trades_count": 2823367,
      "daily_base_token_volume": 69544.79007,
      "daily_quote_token_volume": 6134016644.035296,
      "daily_price_low": 86140.4,
      "daily_price_high": 92278.1,
      "daily_price_change": 5.4223006935312785,
      "open_interest": 2930.66837,
      "daily_chart": {},
      "market_config": {}
    }
  ]
}
```

**IMPORTANT NOTES:**
- Response uses `"code": 200` (HTTP status code), NOT `"code": 0`
- Data is in `order_book_details` array, NOT `data` object
- Response is an ARRAY even for single market requests
- Many numeric values are actual numbers, not strings

#### orderBookOrders Response (Bids/Asks)
```json
{
  "code": 200,
  "total_asks": 5,
  "total_bids": 5,
  "asks": [
    {
      "order_index": 12345,
      "order_id": 67890,
      "owner_account_index": 111,
      "initial_base_amount": "1.00000",
      "remaining_base_amount": "0.50000",
      "price": "91227.8",
      "order_expiry": 1701475200
    }
  ],
  "bids": [
    {
      "order_index": 12346,
      "order_id": 67891,
      "owner_account_index": 222,
      "initial_base_amount": "2.00000",
      "remaining_base_amount": "2.00000",
      "price": "91225.4",
      "order_expiry": 1701475200
    }
  ]
}
```

**IMPORTANT NOTES:**
- This is the endpoint for getting actual order book depth
- Returns individual orders, not aggregated price levels
- You may need to aggregate by price to get traditional order book levels

#### orderBooks Response (Market List)
```json
{
  "code": 200,
  "order_books": [
    {
      "symbol": "BTC",
      "market_id": 1,
      "status": "active",
      "taker_fee": "0.0000",
      "maker_fee": "0.0000",
      "liquidation_fee": "1.0000",
      "min_base_amount": "0.00020",
      "min_quote_amount": "10.000000",
      "order_quote_limit": "",
      "supported_size_decimals": 5,
      "supported_price_decimals": 1,
      "supported_quote_decimals": 6
    }
  ]
}
```

## Required Model Changes

### Option A: Fix to Match API Reality (Recommended)

**1. Rename current `OrderBookDetailResponse` to `OrderBookMetadataResponse`:**
```csharp
public sealed class OrderBookMetadataResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("order_book_details")]
    public List<OrderBookMetadata>? OrderBookDetails { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Code == 200;  // Note: 200 not 0!
}
```

**2. Create new model for `orderBookOrders` endpoint:**
```csharp
public sealed class OrderBookOrdersResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("total_asks")]
    public int TotalAsks { get; set; }

    [JsonPropertyName("total_bids")]
    public int TotalBids { get; set; }

    [JsonPropertyName("asks")]
    public List<OrderBookOrder>? Asks { get; set; }

    [JsonPropertyName("bids")]
    public List<OrderBookOrder>? Bids { get; set; }

    [JsonIgnore]
    public bool IsSuccess => Code == 200;
}

public sealed class OrderBookOrder
{
    [JsonPropertyName("order_index")]
    public long OrderIndex { get; set; }

    [JsonPropertyName("order_id")]
    public long OrderId { get; set; }

    [JsonPropertyName("owner_account_index")]
    public long OwnerAccountIndex { get; set; }

    [JsonPropertyName("initial_base_amount")]
    public string InitialBaseAmount { get; set; } = "0";

    [JsonPropertyName("remaining_base_amount")]
    public string RemainingBaseAmount { get; set; } = "0";

    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    [JsonPropertyName("order_expiry")]
    public long OrderExpiry { get; set; }
}
```

**3. Create new `OrderBookMetadata` model:**
```csharp
public sealed class OrderBookMetadata
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("market_id")]
    public int MarketId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("taker_fee")]
    public string TakerFee { get; set; } = "0";

    [JsonPropertyName("maker_fee")]
    public string MakerFee { get; set; } = "0";

    [JsonPropertyName("liquidation_fee")]
    public string LiquidationFee { get; set; } = "0";

    [JsonPropertyName("min_base_amount")]
    public string MinBaseAmount { get; set; } = "0";

    [JsonPropertyName("min_quote_amount")]
    public string MinQuoteAmount { get; set; } = "0";

    [JsonPropertyName("order_quote_limit")]
    public string OrderQuoteLimit { get; set; } = string.Empty;

    [JsonPropertyName("supported_size_decimals")]
    public int SupportedSizeDecimals { get; set; }

    [JsonPropertyName("supported_price_decimals")]
    public int SupportedPriceDecimals { get; set; }

    [JsonPropertyName("supported_quote_decimals")]
    public int SupportedQuoteDecimals { get; set; }

    [JsonPropertyName("size_decimals")]
    public int SizeDecimals { get; set; }

    [JsonPropertyName("price_decimals")]
    public int PriceDecimals { get; set; }

    [JsonPropertyName("quote_multiplier")]
    public int QuoteMultiplier { get; set; }

    [JsonPropertyName("default_initial_margin_fraction")]
    public int DefaultInitialMarginFraction { get; set; }

    [JsonPropertyName("min_initial_margin_fraction")]
    public int MinInitialMarginFraction { get; set; }

    [JsonPropertyName("maintenance_margin_fraction")]
    public int MaintenanceMarginFraction { get; set; }

    [JsonPropertyName("closeout_margin_fraction")]
    public int CloseoutMarginFraction { get; set; }

    // NOTE: These come as actual numbers, not strings
    [JsonPropertyName("last_trade_price")]
    public decimal LastTradePrice { get; set; }

    [JsonPropertyName("daily_trades_count")]
    public long DailyTradesCount { get; set; }

    [JsonPropertyName("daily_base_token_volume")]
    public decimal DailyBaseTokenVolume { get; set; }

    [JsonPropertyName("daily_quote_token_volume")]
    public decimal DailyQuoteTokenVolume { get; set; }

    [JsonPropertyName("daily_price_low")]
    public decimal DailyPriceLow { get; set; }

    [JsonPropertyName("daily_price_high")]
    public decimal DailyPriceHigh { get; set; }

    [JsonPropertyName("daily_price_change")]
    public decimal DailyPriceChange { get; set; }

    [JsonPropertyName("open_interest")]
    public decimal OpenInterest { get; set; }
}
```

## Required Client Method Changes

### LighterQueryClient.cs

**Add new method for order book orders (bids/asks):**
```csharp
/// <summary>
/// Gets order book orders (bids and asks) for a specific market.
/// </summary>
/// <param name="marketId">Market ID.</param>
/// <param name="limit">Maximum number of orders per side to return (optional).</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Order book orders with bids and asks.</returns>
public async Task<OrderBookOrdersResponse> GetOrderBookOrdersAsync(
    int marketId,
    int? limit = null,
    CancellationToken cancellationToken = default)
{
    var queryParams = $"?market_id={marketId}";
    if (limit.HasValue)
        queryParams += $"&limit={limit.Value}";

    var response = await GetAsync<OrderBookOrdersResponse>($"orderBookOrders{queryParams}", cancellationToken);

    if (!response.IsSuccess)
        throw new LighterApiException(response.Message ?? "Failed to get order book orders", response.Code);

    return response;
}
```

**Fix existing `GetOrderBookDetailsAsync`:**
```csharp
/// <summary>
/// Gets order book metadata for a specific market (fees, margins, stats).
/// Note: For bids/asks, use GetOrderBookOrdersAsync instead.
/// </summary>
public async Task<OrderBookMetadata> GetOrderBookMetadataAsync(
    int marketId,
    CancellationToken cancellationToken = default)
{
    var queryParams = $"?market_id={marketId}";
    var response = await GetAsync<OrderBookMetadataResponse>($"orderBookDetails{queryParams}", cancellationToken);

    if (!response.IsSuccess)
        throw new LighterApiException(response.Message ?? "Failed to get order book details", response.Code);

    // Note: Response is an array even for single market
    var market = response.OrderBookDetails?.FirstOrDefault(m => m.MarketId == marketId);
    return market ?? throw new LighterApiException($"Market {marketId} not found in response");
}
```

## API Base URL
- **Mainnet**: `https://mainnet.zklighter.elliot.ai/api/v1/`
- **Testnet**: `https://testnet.zklighter.elliot.ai/api/v1/` (assumed pattern)

## Success Code
- All Lighter API endpoints return `"code": 200` for success
- Current code checks `Code == 0` which will ALWAYS fail
- Change `IsSuccess` property to check `Code == 200`

## Critical Issues Identified

1. **Wrong success code check**: `Code == 0` should be `Code == 200`
2. **Wrong response property**: `data` should be `order_book_details` (array)
3. **Wrong endpoint for bids/asks**: Need `orderBookOrders` instead of `orderBookDetails`
4. **Missing endpoint**: No method exists for `orderBookOrders`

## Interface Updates Required

### ILighterQueryClient.cs
Add:
```csharp
/// <summary>
/// Gets order book orders (bids and asks) for a specific market.
/// </summary>
Task<OrderBookOrdersResponse> GetOrderBookOrdersAsync(
    int marketId,
    int? limit = null,
    CancellationToken cancellationToken = default);
```

## Summary of Required Changes

| File | Change |
|------|--------|
| `OrderBookDetail.cs` | Rename to `OrderBookMetadata.cs`, fix property names |
| `OrderBookDetailResponse.cs` | Change `data` to `order_book_details`, change success check to `Code == 200` |
| `LighterQueryClient.cs` | Add `GetOrderBookOrdersAsync`, fix success code check |
| `ILighterQueryClient.cs` | Add interface method for `GetOrderBookOrdersAsync` |
| New file: `OrderBookOrder.cs` | Create model for individual orders |
| New file: `OrderBookOrdersResponse.cs` | Create response wrapper |

## Testing Recommendations

1. Call `https://mainnet.zklighter.elliot.ai/api/v1/orderBookDetails?market_id=1` directly to verify response
2. Call `https://mainnet.zklighter.elliot.ai/api/v1/orderBookOrders?market_id=1&limit=10` to verify bids/asks
3. Test with market_id=1 (BTC) as a known active market
