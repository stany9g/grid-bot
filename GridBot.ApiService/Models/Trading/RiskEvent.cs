namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Records a risk rule trigger event for logging and analysis.
/// </summary>
/// <param name="Id">Unique identifier for the event.</param>
/// <param name="Timestamp">When the event occurred.</param>
/// <param name="RuleId">Rule identifier (e.g., "FC-001", "IM-003", "GG-002").</param>
/// <param name="Severity">Alert severity level.</param>
/// <param name="Description">Human-readable description of what triggered.</param>
/// <param name="TriggerValue">The actual value that triggered the rule (optional).</param>
/// <param name="ThresholdValue">The threshold value that was breached (optional).</param>
/// <param name="ActionTaken">Description of the automated action taken in response.</param>
public record RiskEvent(
    Guid Id,
    DateTimeOffset Timestamp,
    string RuleId,
    AlertSeverity Severity,
    string Description,
    decimal? TriggerValue,
    decimal? ThresholdValue,
    string ActionTaken)
{
    /// <summary>
    /// Creates a new RiskEvent with auto-generated Id and current timestamp.
    /// </summary>
    public static RiskEvent Create(
        string ruleId,
        AlertSeverity severity,
        string description,
        string actionTaken,
        decimal? triggerValue = null,
        decimal? thresholdValue = null)
    {
        return new RiskEvent(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            ruleId,
            severity,
            description,
            triggerValue,
            thresholdValue,
            actionTaken);
    }
}
