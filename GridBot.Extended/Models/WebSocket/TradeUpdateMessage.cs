namespace GridBot.Extended.Models.WebSocket;

/// <summary>
/// Trade update event for the realtime data provider.
/// </summary>
public sealed class TradeUpdateEvent
{
    /// <summary>
    /// Trade ID.
    /// </summary>
    public required string TradeId { get; init; }

    /// <summary>
    /// Associated order ID.
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// Market identifier.
    /// </summary>
    public required string MarketId { get; init; }

    /// <summary>
    /// Trade side: true for buy, false for sell.
    /// </summary>
    public required bool IsBuy { get; init; }

    /// <summary>
    /// Execution price.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Trade quantity.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// Fee paid.
    /// </summary>
    public decimal Fee { get; init; }

    /// <summary>
    /// Fee asset symbol.
    /// </summary>
    public string? FeeAsset { get; init; }

    /// <summary>
    /// Whether this was a maker trade.
    /// </summary>
    public bool IsMaker { get; init; }

    /// <summary>
    /// Timestamp of the trade.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
