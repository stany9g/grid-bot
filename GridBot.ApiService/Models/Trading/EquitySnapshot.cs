namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents a point-in-time equity snapshot for validation and fallback calculations.
/// Immutable record for thread-safe access.
/// </summary>
public sealed class EquitySnapshot
{
    /// <summary>
    /// UTC timestamp of this snapshot.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Market ID for this snapshot.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Total equity value in USD.
    /// </summary>
    public required decimal Equity { get; init; }

    /// <summary>
    /// Unrealized P&amp;L at snapshot time.
    /// </summary>
    public decimal UnrealizedPnl { get; init; }

    /// <summary>
    /// Creates a new equity snapshot.
    /// </summary>
    public static EquitySnapshot Create(int marketId, decimal equity, decimal unrealizedPnl = 0m)
    {
        return new EquitySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            MarketId = marketId,
            Equity = equity,
            UnrealizedPnl = unrealizedPnl
        };
    }
}
