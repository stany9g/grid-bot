using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended;

/// <summary>
/// Thread-safe nonce manager for Stark signature transactions.
/// CRITICAL: Nonces must only increment, never decrement. Gaps are allowed but lower values are rejected.
/// </summary>
/// <remarks>
/// Thread safety is ensured via lock. All operations that read or modify the nonce
/// must acquire the lock to prevent race conditions in concurrent order submission.
/// </remarks>
public sealed class NonceManager
{
    private readonly ILogger<NonceManager> _logger;
    private readonly object _lock = new();
    private long _currentNonce;
    private bool _isInitialized;
    private DateTimeOffset _lastSyncTime;

    /// <summary>
    /// Initializes a new instance of the <see cref="NonceManager"/> class.
    /// </summary>
    /// <param name="options">Extended options containing initial nonce.</param>
    /// <param name="logger">Logger instance.</param>
    public NonceManager(IOptions<ExtendedOptions> options, ILogger<NonceManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentNonce = options.Value.InitialNonce;
        _isInitialized = options.Value.InitialNonce > 0;
    }

    /// <summary>
    /// Gets whether the nonce manager is initialized (synced from server).
    /// </summary>
    public bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return _isInitialized;
            }
        }
    }

    /// <summary>
    /// Gets the current nonce value (for inspection only, do not use directly for signing).
    /// </summary>
    public long CurrentNonce
    {
        get
        {
            lock (_lock)
            {
                return _currentNonce;
            }
        }
    }

    /// <summary>
    /// Gets the time since last server synchronization.
    /// </summary>
    public TimeSpan? TimeSinceLastSync
    {
        get
        {
            lock (_lock)
            {
                return _lastSyncTime == default ? null : DateTimeOffset.UtcNow - _lastSyncTime;
            }
        }
    }

    /// <summary>
    /// Gets the next nonce for signing and atomically increments the counter.
    /// CRITICAL: Once a nonce is consumed, it cannot be reused even if the operation fails.
    /// </summary>
    /// <returns>The nonce to use for the next transaction.</returns>
    /// <exception cref="InvalidOperationException">Thrown if nonce manager is not initialized.</exception>
    /// <exception cref="InvalidOperationException">Thrown if nonce has reached maximum value.</exception>
    public long GetNextNonce()
    {
        lock (_lock)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException(
                    "NonceManager not initialized. Call SyncFromServer before using.");
            }

            if (_currentNonce >= ExtendedConstants.MaxNonceValue)
            {
                _logger.LogCritical(
                    "NONCE EXHAUSTED: Current nonce {Nonce} has reached maximum {Max}. " +
                    "This account cannot submit more transactions.",
                    _currentNonce, ExtendedConstants.MaxNonceValue);
                throw new InvalidOperationException(
                    "Nonce has reached maximum value. Account cannot submit more transactions.");
            }

            // Increment and return the new value
            var nonce = ++_currentNonce;

            // Warn when approaching limit
            if (_currentNonce >= ExtendedConstants.NonceWarningThreshold)
            {
                _logger.LogWarning(
                    "Nonce approaching limit: {Current} / {Max}. Consider creating a new account.",
                    _currentNonce, ExtendedConstants.MaxNonceValue);
            }

            _logger.LogTrace("Consumed nonce: {Nonce}", nonce);
            return nonce;
        }
    }

    /// <summary>
    /// Peeks at the next nonce value without consuming it.
    /// Use for read-only scenarios like displaying pending nonce.
    /// </summary>
    /// <returns>The next nonce value that would be used.</returns>
    public long PeekNextNonce()
    {
        lock (_lock)
        {
            return _currentNonce + 1;
        }
    }

    /// <summary>
    /// Synchronizes the nonce from server value.
    /// CRITICAL: If server nonce is lower than local, this indicates corruption and should be investigated.
    /// </summary>
    /// <param name="serverNonce">The nonce value from the server.</param>
    /// <exception cref="InvalidOperationException">Thrown if server nonce is lower than local nonce.</exception>
    public void SyncFromServer(long serverNonce)
    {
        lock (_lock)
        {
            if (_isInitialized && serverNonce < _currentNonce)
            {
                _logger.LogCritical(
                    "SERVER NONCE LOWER THAN LOCAL: Server={Server}, Local={Local}. " +
                    "This may indicate server rollback or data corruption. " +
                    "HALTING operations - manual investigation required.",
                    serverNonce, _currentNonce);
                throw new InvalidOperationException(
                    $"Server nonce ({serverNonce}) is lower than local nonce ({_currentNonce}). " +
                    "Manual investigation required.");
            }

            var oldNonce = _currentNonce;
            _currentNonce = serverNonce;
            _isInitialized = true;
            _lastSyncTime = DateTimeOffset.UtcNow;

            if (serverNonce > oldNonce && oldNonce > 0)
            {
                _logger.LogInformation(
                    "Nonce synchronized from server: {Old} -> {New} (gap of {Gap})",
                    oldNonce, serverNonce, serverNonce - oldNonce);
            }
            else
            {
                _logger.LogInformation("Nonce initialized from server: {Nonce}", serverNonce);
            }
        }
    }

    /// <summary>
    /// Marks a specific nonce as used (for tracking purposes only).
    /// Does not affect the internal counter - use GetNextNonce for that.
    /// </summary>
    /// <param name="nonce">The nonce that was used.</param>
    public void MarkNonceUsed(long nonce)
    {
        // This is for logging/auditing purposes
        _logger.LogDebug("Nonce {Nonce} marked as used", nonce);
    }

    /// <summary>
    /// Validates that a proposed nonce is acceptable.
    /// </summary>
    /// <param name="proposedNonce">The nonce to validate.</param>
    /// <returns>True if the nonce is valid for use.</returns>
    public bool ValidateNonce(long proposedNonce)
    {
        lock (_lock)
        {
            if (!_isInitialized)
                return false;

            // Nonce must be greater than current (we increment before returning)
            // and less than or equal to max
            return proposedNonce > _currentNonce && proposedNonce <= ExtendedConstants.MaxNonceValue;
        }
    }

    /// <summary>
    /// Gets diagnostics information about the nonce manager state.
    /// </summary>
    /// <returns>Diagnostic string.</returns>
    public string GetDiagnostics()
    {
        lock (_lock)
        {
            var remaining = ExtendedConstants.MaxNonceValue - _currentNonce;
            var percentUsed = (double)_currentNonce / ExtendedConstants.MaxNonceValue * 100;
            var syncAge = _lastSyncTime == default ? "never" : TimeSinceLastSync?.ToString() ?? "unknown";

            return $"Nonce: {_currentNonce:N0}, " +
                   $"Remaining: {remaining:N0}, " +
                   $"Used: {percentUsed:F4}%, " +
                   $"Initialized: {_isInitialized}, " +
                   $"Last Sync: {syncAge}";
        }
    }
}
