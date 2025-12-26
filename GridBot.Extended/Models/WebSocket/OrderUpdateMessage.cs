namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Order update event for the realtime data provider.
/// </summary>
public sealed class OrderUpdateEvent
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Exchange order ID.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// Client order ID.
    /// </summary>
    public string? ClientOrderId { get; init; }

    /// <summary>
    /// Order side: true for buy, false for sell.
    /// </summary>
    public required bool IsBuy { get; init; }

    /// <summary>
    /// Order price.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Original order size.
    /// </summary>
    public required decimal Size { get; init; }

    /// <summary>
    /// Filled size.
    /// </summary>
    public required decimal FilledSize { get; init; }

    /// <summary>
    /// Remaining size.
    /// </summary>
    public decimal RemainingSize => Size - FilledSize;

    /// <summary>
    /// Order status string.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// The order state in our state machine.
    /// </summary>
    public required OrderState State { get; init; }

    /// <summary>
    /// Rejection reason (if rejected).
    /// </summary>
    public string? RejectReason { get; init; }

    /// <summary>
    /// Timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether the order is reduce-only.
    /// </summary>
    public bool ReduceOnly { get; init; }

    /// <summary>
    /// Gets whether this order is now terminal (filled, cancelled, or rejected).
    /// </summary>
    public bool IsTerminal => State is OrderState.Filled or OrderState.Cancelled or OrderState.Rejected;
}
