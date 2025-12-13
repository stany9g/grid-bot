using GridBot.ApiService.Models.Logging;

namespace GridBot.ApiService.Services.Logging;

/// <summary>
/// Service for storing decision cycle log entries in a ring buffer.
/// Provides thread-safe access to recent decision cycle history.
/// </summary>
public interface IDecisionCycleLogService
{
    /// <summary>
    /// Add a new log entry to the buffer.
    /// If the buffer is at capacity, the oldest entry will be removed.
    /// </summary>
    /// <param name="entry">The log entry to add.</param>
    void AddEntry(DecisionCycleLogEntry entry);

    /// <summary>
    /// Get all entries, sorted by timestamp descending (newest first).
    /// </summary>
    /// <returns>Read-only list of all entries.</returns>
    IReadOnlyList<DecisionCycleLogEntry> GetEntries();

    /// <summary>
    /// Get entries within a time range, sorted by timestamp descending (newest first).
    /// </summary>
    /// <param name="from">Start of the time range (inclusive).</param>
    /// <param name="to">End of the time range (inclusive).</param>
    /// <returns>Read-only list of entries within the range.</returns>
    IReadOnlyList<DecisionCycleLogEntry> GetEntries(DateTimeOffset from, DateTimeOffset to);

    /// <summary>
    /// Get the latest N entries, sorted by timestamp descending (newest first).
    /// </summary>
    /// <param name="count">Maximum number of entries to return.</param>
    /// <returns>Read-only list of the latest entries.</returns>
    IReadOnlyList<DecisionCycleLogEntry> GetLatestEntries(int count);

    /// <summary>
    /// Get the current number of entries in the buffer.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Get the timestamp of the oldest entry in the buffer.
    /// Returns null if the buffer is empty.
    /// </summary>
    DateTimeOffset? OldestEntryTime { get; }

    /// <summary>
    /// Get the timestamp of the newest entry in the buffer.
    /// Returns null if the buffer is empty.
    /// </summary>
    DateTimeOffset? NewestEntryTime { get; }

    /// <summary>
    /// Event fired when a new entry is added to the buffer.
    /// </summary>
    event EventHandler<DecisionCycleLogEntry>? EntryAdded;

    /// <summary>
    /// Clear all entries from the buffer.
    /// </summary>
    void Clear();
}
