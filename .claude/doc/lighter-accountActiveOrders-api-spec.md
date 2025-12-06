# Lighter DEX accountActiveOrders API Specification

## Problem Statement

The current code at `GridBot.Lighter\LighterQueryClient.cs:120` is calling:
```
GET accountActiveOrders?account_index={accountIndex}
```

This results in error:
```json
{"code":20001,"message":"invalid param: : field \"market_id\" is not set"}
```

## Root Cause

The `accountActiveOrders` endpoint **requires** the `market_id` parameter. It is NOT optional.

## Correct API Specification

### Endpoint
```
GET /api/v1/accountActiveOrders
```

### Base URL
- Mainnet: `https://mainnet.zklighter.elliot.ai`
- Testnet: `https://testnet.zklighter.elliot.ai`

### Required Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `account_index` | int | Yes | The account index |
| `market_id` | int | Yes | The market ID (e.g., 0 for ETH-USDC, 1 for BTC-USDC) |
| `auth` | string | Conditional | Authentication token (may be required for authenticated access) |

### Correct Request Format
```
GET accountActiveOrders?account_index=123&market_id=0
```

### Response Format
```json
{
  "code": 0,
  "message": "success",
  "orders": [
    {
      "order_id": "string",
      "market_id": 0,
      "account_index": 123,
      "side": "buy",
      "price": "1850.50",
      "size": "1.5",
      "filled_size": "0.5",
      "status": "active",
      "order_type": "limit",
      "time_in_force": "GTC",
      "created_at": 1701792000000
    }
  ]
}
```

## Key Finding: No "Get All Markets" Endpoint

**There is NO single API call to retrieve active orders across ALL markets.**

The Lighter API is designed to query orders per-market. To get all active orders for an account, you must:

1. Get the list of all markets via `GET /orderBooks`
2. Query each market individually for active orders

## Recommended Implementation Changes

### Option 1: Query Single Market (Current Use Case)
If your bot operates on a single market, update the method signature:

```csharp
public async Task<List<Order>> GetActiveOrdersAsync(
    long accountIndex,
    int marketId,  // ADD THIS PARAMETER
    CancellationToken cancellationToken = default)
{
    var response = await GetAsync<ActiveOrdersResponse>(
        $"accountActiveOrders?account_index={accountIndex}&market_id={marketId}",
        cancellationToken);
    // ...
}
```

### Option 2: Query All Markets (Aggregate)
If you need orders across all markets:

```csharp
public async Task<List<Order>> GetAllActiveOrdersAsync(
    long accountIndex,
    CancellationToken cancellationToken = default)
{
    // First get all markets
    var orderBooks = await GetOrderBooksAsync(cancellationToken);

    var allOrders = new List<Order>();

    // Query each market
    foreach (var market in orderBooks)
    {
        var marketOrders = await GetActiveOrdersAsync(
            accountIndex,
            market.MarketId,
            cancellationToken);
        allOrders.AddRange(marketOrders);
    }

    return allOrders;
}
```

### Option 3: Parallel Query for Performance
```csharp
public async Task<List<Order>> GetAllActiveOrdersAsync(
    long accountIndex,
    CancellationToken cancellationToken = default)
{
    var orderBooks = await GetOrderBooksAsync(cancellationToken);

    var tasks = orderBooks.Select(market =>
        GetActiveOrdersAsync(accountIndex, market.MarketId, cancellationToken));

    var results = await Task.WhenAll(tasks);

    return results.SelectMany(r => r).ToList();
}
```

## Market ID Reference

Common market IDs on Lighter (verify with `orderBooks` endpoint):
- The market IDs are integers starting from 0
- Use `GET /orderBooks` to get the full list of markets with their IDs
- Example markets: ETH-USDC, BTC-USDC, etc.

## Interface Update Required

Update `ILighterQueryClient` interface:

```csharp
/// <summary>
/// Gets active orders for an account in a specific market.
/// </summary>
/// <param name="accountIndex">Account index.</param>
/// <param name="marketId">Market ID.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>List of active orders for the account in the specified market.</returns>
Task<List<Order>> GetActiveOrdersAsync(
    long accountIndex,
    int marketId,
    CancellationToken cancellationToken = default);

/// <summary>
/// Gets active orders for an account across all markets.
/// </summary>
/// <param name="accountIndex">Account index.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>List of all active orders for the account.</returns>
Task<List<Order>> GetAllActiveOrdersAsync(
    long accountIndex,
    CancellationToken cancellationToken = default);
```

## Error Codes Reference

| Code | Message | Cause |
|------|---------|-------|
| 20001 | invalid param | Missing or invalid required parameter |
| 0 | success | Request successful |

## Sources

- [Lighter API Documentation](https://apidocs.lighter.xyz/reference/accountactiveorders)
- [elliottech/lighter-python SDK](https://github.com/elliottech/lighter-python)
- [Order API DeepWiki](https://deepwiki.com/elliottech/lighter-python/4.2-retrieving-account-and-market-information)
- [Lighter Docs](https://docs.lighter.xyz/)

## Implementation Priority

**CRITICAL**: This is a breaking issue. The current code cannot function without this fix.

Recommended approach:
1. If the bot only trades one market: Add `marketId` parameter to the existing method
2. If the bot trades multiple markets: Implement the aggregation method with parallel queries for performance
3. Consider caching the market list to avoid repeated `orderBooks` calls
