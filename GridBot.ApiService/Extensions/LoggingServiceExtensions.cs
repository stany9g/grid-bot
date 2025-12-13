using GridBot.ApiService.Services.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering logging services.
/// </summary>
public static class LoggingServiceExtensions
{
    /// <summary>
    /// Adds logging services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLoggingServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register DecisionCycleLogService as singleton
        // Must be singleton to maintain log history across the application lifetime
        services.AddSingleton<IDecisionCycleLogService, DecisionCycleLogService>();

        return services;
    }
}
