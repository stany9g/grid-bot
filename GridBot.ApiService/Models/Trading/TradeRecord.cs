namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Represents a single trade record for rolling P&amp;L calculation.
/// Immutable record for thread-safe access.
/// </summary>
public sealed class TradeRecord
{
    /// <summary>
    /// Unique identifier for this trade record.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Market ID where trade occurred.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// UTC timestamp when trade was executed.
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// P&amp;L of this trade as percentage of equity at time of trade.
    /// Negative values indicate losses.
    /// </summary>
    public required decimal PnlPercent { get; init; }

    /// <summary>
    /// Absolute P&amp;L in USD.
    /// </summary>
    public required decimal PnlUsd { get; init; }

    /// <summary>
    /// Equity value at time of trade (for percentage calculations).
    /// </summary>
    public required decimal EquityAtTrade { get; init; }

    /// <summary>
    /// Order ID that generated this P&amp;L (for audit trail).
    /// </summary>
    public string? OrderId { get; init; }

    /// <summary>
    /// Creates a new trade record from P&amp;L data.
    /// </summary>
    public static TradeRecord Create(
        int marketId,
        decimal pnlPercent,
        decimal currentEquity,
        string? orderId = null)
    {
        var pnlUsd = currentEquity * (pnlPercent / 100m);

        return new TradeRecord
        {
            Id = Guid.NewGuid(),
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            PnlPercent = pnlPercent,
            PnlUsd = pnlUsd,
            EquityAtTrade = currentEquity,
            OrderId = orderId
        };
    }
}
