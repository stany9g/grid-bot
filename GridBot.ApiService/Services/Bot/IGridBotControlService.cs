using GridBot.Abstractions.Factory;

namespace GridBot.ApiService.Services.Bot;

/// <summary>
/// Service for controlling the grid trading bot lifecycle.
/// Provides start, stop, pause, and resume functionality with status tracking.
/// </summary>
public interface IGridBotControlService
{
    /// <summary>
    /// Gets the current bot status.
    /// </summary>
    BotStatus Status { get; }

    /// <summary>
    /// Gets whether the bot is currently running (includes Running and Paused states).
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets the identifier of the currently active exchange client.
    /// </summary>
    string? CurrentExchangeId { get; }

    /// <summary>
    /// Gets the type of the currently active exchange.
    /// </summary>
    ExchangeType? CurrentExchangeType { get; }

    /// <summary>
    /// Gets the last error message if the bot is in Error state.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Event raised when the bot status changes.
    /// </summary>
    event EventHandler<BotStatusChangedEventArgs>? StatusChanged;

    /// <summary>
    /// Starts the trading bot.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when bot is already running or starting.</exception>
    Task StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops the trading bot and cancels all open orders.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when bot is not running.</exception>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Pauses the trading bot temporarily. Grid state is preserved.
    /// </summary>
    /// <param name="reason">The reason for pausing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when bot is not in Running state.</exception>
    Task PauseAsync(string reason, CancellationToken ct = default);

    /// <summary>
    /// Resumes a paused trading bot.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown when bot is not in Paused state.</exception>
    Task ResumeAsync(CancellationToken ct = default);
}
