using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Lighter;
using GridBot.Lighter.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Core.Services.Grid;

public sealed class GridManager : IGridManager
{
    private readonly SimpleGridConfig _config;
    private readonly LighterOptions _lighterOptions;
    private readonly IGridCalculator _calculator;
    private readonly ILighterCommandClient _commandClient;
    private readonly ILighterQueryClient _queryClient;
    private readonly ILogger<GridManager> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly GridState _state = new();

    public GridManager(
        IOptions<SimpleGridConfig> config,
        IOptions<LighterOptions> lighterOptions,
        IGridCalculator calculator,
        ILighterCommandClient commandClient,
        ILighterQueryClient queryClient,
        ILogger<GridManager> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(lighterOptions);
        ArgumentNullException.ThrowIfNull(calculator);
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(queryClient);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config.Value;
        _lighterOptions = lighterOptions.Value;
        _calculator = calculator;
        _commandClient = commandClient;
        _queryClient = queryClient;
        _logger = logger;
    }

    public GridState State => _state;

    public async Task InitializeAsync(decimal currentPrice, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogInformation("Initializing grid at price {Price}", currentPrice);
            await CancelAllOrdersInternalAsync(cancellationToken).ConfigureAwait(false);
            var levels = _calculator.CalculateLevels(currentPrice);
            _state.Levels = levels;
            _state.CenterPrice = currentPrice;
            _state.State = TradingState.Active;
            _state.LastUpdated = DateTimeOffset.UtcNow;
            await PlaceGridOrdersAsync(levels, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task UpdateGridAsync(decimal currentPrice, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state.State != TradingState.Active) return;
            await SyncWithExchangeAsync(cancellationToken).ConfigureAwait(false);
            if (ShouldShiftGrid(currentPrice))
                await ShiftGridAsync(currentPrice, cancellationToken).ConfigureAwait(false);
            _state.LastUpdated = DateTimeOffset.UtcNow;
        }
        finally { _lock.Release(); }
    }

    public async Task PauseAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogWarning("Pausing grid: {Reason}", reason);
            await CancelAllOrdersInternalAsync(cancellationToken).ConfigureAwait(false);
            _state.State = TradingState.Paused;
            _state.PauseReason = reason;
            _state.CooldownUntil = DateTimeOffset.UtcNow.AddMinutes(_config.PauseCooldownMinutes);
        }
        finally { _lock.Release(); }
    }

    public async Task ResumeAsync(decimal currentPrice, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_state.IsCooldownExpired) return;
            _state.State = TradingState.Active;
            _state.PauseReason = null;
            _state.CooldownUntil = null;
            var levels = _calculator.CalculateLevels(currentPrice);
            _state.Levels = levels;
            _state.CenterPrice = currentPrice;
            await PlaceGridOrdersAsync(levels, cancellationToken).ConfigureAwait(false);
        }
        finally { _lock.Release(); }
    }

    public async Task CancelAllOrdersAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await CancelAllOrdersInternalAsync(cancellationToken).ConfigureAwait(false); }
        finally { _lock.Release(); }
    }

    private async Task CancelAllOrdersInternalAsync(CancellationToken cancellationToken)
    {
        var response = await _commandClient.CancelAllOrdersAsync(_config.MarketIndex, cancellationToken: cancellationToken).ConfigureAwait(false);
        _state.Levels = _state.Levels.Select(l => l.WithoutOrder()).ToList();
    }

    private async Task PlaceGridOrdersAsync(List<GridLevel> levels, CancellationToken cancellationToken)
    {
        var requests = levels.Select(level => new CreateOrderRequest
        {
            MarketIndex = _config.MarketIndex,
            ClientOrderIndex = level.ClientOrderIndex,
            BaseAmount = _calculator.ToScaledAmount(level.Size),
            Price = _calculator.ToScaledPrice(level.Price),
            IsAsk = !level.IsBuy,
            OrderType = OrderType.Limit,
            TimeInForce = _config.UsePostOnlyOrders ? TimeInForce.PostOnly : TimeInForce.GoodTillTime
        }).ToArray();
        if (requests.Length == 0) return;
        await _commandClient.CreateOrderBatchAsync(requests, cancellationToken).ConfigureAwait(false);
    }

    private async Task SyncWithExchangeAsync(CancellationToken cancellationToken)
    {
        var (authToken, error) = await _commandClient.CreateAuthTokenAsync().ConfigureAwait(false);
        if (error != null) return;
        var activeOrders = await _queryClient.GetActiveOrdersAsync(_lighterOptions.AccountIndex, _config.MarketIndex, authToken!, cancellationToken).ConfigureAwait(false);
        var activeOrderIds = activeOrders.Select(o => long.Parse(o.OrderId)).ToHashSet();
        _state.Levels = _state.Levels.Select(level => 
            level.OrderId.HasValue && !activeOrderIds.Contains(level.OrderId.Value) 
                ? level.WithoutOrder() 
                : level).ToList();
    }

    private bool ShouldShiftGrid(decimal currentPrice)
    {
        if (_state.CenterPrice <= 0) return false;
        var priceChange = Math.Abs(currentPrice - _state.CenterPrice) / _state.CenterPrice * 100;
        return priceChange >= _config.GridSpacingPercent / 2;
    }

    private async Task ShiftGridAsync(decimal newCenterPrice, CancellationToken cancellationToken)
    {
        await CancelAllOrdersInternalAsync(cancellationToken).ConfigureAwait(false);
        var levels = _calculator.CalculateLevels(newCenterPrice);
        _state.Levels = levels;
        _state.CenterPrice = newCenterPrice;
        await PlaceGridOrdersAsync(levels, cancellationToken).ConfigureAwait(false);
    }
}