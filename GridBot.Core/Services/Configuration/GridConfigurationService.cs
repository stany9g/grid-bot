using System.Text.Json;
using GridBot.Core.Configuration;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Core.Services.Configuration;

/// <summary>
/// Implementation of grid configuration service with Redis persistence.
/// Thread-safe using lock for configuration updates.
/// </summary>
public sealed class GridConfigurationService : IGridConfigurationService
{
    private readonly IDistributedCache _cache;
    private readonly IOptions<SimpleGridConfig> _defaults;
    private readonly ILogger<GridConfigurationService> _logger;
    private readonly object _lock = new();

    private RuntimeGridConfig _current;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <inheritdoc />
    /// <remarks>
    /// Returns a defensive copy to prevent callers from mutating
    /// the internal configuration outside the lock.
    /// </remarks>
    public RuntimeGridConfig Current
    {
        get
        {
            lock (_lock)
            {
                return _current.Clone();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<RuntimeGridConfig>? ConfigChanged;

    public GridConfigurationService(
        IDistributedCache cache,
        IOptions<SimpleGridConfig> defaults,
        ILogger<GridConfigurationService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Initialize with defaults until LoadAsync is called
        _current = RuntimeGridConfig.FromSimpleConfig(_defaults.Value);
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _cache.GetStringAsync(RuntimeGridConfig.CacheKey, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(json))
            {
                var loaded = JsonSerializer.Deserialize<RuntimeGridConfig>(json, JsonOptions);
                if (loaded is not null)
                {
                    lock (_lock)
                    {
                        _current = loaded;
                    }
                    _logger.LogInformation("Loaded configuration from Redis");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration from Redis, using defaults");
        }

        // No cached config or failed to load - use defaults
        lock (_lock)
        {
            _current = RuntimeGridConfig.FromSimpleConfig(_defaults.Value);
        }

        // Save defaults to Redis for next time
        await SaveAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Initialized configuration with defaults and saved to Redis");
    }

    /// <inheritdoc />
    public async Task SaveAsync(CancellationToken ct = default)
    {
        RuntimeGridConfig snapshot;
        lock (_lock)
        {
            // Clone to ensure we serialize a consistent snapshot
            snapshot = _current.Clone();
        }

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await _cache.SetStringAsync(
                RuntimeGridConfig.CacheKey,
                json,
                new DistributedCacheEntryOptions
                {
                    // No expiration - config should persist indefinitely
                },
                ct).ConfigureAwait(false);

            _logger.LogDebug("Saved configuration to Redis");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration to Redis");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Action<RuntimeGridConfig> updateAction, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        RuntimeGridConfig updated;
        lock (_lock)
        {
            updateAction(_current);
            updated = _current;
        }

        // Validate after update
        var errors = updated.Validate();
        if (errors.Count > 0)
        {
            var errorMessage = string.Join("; ", errors);
            _logger.LogWarning("Configuration validation failed: {Errors}", errorMessage);
            throw new InvalidOperationException($"Invalid configuration: {errorMessage}");
        }

        await SaveAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Configuration updated successfully");
        OnConfigChanged(updated);
    }

    /// <inheritdoc />
    public async Task ResetToDefaultsAsync(CancellationToken ct = default)
    {
        RuntimeGridConfig defaults;
        lock (_lock)
        {
            defaults = RuntimeGridConfig.FromSimpleConfig(_defaults.Value);
            _current = defaults;
        }

        await SaveAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Configuration reset to defaults");
        OnConfigChanged(defaults);
    }

    /// <inheritdoc />
    public void UpdateSuggestions(decimal? spacing, int? buyLevels, int? sellLevels, decimal? orderSize)
    {
        lock (_lock)
        {
            if (spacing.HasValue)
                _current.GridSpacingPercent.SuggestedValue = spacing.Value;

            if (buyLevels.HasValue)
                _current.BuyLevels.SuggestedValue = buyLevels.Value;

            if (sellLevels.HasValue)
                _current.SellLevels.SuggestedValue = sellLevels.Value;

            if (orderSize.HasValue)
                _current.OrderSizeUsdc.SuggestedValue = orderSize.Value;
        }

        _logger.LogDebug(
            "Updated suggestions: spacing={Spacing}, buyLevels={BuyLevels}, sellLevels={SellLevels}, orderSize={OrderSize}",
            spacing, buyLevels, sellLevels, orderSize);
    }

    private void OnConfigChanged(RuntimeGridConfig config)
    {
        try
        {
            ConfigChanged?.Invoke(this, config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ConfigChanged event handler");
        }
    }
}
