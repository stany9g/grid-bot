using System.Text.Json;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Risk;
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

    /// <inheritdoc />
    public async Task SaveTradeRecordAsync(int marketId, TradeRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        try
        {
            var date = DateOnly.FromDateTime(record.Timestamp.UtcDateTime);
            var key = StateKeys.TradeRecords(marketId, date);

            // Get existing records for this day
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);
            var records = string.IsNullOrEmpty(json)
                ? []
                : JsonSerializer.Deserialize<List<TradeRecord>>(json, JsonOptions) ?? [];

            // Add new record
            records.Add(record);

            // Save with 35-day TTL
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(35)
            };

            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(records, JsonOptions),
                options,
                ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved trade record for market {MarketId}: {PnlPercent}%",
                marketId, record.PnlPercent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save trade record for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TradeRecord>> LoadTradeRecordsAsync(int marketId, DateTimeOffset since, CancellationToken ct = default)
    {
        try
        {
            var result = new List<TradeRecord>();
            var startDate = DateOnly.FromDateTime(since.UtcDateTime);
            var endDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

            // Iterate through each day in the range
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var key = StateKeys.TradeRecords(marketId, date);
                var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(json))
                {
                    var dayRecords = JsonSerializer.Deserialize<List<TradeRecord>>(json, JsonOptions);
                    if (dayRecords is not null)
                    {
                        result.AddRange(dayRecords.Where(r => r.Timestamp >= since));
                    }
                }
            }

            _logger.LogDebug(
                "Loaded {Count} trade records for market {MarketId} since {Since}",
                result.Count, marketId, since);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load trade records for market {MarketId}", marketId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task CleanupOldTradeRecordsAsync(int marketId, int retentionDays, CancellationToken ct = default)
    {
        try
        {
            var cutoffDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddDays(-retentionDays).UtcDateTime);

            // We need to delete keys older than the retention period
            // Since we store by date, we can calculate which keys to delete
            // For simplicity, we'll try to delete keys for dates before cutoff
            // In production, you might want to use Redis SCAN to find keys matching the pattern

            for (var daysBack = retentionDays + 1; daysBack <= retentionDays + 30; daysBack++)
            {
                var oldDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddDays(-daysBack).UtcDateTime);
                var key = StateKeys.TradeRecords(marketId, oldDate);

                try
                {
                    await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Key may not exist, ignore
                }
            }

            _logger.LogDebug("Cleaned up old trade records for market {MarketId}", marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old trade records for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SaveEquitySnapshotAsync(int marketId, EquitySnapshot snapshot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var date = DateOnly.FromDateTime(snapshot.Timestamp.UtcDateTime);
            var key = StateKeys.EquitySnapshots(marketId, date);

            // Get existing snapshots for this day
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);
            var snapshots = string.IsNullOrEmpty(json)
                ? []
                : JsonSerializer.Deserialize<List<EquitySnapshot>>(json, JsonOptions) ?? [];

            // Add new snapshot
            snapshots.Add(snapshot);

            // Save with 35-day TTL
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(35)
            };

            await _cache.SetStringAsync(
                key,
                JsonSerializer.Serialize(snapshots, JsonOptions),
                options,
                ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved equity snapshot for market {MarketId}: {Equity:F2}",
                marketId, snapshot.Equity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save equity snapshot for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EquitySnapshot>> LoadEquitySnapshotsAsync(int marketId, DateTimeOffset since, CancellationToken ct = default)
    {
        try
        {
            var result = new List<EquitySnapshot>();
            var startDate = DateOnly.FromDateTime(since.UtcDateTime);
            var endDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

            // Iterate through each day in the range
            for (var date = startDate; date <= endDate; date = date.AddDays(1))
            {
                var key = StateKeys.EquitySnapshots(marketId, date);
                var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(json))
                {
                    var daySnapshots = JsonSerializer.Deserialize<List<EquitySnapshot>>(json, JsonOptions);
                    if (daySnapshots is not null)
                    {
                        result.AddRange(daySnapshots.Where(s => s.Timestamp >= since));
                    }
                }
            }

            _logger.LogDebug(
                "Loaded {Count} equity snapshots for market {MarketId} since {Since}",
                result.Count, marketId, since);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load equity snapshots for market {MarketId}", marketId);
            return [];
        }
    }

    /// <inheritdoc />
    public async Task CleanupOldEquitySnapshotsAsync(int marketId, int retentionDays, CancellationToken ct = default)
    {
        try
        {
            // Similar to trade records cleanup
            for (var daysBack = retentionDays + 1; daysBack <= retentionDays + 30; daysBack++)
            {
                var oldDate = DateOnly.FromDateTime(DateTimeOffset.UtcNow.AddDays(-daysBack).UtcDateTime);
                var key = StateKeys.EquitySnapshots(marketId, oldDate);

                try
                {
                    await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Key may not exist, ignore
                }
            }

            _logger.LogDebug("Cleaned up old equity snapshots for market {MarketId}", marketId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup old equity snapshots for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task SaveRollingLossStateAsync(int marketId, PersistedRollingLossState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        try
        {
            var key = StateKeys.RollingLossState(marketId);
            var json = JsonSerializer.Serialize(state, JsonOptions);

            await _cache.SetStringAsync(key, json, ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Saved rolling loss state for market {MarketId}: Equity={Equity:F2}",
                marketId, state.CurrentEquity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save rolling loss state for market {MarketId}", marketId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<PersistedRollingLossState?> LoadRollingLossStateAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            var key = StateKeys.RollingLossState(marketId);
            var json = await _cache.GetStringAsync(key, ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(json))
                return null;

            var state = JsonSerializer.Deserialize<PersistedRollingLossState>(json, JsonOptions);

            _logger.LogDebug(
                "Loaded rolling loss state for market {MarketId}: Equity={Equity:F2}",
                marketId, state?.CurrentEquity);

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load rolling loss state for market {MarketId}", marketId);
            return null;
        }
    }
}
