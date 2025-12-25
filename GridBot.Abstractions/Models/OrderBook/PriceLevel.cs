namespace GridBot.Abstractions.Models.OrderBook;

/// <summary>
/// Represents a single price level in an order book with price and quantity.
/// </summary>
public sealed record PriceLevel
{
    /// <summary>
    /// Gets the price at this level.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// Gets the total quantity available at this price level.
    /// </summary>
    public required decimal Quantity { get; init; }

    /// <summary>
    /// Gets the number of orders at this price level (if available from exchange).
    /// </summary>
    public int? OrderCount { get; init; }
}
