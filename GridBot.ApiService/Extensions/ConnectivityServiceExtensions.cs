using GridBot.ApiService.Services.Connectivity;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering connectivity monitoring services.
/// </summary>
public static class ConnectivityServiceExtensions
{
    /// <summary>
    /// Adds connectivity monitoring services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddConnectivityServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register WebSocket health monitor as singleton
        // Must be singleton to maintain state across decision cycles
        services.AddSingleton<IWebSocketHealthMonitor, WebSocketHealthMonitor>();

        return services;
    }
}
