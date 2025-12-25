using GridBot.Abstractions.Models.Orders;

namespace GridBot.Abstractions.Trading;

/// <summary>
/// Client for order management operations (create, modify, cancel).
/// </summary>
public interface IOrderClient
{
    /// <summary>
    /// Creates a new order.
    /// </summary>
    /// <param name="request">The order creation request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the order creation.</returns>
    Task<OrderResult> CreateOrderAsync(CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates multiple orders in a single batch.
    /// </summary>
    /// <param name="requests">The order creation requests.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The batch result containing individual order results.</returns>
    Task<BatchOrderResult> CreateOrderBatchAsync(CreateOrderRequest[] requests, CancellationToken ct = default);

    /// <summary>
    /// Cancels an existing order.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="orderId">The exchange-assigned order identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the cancellation.</returns>
    Task<OrderResult> CancelOrderAsync(string marketId, string orderId, CancellationToken ct = default);

    /// <summary>
    /// Cancels all orders for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the cancellation.</returns>
    Task<OrderResult> CancelAllOrdersAsync(string marketId, CancellationToken ct = default);

    /// <summary>
    /// Modifies an existing order.
    /// </summary>
    /// <param name="request">The order modification request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The result of the modification.</returns>
    Task<OrderResult> ModifyOrderAsync(ModifyOrderRequest request, CancellationToken ct = default);
}
