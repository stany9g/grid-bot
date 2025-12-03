namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Result of a rebalancing operation.
/// </summary>
public sealed class RebalanceResult
{
    /// <summary>
    /// Whether the rebalance operation completed successfully.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Amount that was rebalanced (in portfolio percentage points).
    /// </summary>
    public decimal AmountRebalanced { get; init; }

    /// <summary>
    /// New crypto skew after rebalancing.
    /// </summary>
    public decimal NewCryptoSkew { get; init; }

    /// <summary>
    /// Error message if rebalance failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// When the rebalance was executed.
    /// </summary>
    public DateTimeOffset ExecutedAt { get; init; }

    /// <summary>
    /// Transaction hash from the exchange (if applicable).
    /// </summary>
    public string? TransactionHash { get; init; }

    /// <summary>
    /// Amount of crypto bought or sold.
    /// Positive = bought, Negative = sold.
    /// </summary>
    public decimal CryptoAmount { get; init; }

    /// <summary>
    /// Price at which the rebalance was executed.
    /// </summary>
    public decimal ExecutionPrice { get; init; }

    /// <summary>
    /// Creates a successful rebalance result.
    /// </summary>
    public static RebalanceResult Succeeded(
        decimal amountRebalanced,
        decimal newCryptoSkew,
        decimal cryptoAmount,
        decimal executionPrice,
        string? transactionHash = null)
    {
        return new RebalanceResult
        {
            Success = true,
            AmountRebalanced = amountRebalanced,
            NewCryptoSkew = newCryptoSkew,
            CryptoAmount = cryptoAmount,
            ExecutionPrice = executionPrice,
            TransactionHash = transactionHash,
            ExecutedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a failed rebalance result.
    /// </summary>
    public static RebalanceResult Failed(string errorMessage)
    {
        return new RebalanceResult
        {
            Success = false,
            ErrorMessage = errorMessage,
            ExecutedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a result indicating no rebalance was needed.
    /// </summary>
    public static RebalanceResult NotNeeded(decimal currentSkew)
    {
        return new RebalanceResult
        {
            Success = true,
            AmountRebalanced = 0m,
            NewCryptoSkew = currentSkew,
            ExecutedAt = DateTimeOffset.UtcNow
        };
    }
}
