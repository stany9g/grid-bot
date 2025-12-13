using GridBot.ApiService.Services.Risk;
using Microsoft.Extensions.DependencyInjection;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering risk monitoring services.
/// </summary>
public static class RiskServiceExtensions
{
    /// <summary>
    /// Adds risk monitoring services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRiskServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register event logger first (dependency of other services)
        services.AddSingleton<IRiskEventLogger, RiskEventLogger>();

        // Register individual monitors
        services.AddSingleton<ILossMonitor, LossMonitor>();
        services.AddSingleton<IFlashCrashDetector, FlashCrashDetector>();
        services.AddSingleton<IFlashPumpDetector, FlashPumpDetector>();
        services.AddSingleton<ILiquidityMonitor, LiquidityMonitor>();

        // Register orchestrator
        services.AddSingleton<IRiskSentinel, RiskSentinel>();

        return services;
    }
}
