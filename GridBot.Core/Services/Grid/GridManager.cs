using GridBot.Abstractions.Factory;
using GridBot.Abstractions.Models.Enums;
using GridBot.Abstractions.Models.Orders;
using GridBot.Abstractions.Scaling;
using GridBot.Abstractions.Trading;
using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Core.Services.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GridBot.Core.Services.Grid;

/// <summary>
/// Manages grid trading by placing and maintaining grid orders.
/// Uses DEX-agnostic abstractions for exchange operations.
/// </summary>
/// <remarks>
/// This is a singleton service that maintains grid state.
/// It uses IServiceScopeFactory to resolve scoped exchange client dependencies per-operation,
/// supporting dynamic network switching.
/// </remarks>
public sealed class GridManager : IGridManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGridConfigurationService _configService;
    private readonly IGridCalculator _calculator;
    private readonly ILogger<GridManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly GridState _state = new();

    private MarketScaling? _cachedScaling;

    public GridManager(
        IServiceScopeFactory scopeFactory,
        IGridConfigurationService configService,
        IGridCalculator calculator,
        ILogger<GridManager> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public GridState State => _state;

    public async Task InitializeAsync(decimal currentPrice, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var orderClient = scope.ServiceProvider.GetRequiredService<IOrderClient>();

            _logger.LogInformation("Initializing grid at price {Price}", currentPrice);
            await CancelAllOrdersInternalAsync(orderClient, cancellationToken).ConfigureAwait(false);
            var levels = _calculator.CalculateLevels(currentPrice);
            _state.Levels = levels;
            _state.CenterPrice = currentPrice;
            _state.State = TradingState.Active;
            _state.LastUpdated = DateTimeOffset.UtcNow;
            await PlaceGridOrdersAsync(orderClient, scope, levels, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task UpdateGridAsync(decimal currentPrice, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.State != TradingState.Active) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var orderClient = scope.ServiceProvider.GetRequiredService<IOrderClient>();
            var accountClient = scope.ServiceProvider.GetRequiredService<IAccountClient>();

            await SyncWithExchangeAsync(accountClient, cancellationToken).ConfigureAwait(false);
            if (ShouldShiftGrid(currentPrice))
                await ShiftGridAsync(orderClient, scope, currentPrice, cancellationToken).ConfigureAwait(false);
            _state.LastUpdated = DateTimeOffset.UtcNow;
        }
        finally { _lock.Release(); }
    }

    public async Task PauseAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var orderClient = scope.ServiceProvider.GetRequiredService<IOrderClient>();

            _logger.LogWarning("Pausing grid: {Reason}", reason);
            await CancelAllOrdersInternalAsync(orderClient, cancellationToken).ConfigureAwait(false);
            _state.State = TradingState.Paused;
            _state.PauseReason = reason;
            _state.CooldownUntil = DateTimeOffset.UtcNow.AddMinutes(_configService.Current.PauseCooldownMinutes);
        }
        finally { _lock.Release(); }
    }

    public async Task ResumeAsync(decimal currentPrice, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.IsCooldownExpired) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var orderClient = scope.ServiceProvider.GetRequiredService<IOrderClient>();

            _state.State = TradingState.Active;
            _state.PauseReason = null;
            _state.CooldownUntil = null;
            var levels = _calculator.CalculateLevels(currentPrice);
            _state.Levels = levels;
            _state.CenterPrice = currentPrice;
            await PlaceGridOrdersAsync(orderClient, scope, levels, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task CancelAllOrdersAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var orderClient = scope.ServiceProvider.GetRequiredService<IOrderClient>();
            await CancelAllOrdersInternalAsync(orderClient, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    private async Task CancelAllOrdersInternalAsync(IOrderClient orderClient, CancellationToken cancellationToken)
    {
        var config = _configService.Current;
        await orderClient.CancelAllOrdersAsync(config.Market, cancellationToken).ConfigureAwait(false);
        _state.Levels = _state.Levels.Select(l => l.WithoutOrder()).ToList();
    }

    private async Task PlaceGridOrdersAsync(IOrderClient orderClient, AsyncServiceScope scope, List<GridLevel> levels, CancellationToken cancellationToken)
    {
        var config = _configService.Current;
        var scalingProvider = scope.ServiceProvider.GetRequiredService<IScalingProvider>();
        var scaling = await GetScalingAsync(scalingProvider, config.Market, cancellationToken).ConfigureAwait(false);

        var requests = levels.Select(level => new CreateOrderRequest
        {
            MarketId = config.Market,
            ClientOrderId = level.ClientOrderIndex.ToString(),
            Size = level.Size,
            Price = level.Price,
            Side = level.IsBuy ? OrderSide.Buy : OrderSide.Sell,
            Type = Abstractions.Models.Enums.OrderType.Limit,
            TimeInForce = config.UsePostOnlyOrders
                ? Abstractions.Models.Enums.TimeInForce.PostOnly
                : Abstractions.Models.Enums.TimeInForce.GoodTillCancel
        }).ToArray();

        if (requests.Length == 0) return;
        await orderClient.CreateOrderBatchAsync(requests, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncWithExchangeAsync(IAccountClient accountClient, CancellationToken cancellationToken)
    {
        var config = _configService.Current;
        var activeOrders = await accountClient.GetActiveOrdersAsync(config.Market, cancellationToken).ConfigureAwait(false);

        // Build set of active order IDs from the exchange
        var activeOrderIds = new HashSet<long>();
        foreach (var order in activeOrders)
        {
            if (long.TryParse(order.ClientOrderId, out var clientOrderId))
            {
                activeOrderIds.Add(clientOrderId);
            }
        }

        // Mark levels as unfilled if their corresponding order is no longer active
        _state.Levels = _state.Levels.Select(level =>
            level.OrderId.HasValue && !activeOrderIds.Contains(level.ClientOrderIndex)
                ? level.WithoutOrder()
                : level).ToList();
    }

    private bool ShouldShiftGrid(decimal currentPrice)
    {
        if (_state.CenterPrice <= 0) return false;
        var config = _configService.Current;
        var priceChange = Math.Abs(currentPrice - _state.CenterPrice) / _state.CenterPrice * 100;
        return priceChange >= config.GridSpacingPercent.EffectiveValue / 2;
    }

    private async Task ShiftGridAsync(IOrderClient orderClient, AsyncServiceScope scope, decimal newCenterPrice, CancellationToken cancellationToken)
    {
        await CancelAllOrdersInternalAsync(orderClient, cancellationToken).ConfigureAwait(false);
        var levels = _calculator.CalculateLevels(newCenterPrice);
        _state.Levels = levels;
        _state.CenterPrice = newCenterPrice;
        await PlaceGridOrdersAsync(orderClient, scope, levels, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MarketScaling> GetScalingAsync(IScalingProvider scalingProvider, string marketId, CancellationToken cancellationToken)
    {
        // Cache the scaling info since it rarely changes
        if (_cachedScaling is null || _cachedScaling.MarketId != marketId)
        {
            _cachedScaling = await scalingProvider.GetMarketScalingAsync(marketId, cancellationToken).ConfigureAwait(false);
        }
        return _cachedScaling;
    }
}
