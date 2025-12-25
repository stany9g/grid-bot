using GridBot.Abstractions.Models.Account;
using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Models.Orders;
using GridBot.Abstractions.Trading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LighterOrderSnapshot = GridBot.Lighter.Models.WebSocket.OrderSnapshot;
using LighterPositionSnapshot = GridBot.Lighter.Models.WebSocket.PositionSnapshot;

namespace GridBot.Lighter.Adapters;

/// <summary>
/// Adapts <see cref="ILighterQueryClient"/> and <see cref="ILighterRealtimeState"/>
/// to the <see cref="IAccountClient"/> interface.
/// Prefers realtime data when available, falls back to REST API.
/// </summary>
internal sealed class LighterAccountAdapter : IAccountClient
{
    private readonly ILighterQueryClient _queryClient;
    private readonly ILighterCommandClient _commandClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly LighterMarketMapper _marketMapper;
    private readonly LighterOptions _options;
    private readonly ILogger<LighterAccountAdapter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LighterAccountAdapter"/> class.
    /// </summary>
    /// <param name="queryClient">The Lighter query client.</param>
    /// <param name="commandClient">The Lighter command client for auth token.</param>
    /// <param name="realtimeState">The realtime state service.</param>
    /// <param name="marketMapper">The market ID mapper.</param>
    /// <param name="options">Lighter configuration options.</param>
    /// <param name="logger">Logger instance.</param>
    public LighterAccountAdapter(
        ILighterQueryClient queryClient,
        ILighterCommandClient commandClient,
        ILighterRealtimeState realtimeState,
        LighterMarketMapper marketMapper,
        IOptions<LighterOptions> options,
        ILogger<LighterAccountAdapter> logger)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _commandClient = commandClient ?? throw new ArgumentNullException(nameof(commandClient));
        _realtimeState = realtimeState ?? throw new ArgumentNullException(nameof(realtimeState));
        _marketMapper = marketMapper ?? throw new ArgumentNullException(nameof(marketMapper));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<AccountInfo> GetAccountAsync(CancellationToken ct = default)
    {
        // Prefer realtime data if available and fresh
        var realtimeAccount = _realtimeState.GetAccount();
        if (realtimeAccount != null && _realtimeState.IsConnected)
        {
            var timeSinceUpdate = DateTimeOffset.UtcNow - realtimeAccount.LastUpdated;
            if (timeSinceUpdate.TotalSeconds < 30)
            {
                _logger.LogDebug("Using realtime account data (age: {Age}s)", timeSinceUpdate.TotalSeconds);
                return Task.FromResult(MapAccount(realtimeAccount));
            }
        }

        _logger.LogDebug("Realtime data unavailable, fetching from REST API");
        return FetchAccountFromRestAsync(ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PositionInfo>> GetPositionsAsync(CancellationToken ct = default)
    {
        // Prefer realtime data if available
        var realtimeAccount = _realtimeState.GetAccount();
        if (realtimeAccount != null && _realtimeState.IsConnected)
        {
            var positions = realtimeAccount.Positions
                .Select(kvp => MapPosition(
                    _marketMapper.FromLighterId(kvp.Key),
                    kvp.Value))
                .ToList();

            _logger.LogDebug("Returning {Count} positions from realtime data", positions.Count);
            return Task.FromResult<IReadOnlyList<PositionInfo>>(positions);
        }

        return FetchPositionsFromRestAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OrderInfo>> GetActiveOrdersAsync(string marketId, CancellationToken ct = default)
    {
        var lighterId = _marketMapper.ToLighterId(marketId);

        // Prefer realtime data if available
        var realtimeOrders = _realtimeState.GetOrders(lighterId);
        if (realtimeOrders.Count > 0 || _realtimeState.IsConnected)
        {
            var orders = realtimeOrders
                .Select(o => MapOrder(marketId, o))
                .ToList();

            _logger.LogDebug("Returning {Count} orders from realtime data for market {MarketId}", orders.Count, marketId);
            return orders;
        }

        // Fall back to REST API
        _logger.LogDebug("Fetching orders from REST API for market {MarketId}", marketId);
        return await FetchOrdersFromRestAsync(marketId, lighterId, ct);
    }

    private async Task<AccountInfo> FetchAccountFromRestAsync(CancellationToken ct)
    {
        var account = await _queryClient.GetAccountAsync(_options.AccountIndex, ct);

        var positions = new Dictionary<string, PositionInfo>();

        // The REST API Account model has positions data
        // Map them if available
        if (account.Positions != null)
        {
            foreach (var pos in account.Positions)
            {
                var marketIdStr = _marketMapper.FromLighterId(pos.MarketId);
                positions[marketIdStr] = new PositionInfo
                {
                    MarketId = marketIdStr,
                    Size = decimal.TryParse(pos.Positionn, out var size) ? size : 0,
                    EntryPrice = decimal.TryParse(pos.AvgEntryPrice, out var entry) ? entry : 0,
                    MarkPrice = 0m, // Not in REST Position model
                    UnrealizedPnl = decimal.TryParse(pos.UnrealizedPnl, out var upnl) ? upnl : 0,
                    RealizedPnl = decimal.TryParse(pos.RealizedPnl, out var rpnl) ? rpnl : 0,
                    Leverage = decimal.TryParse(pos.InitialMarginFraction, out var imf) && imf > 0
                        ? (int)(1m / imf)
                        : 1,
                    LiquidationPrice = decimal.TryParse(pos.LiquidationPrice, out var liq) && liq > 0 ? liq : null,
                    MarginMode = pos.MarginMode == 1 ? MarginMode.Isolated : MarginMode.Cross
                };
            }
        }

        return new AccountInfo
        {
            AccountId = account.AccountIndex.ToString(),
            Collateral = decimal.TryParse(account.Collateral, out var col) ? col : 0,
            AvailableBalance = decimal.TryParse(account.AvailableBalance, out var avail) ? avail : 0,
            PortfolioValue = decimal.TryParse(account.TotalAssetValue, out var portfolio) ? portfolio : 0,
            Positions = positions,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    private async Task<IReadOnlyList<PositionInfo>> FetchPositionsFromRestAsync(CancellationToken ct)
    {
        var account = await _queryClient.GetAccountAsync(_options.AccountIndex, ct);

        if (account.Positions == null)
            return Array.Empty<PositionInfo>();

        return account.Positions
            .Select(pos =>
            {
                var marketIdStr = _marketMapper.FromLighterId(pos.MarketId);
                return new PositionInfo
                {
                    MarketId = marketIdStr,
                    Size = decimal.TryParse(pos.Positionn, out var size) ? size : 0,
                    EntryPrice = decimal.TryParse(pos.AvgEntryPrice, out var entry) ? entry : 0,
                    MarkPrice = 0m, // Not in REST Position model
                    UnrealizedPnl = decimal.TryParse(pos.UnrealizedPnl, out var upnl) ? upnl : 0,
                    RealizedPnl = decimal.TryParse(pos.RealizedPnl, out var rpnl) ? rpnl : 0,
                    Leverage = decimal.TryParse(pos.InitialMarginFraction, out var imf) && imf > 0
                        ? (int)(1m / imf)
                        : 1,
                    LiquidationPrice = decimal.TryParse(pos.LiquidationPrice, out var liq) && liq > 0 ? liq : null,
                    MarginMode = pos.MarginMode == 1 ? MarginMode.Isolated : MarginMode.Cross
                };
            })
            .ToList();
    }

    private async Task<IReadOnlyList<OrderInfo>> FetchOrdersFromRestAsync(string marketId, int lighterId, CancellationToken ct)
    {
        var (authToken, error) = await _commandClient.CreateAuthTokenAsync();
        if (error != null || authToken == null)
        {
            _logger.LogWarning("Failed to create auth token for orders fetch: {Error}", error);
            return Array.Empty<OrderInfo>();
        }

        var orders = await _queryClient.GetActiveOrdersAsync(_options.AccountIndex, lighterId, authToken, ct);

        return orders
            .Select(o => MapRestOrder(marketId, o))
            .ToList();
    }

    private AccountInfo MapAccount(AccountSnapshot lighterSnapshot)
    {
        var positions = lighterSnapshot.Positions
            .ToDictionary(
                kvp => _marketMapper.FromLighterId(kvp.Key),
                kvp => MapPosition(_marketMapper.FromLighterId(kvp.Key), kvp.Value));

        return new AccountInfo
        {
            AccountId = lighterSnapshot.AccountId.ToString(),
            Collateral = lighterSnapshot.Collateral,
            AvailableBalance = lighterSnapshot.AvailableBalance,
            PortfolioValue = lighterSnapshot.PortfolioValue,
            Positions = positions,
            LastUpdated = lighterSnapshot.LastUpdated
        };
    }

    private static PositionInfo MapPosition(string marketId, LighterPositionSnapshot lighterPosition)
    {
        return new PositionInfo
        {
            MarketId = marketId,
            Size = lighterPosition.Size,
            EntryPrice = lighterPosition.AvgEntryPrice,
            MarkPrice = 0m, // Not available in snapshot
            UnrealizedPnl = lighterPosition.UnrealizedPnl,
            RealizedPnl = 0m, // Not available in snapshot
            Leverage = 1, // Default, not available in snapshot
            LiquidationPrice = lighterPosition.LiquidationPrice,
            MarginMode = lighterPosition.IsCross ? MarginMode.Cross : MarginMode.Isolated
        };
    }

    private static OrderInfo MapOrder(string marketId, LighterOrderSnapshot lighterOrder)
    {
        return new OrderInfo
        {
            OrderId = lighterOrder.OrderIndex.ToString(),
            ClientOrderId = lighterOrder.ClientOrderIndex.ToString(),
            MarketId = marketId,
            Side = lighterOrder.IsBuy ? OrderSide.Buy : OrderSide.Sell,
            Type = Abstractions.Models.Enums.OrderType.Limit, // Snapshot doesn't have type
            Price = lighterOrder.Price,
            Size = lighterOrder.Size,
            RemainingSize = lighterOrder.Size - lighterOrder.FilledSize,
            TimeInForce = Abstractions.Models.Enums.TimeInForce.GoodTillCancel, // Default
            ReduceOnly = false, // Not available in snapshot
            CreatedAt = DateTimeOffset.UtcNow, // Not available in snapshot
            UpdatedAt = null,
            TriggerPrice = null // Not available in snapshot
        };
    }

    private static OrderInfo MapRestOrder(string marketId, Models.Api.Order order)
    {
        return new OrderInfo
        {
            OrderId = order.OrderIndex.ToString(),
            ClientOrderId = order.ClientOrderIndex?.ToString() ?? order.ClientOrderId ?? "",
            MarketId = marketId,
            Side = order.Side?.ToLowerInvariant() == "sell" ? OrderSide.Sell : OrderSide.Buy,
            Type = MapOrderType(order.Type),
            Price = decimal.TryParse(order.Price, out var p) ? p : 0,
            Size = decimal.TryParse(order.InitialBaseAmount, out var s) ? s : 0,
            RemainingSize = decimal.TryParse(order.RemainingBaseAmount, out var r) ? r : 0,
            TimeInForce = MapTimeInForce(order.TimeInForce),
            ReduceOnly = order.ReduceOnly ?? false,
            CreatedAt = order.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(order.Timestamp) : DateTimeOffset.UtcNow,
            UpdatedAt = null,
            TriggerPrice = decimal.TryParse(order.TriggerPrice, out var t) && t > 0 ? t : null
        };
    }

    private static Abstractions.Models.Enums.OrderType MapOrderType(string lighterType)
    {
        return lighterType.ToLowerInvariant() switch
        {
            "limit" => Abstractions.Models.Enums.OrderType.Limit,
            "market" => Abstractions.Models.Enums.OrderType.Market,
            "stop_loss" or "stoploss" => Abstractions.Models.Enums.OrderType.StopLoss,
            "stop_loss_limit" or "stoplosslimit" => Abstractions.Models.Enums.OrderType.StopLossLimit,
            "take_profit" or "takeprofit" => Abstractions.Models.Enums.OrderType.TakeProfit,
            "take_profit_limit" or "takeprofitlimit" => Abstractions.Models.Enums.OrderType.TakeProfitLimit,
            _ => Abstractions.Models.Enums.OrderType.Limit
        };
    }

    private static Abstractions.Models.Enums.TimeInForce MapTimeInForce(string lighterTif)
    {
        return lighterTif.ToLowerInvariant() switch
        {
            "ioc" or "immediate_or_cancel" => Abstractions.Models.Enums.TimeInForce.ImmediateOrCancel,
            "gtc" or "good_till_cancel" or "gtt" or "good_till_time" => Abstractions.Models.Enums.TimeInForce.GoodTillCancel,
            "post_only" or "postonly" => Abstractions.Models.Enums.TimeInForce.PostOnly,
            "fok" or "fill_or_kill" => Abstractions.Models.Enums.TimeInForce.FillOrKill,
            _ => Abstractions.Models.Enums.TimeInForce.GoodTillCancel
        };
    }
}
