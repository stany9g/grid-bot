using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Position information from Extended API.
/// </summary>
public sealed record PositionResponse
{
    /// <summary>
    /// Market identifier.
    /// </summary>
    [JsonPropertyName("market")]
    public required string Market { get; init; }

    /// <summary>
    /// Position size (positive for long, negative for short).
    /// </summary>
    [JsonPropertyName("size")]
    public required string Size { get; init; }

    /// <summary>
    /// Position side: "LONG" or "SHORT".
    /// </summary>
    [JsonPropertyName("side")]
    public required string Side { get; init; }

    /// <summary>
    /// Average entry price.
    /// </summary>
    [JsonPropertyName("entryPrice")]
    public required string EntryPrice { get; init; }

    /// <summary>
    /// Current mark price.
    /// </summary>
    [JsonPropertyName("markPrice")]
    public required string MarkPrice { get; init; }

    /// <summary>
    /// Unrealized PnL.
    /// </summary>
    [JsonPropertyName("unrealizedPnl")]
    public required string UnrealizedPnl { get; init; }

    /// <summary>
    /// Realized PnL.
    /// </summary>
    [JsonPropertyName("realizedPnl")]
    public string? RealizedPnl { get; init; }

    /// <summary>
    /// Current leverage.
    /// </summary>
    [JsonPropertyName("leverage")]
    public int Leverage { get; init; }

    /// <summary>
    /// Liquidation price.
    /// </summary>
    [JsonPropertyName("liquidationPrice")]
    public string? LiquidationPrice { get; init; }

    /// <summary>
    /// Margin mode: "cross" or "isolated".
    /// </summary>
    [JsonPropertyName("marginMode")]
    public string? MarginMode { get; init; }

    /// <summary>
    /// Initial margin required for this position.
    /// </summary>
    [JsonPropertyName("initialMargin")]
    public string? InitialMargin { get; init; }

    /// <summary>
    /// Maintenance margin required for this position.
    /// </summary>
    [JsonPropertyName("maintenanceMargin")]
    public string? MaintenanceMargin { get; init; }
}
