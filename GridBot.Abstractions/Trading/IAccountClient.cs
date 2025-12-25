using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Orders;

namespace GridBot.Abstractions.Trading;

/// <summary>
/// Client for account and position information.
/// </summary>
public interface IAccountClient
{
    /// <summary>
    /// Gets the current account information.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The account information.</returns>
    Task<AccountInfo> GetAccountAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all open positions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of open positions.</returns>
    Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all active orders for a market.
    /// </summary>
    /// <param name="marketId">The market identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of active orders.</returns>
    Task<IReadOnlyList<OrderInfo>> GetActiveOrdersAsync(string marketId, CancellationToken ct = default);
}
