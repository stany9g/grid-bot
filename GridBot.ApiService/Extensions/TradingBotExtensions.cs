using GridBot.ApiService.Services;
using GridBot.ApiService.Services.Adaptive;
using GridBot.ApiService.Services.Bot;
using GridBot.ApiService.Services.Dashboard;
using GridBot.ApiService.Services.Exchange;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Network;
using GridBot.ApiService.Services.Telemetry;
using GridBot.Core.Extensions;
using GridBot.Core.Services.Adaptive;
using GridBot.TrendIntelligence.Extensions;
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

        // Only register IIndicatorService (used by AdaptiveParameterService)
        // Full TrendIntelligence (ITrendDetector) requires additional dependencies not yet implemented
        services.AddIndicatorService();

        // Adaptive parameter service for auto-tuning suggestions
        services.AddSingleton<IAdaptiveParameterService, AdaptiveParameterService>();

        // Market data services
        services.AddSingleton<IMarketResolver, MarketResolver>();
        services.AddSingleton<IMarketScalingService, MarketScalingService>();
        services.AddSingleton<IMarketDataService, MarketDataService>();

        // Dashboard services
        services.AddSingleton<DashboardStateService>();
        services.AddSingleton<IDashboardStateService>(sp => sp.GetRequiredService<DashboardStateService>());
        services.AddHostedService(sp => sp.GetRequiredService<DashboardStateService>());

        // Bot control service (must be registered before exchange selection service)
        services.AddSingleton<GridBotControlService>();
        services.AddSingleton<IGridBotControlService>(sp => sp.GetRequiredService<GridBotControlService>());

        // Exchange selection service
        services.AddSingleton<IExchangeSelectionService, ExchangeSelectionService>();

        // Network selection service (testnet/mainnet)
        services.AddSingleton<INetworkSelectionService, NetworkSelectionService>();

        // Network initialization service (initializes default network on startup)
        // This must run before other hosted services that need exchange client
        services.AddHostedService<NetworkInitializationService>();

        // Trading bot hosted service (no longer auto-starts, controlled by IGridBotControlService)
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
