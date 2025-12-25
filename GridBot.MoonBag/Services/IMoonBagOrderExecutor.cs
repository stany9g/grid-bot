namespace GridBot.MoonBag.Services;

/// <summary>
/// Order execution result.
/// </summary>
public sealed record MoonBagOrderResult(
    bool Success,
    string? TxHash,
    string? ErrorMessage,
    long? OrderId);

/// <summary>
/// Abstraction for order execution required by trailing stop service.
/// Implemented by GridBot.ApiService to interact with Lighter DEX.
/// </summary>
public interface IMoonBagOrderExecutor
{
    /// <summary>
    /// Gets the current position size for a market.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Position size (absolute value).</returns>
    Task<decimal> GetPositionSizeAsync(int marketId, CancellationToken ct = default);

    /// <summary>
    /// Creates a market sell order (reduce-only) for trailing stop execution.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="quantity">Quantity to sell.</param>
    /// <param name="price">Limit price with slippage.</param>
    /// <param name="isLong">Position direction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Order execution result.</returns>
    Task<MoonBagOrderResult> ExecuteTrailingStopSellAsync(
        int marketId,
        decimal quantity,
        decimal price,
        bool isLong,
        CancellationToken ct = default);

    /// <summary>
    /// Creates or updates a stop-loss limit order.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="quantity">Quantity.</param>
    /// <param name="stopPrice">Trigger price.</param>
    /// <param name="limitPrice">Execution price.</param>
    /// <param name="isLong">Position direction.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Order result with order ID.</returns>
    Task<MoonBagOrderResult> PlaceStopLossOrderAsync(
        int marketId,
        decimal quantity,
        decimal stopPrice,
        decimal limitPrice,
        bool isLong,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels an existing order.
    /// </summary>
    /// <param name="marketId">Market ID.</param>
    /// <param name="orderId">Order ID to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if cancelled successfully.</returns>
    Task<bool> CancelOrderAsync(int marketId, long orderId, CancellationToken ct = default);
}
