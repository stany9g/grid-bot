namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Tracks recovery progress for a market after a halt condition.
/// Used by the Recovery Manager to manage phase transitions.
/// </summary>
public sealed class RecoveryState
{
    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Current recovery phase.
    /// </summary>
    public RecoveryPhase CurrentPhase { get; set; } = RecoveryPhase.Phase1;

    /// <summary>
    /// When the current phase started.
    /// </summary>
    public DateTimeOffset PhaseStartedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When recovery started (entered Recovering state).
    /// </summary>
    public DateTimeOffset RecoveryStartedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Type of trigger that caused the halt (e.g., "DailyLossLimit", "FlashCrash").
    /// </summary>
    public required string TriggerType { get; init; }

    /// <summary>
    /// When the original trigger event occurred.
    /// </summary>
    public DateTimeOffset TriggerTimestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of failed phase advancement attempts.
    /// After 3 failures, requires operator review.
    /// </summary>
    public int FailedAdvancementCount { get; set; }

    /// <summary>
    /// When the last phase advancement was attempted.
    /// </summary>
    public DateTimeOffset? LastAdvancementAttempt { get; set; }

    /// <summary>
    /// Price when the current phase started.
    /// Used to calculate volatility during phase.
    /// </summary>
    public decimal PhaseStartPrice { get; set; }

    /// <summary>
    /// Equity when the current phase started.
    /// Used to calculate P&L during phase.
    /// </summary>
    public decimal PhaseStartEquity { get; set; }

    /// <summary>
    /// Highest price seen during current phase.
    /// </summary>
    public decimal PhaseHighPrice { get; set; }

    /// <summary>
    /// Lowest price seen during current phase.
    /// </summary>
    public decimal PhaseLowPrice { get; set; }

    /// <summary>
    /// Whether API errors have occurred during current phase.
    /// Blocks advancement if true.
    /// </summary>
    public bool HasApiErrorsInPhase { get; set; }

    /// <summary>
    /// Timestamp of last API error, if any.
    /// </summary>
    public DateTimeOffset? LastApiErrorAt { get; set; }

    /// <summary>
    /// Whether a new circuit breaker triggered during this recovery.
    /// </summary>
    public bool NewBreakerTriggered { get; set; }

    /// <summary>
    /// Gets the time elapsed in the current phase.
    /// </summary>
    public TimeSpan GetPhaseElapsed() => DateTimeOffset.UtcNow - PhaseStartedAt;

    /// <summary>
    /// Gets the total time elapsed since recovery started.
    /// </summary>
    public TimeSpan GetRecoveryElapsed() => DateTimeOffset.UtcNow - RecoveryStartedAt;

    /// <summary>
    /// Calculates the price volatility during the current phase.
    /// </summary>
    /// <returns>Volatility as a decimal (e.g., 0.02 for 2%).</returns>
    public decimal CalculatePhaseVolatility()
    {
        if (PhaseStartPrice <= 0 || PhaseHighPrice <= 0 || PhaseLowPrice <= 0)
            return 0m;

        var range = PhaseHighPrice - PhaseLowPrice;
        return range / PhaseStartPrice;
    }

    /// <summary>
    /// Calculates the P&L during the current phase.
    /// </summary>
    /// <param name="currentEquity">Current equity value.</param>
    /// <returns>P&L as a decimal (negative for loss).</returns>
    public decimal CalculatePhasePnL(decimal currentEquity)
    {
        if (PhaseStartEquity <= 0)
            return 0m;

        return (currentEquity - PhaseStartEquity) / PhaseStartEquity;
    }

    /// <summary>
    /// Updates price tracking for volatility calculation.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    public void UpdatePriceTracking(decimal currentPrice)
    {
        if (currentPrice <= 0)
            return;

        if (PhaseHighPrice == 0 || currentPrice > PhaseHighPrice)
            PhaseHighPrice = currentPrice;

        if (PhaseLowPrice == 0 || currentPrice < PhaseLowPrice)
            PhaseLowPrice = currentPrice;
    }

    /// <summary>
    /// Records an API error during this phase.
    /// </summary>
    public void RecordApiError()
    {
        HasApiErrorsInPhase = true;
        LastApiErrorAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Resets phase-specific tracking when advancing to a new phase.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="currentEquity">Current equity value.</param>
    public void ResetPhaseTracking(decimal currentPrice, decimal currentEquity)
    {
        PhaseStartedAt = DateTimeOffset.UtcNow;
        PhaseStartPrice = currentPrice;
        PhaseStartEquity = currentEquity;
        PhaseHighPrice = currentPrice;
        PhaseLowPrice = currentPrice;
        HasApiErrorsInPhase = false;
        LastApiErrorAt = null;
    }

    /// <summary>
    /// Creates a new recovery state for a market.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="triggerType">Type of trigger that caused the halt.</param>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="currentEquity">Current equity value.</param>
    public static RecoveryState Create(
        int marketId,
        string triggerType,
        decimal currentPrice,
        decimal currentEquity)
    {
        var now = DateTimeOffset.UtcNow;
        return new RecoveryState
        {
            MarketId = marketId,
            CurrentPhase = RecoveryPhase.Phase1,
            PhaseStartedAt = now,
            RecoveryStartedAt = now,
            TriggerType = triggerType,
            TriggerTimestamp = now,
            PhaseStartPrice = currentPrice,
            PhaseStartEquity = currentEquity,
            PhaseHighPrice = currentPrice,
            PhaseLowPrice = currentPrice
        };
    }

    /// <summary>
    /// Creates a snapshot copy of this recovery state for thread-safe external access.
    /// </summary>
    /// <returns>A new RecoveryState instance with all properties copied.</returns>
    public RecoveryState ToSnapshot()
    {
        return new RecoveryState
        {
            MarketId = MarketId,
            CurrentPhase = CurrentPhase,
            PhaseStartedAt = PhaseStartedAt,
            RecoveryStartedAt = RecoveryStartedAt,
            TriggerType = TriggerType,
            TriggerTimestamp = TriggerTimestamp,
            FailedAdvancementCount = FailedAdvancementCount,
            LastAdvancementAttempt = LastAdvancementAttempt,
            PhaseStartPrice = PhaseStartPrice,
            PhaseStartEquity = PhaseStartEquity,
            PhaseHighPrice = PhaseHighPrice,
            PhaseLowPrice = PhaseLowPrice,
            HasApiErrorsInPhase = HasApiErrorsInPhase,
            LastApiErrorAt = LastApiErrorAt,
            NewBreakerTriggered = NewBreakerTriggered
        };
    }
}
