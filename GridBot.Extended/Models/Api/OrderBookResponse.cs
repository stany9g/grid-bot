using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Order book response from Extended API.
/// </summary>
public sealed record OrderBookResponse
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Bid orders (price, size) sorted by price descending.
    /// </summary>
    [JsonPropertyName("bids")]
    public required IReadOnlyList<OrderBookLevel> Bids { get; init; }

    /// <summary>
    /// Ask orders (price, size) sorted by price ascending.
    /// </summary>
    [JsonPropertyName("asks")]
    public required IReadOnlyList<OrderBookLevel> Asks { get; init; }

    /// <summary>
    /// Timestamp of the snapshot.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }
}

/// <summary>
/// Single price level in the order book.
/// </summary>
public sealed record OrderBookLevel
{
    /// <summary>
    /// Price at this level.
    /// </summary>
    [JsonPropertyName("price")]
    public required string Price { get; init; }

    /// <summary>
    /// Total size at this level.
    /// </summary>
    [JsonPropertyName("size")]
    public required string Size { get; init; }
}
