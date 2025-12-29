using GridBot.Core.Configuration;
using GridBot.Core.Services.Configuration;
using GridBot.Core.Services.Engine;
using GridBot.Core.Services.Grid;
using GridBot.Core.Services.Risk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.Core.Extensions;

/// <summary>
/// Extension methods for registering GridBot.Core services.
/// </summary>
public static class CoreServiceExtensions
{
    /// <summary>
    /// Adds GridBot.Core services to the service collection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method registers the core trading engine services. Before calling this method,
    /// the consuming application must register the following abstraction interfaces from
    /// GridBot.Abstractions:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>IOrderClient</c> - For order management (create, cancel, modify)</description></item>
    ///   <item><description><c>IAccountClient</c> - For account and position queries</description></item>
    ///   <item><description><c>IMarketDataClient</c> - For market data (prices, orderbook)</description></item>
    ///   <item><description><c>IScalingProvider</c> - For price/amount scaling</description></item>
    /// </list>
    /// <para>
    /// For Lighter DEX, use <c>AddLighterExchange()</c> from GridBot.Lighter.
    /// For other exchanges, register the appropriate adapter implementations.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // Register exchange adapters first
    /// services.AddLighterExchange(configuration);
    ///
    /// // Then register core services
    /// services.AddGridBotCore(configuration);
    /// </code>
    /// </example>
    public static IServiceCollection AddGridBotCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind configuration
        services.Configure<SimpleGridConfig>(
            configuration.GetSection(SimpleGridConfig.SectionName));

        // Register configuration service (singleton for runtime config management)
        services.AddSingleton<IGridConfigurationService, GridConfigurationService>();

        // Register stateless calculator as singleton
        services.AddSingleton<IGridCalculator, GridCalculator>();

        // Register trading services as singletons that maintain state
        // These use IServiceScopeFactory internally to resolve scoped exchange client services,
        // supporting dynamic network switching while preserving trading state
        services.AddSingleton<IBasicRiskMonitor, BasicRiskMonitor>();
        services.AddSingleton<IGridManager, GridManager>();
        services.AddSingleton<ISimpleTradingEngine, SimpleTradingEngine>();

        return services;
    }

    /// <summary>
    /// Validates the GridBot configuration on startup.
    /// Call this after building the service provider to fail fast on misconfiguration.
    /// </summary>
    public static void ValidateGridBotConfiguration(this IServiceProvider services)
    {
        var config = services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SimpleGridConfig>>().Value;
        var error = config.Validate();
        if (error != null)
        {
            throw new InvalidOperationException(
                $"Invalid GridBot configuration: {error}");
        }
    }
}
