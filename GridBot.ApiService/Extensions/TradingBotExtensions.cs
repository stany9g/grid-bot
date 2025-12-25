using GridBot.AdvancedRisk.Services;
using GridBot.ApiService.Adapters;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Services;
using GridBot.ApiService.Services.Capacity;
using GridBot.ApiService.Services.Connectivity;
using GridBot.ApiService.Services.Dashboard;
using GridBot.ApiService.Services.DecisionEngine;
using GridBot.ApiService.Services.Grid;
using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.Inventory;
using GridBot.ApiService.Services.Logging;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.Metrics;
using GridBot.ApiService.Services.MoonBag;
using GridBot.ApiService.Services.Notifications;
using GridBot.ApiService.Services.OrderBook;
using GridBot.ApiService.Services.Persistence;
using GridBot.ApiService.Services.Rebalancing;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using GridBot.ApiService.Services.Telemetry;
using GridBot.ApiService.Services.Trend;
using GridBot.ApiService.Services.Validation;
using GridBot.Core.Extensions;
using GridBot.TrendIntelligence.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

namespace GridBot.ApiService.Extensions;

/// <summary>
/// Consolidated extension methods for ALTE trading bot services.
/// Replaces the previous 12 separate extension files.
/// </summary>
public static class TradingBotExtensions
{
    /// <summary>
    /// Adds ALTE trading bot services with optional modules.
    /// </summary>
    public static IServiceCollection AddTradingBot(
        this IServiceCollection services,
        IConfiguration configuration,
        bool useSimpleMode = false)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<TradingBotOptions>(
            configuration.GetSection(TradingBotOptions.SectionName));

        services.AddSingleton<IRiskConfiguration, RiskConfiguration>();

        if (useSimpleMode)
        {
            services.AddSimpleModeServices(configuration);
        }
        else
        {
            services.AddFullModeServices(configuration);
        }

        return services;
    }

    private static void AddSimpleModeServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddGridBotCore(configuration);
        services.AddSingleton<IStateRepository, RedisStateRepository>();
        services.AddSingleton<IMarketResolver, MarketResolver>();
        services.AddSingleton<IMarketScalingService, MarketScalingService>();
        services.AddSingleton<IMarketDataService, MarketDataService>();

        services.ConfigureOpenTelemetryMeterProvider(builder =>
        {
            builder.AddMeter(TradingMetrics.MeterName);
        });

        services.AddHealthChecks()
            .AddCheck<TradingBotHealthCheck>("trading-bot", tags: ["ready"]);
    }

    private static void AddFullModeServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // GridBot.Core is available but not used as primary engine in full mode
        services.AddGridBotCore(configuration);

        services.ConfigureOpenTelemetryMeterProvider(builder =>
        {
            builder.AddMeter(TradingMetrics.MeterName);
        });

        // Persistence
        services.AddSingleton<IStateRepository, RedisStateRepository>();

        // Connectivity
        services.AddSingleton<IWebSocketHealthMonitor, WebSocketHealthMonitor>();
        services.AddSingleton<INonceHealthMonitor, NonceHealthMonitor>();

        // Logging
        services.AddSingleton<IDecisionCycleLogService, DecisionCycleLogService>();

        // Validation
        services.AddSingleton<IPreTradeValidator, PreTradeValidator>();

        // Market data
        services.AddSingleton<IMarketResolver, MarketResolver>();
        services.AddSingleton<IMarketScalingService, MarketScalingService>();
        services.AddSingleton<IMarketDataService, MarketDataService>();
        services.AddSingleton<IIndicatorService, IndicatorService>();
        services.AddSingleton<IOrderBookAnalyzer, OrderBookAnalyzer>();
        services.AddSingleton<IMarketMetricsService, MarketMetricsService>();

        // Grid services (legacy ApiService implementations)
        services.AddSingleton<Services.Grid.IGridCalculator, Services.Grid.GridCalculator>();
        services.AddSingleton<IGridOrderManager, GridOrderManager>();
        services.AddSingleton<IGridLifecycleService, GridLifecycleService>();

        // Trend services (legacy ApiService implementations)
        services.AddSingleton<ITrendDetector, TrendDetector>();
        services.AddSingleton<InventoryManager>();
        services.AddSingleton<IInventoryManager>(sp => sp.GetRequiredService<InventoryManager>());
        services.AddSingleton<IRebalancingService, RebalancingService>();
        services.AddSingleton<ITrendIntelligenceService, TrendIntelligenceService>();

        // TrendIntelligence module adapters (bridge to new module interfaces)
        services.AddSingleton<IMarketDataProvider, TrendMarketDataAdapter>();
        services.AddSingleton<ITrendConfiguration, TrendConfigurationAdapter>();

        // Risk services (legacy ApiService implementations)
        services.AddHttpClient<IWebhookNotifier, WebhookNotifier>();
        services.AddSingleton<IRiskEventLogger, RiskEventLogger>();
        services.AddSingleton<ILossMonitor, LossMonitor>();
        services.AddSingleton<IFlashCrashDetector, FlashCrashDetector>();
        services.AddSingleton<IFlashPumpDetector, FlashPumpDetector>();
        services.AddSingleton<ILiquidityMonitor, LiquidityMonitor>();
        services.AddSingleton<IRiskSentinel, RiskSentinel>();

        // AdvancedRisk module adapters
        services.AddSingleton<IAdvancedRiskMarketDataProvider, AdvancedRiskMarketDataAdapter>();

        // MoonBag services (legacy ApiService implementations)
        services.AddSingleton<IFlashSpikeDetector, FlashSpikeDetector>();
        services.AddSingleton<ITrailingGridService, TrailingGridService>();
        services.AddSingleton<IMoonBagManager, MoonBagManager>();
        services.AddSingleton<ITrailingStopService, TrailingStopService>();

        // MoonBag module adapters (fully qualified to avoid ambiguity)
        services.AddSingleton<GridBot.MoonBag.Services.IMoonBagConfiguration, MoonBagConfigurationAdapter>();
        services.AddSingleton<GridBot.MoonBag.Services.IMoonBagMarketDataProvider, MoonBagMarketDataAdapter>();

        // Decision engine
        services.AddSingleton<IOperationalCapacityService, OperationalCapacityService>();
        services.AddSingleton<IRecoveryManager, RecoveryManager>();
        services.AddSingleton<ITradingDecisionEngine, TradingDecisionEngine>();

        // State management
        services.AddSingleton<ITradingStateService, TradingStateService>();

        // Hosted service
        services.AddSingleton<TradingBotHostedService>();
        services.AddHostedService(sp => sp.GetRequiredService<TradingBotHostedService>());

        // Dashboard
        services.AddSingleton<DashboardStateService>();
        services.AddSingleton<IDashboardStateService>(sp => sp.GetRequiredService<DashboardStateService>());

        // Health check
        services.AddHealthChecks()
            .AddCheck<TradingBotHealthCheck>("trading-bot", tags: ["ready"]);
    }
}
