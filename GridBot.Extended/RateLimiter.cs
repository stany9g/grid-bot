using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Extended;

/// <summary>
/// Priority levels for rate-limited requests.
/// </summary>
public enum RequestPriority
{
    /// <summary>
    /// Critical requests (cancel, get positions) - always allowed, bypass soft limit.
    /// </summary>
    Critical = 0,

    /// <summary>
    /// High priority (create order, modify order) - allowed up to 95% limit.
    /// </summary>
    High = 1,

    /// <summary>
    /// Medium priority (get orders, get balance) - throttled at 80% limit.
    /// </summary>
    Medium = 2,

    /// <summary>
    /// Low priority (get markets, get orderbook, get candles) - deferred when above 70% limit.
    /// </summary>
    Low = 3
}

/// <summary>
/// Proactive rate limiter for Extended API requests.
/// Implements sliding window rate limiting with priority-based throttling.
/// </summary>
/// <remarks>
/// Thread-safe implementation using lock-based synchronization.
/// Implements safety margins to avoid hitting hard limits:
/// - 70% of limit: Low priority requests start getting throttled
/// - 80% of limit: Medium priority requests start getting throttled
/// - 95% of limit: High priority requests start getting throttled
/// - Critical requests always proceed (for emergency cancellations)
/// </remarks>
public sealed class RateLimiter
{
    private readonly ILogger<RateLimiter> _logger;
    private readonly bool _isMarketMaker;
    private readonly int _requestLimit;
    private readonly TimeSpan _windowDuration;
    private readonly object _lock = new();
    private readonly Queue<DateTimeOffset> _requestTimestamps = new();
    private DateTimeOffset _rateLimitedUntil = DateTimeOffset.MinValue;
    private int _consecutiveRateLimitHits;

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimiter"/> class.
    /// </summary>
    /// <param name="options">Extended options.</param>
    /// <param name="logger">Logger instance.</param>
    public RateLimiter(IOptions<ExtendedOptions> options, ILogger<RateLimiter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isMarketMaker = options.Value.IsMarketMaker;

        if (_isMarketMaker)
        {
            _requestLimit = ExtendedConstants.MarketMakerRateLimitPer5Min;
            _windowDuration = TimeSpan.FromMinutes(5);
        }
        else
        {
            _requestLimit = ExtendedConstants.StandardRateLimitPerMinute;
            _windowDuration = TimeSpan.FromMinutes(1);
        }
    }

    /// <summary>
    /// Gets the current utilization as a percentage (0-100).
    /// </summary>
    public double CurrentUtilization
    {
        get
        {
            lock (_lock)
            {
                CleanupOldTimestamps();
                return (double)_requestTimestamps.Count / _requestLimit * 100;
            }
        }
    }

    /// <summary>
    /// Gets the number of requests remaining in the current window.
    /// </summary>
    public int RemainingRequests
    {
        get
        {
            lock (_lock)
            {
                CleanupOldTimestamps();
                return Math.Max(0, _requestLimit - _requestTimestamps.Count);
            }
        }
    }

    /// <summary>
    /// Gets whether the rate limiter is currently in backoff mode due to hitting a 429.
    /// </summary>
    public bool IsInBackoff
    {
        get
        {
            lock (_lock)
            {
                return DateTimeOffset.UtcNow < _rateLimitedUntil;
            }
        }
    }

    /// <summary>
    /// Checks if a request with the given priority can proceed.
    /// Does not record the request - use RecordRequest after successful execution.
    /// </summary>
    /// <param name="priority">The request priority.</param>
    /// <returns>True if the request can proceed, false if it should be throttled.</returns>
    public bool CanProceed(RequestPriority priority)
    {
        lock (_lock)
        {
            // Check backoff first
            if (DateTimeOffset.UtcNow < _rateLimitedUntil)
            {
                // Only critical requests can proceed during backoff
                return priority == RequestPriority.Critical;
            }

            CleanupOldTimestamps();
            var utilization = (double)_requestTimestamps.Count / _requestLimit;

            return priority switch
            {
                RequestPriority.Critical => true, // Always allowed
                RequestPriority.High => utilization < ExtendedConstants.RateLimitCriticalThreshold,
                RequestPriority.Medium => utilization < ExtendedConstants.RateLimitSafetyMargin,
                RequestPriority.Low => utilization < 0.70,
                _ => utilization < ExtendedConstants.RateLimitSafetyMargin
            };
        }
    }

    /// <summary>
    /// Gets the recommended delay before making a request of the given priority.
    /// </summary>
    /// <param name="priority">The request priority.</param>
    /// <returns>Recommended delay, or TimeSpan.Zero if request can proceed immediately.</returns>
    public TimeSpan GetRecommendedDelay(RequestPriority priority)
    {
        lock (_lock)
        {
            // Check backoff first
            var now = DateTimeOffset.UtcNow;
            if (now < _rateLimitedUntil)
            {
                if (priority == RequestPriority.Critical)
                    return TimeSpan.Zero;

                return _rateLimitedUntil - now;
            }

            if (CanProceed(priority))
                return TimeSpan.Zero;

            CleanupOldTimestamps();
            var utilization = (double)_requestTimestamps.Count / _requestLimit;

            // Calculate delay based on utilization
            if (utilization >= ExtendedConstants.RateLimitSafetyMargin)
            {
                // Add delay proportional to how far over the soft limit we are
                var excessUtilization = utilization - ExtendedConstants.RateLimitSafetyMargin;
                var delayMs = (int)(excessUtilization * 500); // 50ms per 0.1 over 80%
                return TimeSpan.FromMilliseconds(Math.Min(delayMs, 1000));
            }

            return TimeSpan.FromMilliseconds(50);
        }
    }

    /// <summary>
    /// Records a request for rate limiting purposes.
    /// Call this after successfully sending a request.
    /// </summary>
    public void RecordRequest()
    {
        lock (_lock)
        {
            _requestTimestamps.Enqueue(DateTimeOffset.UtcNow);
            CleanupOldTimestamps();

            if (_logger.IsEnabled(LogLevel.Trace))
            {
                _logger.LogTrace(
                    "Request recorded. Window: {Count}/{Limit} ({Percent:F1}%)",
                    _requestTimestamps.Count, _requestLimit, CurrentUtilization);
            }
        }
    }

    /// <summary>
    /// Records that a rate limit (HTTP 429) was hit.
    /// Triggers exponential backoff.
    /// </summary>
    /// <param name="retryAfterSeconds">Retry-After header value in seconds (if provided).</param>
    public void RecordRateLimitHit(int? retryAfterSeconds = null)
    {
        lock (_lock)
        {
            _consecutiveRateLimitHits++;

            // Calculate backoff duration
            TimeSpan backoffDuration;
            if (retryAfterSeconds.HasValue)
            {
                backoffDuration = TimeSpan.FromSeconds(retryAfterSeconds.Value);
            }
            else
            {
                // Exponential backoff: 1s, 2s, 4s, 8s, 16s, 32s, up to 60s
                var backoffSeconds = Math.Min(60, Math.Pow(2, _consecutiveRateLimitHits - 1));
                backoffDuration = TimeSpan.FromSeconds(backoffSeconds);
            }

            _rateLimitedUntil = DateTimeOffset.UtcNow + backoffDuration;

            _logger.LogWarning(
                "Rate limit hit (consecutive: {Count}). Backing off for {Duration}s until {Until:HH:mm:ss}",
                _consecutiveRateLimitHits, backoffDuration.TotalSeconds, _rateLimitedUntil);

            // If 3+ consecutive hits, reduce rate for next 5 minutes
            if (_consecutiveRateLimitHits >= 3)
            {
                _logger.LogWarning(
                    "Multiple consecutive rate limits detected. Consider reducing request frequency.");
            }
        }
    }

    /// <summary>
    /// Records a successful request, resetting the consecutive rate limit counter.
    /// </summary>
    public void RecordSuccessfulRequest()
    {
        lock (_lock)
        {
            if (_consecutiveRateLimitHits > 0)
            {
                _logger.LogDebug("Rate limit backoff cleared after successful request");
                _consecutiveRateLimitHits = 0;
            }
        }
    }

    /// <summary>
    /// Waits asynchronously until a request of the given priority can proceed.
    /// </summary>
    /// <param name="priority">The request priority.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the request can proceed.</returns>
    public async Task WaitAsync(RequestPriority priority, CancellationToken ct = default)
    {
        while (!CanProceed(priority))
        {
            ct.ThrowIfCancellationRequested();
            var delay = GetRecommendedDelay(priority);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>
    /// Gets diagnostics information about the rate limiter state.
    /// </summary>
    /// <returns>Diagnostic string.</returns>
    public string GetDiagnostics()
    {
        lock (_lock)
        {
            CleanupOldTimestamps();
            var inBackoff = DateTimeOffset.UtcNow < _rateLimitedUntil;
            var backoffRemaining = inBackoff ? (_rateLimitedUntil - DateTimeOffset.UtcNow).TotalSeconds : 0;

            return $"Requests: {_requestTimestamps.Count}/{_requestLimit}, " +
                   $"Utilization: {CurrentUtilization:F1}%, " +
                   $"Mode: {(_isMarketMaker ? "MarketMaker" : "Standard")}, " +
                   $"Window: {_windowDuration.TotalMinutes}min, " +
                   $"Backoff: {(inBackoff ? $"{backoffRemaining:F1}s remaining" : "none")}, " +
                   $"Consecutive429s: {_consecutiveRateLimitHits}";
        }
    }

    private void CleanupOldTimestamps()
    {
        var cutoff = DateTimeOffset.UtcNow - _windowDuration;
        while (_requestTimestamps.Count > 0 && _requestTimestamps.Peek() < cutoff)
        {
            _requestTimestamps.Dequeue();
        }
    }
}
