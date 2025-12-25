namespace GridBot.MoonBag.Services;

/// <summary>
/// Alert severity levels for moon bag events.
/// </summary>
public enum MoonBagAlertSeverity
{
    /// <summary>
    /// Informational event.
    /// </summary>
    Low,

    /// <summary>
    /// Warning event requiring attention.
    /// </summary>
    Medium,

    /// <summary>
    /// Important event requiring prompt attention.
    /// </summary>
    High,

    /// <summary>
    /// Critical event requiring immediate action.
    /// </summary>
    Critical
}

/// <summary>
/// Represents a moon bag risk event for logging.
/// </summary>
public sealed record MoonBagEvent(
    string Code,
    MoonBagAlertSeverity Severity,
    string Title,
    string Description,
    decimal Value1,
    decimal Value2);

/// <summary>
/// Abstraction for logging moon bag events.
/// Implemented by GridBot.ApiService to log to appropriate sinks.
/// </summary>
public interface IMoonBagEventLogger
{
    /// <summary>
    /// Logs a moon bag event.
    /// </summary>
    /// <param name="evt">The event to log.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogEventAsync(MoonBagEvent evt, CancellationToken ct = default);
}
