using GridBot.AdvancedRisk.Services.Notifications;
using GridBot.AdvancedRisk.Services.Recovery;
using GridBot.AdvancedRisk.Services.Risk;
using Microsoft.Extensions.DependencyInjection;
namespace GridBot.AdvancedRisk.Extensions;
/// <summary>
/// Extension methods for registering AdvancedRisk services.
/// </summary>
public static class AdvancedRiskServiceExtensions
{
    /// <summary>
    /// Adds AdvancedRisk services to the service collection.
    /// Requires the following services to be registered by the consuming application:
    /// - IAdvancedRiskConfiguration
    /// - IAdvancedRiskMarketDataProvider
    /// - IAdvancedRiskStateProvider
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAdvancedRisk(this IServiceCollection services)
    {
        // Register webhook notifier with HttpClient
        services.AddHttpClient<IWebhookNotifier, WebhookNotifier>();
        // Register risk event logger (depends on webhook notifier)
        services.AddSingleton<IRiskEventLogger, RiskEventLogger>();
        // Register flash pump detector (has state, singleton)
        services.AddSingleton<IFlashPumpDetector, FlashPumpDetector>();
        // Register liquidity monitor (has cached state, singleton)
        services.AddSingleton<ILiquidityMonitor, LiquidityMonitor>();
        // Register recovery manager (has state, singleton)
        services.AddSingleton<IRecoveryManager, RecoveryManager>();
        return services;
    }
}
