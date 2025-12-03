namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Result of placing grid orders on the exchange.
/// </summary>
public sealed class GridPlacementResult
{
    /// <summary>
    /// Number of orders successfully placed.
    /// </summary>
    public required int OrdersPlaced { get; init; }

    /// <summary>
    /// Number of orders that failed to place.
    /// </summary>
    public required int OrdersFailed { get; init; }

    /// <summary>
    /// Details of any errors that occurred during placement.
    /// </summary>
    public List<GridOrderError> Errors { get; init; } = [];

    /// <summary>
    /// True if all orders were placed successfully.
    /// </summary>
    public bool IsFullyPlaced => OrdersFailed == 0;
}

/// <summary>
/// Details of an error when placing a grid order.
/// </summary>
public sealed class GridOrderError
{
    /// <summary>
    /// Index of the grid level that failed.
    /// </summary>
    public required int LevelIndex { get; init; }

    /// <summary>
    /// Price at the failed level.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Whether this was a bid (buy) or ask (sell) order.
    /// </summary>
    public required bool IsBid { get; init; }

    /// <summary>
    /// Error message from the exchange or internal error.
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// Timestamp when the error occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
