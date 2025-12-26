namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Balance update event for the realtime data provider.
/// </summary>
public sealed class BalanceUpdateEvent
{
    /// <summary>
    /// Asset symbol.
    /// </summary>
    public required string Asset { get; init; }

    /// <summary>
    /// Total balance.
    /// </summary>
    public required decimal Total { get; init; }

    /// <summary>
    /// Available balance.
    /// </summary>
    public required decimal Available { get; init; }

    /// <summary>
    /// Locked balance.
    /// </summary>
    public decimal Locked { get; init; }

    /// <summary>
    /// Timestamp of the update.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
