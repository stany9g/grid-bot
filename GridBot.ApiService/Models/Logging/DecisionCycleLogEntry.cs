namespace GridBot.ApiService.Models.Logging;

/// <summary>
/// Represents a single decision cycle log entry for the trading bot.
/// Contains comprehensive information about the state of the system at the time of the decision.
/// </summary>
public sealed record DecisionCycleLogEntry
{
    /// <summary>
    /// Unique identifier for this log entry.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Timestamp when this decision cycle was executed.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// The market ID this decision was made for.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Current market price at the time of decision.
    /// </summary>
    public required decimal CurrentPrice { get; init; }

    // Position Information

    /// <summary>
    /// Current position size (absolute value).
    /// </summary>
    public decimal? PositionSize { get; init; }

    /// <summary>
    /// Position direction: LONG, SHORT, or FLAT.
    /// </summary>
    public string PositionDirection { get; init; } = "FLAT";

    // Trend Information

    /// <summary>
    /// Current trend state (e.g., StrongBull, Bull, Neutral, Bear, StrongBear).
    /// </summary>
    public string TrendState { get; init; } = "N/A";

    /// <summary>
    /// Target skew percentage based on trend analysis.
    /// </summary>
    public decimal TargetSkew { get; init; }

    /// <summary>
    /// Actual current skew percentage.
    /// </summary>
    public decimal ActualSkew { get; init; }

    // Grid Information

    /// <summary>
    /// Number of active buy orders on the grid.
    /// </summary>
    public int ActiveBuyOrders { get; init; }

    /// <summary>
    /// Number of active sell orders on the grid.
    /// </summary>
    public int ActiveSellOrders { get; init; }

    // Risk Information

    /// <summary>
    /// Current risk status (OK, CRASH:Moderate, PUMP:Severe, LOSS_LIMIT, etc.).
    /// </summary>
    public string RiskStatus { get; init; } = "OK";

    /// <summary>
    /// Current blocking status (OK, BLOCKED:BUYS, BLOCKED:SELLS, BLOCKED:ALL).
    /// </summary>
    public string BlockStatus { get; init; } = "OK";

    // Operations

    /// <summary>
    /// Current operational capacity percentage (0-100).
    /// </summary>
    public int Capacity { get; init; }

    /// <summary>
    /// Number of orders placed in this decision cycle.
    /// </summary>
    public int OrdersPlaced { get; init; }

    /// <summary>
    /// Number of orders cancelled in this decision cycle.
    /// </summary>
    public int OrdersCancelled { get; init; }

    /// <summary>
    /// Duration of the decision cycle in milliseconds.
    /// </summary>
    public double DurationMs { get; init; }

    // Moon Bag

    /// <summary>
    /// Moon bag status if active (e.g., HOLD:0.0010, TRAIL:0.0010, RELEASED).
    /// </summary>
    public string? MoonBagStatus { get; init; }

    // Warnings and Actions

    /// <summary>
    /// List of warnings generated during this decision cycle.
    /// </summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// List of actions that were blocked during this decision cycle.
    /// </summary>
    public List<string> ActionsBlocked { get; init; } = [];
}
