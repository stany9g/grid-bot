using GridBot.Abstractions.Models.Enums;

namespace GridBot.Abstractions.Models.Orders;

/// <summary>
/// Represents information about an order on the exchange.
/// </summary>
public sealed record OrderInfo
{
    /// <summary>
    /// Gets the exchange-assigned order identifier.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// Gets the client-assigned order identifier.
    /// </summary>
    public required string ClientOrderId { get; init; }

    /// <summary>
    /// Gets the market identifier for this order.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Gets the order side (buy or sell).
    /// </summary>
    public required OrderSide Side { get; init; }

    /// <summary>
    /// Gets the order type.
    /// </summary>
    public required OrderType Type { get; init; }

    /// <summary>
    /// Gets the order price (for limit orders).
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Gets the original order size.
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Gets the remaining unfilled size.
    /// </summary>
    public required decimal RemainingSize { get; init; }

    /// <summary>
    /// Gets the filled size.
    /// </summary>
    public decimal FilledSize => Size - RemainingSize;

    /// <summary>
    /// Gets the time in force setting.
    /// </summary>
    public TimeInForce TimeInForce { get; init; } = TimeInForce.GoodTillCancel;

    /// <summary>
    /// Gets whether this is a reduce-only order.
    /// </summary>
    public bool ReduceOnly { get; init; }

    /// <summary>
    /// Gets the current order status.
    /// </summary>
    public OrderStatus Status { get; init; } = OrderStatus.Open;

    /// <summary>
    /// Gets the timestamp when the order was created.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Gets the timestamp when the order was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the timestamp when the order expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets the trigger price for stop/take-profit orders.
    /// </summary>
    public decimal? TriggerPrice { get; init; }

    /// <summary>
    /// Gets whether the order is fully filled.
    /// </summary>
    public bool IsFilled => RemainingSize == 0;

    /// <summary>
    /// Gets whether the order is partially filled.
    /// </summary>
    public bool IsPartiallyFilled => FilledSize > 0 && RemainingSize > 0;

    /// <summary>
    /// Gets the fill percentage.
    /// </summary>
    public decimal FillPercent => Size > 0 ? FilledSize / Size * 100m : 0m;
}
