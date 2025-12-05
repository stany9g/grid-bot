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
        // NEVER HALT: All states are healthy or degraded, never unhealthy due to state alone
        return state switch
        {
            TradingState.Active => Task.FromResult(HealthCheckResult.Healthy(
                "Trading bot active at full capacity",
                data)),

            TradingState.Degraded_Bootstrap => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot in bootstrap mode - building initial position",
                data: data)),

            TradingState.Degraded_SkewCorrection => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot correcting inventory skew",
                data: data)),

            TradingState.Degraded_HighVolatility => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot in high volatility mode - reduced capacity",
                data: data)),

            TradingState.Degraded_LowLiquidity => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot in low liquidity mode - wider spreads",
                data: data)),

            TradingState.Degraded_ProtectiveMode => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot in protective mode - minimal capacity",
                data: data)),

            TradingState.Recovering => Task.FromResult(HealthCheckResult.Degraded(
                "Trading bot recovering - gradually increasing capacity",
                data: data)),

            _ => Task.FromResult(HealthCheckResult.Degraded(
                $"Trading bot in state: {state}",
                data: data))
        };
    }
}
