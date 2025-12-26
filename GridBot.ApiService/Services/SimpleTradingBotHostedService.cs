using GridBot.ApiService.Services.Bot;
using GridBot.Core.Configuration;
using GridBot.Core.Services.Engine;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services;

/// <summary>
/// Background service that runs the SimpleTradingEngine from GridBot.Core.
/// Does NOT auto-start - waits for IGridBotControlService to signal start.
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
            "SimpleTradingBotHostedService ready for {Market} with {LoopInterval}s interval (waiting for start command)",
            _config.Market,
            _config.LoopIntervalSeconds);

        var loopInterval = TimeSpan.FromSeconds(_config.LoopIntervalSeconds);

        try
        {
            // Main loop - runs trading cycles when engine is running
            while (!stoppingToken.IsCancellationRequested)
            {
                // Only run cycles when engine is running
                if (_engine.IsRunning)
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
                else
                {
                    // Wait a bit before checking again if engine is not running
                    await Task.Delay(TimeSpan.FromMilliseconds(500), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            // Ensure engine is stopped on shutdown
            if (_engine.IsRunning)
            {
                try
                {
                    await _engine.StopAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping trading engine");
                }
            }

            _logger.LogInformation("SimpleTradingBotHostedService stopped");
        }
    }
}
