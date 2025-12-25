namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Represents the current phase of recovery from protective mode.
/// </summary>
/// <remarks>
/// Recovery is graduated to prevent whipsawing back into protective mode:
/// - Phase1: Minimal exposure, wide spreads, observe market
/// - Phase2: Reduced exposure, normal spreads, test waters
/// - Phase3: Near-normal exposure, slight conservatism
/// - None: Full normal operation
/// </remarks>
public enum RecoveryPhase
{
    /// <summary>
    /// No recovery in progress - normal operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Phase 1: Minimal exposure (25% of normal).
    /// Wide spreads (2x normal). Duration: 15 minutes.
    /// Observation period after protective mode exit.
    /// </summary>
    Phase1 = 1,

    /// <summary>
    /// Phase 2: Reduced exposure (50% of normal).
    /// Normal spreads. Duration: 30 minutes.
    /// Market has shown stability, testing the waters.
    /// </summary>
    Phase2 = 2,

    /// <summary>
    /// Phase 3: Near-normal exposure (75% of normal).
    /// Normal spreads with slight conservatism. Duration: 60 minutes.
    /// Confidence building before full restoration.
    /// </summary>
    Phase3 = 3
}
