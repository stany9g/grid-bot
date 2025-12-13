using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Connectivity;

/// <summary>
/// Monitors nonce operation health and detects persistent failures.
/// Implements H.6 HIGH: Nonce Failure Alert specification.
///
/// Thread-safe singleton implementation.
///
/// Rules:
/// 1. IF nonce_failures >= 2 THEN alert_operator (Warning)
/// 2. IF nonce_failures >= 3 THEN pause_trading (Critical)
/// 3. IF nonce_success_count >= 10 THEN reset_failure_count
/// 4. IF nonce_failure_during_emergency THEN escalate_to_critical
/// </summary>
public sealed class NonceHealthMonitor : INonceHealthMonitor
{
    private readonly ILogger<NonceHealthMonitor> _logger;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ITradingStateService _tradingState;
    private readonly NonceOptions _options;

    // State tracking - all access must be inside lock
    private int _consecutiveFailures;
    private int _successesSinceLastFailure;
    private readonly List<DateTimeOffset> _failureTimestamps = new();
    private readonly object _lock = new();
    private DateTimeOffset? _lastFailure;
    private string? _lastErrorMessage;

    public NonceHealthMonitor(
        ILogger<NonceHealthMonitor> logger,
        IRiskEventLogger eventLogger,
        ITradingStateService tradingState,
        IOptions<TradingBotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(tradingState);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _eventLogger = eventLogger;
        _tradingState = tradingState;
        _options = options.Value.Nonce;

        _logger.LogInformation(
            "NonceHealthMonitor initialized. Thresholds: Warning={Warning}, Halt={Halt}, RecoverySuccesses={Recovery}",
            _options.WarningThreshold,
            _options.HaltThreshold,
            _options.RecoverySuccessCount);
    }

    /// <inheritdoc />
    public bool IsHealthy
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveFailures < _options.WarningThreshold;
            }
        }
    }

    /// <inheritdoc />
    public int ConsecutiveFailures
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveFailures;
            }
        }
    }

    /// <inheritdoc />
    public bool ShouldPauseTrading
    {
        get
        {
            lock (_lock)
            {
                return _consecutiveFailures >= _options.HaltThreshold;
            }
        }
    }

    /// <inheritdoc />
    public int TotalFailures24h
    {
        get
        {
            lock (_lock)
            {
                var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
                _failureTimestamps.RemoveAll(t => t < cutoff);
                return _failureTimestamps.Count;
            }
        }
    }

    /// <inheritdoc />
    public void RecordSuccess()
    {
        bool wasUnhealthy;
        int previousFailures;
        int successCount;

        lock (_lock)
        {
            previousFailures = _consecutiveFailures;
            wasUnhealthy = previousFailures >= _options.WarningThreshold;

            _successesSinceLastFailure++;
            successCount = _successesSinceLastFailure;

            // Reset consecutive failures after enough successes
            if (_successesSinceLastFailure >= _options.RecoverySuccessCount && _consecutiveFailures > 0)
            {
                _logger.LogInformation(
                    "NONCE-HEALTH: Recovered after {Successes} successful operations. Clearing {Failures} consecutive failures.",
                    _successesSinceLastFailure, _consecutiveFailures);

                _consecutiveFailures = 0;
                _successesSinceLastFailure = 0;
            }
        }

        // Log health transition outside lock
        if (wasUnhealthy && previousFailures >= _options.WarningThreshold && successCount >= _options.RecoverySuccessCount)
        {
            _logger.LogInformation(
                "NONCE-HEALTH: Transitioned to HEALTHY after {Successes} consecutive successes",
                successCount);
        }
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(string errorMessage, bool isEmergencyOperation = false, CancellationToken ct = default)
    {
        int failures;
        AlertSeverity severity;

        lock (_lock)
        {
            _consecutiveFailures++;
            _successesSinceLastFailure = 0;
            _lastFailure = DateTimeOffset.UtcNow;
            _lastErrorMessage = errorMessage;
            _failureTimestamps.Add(DateTimeOffset.UtcNow);
            failures = _consecutiveFailures;
        }

        // Determine severity based on failure count and emergency status
        if (isEmergencyOperation)
        {
            // Rule 4: Emergency operation failures are always CRITICAL
            severity = AlertSeverity.Critical;
            _logger.LogCritical(
                "NONCE-HEALTH: FAILURE DURING EMERGENCY OPERATION: {Error}. Consecutive failures: {Failures}",
                errorMessage, failures);
        }
        else if (failures >= _options.HaltThreshold)
        {
            // Rule 2: 3+ failures = Critical, pause trading
            severity = AlertSeverity.Critical;
            _logger.LogCritical(
                "NONCE-HEALTH: HALT THRESHOLD REACHED: {Failures} consecutive failures. Trading should be paused. Error: {Error}",
                failures, errorMessage);
        }
        else if (failures >= _options.WarningThreshold)
        {
            // Rule 1: 2 failures = High severity warning
            severity = AlertSeverity.High;
            _logger.LogWarning(
                "NONCE-HEALTH: WARNING THRESHOLD REACHED: {Failures} consecutive failures. Error: {Error}",
                failures, errorMessage);
        }
        else
        {
            // First failure - Medium severity
            severity = AlertSeverity.Medium;
            _logger.LogWarning(
                "NONCE-HEALTH: Failure #{Failures}: {Error}",
                failures, errorMessage);
        }

        // Build action description
        var actionTaken = failures >= _options.HaltThreshold
            ? "Trading paused due to nonce failures"
            : failures >= _options.WarningThreshold
                ? "Operator alerted"
                : "Monitoring";

        if (isEmergencyOperation)
        {
            actionTaken = "EMERGENCY OPERATION AFFECTED - " + actionTaken;
        }

        // Log risk event
        var riskEvent = RiskEvent.Create(
            failures >= _options.HaltThreshold ? "NONCE-HALT" : "NONCE-WARN",
            severity,
            $"Nonce failure #{failures}: {errorMessage}",
            actionTaken,
            failures,
            _options.HaltThreshold);

        await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);

        // Transition to protective mode if threshold reached
        if (failures >= _options.HaltThreshold)
        {
            await _tradingState.TransitionToAsync(
                TradingState.Degraded_ProtectiveMode,
                $"Nonce failures: {failures} consecutive").ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public NonceHealthStatus GetStatus()
    {
        lock (_lock)
        {
            var cutoff = DateTimeOffset.UtcNow.AddHours(-24);
            _failureTimestamps.RemoveAll(t => t < cutoff);

            return new NonceHealthStatus
            {
                IsHealthy = _consecutiveFailures < _options.WarningThreshold,
                ConsecutiveFailures = _consecutiveFailures,
                SuccessesSinceLastFailure = _successesSinceLastFailure,
                TotalFailures24h = _failureTimestamps.Count,
                ShouldPauseTrading = _consecutiveFailures >= _options.HaltThreshold,
                LastFailure = _lastFailure,
                LastErrorMessage = _lastErrorMessage,
                Timestamp = DateTimeOffset.UtcNow
            };
        }
    }

    /// <inheritdoc />
    public void ClearFailures()
    {
        lock (_lock)
        {
            _logger.LogWarning(
                "NONCE-HEALTH: Failure count manually cleared. Previous consecutive failures: {Failures}, Successes since last failure: {Successes}",
                _consecutiveFailures, _successesSinceLastFailure);

            _consecutiveFailures = 0;
            _successesSinceLastFailure = 0;
        }
    }
}
