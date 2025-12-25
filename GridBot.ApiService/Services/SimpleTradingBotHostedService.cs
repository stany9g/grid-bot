using GridBot.Core.Configuration;
using GridBot.Core.Services.Engine;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services;

/// <summary>
/// Background service that runs the SimpleTradingEngine from GridBot.Core.
/// </summary>
public sealed class SimpleTradingBotHostedService : BackgroundService
{
    private readonly ISimpleTradingEngine _engine;
    private readonly SimpleGridConfig _config;
    private readonly ILogger<SimpleTradingBotHostedService> _logger;

    /// <summary>
    /// Gets the last time a trading cycle was executed.
    /// </summary>
    public DateTimeOffset LastCycleTime { get; private set; } = DateTimeOffset.MinValue;

    public SimpleTradingBotHostedService(
        ISimpleTradingEngine engine,
        IOptions<SimpleGridConfig> config,
        ILogger<SimpleTradingBotHostedService> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SimpleTradingBotHostedService starting for {Market} with {LoopInterval}s interval",
            _config.Market,
            _config.LoopIntervalSeconds);

        try
        {
            await _engine.StartAsync(stoppingToken);

            var loopInterval = TimeSpan.FromSeconds(_config.LoopIntervalSeconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _engine.RunCycleAsync(stoppingToken);
                    LastCycleTime = DateTimeOffset.UtcNow;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in trading cycle");
                }

                await Task.Delay(loopInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            try
            {
                await _engine.StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping trading engine");
            }

            _logger.LogInformation("SimpleTradingBotHostedService stopped");
        }
    }
}
