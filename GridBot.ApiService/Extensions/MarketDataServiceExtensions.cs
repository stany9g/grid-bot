using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Metrics;
using GridBot.ApiService.Services.OrderBook;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering market data services.
/// </summary>
public static class MarketDataServiceExtensions
{
    /// <summary>
    /// Adds market data services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddMarketDataServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register market data service (depends on ILighterQueryClient)
        services.AddSingleton<IMarketDataService, MarketDataService>();

        // Register indicator calculation service (stateless)
        services.AddSingleton<IIndicatorService, IndicatorService>();

        // Register order book analyzer (stateless)
        services.AddSingleton<IOrderBookAnalyzer, OrderBookAnalyzer>();

        // Register market metrics aggregation service (with caching)
        services.AddSingleton<IMarketMetricsService, MarketMetricsService>();

        return services;
    }
}
