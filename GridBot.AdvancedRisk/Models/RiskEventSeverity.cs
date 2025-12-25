namespace GridBot.AdvancedRisk.Models;

/// <summary>
/// Severity levels for risk events.
/// </summary>
public enum RiskEventSeverity
{
    /// <summary>
    /// Informational event - logged for audit trail.
    /// </summary>
    Info = 0,

    /// <summary>
    /// Warning event - requires monitoring.
    /// </summary>
    Warning = 1,

    /// <summary>
    /// High severity - requires prompt attention.
    /// </summary>
    High = 2,

    /// <summary>
    /// Critical event - immediate action required.
    /// </summary>
    Critical = 3
}
