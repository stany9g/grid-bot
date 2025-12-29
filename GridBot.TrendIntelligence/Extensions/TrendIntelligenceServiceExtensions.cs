using GridBot.TrendIntelligence.Services.Indicators;
using GridBot.TrendIntelligence.Services.Trend;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.TrendIntelligence.Extensions;

/// <summary>
/// Extension methods for registering TrendIntelligence services.
/// </summary>
public static class TrendIntelligenceServiceExtensions
{
    /// <summary>
    /// Adds only the indicator service (stateless) to the service collection.
    /// Use this when you only need ATR/EMA calculations without trend detection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddIndicatorService(this IServiceCollection services)
    {
        // Register indicator service (stateless, singleton)
        services.AddSingleton<IIndicatorService, IndicatorService>();

        return services;
    }

    /// <summary>
    /// Adds full TrendIntelligence services to the service collection.
    /// Requires the following services to be registered by the consuming application:
    /// - IMarketDataProvider
    /// - ITrendStateProvider
    /// - ITrendConfiguration
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTrendIntelligence(this IServiceCollection services)
    {
        // Register indicator service (stateless, singleton)
        services.AddIndicatorService();

        // Register trend detector (has state for confirmations, singleton for persistence)
        services.AddSingleton<ITrendDetector, TrendDetector>();

        return services;
    }
}
