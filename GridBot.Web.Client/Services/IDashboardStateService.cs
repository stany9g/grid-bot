using GridBot.Web.Client.Models;

namespace GridBot.Web.Client.Services;

/// <summary>
/// Service for managing dashboard state and real-time updates.
/// Runs in the browser via WebAssembly.
/// </summary>
public interface IDashboardStateService : IAsyncDisposable
{
    /// <summary>
    /// Event raised when the dashboard state changes.
    /// </summary>
    event EventHandler<DashboardState>? StateChanged;

    /// <summary>
    /// Gets the current dashboard state.
    /// </summary>
    DashboardState CurrentState { get; }

    /// <summary>
    /// Gets whether the service is currently polling for updates.
    /// </summary>
    bool IsPolling { get; }

    /// <summary>
    /// Starts polling for state updates.
    /// </summary>
    Task StartPollingAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops polling for state updates.
    /// </summary>
    Task StopPollingAsync();

    /// <summary>
    /// Forces an immediate state refresh.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds an alert to the dashboard.
    /// </summary>
    Task AddAlertAsync(AlertItem alert, CancellationToken ct = default);

    /// <summary>
    /// Clears all alerts from the dashboard.
    /// </summary>
    void ClearAlerts();

    /// <summary>
    /// Acknowledges an alert by ID.
    /// </summary>
    void AcknowledgeAlert(Guid alertId);
}
