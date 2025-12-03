using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.DecisionEngine;

/// <summary>
/// Manages recovery state transitions and capacity scaling after halt conditions.
/// Thread-safe implementation using ConcurrentDictionary and per-market locks.
/// </summary>
public sealed class RecoveryManager : IRecoveryManager, IDisposable
{
    private readonly ILogger<RecoveryManager> _logger;
    private readonly DecisionEngineOptions _options;
    private readonly IStateRepository _stateRepository;
    private readonly ConcurrentDictionary<int, RecoveryState> _recoveryStates = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new RecoveryManager instance.
    /// </summary>
    public RecoveryManager(
        ILogger<RecoveryManager> logger,
        IOptions<TradingBotOptions> options,
        IStateRepository stateRepository)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stateRepository);

        _logger = logger;
        _options = options.Value.DecisionEngine;
        _stateRepository = stateRepository;
    }

    /// <inheritdoc />
    public Task<RecoveryState?> GetRecoveryStateAsync(int marketId, CancellationToken ct = default)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return Task.FromResult<RecoveryState?>(null);

        // Return a snapshot to prevent external mutation
        return Task.FromResult<RecoveryState?>(state.ToSnapshot());
    }

    /// <inheritdoc />
    public async Task StartRecoveryAsync(
        int marketId,
        string triggerType,
        decimal currentPrice,
        decimal currentEquity,
        CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var state = RecoveryState.Create(marketId, triggerType, currentPrice, currentEquity);
            _recoveryStates[marketId] = state;

            // Persist to Redis
            await _stateRepository.SaveRecoveryStateAsync(marketId, state, ct).ConfigureAwait(false);
            await _stateRepository.RecordCircuitBreakerEventAsync(marketId, triggerType, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Recovery started for market {MarketId}. Trigger: {TriggerType}, Phase: {Phase}",
                marketId, triggerType, state.CurrentPhase);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckPhaseAdvancementAsync(
        int marketId,
        decimal currentPrice,
        decimal currentEquity,
        decimal bookDepth,
        CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Read state inside lock to prevent race conditions
            if (!_recoveryStates.TryGetValue(marketId, out var state))
                return false;

            // Cannot advance beyond Phase4
            if (state.CurrentPhase >= RecoveryPhase.Phase4)
                return false;

            // Update price tracking
            state.UpdatePriceTracking(currentPrice);

            // Get minimum time for current phase
            var minPhaseTime = GetMinPhaseDuration(state.CurrentPhase);
            var phaseElapsed = state.GetPhaseElapsed();

            // Criteria 1: Minimum time in phase elapsed
            if (phaseElapsed < minPhaseTime)
            {
                _logger.LogDebug(
                    "Market {MarketId}: Phase advancement blocked - time remaining: {Remaining}",
                    marketId, minPhaseTime - phaseElapsed);
                return false;
            }

            // Criteria 2: No new circuit breakers triggered
            if (state.NewBreakerTriggered)
            {
                _logger.LogDebug(
                    "Market {MarketId}: Phase advancement blocked - new circuit breaker triggered",
                    marketId);
                return false;
            }

            // Criteria 3: Price stability (< configured threshold volatility)
            var volatility = state.CalculatePhaseVolatility();
            if (volatility > _options.RecoveryVolatilityThreshold)
            {
                _logger.LogDebug(
                    "Market {MarketId}: Phase advancement blocked - volatility {Volatility:P2} exceeds threshold {Threshold:P2}",
                    marketId, volatility, _options.RecoveryVolatilityThreshold);
                return false;
            }

            // Criteria 4: P&L stability (no losses > configured threshold)
            var pnl = state.CalculatePhasePnL(currentEquity);
            if (pnl < -_options.RecoveryLossThreshold)
            {
                _logger.LogDebug(
                    "Market {MarketId}: Phase advancement blocked - PnL {PnL:P2} exceeds loss threshold {Threshold:P2}",
                    marketId, pnl, -_options.RecoveryLossThreshold);
                return false;
            }

            // Criteria 5: Order book depth > minimum
            if (bookDepth < _options.RecoveryDepthMinimum)
            {
                _logger.LogDebug(
                    "Market {MarketId}: Phase advancement blocked - book depth ${Depth:N0} below minimum ${Min:N0}",
                    marketId, bookDepth, _options.RecoveryDepthMinimum);
                return false;
            }

            // Criteria 6: No API errors in last 5 minutes
            if (state.HasApiErrorsInPhase &&
                state.LastApiErrorAt.HasValue &&
                (DateTimeOffset.UtcNow - state.LastApiErrorAt.Value).TotalMinutes < 5)
            {
                _logger.LogDebug(
                    "Market {MarketId}: Phase advancement blocked - API error within last 5 minutes",
                    marketId);
                return false;
            }

            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> AdvancePhaseAsync(
        int marketId,
        decimal currentPrice,
        decimal currentEquity,
        CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // Read state inside lock to prevent race conditions
            if (!_recoveryStates.TryGetValue(marketId, out var state))
                return false;

            if (state.CurrentPhase >= RecoveryPhase.Phase4)
                return false;

            var previousPhase = state.CurrentPhase;
            state.CurrentPhase = (RecoveryPhase)((int)state.CurrentPhase + 1);
            state.ResetPhaseTracking(currentPrice, currentEquity);
            state.LastAdvancementAttempt = DateTimeOffset.UtcNow;
            state.NewBreakerTriggered = false;

            // Persist to Redis
            await _stateRepository.SaveRecoveryStateAsync(marketId, state, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Recovery advanced for market {MarketId}. Phase: {Previous} -> {Current}",
                marketId, previousPhase, state.CurrentPhase);

            return true;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task ResetRecoveryAsync(
        int marketId,
        string newTriggerType,
        decimal currentPrice,
        decimal currentEquity,
        CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var existingState = _recoveryStates.GetValueOrDefault(marketId);
            var failedCount = existingState?.FailedAdvancementCount ?? 0;

            var state = RecoveryState.Create(marketId, newTriggerType, currentPrice, currentEquity);
            state.FailedAdvancementCount = failedCount + 1;
            _recoveryStates[marketId] = state;

            // Persist to Redis
            await _stateRepository.SaveRecoveryStateAsync(marketId, state, ct).ConfigureAwait(false);
            await _stateRepository.RecordCircuitBreakerEventAsync(marketId, newTriggerType, ct).ConfigureAwait(false);

            _logger.LogWarning(
                "Recovery reset for market {MarketId}. New trigger: {TriggerType}, Failed advancements: {Count}",
                marketId, newTriggerType, state.FailedAdvancementCount);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public (decimal PositionMultiplier, decimal SpreadMultiplier, int GridOrderPercent) GetPhaseMultipliers(RecoveryPhase phase)
    {
        return phase switch
        {
            RecoveryPhase.None => (1.0m, 1.0m, 100),
            RecoveryPhase.Phase1 => (0.25m, 1.5m, 25),
            RecoveryPhase.Phase2 => (0.50m, 1.25m, 50),
            RecoveryPhase.Phase3 => (0.75m, 1.0m, 75),
            RecoveryPhase.Phase4 => (1.0m, 1.0m, 100),
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown recovery phase")
        };
    }

    /// <inheritdoc />
    public Task<bool> IsRecoveryCompleteAsync(int marketId, CancellationToken ct = default)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return Task.FromResult(false);

        // Must be in Phase4 with minimum time elapsed
        if (state.CurrentPhase != RecoveryPhase.Phase4)
            return Task.FromResult(false);

        var phase4Duration = TimeSpan.FromMilliseconds(_options.RecoveryStabilityWindowMs);
        var isComplete = state.GetPhaseElapsed() >= phase4Duration;

        return Task.FromResult(isComplete);
    }

    /// <inheritdoc />
    public async Task CompleteRecoveryAsync(int marketId, CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_recoveryStates.TryRemove(marketId, out var state))
            {
                var totalRecoveryTime = state.GetRecoveryElapsed();

                // Delete from Redis
                await _stateRepository.DeleteRecoveryStateAsync(marketId, ct).ConfigureAwait(false);

                _logger.LogInformation(
                    "Recovery completed for market {MarketId}. Total time: {Duration}, Original trigger: {Trigger}",
                    marketId, totalRecoveryTime, state.TriggerType);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordApiErrorAsync(int marketId, CancellationToken ct = default)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return;

        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            state.RecordApiError();
            _logger.LogDebug("API error recorded during recovery for market {MarketId}", marketId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordCircuitBreakerAsync(int marketId, CancellationToken ct = default)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return;

        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            state.NewBreakerTriggered = true;
            _logger.LogWarning("Circuit breaker triggered during recovery for market {MarketId}", marketId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task UpdatePriceTrackingAsync(int marketId, decimal currentPrice, CancellationToken ct = default)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return;

        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            state.UpdatePriceTracking(currentPrice);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public TimeSpan? GetEstimatedTimeRemaining(int marketId)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return null;

        // Calculate total time for all remaining phases
        var totalRemaining = TimeSpan.Zero;

        // Time remaining in current phase
        var currentPhaseTime = GetMinPhaseDuration(state.CurrentPhase);
        var phaseElapsed = state.GetPhaseElapsed();
        if (phaseElapsed < currentPhaseTime)
        {
            totalRemaining += currentPhaseTime - phaseElapsed;
        }

        // Add full time for remaining phases
        for (var phase = (int)state.CurrentPhase + 1; phase <= (int)RecoveryPhase.Phase4; phase++)
        {
            totalRemaining += GetMinPhaseDuration((RecoveryPhase)phase);
        }

        // Add stability window for Phase4 completion
        if (state.CurrentPhase != RecoveryPhase.Phase4)
        {
            totalRemaining += TimeSpan.FromMilliseconds(_options.RecoveryStabilityWindowMs);
        }

        return totalRemaining;
    }

    private TimeSpan GetMinPhaseDuration(RecoveryPhase phase)
    {
        return phase switch
        {
            RecoveryPhase.Phase1 => TimeSpan.FromMilliseconds(_options.RecoveryPhase1DurationMs),
            RecoveryPhase.Phase2 => TimeSpan.FromMilliseconds(_options.RecoveryPhase2DurationMs),
            RecoveryPhase.Phase3 => TimeSpan.FromMilliseconds(_options.RecoveryPhase3DurationMs),
            RecoveryPhase.Phase4 => TimeSpan.FromMilliseconds(_options.RecoveryStabilityWindowMs),
            _ => TimeSpan.Zero
        };
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    /// <inheritdoc />
    public RecoveryState? GetRecoveryState(int marketId)
    {
        if (!_recoveryStates.TryGetValue(marketId, out var state))
            return null;

        return state.ToSnapshot();
    }

    /// <inheritdoc />
    public async Task<bool> LoadPersistedStateAsync(int marketId, CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var state = await _stateRepository.LoadRecoveryStateAsync(marketId, ct).ConfigureAwait(false);

            if (state is null)
            {
                _logger.LogDebug("No persisted recovery state found for market {MarketId}", marketId);
                return false;
            }

            _recoveryStates[marketId] = state;

            _logger.LogInformation(
                "Loaded persisted recovery state for market {MarketId}: Phase={Phase}, Trigger={Trigger}, Started={StartedAt}",
                marketId, state.CurrentPhase, state.TriggerType, state.RecoveryStartedAt);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted recovery state for market {MarketId}", marketId);
            return false;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes all semaphore resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var semaphore in _marketLocks.Values)
        {
            semaphore.Dispose();
        }

        _marketLocks.Clear();
    }
}
