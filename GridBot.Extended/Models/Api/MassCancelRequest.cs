using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Request to cancel multiple orders at once.
/// </summary>
public sealed record MassCancelRequest
{
    /// <summary>
    /// Market to cancel orders for (optional, if not specified cancels all markets).
    /// </summary>
    [JsonPropertyName("market")]
    public string? Market { get; init; }

    /// <summary>
    /// Side to cancel: "BUY", "SELL", or null for both.
    /// </summary>
    [JsonPropertyName("side")]
    public string? Side { get; init; }

    /// <summary>
    /// Specific order IDs to cancel.
    /// </summary>
    [JsonPropertyName("orderIds")]
    public IReadOnlyList<string>? OrderIds { get; init; }
}

/// <summary>
/// Response from mass cancel operation.
/// </summary>
public sealed record MassCancelResponse
{
    /// <summary>
    /// Number of orders cancelled.
    /// </summary>
    [JsonPropertyName("cancelledCount")]
    public int CancelledCount { get; init; }

    /// <summary>
    /// IDs of cancelled orders.
    /// </summary>
    [JsonPropertyName("cancelledOrderIds")]
    public IReadOnlyList<string>? CancelledOrderIds { get; init; }

    /// <summary>
    /// Any errors that occurred during cancellation.
    /// </summary>
    [JsonPropertyName("errors")]
    public IReadOnlyList<string>? Errors { get; init; }
}
