namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Outcome of a decision cycle execution.
/// Contains all results, state changes, and audit information.
/// </summary>
public sealed class DecisionResult
{
    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// When this decision cycle completed.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Whether the decision cycle completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Error message if Success is false.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Trading state before this cycle.
    /// </summary>
    public TradingState PreviousState { get; init; }

    /// <summary>
    /// Trading state after this cycle.
    /// </summary>
    public TradingState CurrentState { get; init; }

    /// <summary>
    /// Risk assessment result (null if skipped or failed).
    /// </summary>
    public RiskAssessment? RiskAssessment { get; init; }

    /// <summary>
    /// Moon bag status result (null if skipped or failed).
    /// </summary>
    public MoonBagStatus? MoonBagStatus { get; init; }

    /// <summary>
    /// Trend intelligence result (null if skipped or failed).
    /// </summary>
    public TrendIntelligenceResult? TrendResult { get; init; }

    /// <summary>
    /// Grid update result (null if skipped or failed).
    /// </summary>
    public GridUpdateResult? GridResult { get; init; }

    /// <summary>
    /// Number of orders placed during this cycle.
    /// </summary>
    public int OrdersPlaced { get; init; }

    /// <summary>
    /// Number of orders cancelled during this cycle.
    /// </summary>
    public int OrdersCancelled { get; init; }

    /// <summary>
    /// Effective position multiplier applied during this cycle.
    /// Combines risk, recovery, and liquidity multipliers.
    /// </summary>
    public decimal EffectivePositionMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Effective spread multiplier applied during this cycle.
    /// Combines risk, recovery, and liquidity multipliers.
    /// </summary>
    public decimal EffectiveSpreadMultiplier { get; init; } = 1.0m;

    /// <summary>
    /// Current recovery phase (None if not in recovery).
    /// </summary>
    public RecoveryPhase RecoveryPhase { get; init; } = RecoveryPhase.None;

    /// <summary>
    /// Estimated time remaining in recovery (null if not in recovery).
    /// </summary>
    public TimeSpan? RecoveryTimeRemaining { get; init; }

    /// <summary>
    /// Warning messages generated during this cycle.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// Actions that were blocked during this cycle with reasons.
    /// </summary>
    public IReadOnlyList<string> ActionsBlocked { get; init; } = [];

    /// <summary>
    /// Total execution duration of this decision cycle.
    /// </summary>
    public TimeSpan ExecutionDuration { get; init; }

    /// <summary>
    /// Whether the trading state changed during this cycle.
    /// </summary>
    public bool StateChanged => PreviousState != CurrentState;

    /// <summary>
    /// Whether this cycle was skipped (e.g., due to insufficient data).
    /// </summary>
    public bool WasSkipped { get; init; }

    /// <summary>
    /// Reason for skipping, if applicable.
    /// </summary>
    public string? SkipReason { get; init; }

    /// <summary>
    /// Creates a successful decision result.
    /// </summary>
    public static DecisionResult Succeeded(
        int marketId,
        TradingState previousState,
        TradingState currentState,
        RiskAssessment? riskAssessment,
        MoonBagStatus? moonBagStatus,
        TrendIntelligenceResult? trendResult,
        GridUpdateResult? gridResult,
        decimal positionMultiplier,
        decimal spreadMultiplier,
        RecoveryPhase recoveryPhase,
        TimeSpan? recoveryTimeRemaining,
        List<string> warnings,
        List<string> actionsBlocked,
        TimeSpan executionDuration,
        int ordersPlaced = 0,
        int ordersCancelled = 0)
    {
        return new DecisionResult
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            PreviousState = previousState,
            CurrentState = currentState,
            RiskAssessment = riskAssessment,
            MoonBagStatus = moonBagStatus,
            TrendResult = trendResult,
            GridResult = gridResult,
            EffectivePositionMultiplier = positionMultiplier,
            EffectiveSpreadMultiplier = spreadMultiplier,
            RecoveryPhase = recoveryPhase,
            RecoveryTimeRemaining = recoveryTimeRemaining,
            Warnings = warnings,
            ActionsBlocked = actionsBlocked,
            ExecutionDuration = executionDuration,
            OrdersPlaced = ordersPlaced,
            OrdersCancelled = ordersCancelled
        };
    }

    /// <summary>
    /// Creates a failed decision result.
    /// </summary>
    public static DecisionResult Failed(
        int marketId,
        TradingState previousState,
        string errorMessage,
        TimeSpan executionDuration)
    {
        return new DecisionResult
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            Success = false,
            ErrorMessage = errorMessage,
            PreviousState = previousState,
            CurrentState = previousState, // State unchanged on failure
            ExecutionDuration = executionDuration
        };
    }

    /// <summary>
    /// Creates a skipped decision result.
    /// </summary>
    public static DecisionResult Skipped(
        int marketId,
        TradingState currentState,
        string skipReason,
        TimeSpan executionDuration)
    {
        return new DecisionResult
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            Success = true,
            WasSkipped = true,
            SkipReason = skipReason,
            PreviousState = currentState,
            CurrentState = currentState,
            ExecutionDuration = executionDuration
        };
    }
}
