namespace GridBot.Abstractions.Models.Orders;

/// <summary>
/// Represents a request to modify an existing order.
/// </summary>
public sealed record ModifyOrderRequest
{
    /// <summary>
    /// Gets the market identifier for the order.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the exchange-assigned order identifier to modify.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// Gets the new order size. Null means keep current size.
    /// </summary>
    public decimal? NewSize { get; init; }

    /// <summary>
    /// Gets the new order price. Null means keep current price.
    /// </summary>
    public decimal? NewPrice { get; init; }

    /// <summary>
    /// Gets the new trigger price for stop/take-profit orders. Null means keep current.
    /// </summary>
    public decimal? NewTriggerPrice { get; init; }
}
