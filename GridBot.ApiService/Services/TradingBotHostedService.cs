using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.DecisionEngine;
using GridBot.ApiService.Services.MoonBag;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services;

/// <summary>
/// Main orchestrator background service for the ALTE trading bot.
/// Runs the decision loop at configured intervals using the Decision Engine.
/// </summary>
public sealed class TradingBotHostedService : BackgroundService
{
    private readonly ILogger<TradingBotHostedService> _logger;
    private readonly ITradingStateService _stateService;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingDecisionEngine _decisionEngine;
    private readonly IRecoveryManager _recoveryManager;
    private readonly IMoonBagManager _moonBagManager;
    private readonly ILossMonitor _lossMonitor;

    private DateTimeOffset _lastDecisionLoopTime = DateTimeOffset.MinValue;
    private DecisionResult? _lastDecisionResult;

    /// <summary>
    /// Creates a new TradingBotHostedService instance.
    /// </summary>
    public TradingBotHostedService(
        ILogger<TradingBotHostedService> logger,
        ITradingStateService stateService,
        IRiskConfiguration riskConfig,
        ITradingDecisionEngine decisionEngine,
        IRecoveryManager recoveryManager,
        IMoonBagManager moonBagManager,
        ILossMonitor lossMonitor)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(riskConfig);
        ArgumentNullException.ThrowIfNull(decisionEngine);
        ArgumentNullException.ThrowIfNull(recoveryManager);
        ArgumentNullException.ThrowIfNull(moonBagManager);
        ArgumentNullException.ThrowIfNull(lossMonitor);

        _logger = logger;
        _stateService = stateService;
        _riskConfig = riskConfig;
        _decisionEngine = decisionEngine;
        _recoveryManager = recoveryManager;
        _moonBagManager = moonBagManager;
        _lossMonitor = lossMonitor;
    }

    /// <summary>
    /// Gets the last time the decision loop executed.
    /// </summary>
    public DateTimeOffset LastDecisionLoopTime => _lastDecisionLoopTime;

    /// <summary>
    /// Gets the last decision result for health check and monitoring.
    /// </summary>
    public DecisionResult? LastDecisionResult => _lastDecisionResult;

    /// <inheritdoc />
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ALTE Trading Bot starting...");
        _logger.LogInformation(
            "Configuration: MarketId={MarketId}, DecisionLoopInterval={IntervalMs}ms",
            _riskConfig.MarketId,
            _riskConfig.DecisionLoopIntervalMs);
        _logger.LogInformation(
            "Risk Parameters: MaxLeverage={MaxLeverage}x, DailyLossLimit={DailyLoss}%, MaxDrawdown={MaxDrawdown}%",
            _riskConfig.Capital.MaxLeverage,
            _riskConfig.LossLimits.DailyLossPercent,
            _riskConfig.LossLimits.MaxDrawdownPercent);

        // Load persisted state from Redis
        await LoadPersistedStateAsync(cancellationToken).ConfigureAwait(false);

        // Auto-start trading if configured (typically enabled in Development)
        // NEVER HALT: The bot always runs, just check if we should go to full Active mode
        if (_riskConfig.Options.AutoStartTrading && _stateService.CurrentState != TradingState.Active)
        {
            _logger.LogInformation("AutoStartTrading is enabled - transitioning to Active state");
            await _stateService.TransitionToAsync(TradingState.Active, "Auto-start on startup")
                .ConfigureAwait(false);
        }

        // Initialize the decision engine
        var initialized = await _decisionEngine.InitializeAsync(_riskConfig.MarketId, cancellationToken)
            .ConfigureAwait(false);

        if (!initialized)
        {
            _logger.LogError("Failed to initialize Decision Engine for market {MarketId}", _riskConfig.MarketId);
        }

        await base.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task LoadPersistedStateAsync(CancellationToken ct)
    {
        _logger.LogInformation("Loading persisted state from Redis...");

        var marketId = _riskConfig.MarketId;

        try
        {
            // Load trading state first (it affects other state loading)
            var tradingStateLoaded = await _stateService.LoadPersistedStateAsync(ct).ConfigureAwait(false);
            if (tradingStateLoaded)
            {
                _logger.LogInformation(
                    "Restored trading state: {State}, Trend: {Trend}",
                    _stateService.CurrentState,
                    _stateService.CurrentTrendState);
            }

            // Load recovery state
            var recoveryLoaded = await _recoveryManager.LoadPersistedStateAsync(marketId, ct).ConfigureAwait(false);
            if (recoveryLoaded)
            {
                var recoveryState = _recoveryManager.GetRecoveryState(marketId);
                _logger.LogInformation(
                    "Restored recovery state for market {MarketId}: Phase={Phase}, Trigger={Trigger}",
                    marketId,
                    recoveryState?.CurrentPhase,
                    recoveryState?.TriggerType);
            }

            // Load moon bag state
            var moonBagLoaded = await _moonBagManager.LoadPersistedStateAsync(marketId, ct).ConfigureAwait(false);
            if (moonBagLoaded)
            {
                var moonBagStatus = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Restored moon bag state for market {MarketId}: State={State}",
                    marketId,
                    moonBagStatus.State);
            }

            // Load loss status
            var lossLoaded = await _lossMonitor.LoadPersistedStateAsync(marketId, ct).ConfigureAwait(false);
            if (lossLoaded)
            {
                var lossStatus = await _lossMonitor.GetCurrentLossStatusAsync(marketId, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "Restored loss status for market {MarketId}: Daily={Daily:P2}, Halt={HaltReason}",
                    marketId,
                    lossStatus.DailyPnlPercent,
                    lossStatus.HaltReason ?? "None");
            }

            var loadedCount = (tradingStateLoaded ? 1 : 0) +
                              (recoveryLoaded ? 1 : 0) +
                              (moonBagLoaded ? 1 : 0) +
                              (lossLoaded ? 1 : 0);

            _logger.LogInformation(
                "State restoration complete: {Count}/4 states loaded from Redis",
                loadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error loading persisted state - starting with fresh state. This may result in loss of recovery progress.");
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ALTE Trading Bot decision loop started");

        // Initial state is Paused - wait for activation
        _logger.LogInformation(
            "Trading bot initialized in {State} state. Waiting for activation.",
            _stateService.CurrentState);

        var loopInterval = TimeSpan.FromMilliseconds(_riskConfig.DecisionLoopIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunDecisionLoopAsync(stoppingToken).ConfigureAwait(false);
                _lastDecisionLoopTime = DateTimeOffset.UtcNow;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in decision loop");
                // Continue running after errors
            }

            try
            {
                await Task.Delay(loopInterval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ALTE Trading Bot stopping...");

        // Shutdown the decision engine (cancels orders, preserves positions)
        await _decisionEngine.ShutdownAsync(_riskConfig.MarketId, cancellationToken)
            .ConfigureAwait(false);

        // Transition to Recovering state for graceful shutdown
        // NEVER HALT: Even on shutdown, we use a valid degraded state
        if (_stateService.CurrentState == TradingState.Active)
        {
            await _stateService.TransitionToAsync(TradingState.Recovering, "Service shutdown")
                .ConfigureAwait(false);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("ALTE Trading Bot stopped");
    }

    private async Task RunDecisionLoopAsync(CancellationToken cancellationToken)
    {
        var marketId = _riskConfig.MarketId;

        // Execute the decision cycle through the Decision Engine
        var result = await _decisionEngine.ExecuteDecisionCycleAsync(marketId, cancellationToken)
            .ConfigureAwait(false);

        _lastDecisionResult = result;

        // Log warnings
        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning("Decision cycle warning for market {MarketId}: {Warning}", marketId, warning);
        }

        // Log blocked actions at debug level
        foreach (var blocked in result.ActionsBlocked)
        {
            _logger.LogDebug("Action blocked for market {MarketId}: {Blocked}", marketId, blocked);
        }

        // Log errors
        if (!result.Success)
        {
            _logger.LogError(
                "Decision cycle failed for market {MarketId}: {Error}",
                marketId, result.ErrorMessage);
        }

        // Log state transitions
        if (result.StateChanged)
        {
            _logger.LogInformation(
                "Trading state changed for market {MarketId}: {Previous} -> {Current}",
                marketId, result.PreviousState, result.CurrentState);
        }

        // Log recovery progress
        if (result.RecoveryPhase != RecoveryPhase.None)
        {
            _logger.LogInformation(
                "Recovery progress for market {MarketId}: Phase {Phase}, ETA: {ETA}",
                marketId,
                result.RecoveryPhase,
                result.RecoveryTimeRemaining?.ToString(@"hh\:mm\:ss") ?? "Unknown");
        }
    }
}
