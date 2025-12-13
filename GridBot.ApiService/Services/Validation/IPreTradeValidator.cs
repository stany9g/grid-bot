using GridBot.ApiService.Models.Trading;

namespace GridBot.ApiService.Services.Validation;

/// <summary>
/// Validates orders against market depth before submission.
/// Prevents order submission to thin order books that could result in excessive slippage or failed fills.
/// </summary>
public interface IPreTradeValidator
{
    /// <summary>
    /// Validates an order against current market depth.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="isBuy">True for buy orders, false for sell orders.</param>
    /// <param name="orderSizeUsd">Order size in USD.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with approval status and any recommended adjustments.</returns>
    /// <remarks>
    /// Validation rules:
    /// 1. IF order_size_usd > (available_depth * 0.10) THEN reduce_order_size
    /// 2. IF total_depth < $10,000 THEN reject_order
    /// 3. IF depth_data_age > 5s THEN refresh_depth_before_order
    /// 4. IF depth_on_order_side < order_size * 2 THEN reject_order
    /// 5. IF spread > 1% THEN reject_order_and_alert
    /// </remarks>
    Task<PreTradeValidation> ValidateOrderAsync(
        int marketId,
        bool isBuy,
        decimal orderSizeUsd,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the maximum safe order size for the current market depth.
    /// Returns the smaller of: 10% of total depth, or 50% of side depth.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="isBuy">True for buy orders, false for sell orders.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Maximum safe order size in USD.</returns>
    Task<decimal> GetMaxSafeOrderSizeAsync(
        int marketId,
        bool isBuy,
        CancellationToken ct = default);

    /// <summary>
    /// Validates a batch of orders against current market depth.
    /// Returns validation results for each order in the batch.
    /// </summary>
    /// <param name="marketId">Market identifier.</param>
    /// <param name="orders">Orders to validate (isBuy, sizeUsd).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation results for each order.</returns>
    Task<IReadOnlyList<PreTradeValidation>> ValidateBatchAsync(
        int marketId,
        IReadOnlyList<(bool IsBuy, decimal SizeUsd)> orders,
        CancellationToken ct = default);
}
