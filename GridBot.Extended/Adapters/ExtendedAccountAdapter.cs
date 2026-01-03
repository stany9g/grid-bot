using System.Globalization;
using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Models.Orders;
using GridBot.Abstractions.Trading;
using Microsoft.Extensions.Logging;

namespace GridBot.Extended.Adapters;

/// <summary>
/// Adapts Extended HTTP client to the <see cref="IAccountClient"/> interface.
/// </summary>
internal sealed class ExtendedAccountAdapter : IAccountClient
{
    private readonly IExtendedHttpClient _httpClient;
    private readonly NonceManager _nonceManager;
    private readonly ILogger<ExtendedAccountAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtendedAccountAdapter"/> class.
    /// </summary>
    /// <param name="httpClient">Extended HTTP client.</param>
    /// <param name="nonceManager">Nonce manager for synchronization.</param>
    /// <param name="logger">Logger instance.</param>
    public ExtendedAccountAdapter(
        IExtendedHttpClient httpClient,
        NonceManager nonceManager,
        ILogger<ExtendedAccountAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _nonceManager = nonceManager ?? throw new ArgumentNullException(nameof(nonceManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AccountInfo> GetAccountAsync(CancellationToken ct = default)
    {
        var accountInfo = await _httpClient.GetAccountInfoAsync(ct);
        var balance = await _httpClient.GetBalanceAsync(ct);
        var positions = await _httpClient.GetPositionsAsync(ct);

        // Get balance values (API returns single balance object, not a list)
        var collateral = balance?.Balance ?? 0m;
        var availableBalance = balance?.AvailableForTrade ?? 0m;
        var equity = balance?.Equity ?? 0m;

        // Build position dictionary
        var positionDict = new Dictionary<string, PositionInfo>();

        foreach (var pos in positions)
        {
            if (decimal.TryParse(pos.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var size) && size != 0)
            {
                var posInfo = MapPosition(pos);
                positionDict[pos.Market] = posInfo;
            }
        }

        // Portfolio value = equity (which already includes unrealized PnL)
        var portfolioValue = equity;

        return new AccountInfo
        {
            AccountId = accountInfo.AccountId.ToString(),
            Collateral = collateral,
            AvailableBalance = availableBalance,
            PortfolioValue = portfolioValue,
            Positions = positionDict,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(CancellationToken ct = default)
    {
        var positions = await _httpClient.GetPositionsAsync(ct);

        return positions
            .Where(p => decimal.TryParse(p.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var s) && s != 0)
            .Select(MapPosition)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderInfo>> GetActiveOrdersAsync(string marketId, CancellationToken ct = default)
    {
        var orders = await _httpClient.GetOrdersAsync(marketId, ct);

        return orders
            .Where(o => o.Status is "open" or "partial")
            .Select(MapOrder)
            .ToList();
    }

    private static PositionInfo MapPosition(Models.Api.PositionResponse pos)
    {
        var size = decimal.TryParse(pos.Size, NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0m;
        var entryPrice = decimal.TryParse(pos.EntryPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var ep) ? ep : 0m;
        var markPrice = decimal.TryParse(pos.MarkPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var mp) ? mp : 0m;
        var unrealizedPnl = decimal.TryParse(pos.UnrealizedPnl, NumberStyles.Any, CultureInfo.InvariantCulture, out var upnl) ? upnl : 0m;
        var realizedPnl = decimal.TryParse(pos.RealizedPnl, NumberStyles.Any, CultureInfo.InvariantCulture, out var rpnl) ? rpnl : 0m;
        var liquidationPrice = decimal.TryParse(pos.LiquidationPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var lp) ? lp : (decimal?)null;

        var marginMode = pos.MarginMode?.ToLowerInvariant() switch
        {
            "isolated" => MarginMode.Isolated,
            _ => MarginMode.Cross
        };

        return new PositionInfo
        {
            MarketId = pos.Market,
            Size = size,
            EntryPrice = entryPrice,
            MarkPrice = markPrice,
            UnrealizedPnl = unrealizedPnl,
            RealizedPnl = realizedPnl,
            Leverage = pos.Leverage,
            LiquidationPrice = liquidationPrice,
            MarginMode = marginMode
        };
    }

    private static OrderInfo MapOrder(Models.Api.OrderResponse order)
    {
        var price = decimal.TryParse(order.Price, NumberStyles.Any, CultureInfo.InvariantCulture, out var p) ? p : 0m;
        var qty = decimal.TryParse(order.Qty, NumberStyles.Any, CultureInfo.InvariantCulture, out var q) ? q : 0m;
        var filledQty = decimal.TryParse(order.FilledQty, NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 0m;

        return new OrderInfo
        {
            OrderId = order.Id,
            ClientOrderId = order.ClientOrderId ?? order.Id,
            MarketId = order.Market,
            Side = order.Side.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? OrderSide.Buy : OrderSide.Sell,
            Type = MapOrderType(order.Type),
            Price = price,
            Size = qty,
            RemainingSize = qty - filledQty,
            Status = MapOrderStatus(order.Status),
            TimeInForce = MapTimeInForce(order.TimeInForce),
            ReduceOnly = order.ReduceOnly,
            CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(order.CreatedAt),
            ExpiresAt = order.ExpiryEpochMillis.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(order.ExpiryEpochMillis.Value)
                : null
        };
    }

    private static OrderType MapOrderType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "limit" => OrderType.Limit,
            "market" => OrderType.Market,
            _ => OrderType.Limit
        };
    }

    private static OrderStatus MapOrderStatus(string status)
    {
        return status.ToLowerInvariant() switch
        {
            "open" => OrderStatus.Open,
            "partial" => OrderStatus.PartiallyFilled,
            "filled" => OrderStatus.Filled,
            "cancelled" or "canceled" => OrderStatus.Cancelled,
            "rejected" => OrderStatus.Rejected,
            _ => OrderStatus.Unknown
        };
    }

    private static TimeInForce MapTimeInForce(string? tif)
    {
        return tif?.ToUpperInvariant() switch
        {
            "GTT" or "GTC" => TimeInForce.GoodTillCancel,
            "IOC" => TimeInForce.ImmediateOrCancel,
            "FOK" => TimeInForce.FillOrKill,
            "POST_ONLY" => TimeInForce.PostOnly,
            _ => TimeInForce.GoodTillCancel
        };
    }
}
