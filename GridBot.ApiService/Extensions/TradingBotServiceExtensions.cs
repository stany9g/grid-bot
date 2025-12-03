using GridBot.ApiService.Configuration;
using GridBot.ApiService.Services;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering ALTE trading bot services.
/// </summary>
public static class TradingBotServiceExtensions
{
    /// <summary>
    /// Adds ALTE trading bot services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddTradingBot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register configuration
        services.Configure<TradingBotOptions>(
            configuration.GetSection(TradingBotOptions.SectionName));

        // Register risk configuration service
        services.AddSingleton<IRiskConfiguration, RiskConfiguration>();

        // Register market data services (indicators, analyzers, metrics)
        services.AddMarketDataServices();

        // Register grid trading services
        services.AddGridServices();

        // Register trend intelligence services (trend detection, inventory management, rebalancing)
        services.AddTrendServices();

        // Register risk monitoring services (loss monitor, flash crash detector, liquidity monitor, sentinel)
        services.AddRiskServices();

        // Register moon bag protection services (trailing grid, moon bag manager, trailing stop)
        services.AddMoonBagServices();

        // Register decision engine services (recovery manager, decision engine)
        services.AddDecisionEngine();

        // Register state management service
        services.AddSingleton<ITradingStateService, TradingStateService>();

        // Register hosted service (singleton so health check can access it)
        services.AddSingleton<TradingBotHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<TradingBotHostedService>());

        // Register health check
        services.AddHealthChecks()
            .AddCheck<TradingBotHealthCheck>("trading-bot", tags: ["ready"]);

        return services;
    }
}
