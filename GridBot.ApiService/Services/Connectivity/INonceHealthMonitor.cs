namespace GridBot.ApiService.Services.Connectivity;

/// <summary>
/// Monitors nonce operation health and detects persistent failures.
/// Implements H.6 HIGH: Nonce Failure Alert specification.
///
/// Thresholds:
/// - Warning threshold: 2 consecutive failures -> alert_operator
/// - Halt threshold: 3 consecutive failures -> pause_trading
/// - Reset after: 10 successful operations -> reset_failure_count
/// - Emergency escalation: failure during emergency operation -> escalate_to_critical
/// </summary>
public interface INonceHealthMonitor
{
    /// <summary>
    /// Whether nonce operations are healthy (below warning threshold).
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Current consecutive failure count.
    /// </summary>
    int ConsecutiveFailures { get; }

    /// <summary>
    /// Whether trading should be paused due to nonce issues.
    /// True when consecutive failures >= halt threshold (3).
    /// </summary>
    bool ShouldPauseTrading { get; }

    /// <summary>
    /// Total nonce failures in the last 24 hours.
    /// </summary>
    int TotalFailures24h { get; }

    /// <summary>
    /// Records a successful nonce operation.
    /// After 10 consecutive successes, the failure count is reset.
    /// </summary>
    void RecordSuccess();

    /// <summary>
    /// Records a failed nonce operation.
    /// </summary>
    /// <param name="errorMessage">The error message from the failure.</param>
    /// <param name="isEmergencyOperation">Whether this was during an emergency operation (e.g., trailing stop, position reduction). Emergency failures are escalated to CRITICAL.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordFailureAsync(string errorMessage, bool isEmergencyOperation = false, CancellationToken ct = default);

    /// <summary>
    /// Gets the current health status snapshot.
    /// </summary>
    NonceHealthStatus GetStatus();

    /// <summary>
    /// Clears the failure count (e.g., after manual intervention or nonce resync).
    /// Logs the clear action for audit trail.
    /// </summary>
    void ClearFailures();
}

/// <summary>
/// Nonce health status snapshot with all relevant metrics.
/// </summary>
public sealed record NonceHealthStatus
{
    /// <summary>
    /// Whether nonce operations are healthy (below warning threshold).
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>
    /// Current consecutive failure count.
    /// </summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>
    /// Number of successful operations since last failure.
    /// Used to track progress toward recovery (10 = reset).
    /// </summary>
    public required int SuccessesSinceLastFailure { get; init; }

    /// <summary>
    /// Total failures in the last 24 hours.
    /// </summary>
    public required int TotalFailures24h { get; init; }

    /// <summary>
    /// Whether trading should be paused due to nonce issues.
    /// </summary>
    public required bool ShouldPauseTrading { get; init; }

    /// <summary>
    /// When the last failure occurred.
    /// </summary>
    public DateTimeOffset? LastFailure { get; init; }

    /// <summary>
    /// The error message from the last failure.
    /// </summary>
    public string? LastErrorMessage { get; init; }

    /// <summary>
    /// When this status snapshot was created.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Creates a healthy status snapshot.
    /// </summary>
    public static NonceHealthStatus Healthy() => new()
    {
        IsHealthy = true,
        ConsecutiveFailures = 0,
        SuccessesSinceLastFailure = 0,
        TotalFailures24h = 0,
        ShouldPauseTrading = false,
        LastFailure = null,
        LastErrorMessage = null,
        Timestamp = DateTimeOffset.UtcNow
    };
}
