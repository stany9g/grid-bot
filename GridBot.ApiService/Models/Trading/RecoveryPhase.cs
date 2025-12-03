namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents the current recovery phase after a halt condition.
/// Recovery proceeds through phases with increasing trading capacity.
/// </summary>
public enum RecoveryPhase
{
    /// <summary>
    /// Not in recovery - either Active or Halted state.
    /// </summary>
    None = 0,

    /// <summary>
    /// Phase 1: Initial re-entry (0-15 minutes).
    /// Position multiplier: 0.25 (75% size reduction).
    /// Grid orders: 25% of normal count.
    /// Spread multiplier: 1.5 (50% wider spreads).
    /// Rebalancing: DISABLED.
    /// </summary>
    Phase1 = 1,

    /// <summary>
    /// Phase 2: Cautious trading (15-30 minutes from recovery start).
    /// Position multiplier: 0.50 (50% size reduction).
    /// Grid orders: 50% of normal count.
    /// Spread multiplier: 1.25 (25% wider spreads).
    /// Rebalancing: DISABLED.
    /// </summary>
    Phase2 = 2,

    /// <summary>
    /// Phase 3: Stabilization (30-60 minutes from recovery start).
    /// Position multiplier: 0.75 (25% size reduction).
    /// Grid orders: 75% of normal count.
    /// Spread multiplier: 1.0 (normal spreads).
    /// Rebalancing: ENABLED at 50% rate.
    /// </summary>
    Phase3 = 3,

    /// <summary>
    /// Phase 4: Return to normal (60+ minutes from recovery start).
    /// Position multiplier: 1.0 (full size).
    /// Grid orders: 100% of normal count.
    /// Spread multiplier: 1.0 (normal spreads).
    /// Rebalancing: ENABLED at full rate.
    /// Transitions to Active state upon completion.
    /// </summary>
    Phase4 = 4
}
