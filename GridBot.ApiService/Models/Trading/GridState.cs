namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Complete state of a grid for a specific market.
/// </summary>
public sealed class GridState
{
    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Current operational status of the grid.
    /// </summary>
    public GridStatus Status { get; set; } = GridStatus.Uninitialized;

    /// <summary>
    /// Current grid parameters.
    /// </summary>
    public GridParameters Parameters { get; set; } = null!;

    /// <summary>
    /// All grid levels with their order states.
    /// </summary>
    public List<GridLevel> Levels { get; set; } = [];

    /// <summary>
    /// Timestamp when the grid was first created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Timestamp of last update to the grid.
    /// </summary>
    public DateTimeOffset LastUpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Total number of filled orders since grid creation.
    /// </summary>
    public int TotalFills { get; set; }

    /// <summary>
    /// Cumulative realized PnL from grid trading.
    /// </summary>
    public decimal RealizedPnl { get; set; }

    /// <summary>
    /// Number of times the grid has been shifted.
    /// </summary>
    public int ShiftCount { get; set; }

    /// <summary>
    /// Number of times the grid has been rebuilt.
    /// </summary>
    public int RebuildCount { get; set; }
}

/// <summary>
/// Operational status of a trading grid.
/// </summary>
public enum GridStatus
{
    /// <summary>
    /// Grid has not been initialized.
    /// </summary>
    Uninitialized,

    /// <summary>
    /// Grid is active with orders placed.
    /// </summary>
    Active,

    /// <summary>
    /// Grid is temporarily paused, orders may still be live.
    /// </summary>
    Paused,

    /// <summary>
    /// Grid is being rebuilt due to significant market changes.
    /// </summary>
    Rebuilding
}
