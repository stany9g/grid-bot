using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Detects flash pump events and manages protection periods.
/// Provides symmetric protection for SHORT positions (mirrors FlashCrashDetector for LONG positions).
/// Thread-safe singleton implementation.
/// </summary>
public sealed class FlashPumpDetector : IFlashPumpDetector, IDisposable
{
    private readonly ILogger<FlashPumpDetector> _logger;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingState;
    private readonly IRiskEventLogger _eventLogger;

    private readonly ConcurrentDictionary<int, MarketPumpState> _marketStates = new();
    private readonly ReaderWriterLockSlim _rwLock = new();
    private bool _disposed;

    // Rolling window size: 60 minutes of price data
    private const int PriceHistoryMinutes = 60;

    /// <summary>
    /// Creates a new FlashPumpDetector instance.
    /// </summary>
    public FlashPumpDetector(
        ILogger<FlashPumpDetector> logger,
        IRiskConfiguration riskConfig,
        ITradingStateService tradingState,
        IRiskEventLogger eventLogger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(riskConfig);
        ArgumentNullException.ThrowIfNull(tradingState);
        ArgumentNullException.ThrowIfNull(eventLogger);

        _logger = logger;
        _riskConfig = riskConfig;
        _tradingState = tradingState;
        _eventLogger = eventLogger;
    }

    /// <inheritdoc />
    public async Task<FlashPumpStatus> CheckForFlashPumpAsync(int marketId, CancellationToken ct = default)
    {
        var state = GetOrCreateState(marketId);

        // Atomically get current protection state
        var (protectionUntil, severity, action) = state.GetProtection();

        // Check if still in protection period
        if (protectionUntil.HasValue && DateTimeOffset.UtcNow < protectionUntil.Value)
        {
            return FlashPumpStatus.InProtection(
                severity,
                action,
                protectionUntil.Value,
                $"In protection until {protectionUntil.Value:HH:mm:ss} UTC");
        }

        // Clear expired protection atomically
        if (protectionUntil.HasValue)
        {
            state.SetProtection(null, FlashPumpSeverity.None, FlashPumpAction.None);
            _logger.LogInformation("Flash pump protection expired for market {MarketId}", marketId);
        }

        // Use read lock for thread-safe access to price history
        _rwLock.EnterReadLock();
        List<PricePoint> priceHistorySnapshot;
        try
        {
            // Need at least 2 price points to detect a pump
            if (state.PriceHistory.Count < 2)
            {
                return FlashPumpStatus.NoPump();
            }
            // Take a snapshot for safe iteration
            priceHistorySnapshot = state.PriceHistory.ToList();
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        var currentPrice = priceHistorySnapshot.LastOrDefault().Price;
        var config = _riskConfig.FlashPump;

        // Check each timeframe for gains using the snapshot
        var gain1m = CalculateGain(priceHistorySnapshot, TimeSpan.FromMinutes(1), currentPrice);
        var gain5m = CalculateGain(priceHistorySnapshot, TimeSpan.FromMinutes(5), currentPrice);
        var gain15m = CalculateGain(priceHistorySnapshot, TimeSpan.FromMinutes(15), currentPrice);
        var gain60m = CalculateGain(priceHistorySnapshot, TimeSpan.FromMinutes(60), currentPrice);

        // Check thresholds from most severe to least severe (gains are positive)
        if (gain60m >= config.OneHourGainPercent)
        {
            return await TriggerPumpProtectionAsync(
                marketId, state, FlashPumpSeverity.Extreme, FlashPumpAction.FullHalt,
                gain60m, TimeSpan.FromMinutes(60),
                TimeSpan.FromMinutes(config.OneHourPauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        if (gain15m >= config.FifteenMinuteGainPercent)
        {
            return await TriggerPumpProtectionAsync(
                marketId, state, FlashPumpSeverity.Severe, FlashPumpAction.CancelAndCoverHalf,
                gain15m, TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(config.FifteenMinutePauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        if (gain5m >= config.FiveMinuteGainPercent)
        {
            return await TriggerPumpProtectionAsync(
                marketId, state, FlashPumpSeverity.Moderate, FlashPumpAction.PauseAll,
                gain5m, TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(config.FiveMinutePauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        if (gain1m >= config.OneMinuteGainPercent)
        {
            return await TriggerPumpProtectionAsync(
                marketId, state, FlashPumpSeverity.Minor, FlashPumpAction.PauseSells,
                gain1m, TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(config.OneMinutePauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        return FlashPumpStatus.NoPump();
    }

    /// <inheritdoc />
    public Task RecordPriceAsync(int marketId, decimal price, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = GetOrCreateState(marketId);
        var now = DateTimeOffset.UtcNow;

        _rwLock.EnterWriteLock();
        try
        {
            // Add new price point
            state.PriceHistory.Add(new PricePoint(now, price));

            // Trim old entries (keep only last 60 minutes)
            var cutoff = now.AddMinutes(-PriceHistoryMinutes);
            state.PriceHistory.RemoveAll(p => p.Timestamp < cutoff);

            // Also trim pump events older than 24 hours
            var pumpCutoff = now.AddHours(-24);
            state.PumpEvents.RemoveAll(e => e < pumpCutoff);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsInPumpProtection(int marketId)
    {
        if (!_marketStates.TryGetValue(marketId, out var state))
        {
            return false;
        }

        return state.ProtectionUntil.HasValue && DateTimeOffset.UtcNow < state.ProtectionUntil.Value;
    }

    /// <inheritdoc />
    public DateTimeOffset? GetProtectionExpiry(int marketId)
    {
        if (!_marketStates.TryGetValue(marketId, out var state))
        {
            return null;
        }

        if (state.ProtectionUntil.HasValue && DateTimeOffset.UtcNow < state.ProtectionUntil.Value)
        {
            return state.ProtectionUntil;
        }

        return null;
    }

    /// <inheritdoc />
    public FlashPumpAction GetCurrentAction(int marketId)
    {
        if (!_marketStates.TryGetValue(marketId, out var state))
        {
            return FlashPumpAction.None;
        }

        if (state.ProtectionUntil.HasValue && DateTimeOffset.UtcNow < state.ProtectionUntil.Value)
        {
            return state.CurrentAction;
        }

        return FlashPumpAction.None;
    }

    /// <inheritdoc />
    public void ClearProtection(int marketId)
    {
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            state.SetProtection(null, FlashPumpSeverity.None, FlashPumpAction.None);
            _logger.LogWarning("Manually cleared flash pump protection for market {MarketId}", marketId);
        }
    }

    /// <inheritdoc />
    public int GetPumpCount24h(int marketId)
    {
        if (!_marketStates.TryGetValue(marketId, out var state))
        {
            return 0;
        }

        _rwLock.EnterReadLock();
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            return state.PumpEvents.Count(e => e >= cutoff);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    private MarketPumpState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketPumpState());
    }

    /// <summary>
    /// Calculates gain from minimum price in the timeframe (inverse of CalculateDrop).
    /// </summary>
    private static decimal CalculateGain(List<PricePoint> priceHistory, TimeSpan timeframe, decimal currentPrice)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(timeframe);
        var pricesInWindow = priceHistory
            .Where(p => p.Timestamp >= cutoff)
            .ToList();

        if (pricesInWindow.Count == 0)
        {
            return 0;
        }

        var minPrice = pricesInWindow.Min(p => p.Price);
        if (minPrice <= 0)
        {
            return 0;
        }

        // Positive value for gains (current price above minimum)
        return ((currentPrice - minPrice) / minPrice) * 100m;
    }

    private async Task<FlashPumpStatus> TriggerPumpProtectionAsync(
        int marketId,
        MarketPumpState state,
        FlashPumpSeverity severity,
        FlashPumpAction action,
        decimal gainPercent,
        TimeSpan gainTimeframe,
        TimeSpan protectionDuration,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Record pump event AND calculate pump count within the same write lock scope
        // This avoids the potential deadlock from acquiring read lock after write lock
        int pumpCount;
        _rwLock.EnterWriteLock();
        try
        {
            state.PumpEvents.Add(now);

            // Calculate pump count within the write lock scope instead of calling GetPumpCount24h
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            pumpCount = state.PumpEvents.Count(e => e >= cutoff);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        // Check if we've exceeded max events in 24 hours (pumpCount already calculated)
        if (pumpCount > _riskConfig.FlashPump.MaxEventsIn24Hours)
        {
            // Extended halt
            severity = FlashPumpSeverity.Extreme;
            action = FlashPumpAction.FullHalt;
            protectionDuration = TimeSpan.FromHours(24);

            _logger.LogCritical(
                "EXCESSIVE FLASH PUMPS for market {MarketId}: {Count} events in 24h. Extended halt activated.",
                marketId, pumpCount);
        }

        // Atomically set all protection state properties
        state.SetProtection(now.Add(protectionDuration), severity, action);

        // Log risk event using FP-xxx rule IDs (Flash Pump)
        var ruleId = severity switch
        {
            FlashPumpSeverity.Minor => "FP-001",
            FlashPumpSeverity.Moderate => "FP-002",
            FlashPumpSeverity.Severe => "FP-003",
            FlashPumpSeverity.Extreme => "FP-004",
            _ => "FP-000"
        };

        var actionDescription = action switch
        {
            FlashPumpAction.PauseSells => $"Pause all SELL orders for {protectionDuration.TotalMinutes} minutes",
            FlashPumpAction.PauseAll => $"Pause ALL orders for {protectionDuration.TotalMinutes} minutes",
            FlashPumpAction.CancelAndCoverHalf => $"Cancel all orders, cover shorts by 50%. Protection for {protectionDuration.TotalMinutes} minutes",
            FlashPumpAction.FullHalt => $"Full trading halt, cover shorts by 50%. Protection for {protectionDuration.TotalHours} hours + manual review",
            _ => "No action"
        };

        var riskEvent = RiskEvent.Create(
            ruleId,
            severity == FlashPumpSeverity.Extreme ? AlertSeverity.Critical : AlertSeverity.High,
            $"Flash pump detected: +{gainPercent:F2}% gain in {gainTimeframe.TotalMinutes} minutes",
            actionDescription,
            gainPercent,
            severity switch
            {
                FlashPumpSeverity.Minor => _riskConfig.FlashPump.OneMinuteGainPercent,
                FlashPumpSeverity.Moderate => _riskConfig.FlashPump.FiveMinuteGainPercent,
                FlashPumpSeverity.Severe => _riskConfig.FlashPump.FifteenMinuteGainPercent,
                FlashPumpSeverity.Extreme => _riskConfig.FlashPump.OneHourGainPercent,
                _ => 0
            });

        await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

        // Transition trading state for pumps - NEVER HALT, use Degraded states
        if (severity >= FlashPumpSeverity.Severe)
        {
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_ProtectiveMode,
                $"Flash pump: +{gainPercent:F2}% gain in {gainTimeframe.TotalMinutes} minutes").ConfigureAwait(false);
        }
        else if (severity >= FlashPumpSeverity.Moderate)
        {
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_HighVolatility,
                $"Flash pump protection: +{gainPercent:F2}% gain").ConfigureAwait(false);
        }

        _logger.LogWarning(
            "FLASH PUMP DETECTED for market {MarketId}: +{Gain:F2}% in {Timeframe} minutes. Severity: {Severity}, Action: {Action}, Protection until: {Until:HH:mm:ss} UTC",
            marketId, gainPercent, gainTimeframe.TotalMinutes, severity, action, state.ProtectionUntil);

        return new FlashPumpStatus
        {
            PumpDetected = true,
            Severity = severity,
            GainPercent = gainPercent,
            GainTimeframe = gainTimeframe,
            RequiredAction = action,
            ProtectionUntil = state.ProtectionUntil,
            Reason = $"{severity} pump: +{gainPercent:F2}% gain in {gainTimeframe.TotalMinutes} minutes"
        };
    }

    /// <summary>
    /// Price point with timestamp.
    /// </summary>
    private readonly record struct PricePoint(DateTimeOffset Timestamp, decimal Price);

    /// <summary>
    /// Internal state tracking for a single market.
    /// Thread-safe for protection state modifications.
    /// </summary>
    private sealed class MarketPumpState
    {
        private readonly object _protectionLock = new();
        private DateTimeOffset? _protectionUntil;
        private FlashPumpSeverity _currentSeverity = FlashPumpSeverity.None;
        private FlashPumpAction _currentAction = FlashPumpAction.None;

        public List<PricePoint> PriceHistory { get; } = [];
        public List<DateTimeOffset> PumpEvents { get; } = [];

        public DateTimeOffset? ProtectionUntil
        {
            get { lock (_protectionLock) { return _protectionUntil; } }
            set { lock (_protectionLock) { _protectionUntil = value; } }
        }

        public FlashPumpSeverity CurrentSeverity
        {
            get { lock (_protectionLock) { return _currentSeverity; } }
            set { lock (_protectionLock) { _currentSeverity = value; } }
        }

        public FlashPumpAction CurrentAction
        {
            get { lock (_protectionLock) { return _currentAction; } }
            set { lock (_protectionLock) { _currentAction = value; } }
        }

        /// <summary>
        /// Atomically sets all protection state properties.
        /// </summary>
        public void SetProtection(DateTimeOffset? until, FlashPumpSeverity severity, FlashPumpAction action)
        {
            lock (_protectionLock)
            {
                _protectionUntil = until;
                _currentSeverity = severity;
                _currentAction = action;
            }
        }

        /// <summary>
        /// Atomically gets all protection state properties.
        /// </summary>
        public (DateTimeOffset? Until, FlashPumpSeverity Severity, FlashPumpAction Action) GetProtection()
        {
            lock (_protectionLock)
            {
                return (_protectionUntil, _currentSeverity, _currentAction);
            }
        }
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _rwLock.Dispose();
        _marketStates.Clear();
    }
}
