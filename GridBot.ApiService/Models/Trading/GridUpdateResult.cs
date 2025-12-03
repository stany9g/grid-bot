namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Result of a grid update operation.
/// </summary>
public sealed class GridUpdateResult
{
    /// <summary>
    /// True if the grid was shifted to a new center price.
    /// </summary>
    public bool GridShifted { get; init; }

    /// <summary>
    /// True if grid parameters (spacing, orders per side) changed.
    /// </summary>
    public bool ParametersChanged { get; init; }

    /// <summary>
    /// Number of new orders added during this update.
    /// </summary>
    public int OrdersAdded { get; init; }

    /// <summary>
    /// Number of orders cancelled during this update.
    /// </summary>
    public int OrdersCancelled { get; init; }

    /// <summary>
    /// Number of filled orders detected during this update.
    /// </summary>
    public int FillsDetected { get; init; }

    /// <summary>
    /// True if no changes were needed.
    /// </summary>
    public bool NoChangesNeeded => !GridShifted && !ParametersChanged && OrdersAdded == 0 && OrdersCancelled == 0;

    /// <summary>
    /// Optional message describing what happened during the update.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Creates a result indicating no changes were needed.
    /// </summary>
    public static GridUpdateResult NoChanges() => new()
    {
        GridShifted = false,
        ParametersChanged = false,
        OrdersAdded = 0,
        OrdersCancelled = 0,
        FillsDetected = 0,
        Message = "No changes needed"
    };
}
