namespace GridBot.Abstractions.Models.Account;

/// <summary>
/// Represents account information including balances and positions.
/// </summary>
public sealed record AccountInfo
{
    /// <summary>
    /// Gets the unique account identifier.
    /// </summary>
    public required string AccountId { get; init; }

    /// <summary>
    /// Gets the total collateral (margin) in the account.
    /// </summary>
    public required decimal Collateral { get; init; }

    /// <summary>
    /// Gets the available balance for new positions.
    /// </summary>
    public required decimal AvailableBalance { get; init; }

    /// <summary>
    /// Gets the total portfolio value including unrealized PnL.
    /// </summary>
    public required decimal PortfolioValue { get; init; }

    /// <summary>
    /// Gets all open positions keyed by market ID.
    /// </summary>
    public required IReadOnlyDictionary<string, PositionInfo> Positions { get; init; }

    /// <summary>
    /// Gets the timestamp when this account info was last updated.
    /// </summary>
    public required DateTimeOffset LastUpdated { get; init; }

    /// <summary>
    /// Gets the total unrealized PnL across all positions.
    /// </summary>
    public decimal TotalUnrealizedPnl => Positions.Values.Sum(p => p.UnrealizedPnl);

    /// <summary>
    /// Gets whether the account has any open positions.
    /// </summary>
    public bool HasOpenPositions => Positions.Count > 0;
}
