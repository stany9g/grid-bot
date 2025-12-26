using GridBot.Abstractions.Factory;
using GridBot.ApiService.Services.Network;
using GridBot.Core.Services.Engine;

namespace GridBot.ApiService.Services.Bot;

/// <summary>
/// Service for controlling the grid trading bot lifecycle.
/// Thread-safe implementation with status tracking.
/// </summary>
public sealed class GridBotControlService : IGridBotControlService
{
    private readonly ISimpleTradingEngine _engine;
    private readonly IExchangeRegistry _exchangeRegistry;
    private readonly INetworkSelectionService _networkSelectionService;
    private readonly ILogger<GridBotControlService> _logger;
    private readonly object _lock = new();

    private BotStatus _status = BotStatus.Stopped;
    private string? _lastError;

    public GridBotControlService(
        ISimpleTradingEngine engine,
        IExchangeRegistry exchangeRegistry,
        INetworkSelectionService networkSelectionService,
        ILogger<GridBotControlService> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(exchangeRegistry);
        ArgumentNullException.ThrowIfNull(networkSelectionService);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _exchangeRegistry = exchangeRegistry;
        _networkSelectionService = networkSelectionService;
        _logger = logger;
    }

    public BotStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _status is BotStatus.Running or BotStatus.Paused;
            }
        }
    }

    public string? CurrentExchangeId
    {
        get
        {
            try
            {
                var primary = _exchangeRegistry.GetPrimary();
                return primary.ExchangeId;
            }
            catch
            {
                return null;
            }
        }
    }

    public ExchangeType? CurrentExchangeType
    {
        get
        {
            try
            {
                var primary = _exchangeRegistry.GetPrimary();
                return primary.ExchangeType;
            }
            catch
            {
                return null;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_lock)
            {
                return _lastError;
            }
        }
    }

    public event EventHandler<BotStatusChangedEventArgs>? StatusChanged;

    public async Task StartAsync(CancellationToken ct = default)
    {
        // Check if network switch is in progress before attempting to start
        if (_networkSelectionService.IsSwitchingNetwork)
        {
            throw new InvalidOperationException("Cannot start bot while network switch is in progress");
        }

        BotStatus previousStatus;

        lock (_lock)
        {
            if (_status is BotStatus.Running or BotStatus.Starting)
            {
                throw new InvalidOperationException($"Cannot start bot when status is {_status}");
            }

            previousStatus = _status;
            _status = BotStatus.Starting;
            _lastError = null;
        }

        RaiseStatusChanged(previousStatus, BotStatus.Starting, "Bot is starting");
        _logger.LogInformation("Starting trading bot");

        try
        {
            await _engine.StartAsync(ct);

            lock (_lock)
            {
                _status = BotStatus.Running;
            }

            RaiseStatusChanged(BotStatus.Starting, BotStatus.Running, "Bot started successfully");
            _logger.LogInformation("Trading bot started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start trading bot");

            lock (_lock)
            {
                _status = BotStatus.Error;
                _lastError = ex.Message;
            }

            RaiseStatusChanged(BotStatus.Starting, BotStatus.Error, ex.Message);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        BotStatus previousStatus;

        lock (_lock)
        {
            if (_status is BotStatus.Stopped or BotStatus.Stopping)
            {
                throw new InvalidOperationException($"Cannot stop bot when status is {_status}");
            }

            previousStatus = _status;
            _status = BotStatus.Stopping;
        }

        RaiseStatusChanged(previousStatus, BotStatus.Stopping, "Bot is stopping");
        _logger.LogInformation("Stopping trading bot");

        try
        {
            await _engine.StopAsync(ct);

            lock (_lock)
            {
                _status = BotStatus.Stopped;
            }

            RaiseStatusChanged(BotStatus.Stopping, BotStatus.Stopped, "Bot stopped successfully");
            _logger.LogInformation("Trading bot stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping trading bot");

            lock (_lock)
            {
                _status = BotStatus.Stopped;
                _lastError = ex.Message;
            }

            RaiseStatusChanged(BotStatus.Stopping, BotStatus.Stopped, $"Bot stopped with error: {ex.Message}");
        }
    }

    public async Task PauseAsync(string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reason);

        BotStatus previousStatus;

        lock (_lock)
        {
            if (_status != BotStatus.Running)
            {
                throw new InvalidOperationException($"Cannot pause bot when status is {_status}");
            }

            previousStatus = _status;
            _status = BotStatus.Pausing;
        }

        RaiseStatusChanged(previousStatus, BotStatus.Pausing, $"Pausing: {reason}");
        _logger.LogInformation("Pausing trading bot: {Reason}", reason);

        try
        {
            await _engine.PauseAsync(reason, ct);

            lock (_lock)
            {
                _status = BotStatus.Paused;
            }

            RaiseStatusChanged(BotStatus.Pausing, BotStatus.Paused, reason);
            _logger.LogInformation("Trading bot paused: {Reason}", reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause trading bot");

            lock (_lock)
            {
                _status = BotStatus.Running;
                _lastError = ex.Message;
            }

            RaiseStatusChanged(BotStatus.Pausing, BotStatus.Running, $"Failed to pause: {ex.Message}");
            throw;
        }
    }

    public async Task ResumeAsync(CancellationToken ct = default)
    {
        BotStatus previousStatus;

        lock (_lock)
        {
            if (_status != BotStatus.Paused)
            {
                throw new InvalidOperationException($"Cannot resume bot when status is {_status}");
            }

            previousStatus = _status;
            _status = BotStatus.Resuming;
        }

        RaiseStatusChanged(previousStatus, BotStatus.Resuming, "Resuming bot");
        _logger.LogInformation("Resuming trading bot");

        try
        {
            await _engine.ResumeAsync(ct);

            lock (_lock)
            {
                _status = BotStatus.Running;
            }

            RaiseStatusChanged(BotStatus.Resuming, BotStatus.Running, "Bot resumed");
            _logger.LogInformation("Trading bot resumed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume trading bot");

            lock (_lock)
            {
                _status = BotStatus.Paused;
                _lastError = ex.Message;
            }

            RaiseStatusChanged(BotStatus.Resuming, BotStatus.Paused, $"Failed to resume: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Called by the hosted service to update status when running the trading loop.
    /// </summary>
    internal void SetRunning()
    {
        lock (_lock)
        {
            if (_status == BotStatus.Starting)
            {
                _status = BotStatus.Running;
                RaiseStatusChanged(BotStatus.Starting, BotStatus.Running, "Bot started successfully");
            }
        }
    }

    /// <summary>
    /// Called by the hosted service when the engine is paused externally (e.g., flash crash).
    /// </summary>
    internal void SetPaused(string reason)
    {
        lock (_lock)
        {
            if (_status == BotStatus.Running)
            {
                _status = BotStatus.Paused;
                RaiseStatusChanged(BotStatus.Running, BotStatus.Paused, reason);
            }
        }
    }

    private void RaiseStatusChanged(BotStatus previousStatus, BotStatus newStatus, string? message)
    {
        StatusChanged?.Invoke(this, new BotStatusChangedEventArgs
        {
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            Timestamp = DateTimeOffset.UtcNow,
            Message = message
        });
    }
}
