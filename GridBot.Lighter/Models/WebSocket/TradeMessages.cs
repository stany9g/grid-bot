using System.Text.Json.Serialization;

namespace GridBot.Lighter.Models.WebSocket;

/// <summary>
/// Trade update message.
/// Channel: trade/{MARKET_INDEX}
/// </summary>
public sealed record TradeMessage : WebSocketMessage
{
    /// <summary>
    /// Trade data.
    /// </summary>
    [JsonPropertyName("trades")]
    public List<TradeData>? Trades { get; init; }
}
