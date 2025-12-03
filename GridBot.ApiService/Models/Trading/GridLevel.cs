namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents a single price level in the grid with order tracking.
/// </summary>
public sealed class GridLevel
{
    /// <summary>
    /// Price at this grid level.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// True if this is a bid (buy) order, false if ask (sell).
    /// </summary>
    public required bool IsBid { get; init; }

    /// <summary>
    /// Index of this level relative to center (0 = closest to center).
    /// </summary>
    public required int LevelIndex { get; init; }

    /// <summary>
    /// Order size at this level in base asset units.
    /// Can be updated dynamically based on capital allocation.
    /// </summary>
    public decimal Size { get; set; }

    /// <summary>
    /// Lighter order ID if order is placed, null otherwise.
    /// </summary>
    public long? OrderId { get; set; }

    /// <summary>
    /// Client order index used when placing the order.
    /// </summary>
    public long? ClientOrderIndex { get; set; }

    /// <summary>
    /// Current status of this grid level.
    /// </summary>
    public GridLevelStatus Status { get; set; } = GridLevelStatus.Pending;

    /// <summary>
    /// Timestamp when this level was last updated.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Status of a grid level order.
/// </summary>
public enum GridLevelStatus
{
    /// <summary>
    /// Order has not yet been placed.
    /// </summary>
    Pending,

    /// <summary>
    /// Order is live on the exchange.
    /// </summary>
    Active,

    /// <summary>
    /// Order has been completely filled.
    /// </summary>
    Filled,

    /// <summary>
    /// Order was cancelled.
    /// </summary>
    Cancelled
}
