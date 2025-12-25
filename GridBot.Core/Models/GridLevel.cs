namespace GridBot.Core.Models;

/// <summary>
/// Represents a single level in the grid.
/// </summary>
public sealed record GridLevel
{
    /// <summary>
    /// Price at this grid level (in USDC).
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Order size at this level (in base asset units).
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// True if this is a buy (bid) order, false for sell (ask).
    /// </summary>
    public required bool IsBuy { get; init; }

    /// <summary>
    /// Order ID from the exchange (null if not yet placed).
    /// </summary>
    public long? OrderId { get; init; }

    /// <summary>
    /// Client order index (used for order correlation).
    /// </summary>
    public long ClientOrderIndex { get; init; }

    /// <summary>
    /// Whether this level has an active order on the exchange.
    /// </summary>
    public bool HasActiveOrder => OrderId.HasValue;

    /// <summary>
    /// Creates a new GridLevel with an assigned order ID.
    /// </summary>
    public GridLevel WithOrderId(long orderId) => this with { OrderId = orderId };

    /// <summary>
    /// Creates a new GridLevel without an order ID (order cancelled or filled).
    /// </summary>
    public GridLevel WithoutOrder() => this with { OrderId = null };
}
