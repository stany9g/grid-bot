namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Result of a trailing grid shift operation.
/// </summary>
public sealed class GridShiftResult
{
    /// <summary>
    /// Whether the grid was actually shifted.
    /// </summary>
    public bool Shifted { get; init; }

    /// <summary>
    /// New upper bound of the grid after shift.
    /// </summary>
    public decimal NewUpperBound { get; init; }

    /// <summary>
    /// New lower bound of the grid after shift.
    /// </summary>
    public decimal NewLowerBound { get; init; }

    /// <summary>
    /// Amount the grid was shifted by (percentage).
    /// </summary>
    public decimal ShiftAmount { get; init; }

    /// <summary>
    /// Whether grid shift was prevented due to cooldown.
    /// </summary>
    public bool CoolingDown { get; init; }

    /// <summary>
    /// Reason for the result (success or why shift was prevented).
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// Whether the shift was capped due to max shift limit.
    /// </summary>
    public bool WasCapped { get; init; }

    /// <summary>
    /// Whether flash spike protection is active.
    /// </summary>
    public bool FlashSpikeActive { get; init; }

    /// <summary>
    /// Cumulative shift percentage in the last hour.
    /// </summary>
    public decimal CumulativeShift1h { get; init; }

    /// <summary>
    /// Timestamp of the shift operation.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Creates a result indicating no shift was needed.
    /// </summary>
    public static GridShiftResult NoShiftNeeded() => new()
    {
        Shifted = false,
        Reason = "Price within grid bounds, no shift needed"
    };

    /// <summary>
    /// Creates a result indicating shift was prevented by cooldown.
    /// </summary>
    public static GridShiftResult OnCooldown(TimeSpan remaining) => new()
    {
        Shifted = false,
        CoolingDown = true,
        Reason = $"Shift cooldown active, {remaining.TotalSeconds:F0}s remaining"
    };

    /// <summary>
    /// Creates a result indicating shift was prevented by flash spike protection.
    /// </summary>
    public static GridShiftResult FlashSpikePrevented(TimeSpan remaining) => new()
    {
        Shifted = false,
        FlashSpikeActive = true,
        Reason = $"Flash spike protection active, {remaining.TotalMinutes:F1}min remaining"
    };

    /// <summary>
    /// Creates a result indicating shift was prevented by hourly limit.
    /// </summary>
    public static GridShiftResult HourlyLimitReached(decimal cumulativeShift) => new()
    {
        Shifted = false,
        CumulativeShift1h = cumulativeShift,
        Reason = $"Hourly shift limit reached ({cumulativeShift:P1})"
    };

    /// <summary>
    /// Creates a successful shift result.
    /// </summary>
    public static GridShiftResult Success(
        decimal newUpperBound,
        decimal newLowerBound,
        decimal shiftAmount,
        bool wasCapped,
        decimal cumulativeShift1h) => new()
    {
        Shifted = true,
        NewUpperBound = newUpperBound,
        NewLowerBound = newLowerBound,
        ShiftAmount = shiftAmount,
        WasCapped = wasCapped,
        CumulativeShift1h = cumulativeShift1h,
        Reason = wasCapped
            ? $"Grid shifted by {shiftAmount:P2} (capped from larger shift)"
            : $"Grid shifted by {shiftAmount:P2}"
    };
}
