using GridBot.Abstractions.Models.Enums;

namespace GridBot.Abstractions.Models.Orders;

/// <summary>
/// Represents a request to create a new order.
/// </summary>
public sealed record CreateOrderRequest
{
    /// <summary>
    /// Gets the market identifier for the order.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the client-assigned order identifier for tracking.
    /// </summary>
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// Gets the order size in base asset units.
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Gets the order price.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Gets the order side (buy or sell).
    /// </summary>
    public required OrderSide Side { get; init; }

    /// <summary>
    /// Gets the order type. Defaults to Limit.
    /// </summary>
    public OrderType Type { get; init; } = OrderType.Limit;

    /// <summary>
    /// Gets the time in force setting. Defaults to GoodTillCancel.
    /// </summary>
    public TimeInForce TimeInForce { get; init; } = TimeInForce.GoodTillCancel;

    /// <summary>
    /// Gets whether this is a reduce-only order. Defaults to false.
    /// </summary>
    public bool ReduceOnly { get; init; } = false;

    /// <summary>
    /// Gets the trigger price for stop/take-profit orders.
    /// </summary>
    public decimal? TriggerPrice { get; init; }

    /// <summary>
    /// Gets the expiration time for the order.
    /// </summary>
    public DateTimeOffset? Expiry { get; init; }
}
