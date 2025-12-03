using GridBot.ApiService.Services.MoonBag;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering moon bag protection services.
/// </summary>
public static class MoonBagServiceExtensions
{
    /// <summary>
    /// Adds moon bag protection services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMoonBagServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register flash spike detector first (no dependencies on other moon bag services)
        services.AddSingleton<IFlashSpikeDetector, FlashSpikeDetector>();

        // Register trailing grid service for grid shift management
        services.AddSingleton<ITrailingGridService, TrailingGridService>();

        // Register moon bag manager for position protection
        services.AddSingleton<IMoonBagManager, MoonBagManager>();

        // Register trailing stop service for profit protection
        services.AddSingleton<ITrailingStopService, TrailingStopService>();

        return services;
    }
}
