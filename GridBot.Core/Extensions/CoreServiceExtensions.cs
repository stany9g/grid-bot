using GridBot.Core.Configuration;
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
    /// Requires GridBot.Lighter to be registered first via AddLighterClient().
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGridBotCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind configuration
        services.Configure<SimpleGridConfig>(
            configuration.GetSection(SimpleGridConfig.SectionName));

        // Register services
        services.AddSingleton<IGridCalculator, GridCalculator>();
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