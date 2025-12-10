using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Notification message.
/// Channel: notification/{ACCOUNT_ID}
/// </summary>
public sealed record NotificationMessage : WebSocketMessage
{
    /// <summary>
    /// Notification items.
    /// </summary>
    [JsonPropertyName("notifs")]
    public List<NotificationItem>? Notifs { get; init; }
}

/// <summary>
/// Single notification item.
/// </summary>
public sealed record NotificationItem
{
    /// <summary>
    /// Notification ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Creation timestamp.
    /// </summary>
    [JsonPropertyName("created_at")]
    public string CreatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Update timestamp.
    /// </summary>
    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; init; } = string.Empty;

    /// <summary>
    /// Notification kind (liquidation, deleverage, announcement).
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// Account index.
    /// </summary>
    [JsonPropertyName("account_index")]
    public long AccountIndex { get; init; }

    /// <summary>
    /// Notification content (varies by kind).
    /// </summary>
    [JsonPropertyName("content")]
    public NotificationContent? Content { get; init; }

    /// <summary>
    /// Whether the notification has been acknowledged.
    /// </summary>
    [JsonPropertyName("ack")]
    public bool Ack { get; init; }

    /// <summary>
    /// Acknowledgement timestamp.
    /// </summary>
    [JsonPropertyName("acked_at")]
    public string? AckedAt { get; init; }
}

/// <summary>
/// Notification content (union of all notification types).
/// </summary>
public sealed record NotificationContent
{
    // Common fields
    /// <summary>
    /// Content ID.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Market index.
    /// </summary>
    [JsonPropertyName("market_index")]
    public int MarketIndex { get; init; }

    /// <summary>
    /// Timestamp.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; }

    // Liquidation fields
    /// <summary>
    /// Whether it was an ask (liquidation).
    /// </summary>
    [JsonPropertyName("is_ask")]
    public bool IsAsk { get; init; }

    /// <summary>
    /// USDC amount.
    /// </summary>
    [JsonPropertyName("usdc_amount")]
    public string UsdcAmount { get; init; } = "0";

    /// <summary>
    /// Size.
    /// </summary>
    [JsonPropertyName("size")]
    public string Size { get; init; } = "0";

    /// <summary>
    /// Price.
    /// </summary>
    [JsonPropertyName("price")]
    public string Price { get; init; } = "0";

    /// <summary>
    /// Average price.
    /// </summary>
    [JsonPropertyName("avg_price")]
    public string AvgPrice { get; init; } = "0";

    // Deleverage fields
    /// <summary>
    /// Settlement price (deleverage).
    /// </summary>
    [JsonPropertyName("settlement_price")]
    public string SettlementPrice { get; init; } = "0";

    // Announcement fields
    /// <summary>
    /// Announcement title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    /// <summary>
    /// Announcement content.
    /// </summary>
    [JsonPropertyName("content")]
    public string ContentText { get; init; } = string.Empty;
}
