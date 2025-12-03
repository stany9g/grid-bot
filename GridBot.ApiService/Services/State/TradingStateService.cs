using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Persistence;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.State;

/// <summary>
/// Manages trading bot state with thread-safe transitions.
/// </summary>
public sealed class TradingStateService : ITradingStateService, IDisposable
{
    private readonly ILogger<TradingStateService> _logger;
    private readonly IStateRepository _stateRepository;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private TradingState _currentState = TradingState.Paused;
    private TrendState _currentTrendState = TrendState.Neutral;
    private InventoryState _currentInventory = new();
    private DateTimeOffset _stateStartedAt = DateTimeOffset.UtcNow;
    private bool _disposed;

    /// <summary>
    /// Creates a new TradingStateService instance.
    /// </summary>
    public TradingStateService(
        ILogger<TradingStateService> logger,
        IStateRepository stateRepository)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(stateRepository);

        _logger = logger;
        _stateRepository = stateRepository;
    }

    /// <inheritdoc />
    public TradingState CurrentState => _currentState;

    /// <inheritdoc />
    public TrendState CurrentTrendState => _currentTrendState;

    /// <inheritdoc />
    /// <remarks>
    /// Returns a snapshot of the current inventory state.
    /// Uses volatile read for thread-safe access without blocking.
    /// </remarks>
    public InventoryState CurrentInventory => Volatile.Read(ref _currentInventory).Clone();

    /// <inheritdoc />
    public DateTimeOffset StateStartedAt => _stateStartedAt;

    /// <inheritdoc />
    public event EventHandler<TradingStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<TrendStateChangedEventArgs>? TrendStateChanged;

    /// <inheritdoc />
    public async Task<bool> TransitionToAsync(TradingState newState, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        ObjectDisposedException.ThrowIf(_disposed, this);

        TradingStateChangedEventArgs? eventArgs = null;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var previousState = _currentState;

            if (!IsValidTransition(previousState, newState))
            {
                _logger.LogWarning(
                    "Invalid state transition attempted: {PreviousState} -> {NewState}. Reason: {Reason}",
                    previousState, newState, reason);
                return false;
            }

            _currentState = newState;
            _stateStartedAt = DateTimeOffset.UtcNow;

            _logger.LogInformation(
                "Trading state changed: {PreviousState} -> {NewState}. Reason: {Reason}",
                previousState, newState, reason);

            // Prepare event args while holding lock
            eventArgs = new TradingStateChangedEventArgs
            {
                PreviousState = previousState,
                NewState = newState,
                Reason = reason,
                Timestamp = _stateStartedAt
            };

            // Persist to Redis (inside lock for consistency)
            await _stateRepository.SaveTradingStateAsync(_currentState, _currentTrendState).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }

        // Raise event OUTSIDE lock - no re-acquisition needed
        if (eventArgs is not null)
        {
            StateChanged?.Invoke(this, eventArgs);
        }

        return true;
    }

    /// <inheritdoc />
    public async Task UpdateTrendStateAsync(TrendState newState)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TrendStateChangedEventArgs? eventArgs = null;

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var previousState = _currentTrendState;

            if (previousState == newState)
                return;

            _currentTrendState = newState;

            // Create a new inventory state with updated values
            var updatedInventory = _currentInventory.Clone();
            updatedInventory.TrendState = newState;
            updatedInventory.LastTrendChange = DateTimeOffset.UtcNow;
            updatedInventory.TargetSkew = InventoryState.GetTargetSkewForTrend(newState);
            updatedInventory.CalculateRebalanceNeeded();

            // Atomic update using Volatile.Write
            Volatile.Write(ref _currentInventory, updatedInventory);

            _logger.LogInformation(
                "Trend state changed: {PreviousState} -> {NewState}. New target skew: {TargetSkew}%",
                previousState, newState, updatedInventory.TargetSkew);

            // Prepare event args while holding lock
            eventArgs = new TrendStateChangedEventArgs
            {
                PreviousState = previousState,
                NewState = newState,
                Timestamp = DateTimeOffset.UtcNow
            };

            // Persist to Redis (inside lock for consistency)
            await _stateRepository.SaveTradingStateAsync(_currentState, _currentTrendState).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }

        // Raise event OUTSIDE lock - no re-acquisition needed
        if (eventArgs is not null)
        {
            TrendStateChanged?.Invoke(this, eventArgs);
        }
    }

    /// <inheritdoc />
    public async Task UpdateInventoryAsync(InventoryState inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _stateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var updatedInventory = inventory.Clone();
            updatedInventory.CalculateRebalanceNeeded();

            // Atomic update using Volatile.Write
            Volatile.Write(ref _currentInventory, updatedInventory);

            _logger.LogDebug(
                "Inventory updated: Crypto {CryptoAlloc}%, USDT {UsdtAlloc}%, Target {TargetSkew}%, Rebalance needed: {RebalanceNeeded}",
                updatedInventory.CryptoAllocation,
                updatedInventory.UsdtAllocation,
                updatedInventory.TargetSkew,
                updatedInventory.RebalanceNeeded);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <inheritdoc />
    public bool IsValidTransition(TradingState from, TradingState to)
    {
        // Same state is always valid (no-op)
        if (from == to)
            return true;

        return (from, to) switch
        {
            // From Active - can pause or halt
            (TradingState.Active, TradingState.Paused) => true,
            (TradingState.Active, TradingState.Halted) => true,

            // From Paused - can activate or halt
            (TradingState.Paused, TradingState.Active) => true,
            (TradingState.Paused, TradingState.Halted) => true,

            // From Halted - must go through Recovering first
            (TradingState.Halted, TradingState.Recovering) => true,

            // From Recovering - can go Active, Paused, or back to Halted
            (TradingState.Recovering, TradingState.Active) => true,
            (TradingState.Recovering, TradingState.Paused) => true,
            (TradingState.Recovering, TradingState.Halted) => true,

            // All other transitions are invalid
            _ => false
        };
    }

    /// <inheritdoc />
    public async Task<bool> LoadPersistedStateAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _stateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (tradingState, trendState) = await _stateRepository.LoadTradingStateAsync(ct).ConfigureAwait(false);

            if (!tradingState.HasValue)
            {
                _logger.LogDebug("No persisted trading state found");
                return false;
            }

            _currentState = tradingState.Value;
            _currentTrendState = trendState ?? TrendState.Neutral;
            _stateStartedAt = DateTimeOffset.UtcNow;

            // Update inventory with new trend state
            var updatedInventory = _currentInventory.Clone();
            updatedInventory.TrendState = _currentTrendState;
            updatedInventory.TargetSkew = InventoryState.GetTargetSkewForTrend(_currentTrendState);
            Volatile.Write(ref _currentInventory, updatedInventory);

            _logger.LogInformation(
                "Loaded persisted trading state: State={State}, Trend={Trend}",
                _currentState, _currentTrendState);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load persisted trading state");
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stateLock.Dispose();
    }
}
