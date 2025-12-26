namespace GridBot.ApiService.Services.Bot;

/// <summary>
/// Represents the current operational status of the trading bot.
/// </summary>
public enum BotStatus
{
    /// <summary>
    /// Bot is not running and idle.
    /// </summary>
    Stopped,

    /// <summary>
    /// Bot is in the process of starting up.
    /// </summary>
    Starting,

    /// <summary>
    /// Bot is actively running and trading.
    /// </summary>
    Running,

    /// <summary>
    /// Bot is in the process of pausing.
    /// </summary>
    Pausing,

    /// <summary>
    /// Bot is temporarily paused but can resume.
    /// </summary>
    Paused,

    /// <summary>
    /// Bot is in the process of resuming.
    /// </summary>
    Resuming,

    /// <summary>
    /// Bot is in the process of stopping.
    /// </summary>
    Stopping,

    /// <summary>
    /// Bot encountered an error and stopped.
    /// </summary>
    Error
}
