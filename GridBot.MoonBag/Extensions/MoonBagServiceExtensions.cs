using GridBot.MoonBag.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.MoonBag.Extensions;

/// <summary>
/// Extension methods for registering MoonBag services.
/// </summary>
public static class MoonBagServiceExtensions
{
    /// <summary>
    /// Adds MoonBag services to the service collection.
    /// Requires the following services to be registered by the consuming application:
    /// - IMoonBagConfiguration
    /// - IMoonBagMarketDataProvider
    /// - IMoonBagTrendProvider
    /// - IMoonBagStateRepository
    /// - IMoonBagEventLogger
    /// - IMoonBagGridProvider
    /// - IMoonBagOrderExecutor
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMoonBag(this IServiceCollection services)
    {
        // Register flash spike detector (has state, singleton)
        services.AddSingleton<IFlashSpikeDetector, FlashSpikeDetector>();

        // Register moon bag manager (has state, singleton)
        services.AddSingleton<IMoonBagManager, MoonBagManager>();

        // Register trailing stop service (has state, singleton)
        services.AddSingleton<ITrailingStopService, TrailingStopService>();

        // Register trailing grid service (has state, singleton)
        services.AddSingleton<ITrailingGridService, TrailingGridService>();

        return services;
    }
}
