using GridBot.AdvancedRisk.Models;
namespace GridBot.AdvancedRisk.Services.Risk;
/// <summary>
/// Logs risk events for audit trail and notifications.
/// </summary>
public interface IRiskEventLogger
{
    /// <summary>
    /// Logs a risk event.
    /// </summary>
    Task LogEventAsync(RiskEvent evt, CancellationToken ct = default);
    /// <summary>
    /// Gets recent risk events for a market.
    /// </summary>
    Task<IReadOnlyList<RiskEvent>> GetRecentEventsAsync(int marketId, int count = 50, CancellationToken ct = default);
}
