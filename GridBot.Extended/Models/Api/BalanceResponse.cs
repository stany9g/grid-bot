using System.Text.Json.Serialization;

namespace GridBot.Extended.Models.Api;

/// <summary>
/// Balance information from Extended API.
/// Matches the Python SDK BalanceModel from x10/perpetual/balances.py.
/// Note: API returns numeric values as strings, so we use JsonNumberHandling.
/// </summary>
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed record BalanceResponse
{
    /// <summary>
    /// Collateral asset name (e.g., "USD").
    /// </summary>
    [JsonPropertyName("collateralName")]
    public required string CollateralName { get; init; }

    /// <summary>
    /// Account status (e.g., "ACTIVE").
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }

    /// <summary>
    /// Total balance.
    /// </summary>
    [JsonPropertyName("balance")]
    public required decimal Balance { get; init; }

    /// <summary>
    /// Equity (balance + unrealized PnL).
    /// </summary>
    [JsonPropertyName("equity")]
    public required decimal Equity { get; init; }

    /// <summary>
    /// Spot equity.
    /// </summary>
    [JsonPropertyName("spotEquity")]
    public decimal SpotEquity { get; init; }

    /// <summary>
    /// Spot equity available for trade.
    /// </summary>
    [JsonPropertyName("spotEquityForAvailableForTrade")]
    public decimal SpotEquityForAvailableForTrade { get; init; }

    /// <summary>
    /// Available for trade (can be used for new positions).
    /// </summary>
    [JsonPropertyName("availableForTrade")]
    public required decimal AvailableForTrade { get; init; }

    /// <summary>
    /// Available for withdrawal.
    /// </summary>
    [JsonPropertyName("availableForWithdrawal")]
    public required decimal AvailableForWithdrawal { get; init; }

    /// <summary>
    /// Unrealized profit and loss from open positions.
    /// </summary>
    [JsonPropertyName("unrealisedPnl")]
    public required decimal UnrealisedPnl { get; init; }

    /// <summary>
    /// Initial margin required for positions.
    /// </summary>
    [JsonPropertyName("initialMargin")]
    public required decimal InitialMargin { get; init; }

    /// <summary>
    /// Margin ratio (for liquidation calculations).
    /// </summary>
    [JsonPropertyName("marginRatio")]
    public required decimal MarginRatio { get; init; }

    /// <summary>
    /// Timestamp of last update (Unix milliseconds).
    /// </summary>
    [JsonPropertyName("updatedTime")]
    public required long UpdatedTime { get; init; }

    /// <summary>
    /// Position exposure.
    /// </summary>
    [JsonPropertyName("exposure")]
    public decimal Exposure { get; init; }

    /// <summary>
    /// Account leverage.
    /// </summary>
    [JsonPropertyName("leverage")]
    public decimal Leverage { get; init; }
}
