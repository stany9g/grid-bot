namespace GridBot.TrendIntelligence.Models;

/// <summary>
/// Result of a complete trend intelligence processing cycle.
/// </summary>
public sealed class TrendIntelligenceResult
{
    /// <summary>
    /// Trend analysis results including indicators and state.
    /// </summary>
    public TrendAnalysis TrendAnalysis { get; init; } = null!;

    /// <summary>
    /// Inventory analysis results including allocations and rebalance needs.
    /// </summary>
    public InventoryAnalysis InventoryAnalysis { get; init; } = null!;

    /// <summary>
    /// Result of rebalancing operation, if one was performed.
    /// Null if no rebalance was executed.
    /// </summary>
    public RebalanceResult? RebalanceResult { get; init; }

    /// <summary>
    /// Whether the trend state changed during this cycle.
    /// </summary>
    public bool TrendStateChanged { get; init; }

    /// <summary>
    /// Whether inventory was adjusted during this cycle.
    /// </summary>
    public bool InventoryAdjusted { get; init; }

    /// <summary>
    /// Whether the cycle completed successfully without errors.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if the cycle failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When this cycle was processed.
    /// </summary>
    public DateTimeOffset ProcessedAt { get; init; }

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    public static TrendIntelligenceResult Succeeded(
        TrendAnalysis trendAnalysis,
        InventoryAnalysis inventoryAnalysis,
        RebalanceResult? rebalanceResult,
        bool trendStateChanged,
        bool inventoryAdjusted)
    {
        return new TrendIntelligenceResult
        {
            TrendAnalysis = trendAnalysis,
            InventoryAnalysis = inventoryAnalysis,
            RebalanceResult = rebalanceResult,
            TrendStateChanged = trendStateChanged,
            InventoryAdjusted = inventoryAdjusted,
            Success = true,
            ProcessedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed result.
    /// </summary>
    public static TrendIntelligenceResult Failed(
        string errorMessage,
        TrendAnalysis? trendAnalysis = null,
        InventoryAnalysis? inventoryAnalysis = null)
    {
        return new TrendIntelligenceResult
        {
            TrendAnalysis = trendAnalysis ?? TrendAnalysis.Empty,
            InventoryAnalysis = inventoryAnalysis ?? InventoryAnalysis.Empty,
            Success = false,
            ErrorMessage = errorMessage,
            ProcessedAt = DateTimeOffset.UtcNow
        };
    }
}
