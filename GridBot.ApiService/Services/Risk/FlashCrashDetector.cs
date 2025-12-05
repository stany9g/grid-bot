using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Detects flash crash events and manages protection periods.
/// Thread-safe singleton implementation.
/// </summary>
public sealed class FlashCrashDetector : IFlashCrashDetector, IDisposable
{
    private readonly ILogger<FlashCrashDetector> _logger;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingState;
    private readonly IRiskEventLogger _eventLogger;

    private readonly ConcurrentDictionary<int, MarketCrashState> _marketStates = new();
    private readonly ReaderWriterLockSlim _rwLock = new();
    private bool _disposed;

    // Rolling window size: 60 minutes of price data
    private const int PriceHistoryMinutes = 60;

    /// <summary>
    /// Creates a new FlashCrashDetector instance.
    /// </summary>
    public FlashCrashDetector(
        ILogger<FlashCrashDetector> logger,
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
    public async Task<FlashCrashStatus> CheckForFlashCrashAsync(int marketId, CancellationToken ct = default)
    {
        var state = GetOrCreateState(marketId);

        // Atomically get current protection state
        var (protectionUntil, severity, action) = state.GetProtection();

        // Check if still in protection period
        if (protectionUntil.HasValue && DateTimeOffset.UtcNow < protectionUntil.Value)
        {
            return FlashCrashStatus.InProtection(
                severity,
                action,
                protectionUntil.Value,
                $"In protection until {protectionUntil.Value:HH:mm:ss} UTC");
        }

        // Clear expired protection atomically
        if (protectionUntil.HasValue)
        {
            state.SetProtection(null, FlashCrashSeverity.None, FlashCrashAction.None);
            _logger.LogInformation("Flash crash protection expired for market {MarketId}", marketId);
        }

        // Use read lock for thread-safe access to price history
        _rwLock.EnterReadLock();
        List<PricePoint> priceHistorySnapshot;
        try
        {
            // Need at least 2 price points to detect a crash
            if (state.PriceHistory.Count < 2)
            {
                return FlashCrashStatus.NoCrash();
            }
            // Take a snapshot for safe iteration
            priceHistorySnapshot = state.PriceHistory.ToList();
        }
        finally
        {
            _rwLock.ExitReadLock();
        }

        var now = DateTimeOffset.UtcNow;
        var currentPrice = priceHistorySnapshot.LastOrDefault().Price;
        var config = _riskConfig.FlashCrash;

        // Check each timeframe for drops using the snapshot
        var drop1m = CalculateDrop(priceHistorySnapshot, TimeSpan.FromMinutes(1), currentPrice);
        var drop5m = CalculateDrop(priceHistorySnapshot, TimeSpan.FromMinutes(5), currentPrice);
        var drop15m = CalculateDrop(priceHistorySnapshot, TimeSpan.FromMinutes(15), currentPrice);
        var drop60m = CalculateDrop(priceHistorySnapshot, TimeSpan.FromMinutes(60), currentPrice);

        // Check thresholds from most severe to least severe
        if (drop60m <= config.OneHourDropPercent)
        {
            return await TriggerCrashProtectionAsync(
                marketId, state, FlashCrashSeverity.Extreme, FlashCrashAction.FullHalt,
                drop60m, TimeSpan.FromMinutes(60),
                TimeSpan.FromMinutes(config.OneHourPauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        if (drop15m <= config.FifteenMinuteDropPercent)
        {
            return await TriggerCrashProtectionAsync(
                marketId, state, FlashCrashSeverity.Severe, FlashCrashAction.CancelAndReduceHalf,
                drop15m, TimeSpan.FromMinutes(15),
                TimeSpan.FromMinutes(config.FifteenMinutePauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        if (drop5m <= config.FiveMinuteDropPercent)
        {
            return await TriggerCrashProtectionAsync(
                marketId, state, FlashCrashSeverity.Moderate, FlashCrashAction.PauseAll,
                drop5m, TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(config.FiveMinutePauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        if (drop1m <= config.OneMinuteDropPercent)
        {
            return await TriggerCrashProtectionAsync(
                marketId, state, FlashCrashSeverity.Minor, FlashCrashAction.PauseBuys,
                drop1m, TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(config.OneMinutePauseDurationMinutes),
                ct).ConfigureAwait(false);
        }

        return FlashCrashStatus.NoCrash();
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

            // Also trim crash events older than 24 hours
            var crashCutoff = now.AddHours(-24);
            state.CrashEvents.RemoveAll(e => e < crashCutoff);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public bool IsInCrashProtection(int marketId)
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
    public FlashCrashAction GetCurrentAction(int marketId)
    {
        if (!_marketStates.TryGetValue(marketId, out var state))
        {
            return FlashCrashAction.None;
        }

        if (state.ProtectionUntil.HasValue && DateTimeOffset.UtcNow < state.ProtectionUntil.Value)
        {
            return state.CurrentAction;
        }

        return FlashCrashAction.None;
    }

    /// <inheritdoc />
    public void ClearProtection(int marketId)
    {
        if (_marketStates.TryGetValue(marketId, out var state))
        {
            state.SetProtection(null, FlashCrashSeverity.None, FlashCrashAction.None);
            _logger.LogWarning("Manually cleared flash crash protection for market {MarketId}", marketId);
        }
    }

    /// <inheritdoc />
    public int GetCrashCount24h(int marketId)
    {
        if (!_marketStates.TryGetValue(marketId, out var state))
        {
            return 0;
        }

        _rwLock.EnterReadLock();
        try
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            return state.CrashEvents.Count(e => e >= cutoff);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    private MarketCrashState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketCrashState());
    }

    private static decimal CalculateDrop(List<PricePoint> priceHistory, TimeSpan timeframe, decimal currentPrice)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(timeframe);
        var pricesInWindow = priceHistory
            .Where(p => p.Timestamp >= cutoff)
            .ToList();

        if (pricesInWindow.Count == 0)
        {
            return 0;
        }

        var maxPrice = pricesInWindow.Max(p => p.Price);
        if (maxPrice <= 0)
        {
            return 0;
        }

        return ((currentPrice - maxPrice) / maxPrice) * 100m;
    }

    private async Task<FlashCrashStatus> TriggerCrashProtectionAsync(
        int marketId,
        MarketCrashState state,
        FlashCrashSeverity severity,
        FlashCrashAction action,
        decimal dropPercent,
        TimeSpan dropTimeframe,
        TimeSpan protectionDuration,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Record crash event with write lock
        _rwLock.EnterWriteLock();
        try
        {
            state.CrashEvents.Add(now);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        // Check if we've exceeded max events in 24 hours
        var crashCount = GetCrashCount24h(marketId);
        if (crashCount > _riskConfig.FlashCrash.MaxEventsIn24Hours)
        {
            // Extended halt
            severity = FlashCrashSeverity.Extreme;
            action = FlashCrashAction.FullHalt;
            protectionDuration = TimeSpan.FromHours(24);

            _logger.LogCritical(
                "EXCESSIVE FLASH CRASHES for market {MarketId}: {Count} events in 24h. Extended halt activated.",
                marketId, crashCount);
        }

        // Atomically set all protection state properties
        state.SetProtection(now.Add(protectionDuration), severity, action);

        // Log risk event
        var ruleId = severity switch
        {
            FlashCrashSeverity.Minor => "FC-001",
            FlashCrashSeverity.Moderate => "FC-002",
            FlashCrashSeverity.Severe => "FC-003",
            FlashCrashSeverity.Extreme => "FC-004",
            _ => "FC-000"
        };

        var actionDescription = action switch
        {
            FlashCrashAction.PauseBuys => $"Pause all BUY orders for {protectionDuration.TotalMinutes} minutes",
            FlashCrashAction.PauseAll => $"Pause ALL orders for {protectionDuration.TotalMinutes} minutes",
            FlashCrashAction.CancelAndReduceHalf => $"Cancel all orders, reduce longs by 50%. Protection for {protectionDuration.TotalMinutes} minutes",
            FlashCrashAction.FullHalt => $"Full trading halt, reduce to 50%. Protection for {protectionDuration.TotalHours} hours + manual review",
            _ => "No action"
        };

        var riskEvent = RiskEvent.Create(
            ruleId,
            severity == FlashCrashSeverity.Extreme ? AlertSeverity.Critical : AlertSeverity.High,
            $"Flash crash detected: {dropPercent:F2}% drop in {dropTimeframe.TotalMinutes} minutes",
            actionDescription,
            dropPercent,
            severity switch
            {
                FlashCrashSeverity.Minor => _riskConfig.FlashCrash.OneMinuteDropPercent,
                FlashCrashSeverity.Moderate => _riskConfig.FlashCrash.FiveMinuteDropPercent,
                FlashCrashSeverity.Severe => _riskConfig.FlashCrash.FifteenMinuteDropPercent,
                FlashCrashSeverity.Extreme => _riskConfig.FlashCrash.OneHourDropPercent,
                _ => 0
            });

        await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

        // Transition trading state for crashes - NEVER HALT, use Degraded states
        if (severity >= FlashCrashSeverity.Severe)
        {
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_ProtectiveMode,
                $"Flash crash: {dropPercent:F2}% drop in {dropTimeframe.TotalMinutes} minutes").ConfigureAwait(false);
        }
        else if (severity >= FlashCrashSeverity.Moderate)
        {
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_HighVolatility,
                $"Flash crash protection: {dropPercent:F2}% drop").ConfigureAwait(false);
        }

        _logger.LogWarning(
            "FLASH CRASH DETECTED for market {MarketId}: {Drop:F2}% in {Timeframe} minutes. Severity: {Severity}, Action: {Action}, Protection until: {Until:HH:mm:ss} UTC",
            marketId, dropPercent, dropTimeframe.TotalMinutes, severity, action, state.ProtectionUntil);

        return new FlashCrashStatus
        {
            CrashDetected = true,
            Severity = severity,
            DropPercent = dropPercent,
            DropTimeframe = dropTimeframe,
            RequiredAction = action,
            ProtectionUntil = state.ProtectionUntil,
            Reason = $"{severity} crash: {dropPercent:F2}% drop in {dropTimeframe.TotalMinutes} minutes"
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
    private sealed class MarketCrashState
    {
        private readonly object _protectionLock = new();
        private DateTimeOffset? _protectionUntil;
        private FlashCrashSeverity _currentSeverity = FlashCrashSeverity.None;
        private FlashCrashAction _currentAction = FlashCrashAction.None;

        public List<PricePoint> PriceHistory { get; } = [];
        public List<DateTimeOffset> CrashEvents { get; } = [];

        public DateTimeOffset? ProtectionUntil
        {
            get { lock (_protectionLock) { return _protectionUntil; } }
            set { lock (_protectionLock) { _protectionUntil = value; } }
        }

        public FlashCrashSeverity CurrentSeverity
        {
            get { lock (_protectionLock) { return _currentSeverity; } }
            set { lock (_protectionLock) { _currentSeverity = value; } }
        }

        public FlashCrashAction CurrentAction
        {
            get { lock (_protectionLock) { return _currentAction; } }
            set { lock (_protectionLock) { _currentAction = value; } }
        }

        /// <summary>
        /// Atomically sets all protection state properties.
        /// </summary>
        public void SetProtection(DateTimeOffset? until, FlashCrashSeverity severity, FlashCrashAction action)
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
        public (DateTimeOffset? Until, FlashCrashSeverity Severity, FlashCrashAction Action) GetProtection()
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
