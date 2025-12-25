namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Current status of recovery process for a market.
/// </summary>
/// <param name="MarketId">Lighter DEX market ID.</param>
/// <param name="Phase">Current recovery phase.</param>
/// <param name="PhaseStartedAt">When the current phase started.</param>
/// <param name="PhaseEndsAt">When the current phase is expected to end.</param>
/// <param name="PositionMultiplier">Current position size multiplier (0.0 to 1.0).</param>
/// <param name="SpreadMultiplier">Current spread multiplier (1.0 = normal, 2.0 = double spreads).</param>
/// <param name="ReasonForProtectiveMode">What triggered protective mode initially.</param>
public sealed record RecoveryStatus(
    int MarketId,
    RecoveryPhase Phase,
    DateTimeOffset PhaseStartedAt,
    DateTimeOffset PhaseEndsAt,
    decimal PositionMultiplier,
    decimal SpreadMultiplier,
    string ReasonForProtectiveMode)
{
    /// <summary>
    /// Whether recovery is currently in progress.
    /// </summary>
    public bool IsRecovering => Phase != RecoveryPhase.None;

    /// <summary>
    /// Time remaining in current phase.
    /// </summary>
    public TimeSpan TimeRemaining => PhaseEndsAt > DateTimeOffset.UtcNow
        ? PhaseEndsAt - DateTimeOffset.UtcNow
        : TimeSpan.Zero;

    /// <summary>
    /// Creates a status for normal operation (no recovery).
    /// </summary>
    public static RecoveryStatus Normal(int marketId) => new(
        marketId,
        RecoveryPhase.None,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        PositionMultiplier: 1.0m,
        SpreadMultiplier: 1.0m,
        ReasonForProtectiveMode: string.Empty);
}
