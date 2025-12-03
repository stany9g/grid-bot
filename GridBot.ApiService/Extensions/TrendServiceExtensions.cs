using GridBot.ApiService.Services.Inventory;
using GridBot.ApiService.Services.Rebalancing;
using GridBot.ApiService.Services.Trend;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering trend intelligence services.
/// </summary>
public static class TrendServiceExtensions
{
    /// <summary>
    /// Adds trend intelligence services to the service collection.
    /// Includes trend detection, inventory management, and rebalancing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrendServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register trend detection service
        services.AddSingleton<ITrendDetector, TrendDetector>();

        // Register inventory management service
        // Note: InventoryManager is registered as concrete type too for internal access by RebalancingService
        services.AddSingleton<InventoryManager>();
        services.AddSingleton<IInventoryManager>(sp => sp.GetRequiredService<InventoryManager>());

        // Register rebalancing service
        services.AddSingleton<IRebalancingService, RebalancingService>();

        // Register orchestrator service
        services.AddSingleton<ITrendIntelligenceService, TrendIntelligenceService>();

        return services;
    }
}
