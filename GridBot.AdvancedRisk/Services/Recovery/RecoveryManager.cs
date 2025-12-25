using System.Collections.Concurrent;
using GridBot.AdvancedRisk.Models;
using GridBot.AdvancedRisk.Services.Risk;
using Microsoft.Extensions.Logging;

namespace GridBot.AdvancedRisk.Services.Recovery;

public sealed class RecoveryManager : IRecoveryManager
{
    private readonly IAdvancedRiskConfiguration _config;
    private readonly IAdvancedRiskStateProvider _stateProvider;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ILogger<RecoveryManager> _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();

    public RecoveryManager(
        IAdvancedRiskConfiguration config,
        IAdvancedRiskStateProvider stateProvider,
        IRiskEventLogger eventLogger,
        ILogger<RecoveryManager> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(stateProvider);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _stateProvider = stateProvider;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public async Task<RecoveryStatus> GetRecoveryStatusAsync(int marketId, CancellationToken ct = default)
    {
        var status = await _stateProvider.GetRecoveryStatusAsync(marketId, ct).ConfigureAwait(false);
        return status ?? RecoveryStatus.Normal(marketId);
    }

    public async Task<RecoveryStatus> BeginRecoveryAsync(int marketId, string reasonForProtectiveMode, CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var phase1End = now.AddMinutes(_config.RecoveryPhase1Minutes);
            var status = new RecoveryStatus(marketId, RecoveryPhase.Phase1, now, phase1End, _config.Phase1PositionMultiplier, _config.Phase1SpreadMultiplier, reasonForProtectiveMode);
            await _stateProvider.SaveRecoveryStatusAsync(status, ct).ConfigureAwait(false);
            var evt = RiskEvent.RecoveryPhaseTransition(marketId, RecoveryPhase.None, RecoveryPhase.Phase1);
            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
            _logger.LogInformation("Recovery started for market {MarketId}", marketId);
            return status;
        }
        finally { semaphore.Release(); }
    }

    public async Task<RecoveryStatus> UpdateRecoveryAsync(int marketId, CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await _stateProvider.GetRecoveryStatusAsync(marketId, ct).ConfigureAwait(false);
            if (current is null || current.Phase == RecoveryPhase.None) return RecoveryStatus.Normal(marketId);
            var now = DateTimeOffset.UtcNow;
            if (now < current.PhaseEndsAt) return current;
            var (nextPhase, positionMult, spreadMult, durationMinutes) = current.Phase switch
            {
                RecoveryPhase.Phase1 => (RecoveryPhase.Phase2, _config.Phase2PositionMultiplier, 1.0m, _config.RecoveryPhase2Minutes),
                RecoveryPhase.Phase2 => (RecoveryPhase.Phase3, _config.Phase3PositionMultiplier, 1.0m, _config.RecoveryPhase3Minutes),
                RecoveryPhase.Phase3 => (RecoveryPhase.None, 1.0m, 1.0m, 0),
                _ => (RecoveryPhase.None, 1.0m, 1.0m, 0)
            };
            RecoveryStatus newStatus;
            if (nextPhase == RecoveryPhase.None)
            {
                newStatus = RecoveryStatus.Normal(marketId);
                await _stateProvider.SaveRecoveryStatusAsync(newStatus, ct).ConfigureAwait(false);
                var evt = RiskEvent.RecoveryPhaseTransition(marketId, current.Phase, RecoveryPhase.None);
                await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
                _logger.LogInformation("Recovery completed for market {MarketId}", marketId);
            }
            else
            {
                var phaseEnd = now.AddMinutes(durationMinutes);
                newStatus = new RecoveryStatus(marketId, nextPhase, now, phaseEnd, positionMult, spreadMult, current.ReasonForProtectiveMode);
                await _stateProvider.SaveRecoveryStatusAsync(newStatus, ct).ConfigureAwait(false);
                var evt = RiskEvent.RecoveryPhaseTransition(marketId, current.Phase, nextPhase);
                await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
                _logger.LogInformation("Recovery advanced for market {MarketId} to {Phase}", marketId, nextPhase);
            }
            return newStatus;
        }
        finally { semaphore.Release(); }
    }

    public async Task AbortRecoveryAsync(int marketId, string reason, CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await _stateProvider.GetRecoveryStatusAsync(marketId, ct).ConfigureAwait(false);
            if (current is null || current.Phase == RecoveryPhase.None) return;
            await _stateProvider.SetProtectiveModeAsync(marketId, true, reason, ct).ConfigureAwait(false);
            await _stateProvider.SaveRecoveryStatusAsync(RecoveryStatus.Normal(marketId), ct).ConfigureAwait(false);
            var evt = RiskEvent.EnteringProtectiveMode(marketId, "Recovery aborted: " + reason);
            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
            _logger.LogWarning("Recovery aborted for market {MarketId}: {Reason}", marketId, reason);
        }
        finally { semaphore.Release(); }
    }

    public async Task CompleteRecoveryAsync(int marketId, CancellationToken ct = default)
    {
        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var current = await _stateProvider.GetRecoveryStatusAsync(marketId, ct).ConfigureAwait(false);
            var fromPhase = current?.Phase ?? RecoveryPhase.None;
            var normalStatus = RecoveryStatus.Normal(marketId);
            await _stateProvider.SaveRecoveryStatusAsync(normalStatus, ct).ConfigureAwait(false);
            if (fromPhase != RecoveryPhase.None)
            {
                var evt = RiskEvent.RecoveryPhaseTransition(marketId, fromPhase, RecoveryPhase.None);
                await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
            }
            _logger.LogInformation("Recovery force-completed for market {MarketId}", marketId);
        }
        finally { semaphore.Release(); }
    }

    private SemaphoreSlim GetMarketLock(int marketId) => _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
}
