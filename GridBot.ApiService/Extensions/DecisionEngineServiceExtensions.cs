using GridBot.ApiService.Services.Capacity;
using GridBot.ApiService.Services.DecisionEngine;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering Decision Engine services.
/// </summary>
public static class DecisionEngineServiceExtensions
{
    /// <summary>
    /// Adds Decision Engine services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddDecisionEngine(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register Operational Capacity Service (singleton for consistent capacity calculations)
        services.AddSingleton<IOperationalCapacityService, OperationalCapacityService>();

        // Register Recovery Manager (singleton for state persistence)
        services.AddSingleton<IRecoveryManager, RecoveryManager>();

        // Register Trading Decision Engine (singleton to maintain state across cycles)
        services.AddSingleton<ITradingDecisionEngine, TradingDecisionEngine>();

        return services;
    }
}
