using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.Api;

/// <summary>
/// Represents an individual order in the order book (bid or ask).
/// </summary>
public sealed class OrderBookOrder
{
    /// <summary>
    /// Order index in the system.
    /// </summary>
    [JsonPropertyName("order_index")]
    public long OrderIndex { get; set; }

    /// <summary>
    /// Order ID (returned as string from API).
    /// </summary>
    [JsonPropertyName("order_id")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Account index of the order owner.
    /// </summary>
    [JsonPropertyName("owner_account_index")]
    public long OwnerAccountIndex { get; set; }

    /// <summary>
    /// Initial base amount when order was placed.
    /// </summary>
    [JsonPropertyName("initial_base_amount")]
    public string InitialBaseAmount { get; set; } = "0";

    /// <summary>
    /// Remaining base amount to be filled.
    /// </summary>
    [JsonPropertyName("remaining_base_amount")]
    public string RemainingBaseAmount { get; set; } = "0";

    /// <summary>
    /// Order price.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; set; } = "0";

    /// <summary>
    /// Order expiry timestamp (Unix timestamp in milliseconds).
    /// </summary>
    [JsonPropertyName("order_expiry")]
    public long OrderExpiry { get; set; }
}

/// <summary>
/// Response wrapper for order book orders (bids and asks) query.
/// </summary>
public sealed class OrderBookOrdersResponse
{
    /// <summary>
    /// Response code. 200 indicates success.
    /// </summary>
    [JsonPropertyName("code")]
    public int Code { get; set; }

    /// <summary>
    /// Response message.
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Total number of ask orders in the order book.
    /// </summary>
    [JsonPropertyName("total_asks")]
    public int TotalAsks { get; set; }

    /// <summary>
    /// Total number of bid orders in the order book.
    /// </summary>
    [JsonPropertyName("total_bids")]
    public int TotalBids { get; set; }

    /// <summary>
    /// List of ask orders (sell orders) sorted by price ascending.
    /// </summary>
    [JsonPropertyName("asks")]
    public List<OrderBookOrder> Asks { get; set; } = new();

    /// <summary>
    /// List of bid orders (buy orders) sorted by price descending.
    /// </summary>
    [JsonPropertyName("bids")]
    public List<OrderBookOrder> Bids { get; set; } = new();

    /// <summary>
    /// Gets whether the operation was successful (code == 200 or 0).
    /// </summary>
    [JsonIgnore]
    public bool IsSuccess => Code == 200 || Code == 0;
}
