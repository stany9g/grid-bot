using GridBot.ApiService.Models.Dashboard;

namespace GridBot.ApiService.Services.Dashboard;

/// <summary>
/// Service for providing aggregated dashboard state to Blazor components.
/// Runs as a background service that polls trading services periodically.
/// </summary>
public interface IDashboardStateService : IDisposable
{
    /// <summary>
    /// Event raised when the dashboard state is updated.
    /// </summary>
    event EventHandler<DashboardState>? StateChanged;

    /// <summary>
    /// Gets the current aggregated dashboard state.
    /// </summary>
    DashboardState CurrentState { get; }

    /// <summary>
    /// Forces an immediate refresh of the dashboard state.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds an alert to the dashboard.
    /// </summary>
    /// <param name="alert">The alert to add.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAlertAsync(AlertItem alert, CancellationToken ct = default);

    /// <summary>
    /// Clears all alerts from the dashboard.
    /// </summary>
    void ClearAlerts();

    /// <summary>
    /// Acknowledges a specific alert.
    /// </summary>
    /// <param name="alertId">The ID of the alert to acknowledge.</param>
    void AcknowledgeAlert(Guid alertId);
}
