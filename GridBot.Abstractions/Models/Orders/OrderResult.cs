namespace GridBot.Abstractions.Models.Orders;

/// <summary>
/// Represents the result of an order operation (create, modify, cancel).
/// </summary>
public sealed record OrderResult
{
    /// <summary>
    /// Gets whether the operation was successful.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// Gets the exchange-assigned order identifier.
    /// </summary>
    public string? OrderId { get; init; }

    /// <summary>
    /// Gets the transaction hash (for on-chain exchanges).
    /// </summary>
    public string? TransactionHash { get; init; }

    /// <summary>
    /// Gets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the error code if the operation failed.
    /// </summary>
    public int? ErrorCode { get; init; }

    /// <summary>
    /// Creates a successful order result.
    /// </summary>
    /// <param name="orderId">The exchange-assigned order identifier.</param>
    /// <param name="txHash">Optional transaction hash.</param>
    /// <returns>A successful order result.</returns>
    public static OrderResult Success(string orderId, string? txHash = null) =>
        new() { IsSuccess = true, OrderId = orderId, TransactionHash = txHash };

    /// <summary>
    /// Creates a failed order result.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="code">Optional error code.</param>
    /// <returns>A failed order result.</returns>
    public static OrderResult Failure(string message, int? code = null) =>
        new() { IsSuccess = false, ErrorMessage = message, ErrorCode = code };
}
