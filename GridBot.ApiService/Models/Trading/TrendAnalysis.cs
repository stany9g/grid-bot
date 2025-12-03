namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Result of trend analysis containing current and proposed trend states with indicators.
/// </summary>
public sealed class TrendAnalysis
{
    /// <summary>
    /// Current confirmed trend state.
    /// </summary>
    public TrendState CurrentState { get; init; }

    /// <summary>
    /// Proposed new trend state based on latest indicators.
    /// </summary>
    public TrendState ProposedState { get; init; }

    /// <summary>
    /// Whether the proposed state requires confirmation delay before adoption.
    /// </summary>
    public bool ConfirmationRequired { get; init; }

    /// <summary>
    /// Time when the proposed state can be confirmed (after confirmation delay).
    /// Null if no confirmation is pending.
    /// </summary>
    public DateTimeOffset? ConfirmationTime { get; init; }

    /// <summary>
    /// EMA(20) value used in trend detection.
    /// </summary>
    public decimal Ema20 { get; init; }

    /// <summary>
    /// EMA(50) value used in trend detection.
    /// </summary>
    public decimal Ema50 { get; init; }

    /// <summary>
    /// MACD indicator result.
    /// </summary>
    public MacdResult Macd { get; init; } = new();

    /// <summary>
    /// ADX trend strength indicator (0-100).
    /// </summary>
    public decimal Adx { get; init; }

    /// <summary>
    /// Target inventory skew percentage for the proposed state.
    /// </summary>
    public decimal TargetSkew { get; init; }

    /// <summary>
    /// Human-readable reason for the trend state determination.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Whether the bot is in trend flip cooldown mode.
    /// </summary>
    public bool InCooldown { get; init; }

    /// <summary>
    /// When the cooldown period expires. Null if not in cooldown.
    /// </summary>
    public DateTimeOffset? CooldownExpiry { get; init; }

    /// <summary>
    /// Empty trend analysis for use when no data is available.
    /// </summary>
    public static TrendAnalysis Empty { get; } = new()
    {
        CurrentState = TrendState.Neutral,
        ProposedState = TrendState.Neutral,
        TargetSkew = 50m,
        Reason = "No data available"
    };
}
