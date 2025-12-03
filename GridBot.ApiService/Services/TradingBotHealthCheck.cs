using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GridBot.ApiService.Services;

/// <summary>
/// Health check for the ALTE trading bot.
/// Reports health based on trading state and decision loop status.
/// </summary>
public sealed class TradingBotHealthCheck : IHealthCheck
{
    private readonly ITradingStateService _stateService;
    private readonly TradingBotHostedService _hostedService;
    private readonly IRiskConfiguration _riskConfig;

    /// <summary>
    /// Creates a new TradingBotHealthCheck instance.
    /// </summary>
    public TradingBotHealthCheck(
        ITradingStateService stateService,
        TradingBotHostedService hostedService,
        IRiskConfiguration riskConfig)
    {
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(hostedService);
        ArgumentNullException.ThrowIfNull(riskConfig);

        _stateService = stateService;
        _hostedService = hostedService;
        _riskConfig = riskConfig;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var state = _stateService.CurrentState;
        var trend = _stateService.CurrentTrendState;
        var lastLoop = _hostedService.LastDecisionLoopTime;
        var loopInterval = TimeSpan.FromMilliseconds(_riskConfig.DecisionLoopIntervalMs);

        var data = new Dictionary<string, object>
        {
            ["TradingState"] = state.ToString(),
            ["TrendState"] = trend.ToString(),
            ["LastDecisionLoop"] = lastLoop.ToString("O"),
            ["StateStartedAt"] = _stateService.StateStartedAt.ToString("O"),
            ["MarketId"] = _riskConfig.MarketId
        };

        // Check if decision loop is running (with 3x tolerance for interval)
        var maxLoopAge = loopInterval * 3;
        var loopAge = DateTimeOffset.UtcNow - lastLoop;
        var loopStale = lastLoop != DateTimeOffset.MinValue && loopAge > maxLoopAge;

        if (loopStale)
        {
            data["LoopAge"] = loopAge.ToString();
            return Task.FromResult(HealthCheckResult.Unhealthy(
                $"Decision loop stale. Last execution: {loopAge.TotalSeconds:F1}s ago",
                data: data));
        }

        // Report health based on trading state
        return state switch
        {
            TradingState.Active => Task.FromResult(HealthCheckResult.Healthy(
                "Trading bot active and running",
                data)),

            TradingState.Paused => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot paused",
                data: data)),

            TradingState.Halted => Task.FromResult(HealthCheckResult.Unhealthy(
                "Trading bot halted due to risk trigger",
                data: data)),

            TradingState.Recovering => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot recovering from halt",
                data: data)),

            _ => Task.FromResult(HealthCheckResult.Unhealthy(
                $"Unknown trading state: {state}",
                data: data))
        };
    }
}
