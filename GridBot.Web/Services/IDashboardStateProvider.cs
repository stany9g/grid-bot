using GridBot.Web.Client.Models;

namespace GridBot.Web.Services;

/// <summary>
/// Singleton provider for dashboard state. Polls the API in the background
/// regardless of how many browser sessions are connected.
/// </summary>
public interface IDashboardStateProvider
{
    /// <summary>
    /// Event raised when the dashboard state changes.
    /// All subscribers (across all Blazor circuits) receive this event.
    /// </summary>
    event EventHandler<DashboardState>? StateChanged;

    /// <summary>
    /// Gets the current dashboard state snapshot.
    /// </summary>
    DashboardState CurrentState { get; }

    /// <summary>
    /// Forces an immediate state refresh, bypassing the polling interval.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds an alert to the dashboard. Alerts are stored centrally
    /// and included in the shared state.
    /// </summary>
    Task AddAlertAsync(AlertItem alert, CancellationToken ct = default);

    /// <summary>
    /// Clears all alerts from the dashboard.
    /// </summary>
    void ClearAlerts();

    /// <summary>
    /// Marks an alert as acknowledged by its ID.
    /// </summary>
    void AcknowledgeAlert(Guid alertId);
}
