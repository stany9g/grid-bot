using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Logs and retrieves risk events for monitoring and analysis.
/// Thread-safe for concurrent access.
/// </summary>
public interface IRiskEventLogger
{
    /// <summary>
    /// Logs a risk event.
    /// </summary>
    /// <param name="riskEvent">The risk event to log.</param>
    /// <param name="ct">Cancellation token.</param>
    Task LogEventAsync(RiskEvent riskEvent, CancellationToken ct = default);

    /// <summary>
    /// Gets the most recent risk events.
    /// </summary>
    /// <param name="count">Maximum number of events to return.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of recent events, most recent first.</returns>
    Task<IReadOnlyList<RiskEvent>> GetRecentEventsAsync(int count = 100, CancellationToken ct = default);

    /// <summary>
    /// Gets risk events for a specific market since a given time.
    /// </summary>
    /// <param name="marketId">Lighter DEX market ID.</param>
    /// <param name="since">Start time for event retrieval.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of events for the market.</returns>
    Task<IReadOnlyList<RiskEvent>> GetEventsByMarketAsync(int marketId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Gets risk events by severity level.
    /// </summary>
    /// <param name="severity">Minimum severity level.</param>
    /// <param name="since">Start time for event retrieval.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of events at or above the specified severity.</returns>
    Task<IReadOnlyList<RiskEvent>> GetEventsBySeverityAsync(AlertSeverity severity, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>
    /// Gets the count of events by severity in a time period.
    /// </summary>
    /// <param name="since">Start time.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Dictionary mapping severity to count.</returns>
    Task<Dictionary<AlertSeverity, int>> GetEventCountsBySeverityAsync(DateTimeOffset since, CancellationToken ct = default);
}
