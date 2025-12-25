using GridBot.Core.Configuration;

namespace GridBot.Core.Services.Configuration;

/// <summary>
/// Service for managing runtime grid configuration.
/// Provides persistence to Redis and change notification.
/// Thread-safe for concurrent access.
/// </summary>
public interface IGridConfigurationService
{
    /// <summary>
    /// Gets the current runtime configuration.
    /// Always returns a valid configuration (never null).
    /// </summary>
    RuntimeGridConfig Current { get; }

    /// <summary>
    /// Fired when configuration changes.
    /// Subscribers should use this to react to config updates.
    /// </summary>
    event EventHandler<RuntimeGridConfig>? ConfigChanged;

    /// <summary>
    /// Loads configuration from Redis.
    /// If not found, loads defaults from SimpleGridConfig and saves to Redis.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Saves current configuration to Redis.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates configuration with the provided action.
    /// Automatically saves to Redis and fires ConfigChanged.
    /// </summary>
    /// <param name="updateAction">Action to modify configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(Action<RuntimeGridConfig> updateAction, CancellationToken ct = default);

    /// <summary>
    /// Resets configuration to defaults from SimpleGridConfig.
    /// Saves to Redis and fires ConfigChanged.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task ResetToDefaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// Updates auto-tuned suggestion values.
    /// Called by AdaptiveParameterService.
    /// Does not trigger ConfigChanged (suggestions are informational).
    /// </summary>
    /// <param name="spacing">Suggested grid spacing percent.</param>
    /// <param name="buyLevels">Suggested buy levels.</param>
    /// <param name="sellLevels">Suggested sell levels.</param>
    /// <param name="orderSize">Suggested order size in USDC.</param>
    void UpdateSuggestions(decimal? spacing, int? buyLevels, int? sellLevels, decimal? orderSize);
}
