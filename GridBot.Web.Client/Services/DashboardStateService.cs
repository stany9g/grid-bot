using GridBot.Web.Client.Models;

namespace GridBot.Web.Client.Services;

/// <summary>
/// Stub implementation for WebAssembly mode (not currently used).
/// The actual implementation is in GridBot.Web.Services.DashboardStateService
/// which is used in Interactive Server mode.
/// </summary>
/// <remarks>
/// This stub exists only to allow the Client project to compile.
/// When running in Interactive Server mode, the Web project's DashboardStateService
/// is injected instead.
/// </remarks>
public sealed class DashboardStateService : IDashboardStateService
{
#pragma warning disable CS0067 // Event is never used (intentional - this is a stub)
    public event EventHandler<DashboardState>? StateChanged;
#pragma warning restore CS0067

    public DashboardState CurrentState => throw new NotSupportedException(
        "DashboardStateService stub is not intended for use. " +
        "Use Interactive Server mode with GridBot.Web.Services.DashboardStateService.");

    public Task RefreshAsync(CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "DashboardStateService stub is not intended for use. " +
            "Use Interactive Server mode with GridBot.Web.Services.DashboardStateService.");
    }

    public Task AddAlertAsync(AlertItem alert, CancellationToken ct = default)
    {
        throw new NotSupportedException(
            "DashboardStateService stub is not intended for use. " +
            "Use Interactive Server mode with GridBot.Web.Services.DashboardStateService.");
    }

    public void ClearAlerts()
    {
        throw new NotSupportedException(
            "DashboardStateService stub is not intended for use. " +
            "Use Interactive Server mode with GridBot.Web.Services.DashboardStateService.");
    }

    public void AcknowledgeAlert(Guid alertId)
    {
        throw new NotSupportedException(
            "DashboardStateService stub is not intended for use. " +
            "Use Interactive Server mode with GridBot.Web.Services.DashboardStateService.");
    }

    public void Dispose()
    {
        // No-op for stub
    }
}
