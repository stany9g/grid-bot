namespace GridBot.Abstractions.Models.Orders;

/// <summary>
/// Represents the result of a batch order operation.
/// </summary>
public sealed record BatchOrderResult
{
    /// <summary>
    /// Gets whether all orders in the batch were successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the individual results for each order in the batch.
    /// </summary>
    public required IReadOnlyList<OrderResult> Results { get; init; }

    /// <summary>
    /// Gets the transaction hash if the batch was submitted as a single transaction.
    /// </summary>
    public string? TransactionHash { get; init; }

    /// <summary>
    /// Gets the count of successful orders.
    /// </summary>
    public int SuccessCount => Results.Count(r => r.IsSuccess);

    /// <summary>
    /// Gets the count of failed orders.
    /// </summary>
    public int FailureCount => Results.Count(r => !r.IsSuccess);

    /// <summary>
    /// Gets the failed order results.
    /// </summary>
    public IEnumerable<OrderResult> FailedOrders => Results.Where(r => !r.IsSuccess);

    /// <summary>
    /// Creates a successful batch result.
    /// </summary>
    /// <param name="results">The individual order results.</param>
    /// <param name="txHash">Optional transaction hash.</param>
    /// <returns>A batch result.</returns>
    public static BatchOrderResult FromResults(IReadOnlyList<OrderResult> results, string? txHash = null) =>
        new()
        {
            IsSuccess = results.All(r => r.IsSuccess),
            Results = results,
            TransactionHash = txHash
        };
}
