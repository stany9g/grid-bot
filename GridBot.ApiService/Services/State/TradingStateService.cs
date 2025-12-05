using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Persistence;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.State;

/// <summary>
/// Manages trading bot state with thread-safe transitions.
/// CORE PRINCIPLE: The bot NEVER halts. States represent capacity levels, not stop conditions.
/// </summary>
public sealed class TradingStateService : ITradingStateService, IDisposable
{
    private readonly ILogger<TradingStateService> _logger;
    private readonly IStateRepository _stateRepository;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    // Default to Active - the bot should always be running
    private TradingState _currentState = TradingState.Active;
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

        // NEVER HALT philosophy: All states can transition to any other state
        // The bot is always running, just at different capacity levels
        return (from, to) switch
        {
            // From Active - can go to any degraded state or recovering
            (TradingState.Active, TradingState.Degraded_Bootstrap) => true,
            (TradingState.Active, TradingState.Degraded_SkewCorrection) => true,
            (TradingState.Active, TradingState.Degraded_HighVolatility) => true,
            (TradingState.Active, TradingState.Degraded_LowLiquidity) => true,
            (TradingState.Active, TradingState.Degraded_ProtectiveMode) => true,
            (TradingState.Active, TradingState.Recovering) => true,

            // From any Degraded state - can go to Active or other Degraded states
            (TradingState.Degraded_Bootstrap, TradingState.Active) => true,
            (TradingState.Degraded_Bootstrap, _) when IsDegradedState(to) => true,
            (TradingState.Degraded_Bootstrap, TradingState.Recovering) => true,

            (TradingState.Degraded_SkewCorrection, TradingState.Active) => true,
            (TradingState.Degraded_SkewCorrection, _) when IsDegradedState(to) => true,
            (TradingState.Degraded_SkewCorrection, TradingState.Recovering) => true,

            (TradingState.Degraded_HighVolatility, TradingState.Active) => true,
            (TradingState.Degraded_HighVolatility, _) when IsDegradedState(to) => true,
            (TradingState.Degraded_HighVolatility, TradingState.Recovering) => true,

            (TradingState.Degraded_LowLiquidity, TradingState.Active) => true,
            (TradingState.Degraded_LowLiquidity, _) when IsDegradedState(to) => true,
            (TradingState.Degraded_LowLiquidity, TradingState.Recovering) => true,

            (TradingState.Degraded_ProtectiveMode, TradingState.Active) => true,
            (TradingState.Degraded_ProtectiveMode, _) when IsDegradedState(to) => true,
            (TradingState.Degraded_ProtectiveMode, TradingState.Recovering) => true,

            // From Recovering - can go to Active or back to Degraded
            (TradingState.Recovering, TradingState.Active) => true,
            (TradingState.Recovering, _) when IsDegradedState(to) => true,

            // All other transitions are valid in NEVER HALT mode
            // The bot must always be able to respond to changing conditions
            _ => true
        };
    }

    /// <summary>
    /// Checks if a state is one of the degraded states.
    /// </summary>
    private static bool IsDegradedState(TradingState state)
    {
        return state switch
        {
            TradingState.Degraded_Bootstrap => true,
            TradingState.Degraded_SkewCorrection => true,
            TradingState.Degraded_HighVolatility => true,
            TradingState.Degraded_LowLiquidity => true,
            TradingState.Degraded_ProtectiveMode => true,
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
                _logger.LogDebug("No persisted trading state found, defaulting to Active");
                _currentState = TradingState.Active;
                return false;
            }

            // Map any legacy Paused/Halted states to appropriate new states
            _currentState = MapLegacyState(tradingState.Value);
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
            _logger.LogError(ex, "Failed to load persisted trading state, defaulting to Active");
            _currentState = TradingState.Active;
            return false;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    /// <summary>
    /// Maps legacy states (Paused, Halted) to new NEVER HALT states.
    /// </summary>
    private static TradingState MapLegacyState(TradingState state)
    {
        // Handle any numeric values that might have been Paused (1) or Halted (2) in the old enum
        var stateValue = (int)state;

        return stateValue switch
        {
            0 => TradingState.Active,
            1 => TradingState.Degraded_Bootstrap,       // Old Paused -> Bootstrap (safe start)
            2 => TradingState.Degraded_ProtectiveMode,  // Old Halted -> Protective (safe recovery)
            3 => TradingState.Recovering,
            _ => state  // New states pass through
        };
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
