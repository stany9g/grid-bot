using System.Text.Json;
using GridBot.ApiService.Models.Trading;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Persistence;

/// <summary>
/// Redis implementation of IStateRepository using IDistributedCache.
/// Provides persistent storage for critical trading state across restarts.
/// Thread-safe through IDistributedCache's built-in thread safety.
/// </summary>
public sealed class RedisStateRepository : IStateRepository
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisStateRepository> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Creates a new RedisStateRepository instance.
    /// </summary>
    public RedisStateRepository(
        IDistributedCache cache,
        ILogger<RedisStateRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveRecoveryStateAsync(int marketId, RecoveryState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var key = StateKeys.Recovery(marketId);
            var json = JsonSerializer.Serialize(state, JsonOptions);

            await _cache.SetStringAsync(key, json, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved recovery state for market {MarketId}: Phase={Phase}",
                marketId, state.CurrentPhase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save recovery state for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<RecoveryState?> LoadRecoveryStateAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.Recovery(marketId);
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return null;

            var state = JsonSerializer.Deserialize<RecoveryState>(json, JsonOptions);

            _logger.LogDebug(
                "Loaded recovery state for market {MarketId}: Phase={Phase}",
                marketId, state?.CurrentPhase);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recovery state for market {MarketId}", marketId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteRecoveryStateAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.Recovery(marketId);
            await _cache.RemoveAsync(key, ct).ConfigureAwait(false);

            _logger.LogDebug("Deleted recovery state for market {MarketId}", marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete recovery state for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SaveMoonBagStatusAsync(int marketId, MoonBagStatus status, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        try
        {
            var key = StateKeys.MoonBag(marketId);
            var json = JsonSerializer.Serialize(status, JsonOptions);

            await _cache.SetStringAsync(key, json, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved moon bag status for market {MarketId}: State={State}",
                marketId, status.State);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save moon bag status for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<MoonBagStatus?> LoadMoonBagStatusAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.MoonBag(marketId);
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return null;

            var status = JsonSerializer.Deserialize<MoonBagStatus>(json, JsonOptions);

            _logger.LogDebug(
                "Loaded moon bag status for market {MarketId}: State={State}",
                marketId, status?.State);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load moon bag status for market {MarketId}", marketId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveTradingStateAsync(TradingState state, TrendState trendState, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.TradingState();
            var persisted = PersistedTradingState.Create(state, trendState);
            var json = JsonSerializer.Serialize(persisted, JsonOptions);

            await _cache.SetStringAsync(key, json, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved trading state: State={State}, Trend={Trend}",
                state, trendState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save trading state");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<(TradingState?, TrendState?)> LoadTradingStateAsync(CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.TradingState();
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return (null, null);

            var persisted = JsonSerializer.Deserialize<PersistedTradingState>(json, JsonOptions);
            if (persisted is null)
                return (null, null);

            var result = persisted.ToDomain();

            _logger.LogDebug(
                "Loaded trading state: State={State}, Trend={Trend}",
                result.Item1, result.Item2);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trading state");
            return (null, null);
        }
    }

    /// <inheritdoc />
    public async Task SaveLossStatusAsync(int marketId, LossStatus status, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(status);

        try
        {
            var key = StateKeys.LossStatus(marketId);
            var json = JsonSerializer.Serialize(status, JsonOptions);

            await _cache.SetStringAsync(key, json, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved loss status for market {MarketId}: Daily={Daily:P2}",
                marketId, status.DailyPnlPercent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save loss status for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<LossStatus?> LoadLossStatusAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.LossStatus(marketId);
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return null;

            var status = JsonSerializer.Deserialize<LossStatus>(json, JsonOptions);

            _logger.LogDebug(
                "Loaded loss status for market {MarketId}: Daily={Daily:P2}",
                marketId, status?.DailyPnlPercent);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load loss status for market {MarketId}", marketId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RecordCircuitBreakerEventAsync(int marketId, string triggerType, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerType);

        try
        {
            var key = StateKeys.CircuitBreaker(marketId);

            // Create event record with timestamp
            var eventRecord = new CircuitBreakerEvent
            {
                TriggerType = triggerType,
                Timestamp = DateTimeOffset.UtcNow
            };

            // Get existing events or create new list
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);
            var events = string.IsNullOrEmpty(json)
                ? []
                : JsonSerializer.Deserialize<List<CircuitBreakerEvent>>(json, JsonOptions) ?? [];

            // Add new event
            events.Add(eventRecord);

            // Remove events older than 24 hours
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            events.RemoveAll(e => e.Timestamp < cutoff);

            // Save back with 24-hour expiration
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
            };

            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(events, JsonOptions),
                options,
                ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Recorded circuit breaker event for market {MarketId}: Type={TriggerType}, Count24h={Count}",
                marketId, triggerType, events.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record circuit breaker event for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<int> GetCircuitBreakerCount24hAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.CircuitBreaker(marketId);
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return 0;

            var events = JsonSerializer.Deserialize<List<CircuitBreakerEvent>>(json, JsonOptions);
            if (events is null)
                return 0;

            // Count events within last 24 hours
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            var count = events.Count(e => e.Timestamp >= cutoff);

            _logger.LogDebug(
                "Circuit breaker count for market {MarketId}: {Count} in last 24h",
                marketId, count);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get circuit breaker count for market {MarketId}", marketId);
            return 0;
        }
    }

    /// <summary>
    /// Internal record for circuit breaker event storage.
    /// </summary>
    private sealed record CircuitBreakerEvent
    {
        public required string TriggerType { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }
}
