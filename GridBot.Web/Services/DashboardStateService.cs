using GridBot.Web.Client.Models;
using GridBot.Web.Client.Services;

namespace GridBot.Web.Services;

/// <summary>
/// Scoped thin wrapper that delegates to the singleton DashboardStateProvider.
/// Each Blazor circuit gets its own instance, but all share the same underlying state.
/// </summary>
public sealed class DashboardStateService : IDashboardStateService
{
    private readonly IDashboardStateProvider _provider;
    private readonly ILogger<DashboardStateService> _logger;

    public event EventHandler<DashboardState>? StateChanged;
    public DashboardState CurrentState => _provider.CurrentState;

    public DashboardStateService(
        IDashboardStateProvider provider,
        ILogger<DashboardStateService> logger)
    {
        _provider = provider;
        _logger = logger;

        // Subscribe to provider events and forward to local subscribers
        _provider.StateChanged += OnProviderStateChanged;
        _logger.LogDebug("DashboardStateService created and subscribed to provider");
    }

    public Task RefreshAsync(CancellationToken ct = default)
    {
        return _provider.RefreshAsync(ct);
    }

    public Task AddAlertAsync(AlertItem alert, CancellationToken ct = default)
    {
        return _provider.AddAlertAsync(alert, ct);
    }

    public void ClearAlerts()
    {
        _provider.ClearAlerts();
    }

    public void AcknowledgeAlert(Guid alertId)
    {
        _provider.AcknowledgeAlert(alertId);
    }

    public void Dispose()
    {
        _provider.StateChanged -= OnProviderStateChanged;
        _logger.LogDebug("DashboardStateService disposed and unsubscribed from provider");
    }

    private void OnProviderStateChanged(object? sender, DashboardState state)
    {
        // Forward the event to local subscribers
        StateChanged?.Invoke(this, state);
    }
}
