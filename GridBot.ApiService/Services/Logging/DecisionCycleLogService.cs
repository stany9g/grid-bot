using System.Collections.Concurrent;
using GridBot.ApiService.Models.Logging;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Logging;

/// <summary>
/// Thread-safe ring buffer implementation for storing decision cycle log entries.
/// Uses a ConcurrentQueue as backing store with capacity management.
/// </summary>
public sealed class DecisionCycleLogService : IDecisionCycleLogService
{
    /// <summary>
    /// Default capacity: ~12 hours of history at 5-second cycle intervals.
    /// Memory usage: ~5-7 MB at full capacity.
    /// </summary>
    private const int DefaultMaxCapacity = 8640;

    private readonly ConcurrentQueue<DecisionCycleLogEntry> _entries = new();
    private readonly object _trimLock = new();
    private readonly int _maxCapacity;
    private readonly ILogger<DecisionCycleLogService> _logger;

    /// <inheritdoc />
    public event EventHandler<DecisionCycleLogEntry>? EntryAdded;

    /// <summary>
    /// Creates a new DecisionCycleLogService with the default capacity of 500 entries.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public DecisionCycleLogService(ILogger<DecisionCycleLogService> logger)
        : this(logger, DefaultMaxCapacity)
    {
    }

    /// <summary>
    /// Creates a new DecisionCycleLogService with a custom capacity.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="maxCapacity">Maximum number of entries to store.</param>
    public DecisionCycleLogService(ILogger<DecisionCycleLogService> logger, int maxCapacity)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (maxCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCapacity), "Capacity must be greater than zero.");
        }

        _logger = logger;
        _maxCapacity = maxCapacity;

        _logger.LogDebug("DecisionCycleLogService initialized with capacity {Capacity}", _maxCapacity);
    }

    /// <inheritdoc />
    public void AddEntry(DecisionCycleLogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entries.Enqueue(entry);

        // Trim if over capacity
        TrimIfNeeded();

        // Fire event
        EntryAdded?.Invoke(this, entry);

        _logger.LogTrace(
            "Decision cycle log entry added: Market={MarketId}, Price={Price:F2}, Duration={Duration:F0}ms",
            entry.MarketId, entry.CurrentPrice, entry.DurationMs);
    }

    /// <inheritdoc />
    public IReadOnlyList<DecisionCycleLogEntry> GetEntries()
    {
        return _entries
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<DecisionCycleLogEntry> GetEntries(DateTimeOffset from, DateTimeOffset to)
    {
        return _entries
            .Where(e => e.Timestamp >= from && e.Timestamp <= to)
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<DecisionCycleLogEntry> GetLatestEntries(int count)
    {
        if (count <= 0)
        {
            return [];
        }

        return _entries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .ToList();
    }

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public DateTimeOffset? OldestEntryTime
    {
        get
        {
            var oldest = _entries.MinBy(e => e.Timestamp);
            return oldest?.Timestamp;
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? NewestEntryTime
    {
        get
        {
            var newest = _entries.MaxBy(e => e.Timestamp);
            return newest?.Timestamp;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_trimLock)
        {
            while (_entries.TryDequeue(out _))
            {
                // Empty the queue
            }
        }

        _logger.LogDebug("Decision cycle log buffer cleared");
    }

    /// <summary>
    /// Removes oldest entries if buffer exceeds capacity.
    /// Uses lock to ensure thread-safe trimming.
    /// </summary>
    private void TrimIfNeeded()
    {
        if (_entries.Count <= _maxCapacity)
        {
            return;
        }

        lock (_trimLock)
        {
            // Re-check inside lock (double-check pattern)
            while (_entries.Count > _maxCapacity)
            {
                if (!_entries.TryDequeue(out var removed))
                {
                    break;
                }

                _logger.LogTrace(
                    "Trimmed oldest entry from decision cycle log: Timestamp={Timestamp}",
                    removed.Timestamp);
            }
        }
    }
}
