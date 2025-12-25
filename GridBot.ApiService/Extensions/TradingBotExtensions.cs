using GridBot.ApiService.Services;
using GridBot.ApiService.Services.Dashboard;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Telemetry;
using GridBot.Core.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Extension methods for registering ALTE trading bot services.
/// Simplified for simple mode only - legacy full mode has been removed.
/// </summary>
public static class TradingBotExtensions
{
    /// <summary>
    /// Adds ALTE trading bot services in simple mode.
    /// Uses GridBot.Core for trading logic.
    /// </summary>
    public static IServiceCollection AddTradingBot(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Core trading engine from GridBot.Core
        services.AddGridBotCore(configuration);

        // Market data services
        services.AddSingleton<IMarketResolver, MarketResolver>();
        services.AddSingleton<IMarketScalingService, MarketScalingService>();
        services.AddSingleton<IMarketDataService, MarketDataService>();

        // Dashboard services
        services.AddSingleton<DashboardStateService>();
        services.AddSingleton<IDashboardStateService>(sp => sp.GetRequiredService<DashboardStateService>());
        services.AddHostedService(sp => sp.GetRequiredService<DashboardStateService>());

        // Trading bot hosted service
        services.AddSingleton<SimpleTradingBotHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<SimpleTradingBotHostedService>());

        // Telemetry
        services.ConfigureOpenTelemetryMeterProvider(builder =>
        {
            builder.AddMeter(TradingMetrics.MeterName);
        });

        // Health check
        services.AddHealthChecks()
            .AddCheck<SimpleTradingBotHealthCheck>("trading-bot", tags: ["ready"]);

        return services;
    }
}
