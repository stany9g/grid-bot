using GridBot.Core.Models;

namespace GridBot.Core.Services.Engine;

/// <summary>
/// Simple trading engine interface.
/// </summary>
public interface ISimpleTradingEngine
{
    /// <summary>
    /// Gets the current grid state.
    /// </summary>
    GridState State { get; }

    /// <summary>
    /// Gets whether the engine is running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Runs a single trading cycle.
    /// </summary>
    Task RunCycleAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the trading engine.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the trading engine.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually pauses trading.
    /// </summary>
    Task PauseAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually resumes trading.
    /// </summary>
    Task ResumeAsync(CancellationToken cancellationToken = default);
}