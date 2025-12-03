using GridBot.ApiService.Services.Grid;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering grid trading services.
/// </summary>
public static class GridServiceExtensions
{
    /// <summary>
    /// Adds grid trading services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGridServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register grid calculator
        services.AddSingleton<IGridCalculator, GridCalculator>();

        // Register grid order manager
        services.AddSingleton<IGridOrderManager, GridOrderManager>();

        // Register grid lifecycle service
        services.AddSingleton<IGridLifecycleService, GridLifecycleService>();

        return services;
    }
}
