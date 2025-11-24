using GridBot.Lighter;
using GridBot.Lighter.Models;

namespace GridBot.ApiService.Examples;

/// <summary>
/// Example service showing how to use LighterClient with dependency injection and configuration.
/// </summary>
public class LighterConfigurationExample
{
    private readonly LighterClient _lighter;

    public LighterConfigurationExample(LighterClient lighter)
    {
        _lighter = lighter;
    }

    /// <summary>
    /// Example: Place a market order using injected client.
    /// </summary>
    public async Task<string> PlaceMarketOrderAsync(int marketId, decimal amount, bool isBuy)
    {
        var orderRequest = new CreateOrderRequest
        {
            MarketIndex = marketId,
            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BaseAmount = (long)(amount * 100_000_000), // Convert to scaled amount
            Price = 0, // Market orders have no price
            IsAsk = !isBuy,
            OrderType = OrderType.Market,
            TimeInForce = TimeInForce.ImmediateOrCancel,
            OrderExpiry = OrderConstants.DefaultIocExpiry
        };

        var response = await _lighter.CreateOrderAsync(orderRequest);
        return response.TxHash;
    }

    /// <summary>
    /// Example: Place a limit order using injected client.
    /// </summary>
    public async Task<string> PlaceLimitOrderAsync(
        int marketId,
        decimal amount,
        decimal price,
        bool isBuy)
    {
        var orderRequest = new CreateOrderRequest
        {
            MarketIndex = marketId,
            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            BaseAmount = (long)(amount * 100_000_000),
            Price = (long)price,
            IsAsk = !isBuy,
            OrderType = OrderType.Limit,
            TimeInForce = TimeInForce.GoodTillTime,
            OrderExpiry = OrderConstants.Default28DayOrderExpiry
        };

        var response = await _lighter.CreateOrderAsync(orderRequest);
        return response.TxHash;
    }

    /// <summary>
    /// Example: Get account balance.
    /// </summary>
    public async Task<(string collateral, string available)> GetAccountBalanceAsync(long accountIndex)
    {
        var account = await _lighter.GetAccountAsync(accountIndex);
        return (account.Collateral, account.AvailableBalance);
    }

    /// <summary>
    /// Example: Get all active orders.
    /// </summary>
    public async Task<int> GetActiveOrderCountAsync(long accountIndex)
    {
        var orders = await _lighter.GetActiveOrdersAsync(accountIndex);
        return orders.Count;
    }

    /// <summary>
    /// Example: Cancel all orders in a market.
    /// </summary>
    public async Task<string> CancelAllOrdersAsync(int marketId)
    {
        var response = await _lighter.CancelAllOrdersAsync(marketId);
        return response.TxHash;
    }
}

/// <summary>
/// Example API endpoint showing how to use the LighterConfigurationExample service.
/// Add this to your Program.cs:
///
/// <code>
/// // Register service
/// builder.Services.AddScoped&lt;LighterConfigurationExample&gt;();
///
/// // Add endpoint
/// app.MapPost("/api/lighter/order", async (
///     LighterConfigurationExample service,
///     CreateOrderDto dto) =>
/// {
///     var txHash = await service.PlaceLimitOrderAsync(
///         dto.MarketId,
///         dto.Amount,
///         dto.Price,
///         dto.IsBuy);
///     return Results.Ok(new { txHash });
/// });
/// </code>
/// </summary>
public record CreateOrderDto(int MarketId, decimal Amount, decimal Price, bool IsBuy);
