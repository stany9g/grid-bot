using GridBot.Core.Configuration;
using GridBot.Core.Services.Engine;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services;

/// <summary>
/// Health check for the simple trading bot.
/// Reports health based on trading engine state and decision loop status.
/// </summary>
public sealed class SimpleTradingBotHealthCheck : IHealthCheck
{
    private readonly ISimpleTradingEngine _engine;
    private readonly SimpleTradingBotHostedService _hostedService;
    private readonly SimpleGridConfig _config;

    public SimpleTradingBotHealthCheck(
        ISimpleTradingEngine engine,
        SimpleTradingBotHostedService hostedService,
        IOptions<SimpleGridConfig> config)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(hostedService);
        ArgumentNullException.ThrowIfNull(config);

        _engine = engine;
        _hostedService = hostedService;
        _config = config.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var lastLoop = _hostedService.LastCycleTime;
        var loopInterval = TimeSpan.FromSeconds(_config.LoopIntervalSeconds);
        var state = _engine.State;

        var data = new Dictionary<string, object>
        {
            ["TradingState"] = state.State.ToString(),
            ["IsRunning"] = _engine.IsRunning,
            ["LastCycleTime"] = lastLoop.ToString("O"),
            ["Market"] = _config.Market,
            ["MarketIndex"] = _config.MarketIndex
        };

        // Check if decision loop is running (with 3x tolerance for interval)
        var maxLoopAge = loopInterval * 3;
        var loopAge = DateTimeOffset.UtcNow - lastLoop;
        var loopStale = lastLoop != DateTimeOffset.MinValue && loopAge > maxLoopAge;

        if (loopStale)
        {
            data["LoopAge"] = loopAge.ToString();
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Trading loop stale. Last execution: {loopAge.TotalSeconds:F1}s ago",
                data: data));
        }

        if (!_engine.IsRunning)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Trading engine is not running",
                data: data));
        }

        // Report based on trading state
        return state.State switch
        {
            GridBot.Core.Models.TradingState.Active => Task.FromResult(HealthCheckResult.Healthy(
                "Trading bot active",
                data)),

            GridBot.Core.Models.TradingState.Paused => Task.FromResult(HealthCheckResult.Degraded(
                $"Trading bot paused: {state.PauseReason}",
                data: data)),

            _ => Task.FromResult(HealthCheckResult.Healthy(
                $"Trading bot in state: {state.State}",
                data))
        };
    }
}
