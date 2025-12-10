using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Capacity;
using GridBot.ApiService.Services.Grid;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.MoonBag;
using GridBot.ApiService.Services.Risk;
using GridBot.ApiService.Services.State;
using GridBot.ApiService.Services.Telemetry;
using GridBot.ApiService.Services.Trend;
using GridBot.Lighter;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.DecisionEngine;

/// <summary>
/// Central orchestrator for all trading decisions.
/// CORE PRINCIPLE: THE BOT NEVER HALTS. The decision loop ALWAYS runs.
/// State affects WHAT the bot does, not WHETHER it runs.
/// </summary>
public sealed class TradingDecisionEngine : ITradingDecisionEngine, IDisposable
{
    private readonly ILogger<TradingDecisionEngine> _logger;
    private readonly TradingBotOptions _options;
    private readonly LighterOptions _lighterOptions;
    private readonly ITradingStateService _stateService;
    private readonly IRiskSentinel _riskSentinel;
    private readonly IMoonBagManager _moonBagManager;
    private readonly ITrailingStopService _trailingStopService;
    private readonly ITrailingGridService _trailingGridService;
    private readonly ITrendIntelligenceService _trendIntelligence;
    private readonly IGridLifecycleService _gridLifecycle;
    private readonly IMarketDataService _marketDataService;
    private readonly IRecoveryManager _recoveryManager;
    private readonly ILighterQueryClient _lighterClient;
    private readonly ILighterRealtimeState _realtimeState;
    private readonly IOperationalCapacityService _capacityService;

    // Per-market state tracking
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;
    private readonly ConcurrentDictionary<int, int> _consecutiveTimeouts = new();
    private readonly ConcurrentDictionary<int, bool> _buysBlocked = new();
    private readonly ConcurrentDictionary<int, bool> _sellsBlocked = new();
    private readonly ConcurrentDictionary<int, decimal> _lastRiskPositionMultiplier = new();
    private readonly ConcurrentDictionary<int, decimal> _lastRiskSpreadMultiplier = new();
    private readonly ConcurrentDictionary<int, int> _currentCapacity = new();
    private readonly ConcurrentDictionary<int, decimal> _previousPositionSize = new();
    private readonly ConcurrentDictionary<int, bool> _wasInBootstrapMode = new();
    private readonly ConcurrentDictionary<int, decimal> _cachedSkewDeviation = new();

    // Cached data for fallback
    private readonly ConcurrentDictionary<int, (decimal Price, DateTimeOffset Timestamp)> _priceCache = new();
    private readonly ConcurrentDictionary<int, (decimal? Position, DateTimeOffset Timestamp)> _positionCache = new();
    private readonly ConcurrentDictionary<int, (OrderBookSnapshot? Book, DateTimeOffset Timestamp)> _orderBookCache = new();

    // Last decision result for dashboard
    private readonly ConcurrentDictionary<int, DecisionResult> _lastDecisionResults = new();

    /// <summary>
    /// Creates a new TradingDecisionEngine instance.
    /// </summary>
    public TradingDecisionEngine(
        ILogger<TradingDecisionEngine> logger,
        IOptions<TradingBotOptions> options,
        IOptions<LighterOptions> lighterOptions,
        ITradingStateService stateService,
        IRiskSentinel riskSentinel,
        IMoonBagManager moonBagManager,
        ITrailingStopService trailingStopService,
        ITrailingGridService trailingGridService,
        ITrendIntelligenceService trendIntelligence,
        IGridLifecycleService gridLifecycle,
        IMarketDataService marketDataService,
        IRecoveryManager recoveryManager,
        ILighterQueryClient lighterClient,
        ILighterRealtimeState realtimeState,
        IOperationalCapacityService capacityService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(lighterOptions);
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(riskSentinel);
        ArgumentNullException.ThrowIfNull(moonBagManager);
        ArgumentNullException.ThrowIfNull(trailingStopService);
        ArgumentNullException.ThrowIfNull(trailingGridService);
        ArgumentNullException.ThrowIfNull(trendIntelligence);
        ArgumentNullException.ThrowIfNull(gridLifecycle);
        ArgumentNullException.ThrowIfNull(marketDataService);
        ArgumentNullException.ThrowIfNull(recoveryManager);
        ArgumentNullException.ThrowIfNull(lighterClient);
        ArgumentNullException.ThrowIfNull(realtimeState);
        ArgumentNullException.ThrowIfNull(capacityService);

        _logger = logger;
        _options = options.Value;
        _lighterOptions = lighterOptions.Value;
        _stateService = stateService;
        _riskSentinel = riskSentinel;
        _moonBagManager = moonBagManager;
        _trailingStopService = trailingStopService;
        _trailingGridService = trailingGridService;
        _trendIntelligence = trendIntelligence;
        _gridLifecycle = gridLifecycle;
        _marketDataService = marketDataService;
        _recoveryManager = recoveryManager;
        _lighterClient = lighterClient;
        _realtimeState = realtimeState;
        _capacityService = capacityService;
    }

    /// <inheritdoc />
    public async Task<DecisionResult> ExecuteDecisionCycleAsync(int marketId, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var previousState = _stateService.CurrentState;
        var warnings = new List<string>();
        var actionsBlocked = new List<string>();

        // Ensure only one decision cycle runs at a time per market
        var semaphore = GetMarketLock(marketId);
        if (!await semaphore.WaitAsync(0, ct).ConfigureAwait(false))
        {
            TradingMetrics.DecisionCyclesSkipped.Add(1, TradingMetrics.MarketTag(marketId));

            sw.Stop();
            return DecisionResult.Skipped(
                marketId,
                previousState,
                "Previous decision cycle still running",
                sw.Elapsed);
        }

        try
        {
            // Record decision cycle start
            TradingMetrics.DecisionCyclesTotal.Add(1, TradingMetrics.MarketTag(marketId));

            // NEVER SKIP - the loop ALWAYS runs, regardless of state
            // State affects behavior, not whether we run

            // STEP 1: DATA COLLECTION (Parallel with timeout)
            var context = await CollectDataAsync(marketId, ct).ConfigureAwait(false);

            // Record data collection latency
            TradingMetrics.DataCollectionLatency.Record(
                context.DataCollectionDuration.TotalMilliseconds,
                TradingMetrics.MarketTag(marketId));

            // Handle consecutive timeouts - reduce capacity, don't halt
            if (context.DataCollectionTimedOut)
            {
                var timeoutCount = _consecutiveTimeouts.AddOrUpdate(marketId, 1, (_, c) => c + 1);
                TradingMetrics.SetConsecutiveTimeouts(marketId, timeoutCount);
                await HandleTimeoutEscalation(marketId, timeoutCount, warnings, actionsBlocked, ct)
                    .ConfigureAwait(false);
            }
            else
            {
                _consecutiveTimeouts[marketId] = 0;
                TradingMetrics.SetConsecutiveTimeouts(marketId, 0);
            }

            // Calculate operational capacity
            var capacity = CalculateCurrentCapacity(marketId, context);
            _currentCapacity[marketId] = capacity;

            // Check if we have sufficient data to proceed with trading
            // Even without data, we continue monitoring
            if (!context.HasSufficientData)
            {
                warnings.Add("Insufficient data - operating in monitoring-only mode");
            }

            // LIQUIDATION DETECTION (EC-001): Track position changes
            if (context.Position.HasValue)
            {
                var currentSize = Math.Abs(context.Position.Value);
                var previousSize = _previousPositionSize.GetValueOrDefault(marketId, 0m);

                // Detect unexpected position close (possible liquidation)
                if (previousSize > 0 && currentSize == 0)
                {
                    _logger.LogWarning(
                        "EC-001: Position closed unexpectedly on market {MarketId}. Previous={Previous:F4}, Current=0. Possible liquidation.",
                        marketId, previousSize);

                    // Enter protective mode
                    await _stateService.TransitionToAsync(
                        TradingState.Degraded_ProtectiveMode,
                        "Unexpected position close - possible liquidation")
                        .ConfigureAwait(false);

                    warnings.Add("Position closed unexpectedly - possible liquidation detected");
                }

                _previousPositionSize[marketId] = currentSize;
            }

            RiskAssessment? riskAssessment = null;
            MoonBagStatus? moonBagStatus = null;
            TrendIntelligenceResult? trendResult = null;
            GridUpdateResult? gridResult = null;
            var ordersPlaced = 0;
            var ordersCancelled = 0;

            // STEP 2: RISK SENTINEL CHECK
            riskAssessment = await _riskSentinel.AssessRiskAsync(marketId, ct).ConfigureAwait(false);
            context = context with { RiskAssessment = riskAssessment };

            // Update cached multipliers
            _lastRiskPositionMultiplier[marketId] = riskAssessment.RecommendedPositionMultiplier;
            _lastRiskSpreadMultiplier[marketId] = riskAssessment.RecommendedSpreadMultiplier;

            // Update block states
            _buysBlocked[marketId] = riskAssessment.BuysBlocked;
            _sellsBlocked[marketId] = riskAssessment.SellsBlocked;

            if (riskAssessment.RequiresImmediateAction)
            {
                await HandleEmergencyResponseAsync(marketId, riskAssessment, context, actionsBlocked, ct)
                    .ConfigureAwait(false);
            }

            if (!riskAssessment.TradingAllowed)
            {
                actionsBlocked.Add($"Trading blocked: {string.Join(", ", riskAssessment.ActiveWarnings)}");
            }

            // Add warnings from risk assessment
            warnings.AddRange(riskAssessment.ActiveWarnings);

            // STEP 3: MOON BAG STATUS CHECK (HIGH)
            moonBagStatus = await _moonBagManager.GetMoonBagStatusAsync(marketId, ct).ConfigureAwait(false);

            if (moonBagStatus is not null && moonBagStatus.State == MoonBagState.Trailing)
            {
                // Check trailing stop trigger
                if (context.CurrentPrice > 0 &&
                    await _trailingStopService.IsTrailingStopTriggeredAsync(marketId, context.CurrentPrice, ct)
                        .ConfigureAwait(false))
                {
                    _logger.LogInformation(
                        "Trailing stop triggered for market {MarketId} at price {Price}. " +
                        "Executing despite any sell blocks (profit protection exception per spec TSF-001).",
                        marketId, context.CurrentPrice);

                    // Record trailing stop trigger metric
                    TradingMetrics.TrailingStopTriggers.Add(1, TradingMetrics.MarketTag(marketId));

                    // Temporarily allow sells for trailing stop execution per spec TSF-001:
                    // Trailing stop protects existing profits and must execute even during flash crash
                    var previousSellBlock = _sellsBlocked.GetValueOrDefault(marketId, false);
                    _sellsBlocked[marketId] = false;

                    try
                    {
                        await _trailingStopService.ExecuteTrailingStopAsync(marketId, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        // Restore sell block state after trailing stop execution
                        _sellsBlocked[marketId] = previousSellBlock;
                    }
                }
            }

            // Block sells if in MoonBagOnly state
            if (moonBagStatus is not null && moonBagStatus.State == MoonBagState.HoldMode)
            {
                _sellsBlocked[marketId] = true;
                actionsBlocked.Add("All sells blocked: Moon bag in hold mode");
            }

            // STEP 4: TREND INTELLIGENCE CYCLE
            // Always run trend intelligence - state affects behavior, not whether we run
            trendResult = await _trendIntelligence.ProcessTrendCycleAsync(marketId, ct).ConfigureAwait(false);

            if (!trendResult.Success)
            {
                warnings.Add($"Trend intelligence failed: {trendResult.ErrorMessage}");
            }

            // Cache skew deviation for next cycle's capacity calculation
            if (trendResult.InventoryAnalysis is not null)
            {
                _cachedSkewDeviation[marketId] = trendResult.InventoryAnalysis.SkewDeviation;

                // Track bootstrap mode transitions
                var inventoryAnalysis = trendResult.InventoryAnalysis;
                var wasBootstrap = _wasInBootstrapMode.GetValueOrDefault(marketId, false);

                if (inventoryAnalysis.IsBootstrapMode)
                {
                    _wasInBootstrapMode[marketId] = true;
                    _logger.LogDebug("Bootstrap mode active for market {MarketId}", marketId);
                }
                else if (wasBootstrap)
                {
                    _logger.LogInformation(
                        "Exiting bootstrap mode for market {MarketId}. Position built: {CryptoAlloc:F1}%",
                        marketId, inventoryAnalysis.CryptoAllocation);
                    _wasInBootstrapMode[marketId] = false;
                }
            }

            // STEP 5: TRAILING GRID CHECK (if moon bag active)
            if (moonBagStatus is not null && moonBagStatus.State == MoonBagState.Trailing)
            {
                var breakout = await _trailingGridService.DetectBreakoutAsync(marketId, ct).ConfigureAwait(false);
                var flashSpikeActive = await _trailingGridService.IsFlashSpikeActiveAsync(marketId, ct)
                    .ConfigureAwait(false);

                if (breakout && !flashSpikeActive)
                {
                    var cumulativeShift = await _trailingGridService.GetCumulativeShift1hAsync(marketId)
                        .ConfigureAwait(false);

                    if (cumulativeShift < _options.MoonBag.MaxCumulativeShift1h)
                    {
                        var shiftResult = await _trailingGridService.ShiftGridUpwardAsync(marketId, ct)
                            .ConfigureAwait(false);

                        if (!shiftResult.Shifted)
                        {
                            warnings.Add($"Grid shift failed: {shiftResult.Reason}");
                        }
                    }
                    else
                    {
                        actionsBlocked.Add("Grid shift blocked: Cumulative shift limit reached");
                    }
                }
                else if (flashSpikeActive)
                {
                    actionsBlocked.Add("Grid shift blocked: Flash spike protection active");
                }
            }

            // STEP 6: GRID OPERATIONS (capacity-adjusted)
            // Trading is allowed based on capacity and risk assessment
            if (CanTrade(marketId, riskAssessment, capacity))
            {
                // Initialize grid if not already done
                var existingGrid = await _gridLifecycle.GetCurrentGridStateAsync(marketId, ct).ConfigureAwait(false);
                if (existingGrid is null)
                {
                    _logger.LogInformation("Initializing grid for market {MarketId} on first active cycle", marketId);
                    await _gridLifecycle.InitializeGridAsync(marketId, ct).ConfigureAwait(false);
                }

                gridResult = await _gridLifecycle.UpdateGridAsync(marketId, ct).ConfigureAwait(false);

                if (gridResult is not null)
                {
                    ordersPlaced = gridResult.OrdersAdded;
                    ordersCancelled = gridResult.OrdersCancelled;

                    // Record order metrics
                    if (gridResult.OrdersAdded > 0)
                    {
                        TradingMetrics.OrdersPlaced.Add(gridResult.OrdersAdded, TradingMetrics.MarketTag(marketId));
                    }
                    if (gridResult.OrdersCancelled > 0)
                    {
                        TradingMetrics.OrdersCancelled.Add(gridResult.OrdersCancelled, TradingMetrics.MarketTag(marketId));
                    }
                }
            }
            else
            {
                if (capacity < 25)
                {
                    actionsBlocked.Add($"Grid operations limited: Capacity at {capacity}%");
                }
                if (riskAssessment is not null && !riskAssessment.TradingAllowed)
                {
                    actionsBlocked.Add("Grid operations blocked: Risk assessment prohibits trading");
                }
            }

            // STEP 7: HOUSEKEEPING
            await RecordMetricsAsync(marketId, context, ct).ConfigureAwait(false);

            // Check recovery phase advancement
            var recoveryPhase = RecoveryPhase.None;
            TimeSpan? recoveryTimeRemaining = null;

            if (_stateService.CurrentState == TradingState.Recovering)
            {
                await CheckRecoveryAdvancementAsync(marketId, context, ct).ConfigureAwait(false);

                var recoveryState = await _recoveryManager.GetRecoveryStateAsync(marketId, ct)
                    .ConfigureAwait(false);

                if (recoveryState is not null)
                {
                    recoveryPhase = recoveryState.CurrentPhase;
                    recoveryTimeRemaining = _recoveryManager.GetEstimatedTimeRemaining(marketId);
                }

                // Check if recovery is complete
                if (await _recoveryManager.IsRecoveryCompleteAsync(marketId, ct).ConfigureAwait(false))
                {
                    await _recoveryManager.CompleteRecoveryAsync(marketId, ct).ConfigureAwait(false);
                    await _stateService.TransitionToAsync(TradingState.Active, "Recovery complete")
                        .ConfigureAwait(false);

                    _logger.LogInformation("Market {MarketId} transitioned to Active after recovery", marketId);
                }
            }

            // Record state transition metric if state changed
            if (previousState != _stateService.CurrentState)
            {
                TradingMetrics.StateTransitions.Add(1,
                    TradingMetrics.StateTransitionTag(marketId, previousState.ToString(), _stateService.CurrentState.ToString()));
            }

            // Log decision summary
            LogDecisionSummary(marketId, previousState, _stateService.CurrentState, capacity,
                GetEffectivePositionMultiplier(marketId), GetEffectiveSpreadMultiplier(marketId),
                ordersPlaced, ordersCancelled, sw.Elapsed);

            // Record decision loop duration for successful completion
            sw.Stop();
            TradingMetrics.DecisionLoopDuration.Record(sw.Elapsed.TotalMilliseconds,
                TradingMetrics.MarketResultTag(marketId, "success"));

            var successResult = DecisionResult.Succeeded(
                marketId,
                previousState,
                _stateService.CurrentState,
                riskAssessment,
                moonBagStatus,
                trendResult,
                gridResult,
                GetEffectivePositionMultiplier(marketId),
                GetEffectiveSpreadMultiplier(marketId),
                recoveryPhase,
                recoveryTimeRemaining,
                warnings,
                actionsBlocked,
                sw.Elapsed,
                ordersPlaced,
                ordersCancelled);

            // Store for dashboard
            _lastDecisionResults[marketId] = successResult;
            return successResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decision cycle failed for market {MarketId}", marketId);

            // Record failed cycle metrics
            TradingMetrics.DecisionCyclesFailed.Add(1, TradingMetrics.MarketTag(marketId));

            sw.Stop();
            TradingMetrics.DecisionLoopDuration.Record(sw.Elapsed.TotalMilliseconds,
                TradingMetrics.MarketResultTag(marketId, "failed"));

            var failedResult = DecisionResult.Failed(marketId, previousState, ex.Message, sw.Elapsed);
            _lastDecisionResults[marketId] = failedResult;
            return failedResult;
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// Calculates current operational capacity based on state and conditions.
    /// </summary>
    private int CalculateCurrentCapacity(int marketId, DecisionContext context)
    {
        var state = _stateService.CurrentState;
        // Use cached skew deviation from previous cycle (updated after trend intelligence runs)
        var skewDeviation = _cachedSkewDeviation.GetValueOrDefault(marketId, 0m);
        var hasApiErrors = context.FailedDataSources > 0;
        var highVolatility = context.RiskAssessment?.RecommendedSpreadMultiplier > 1.5m;
        var lowLiquidity = context.RiskAssessment?.LiquidityStatus.Level == LiquidityLevel.Low ||
                          context.RiskAssessment?.LiquidityStatus.Level == LiquidityLevel.Critical;

        return _capacityService.CalculateCapacity(state, skewDeviation, hasApiErrors, highVolatility, lowLiquidity);
    }

    /// <inheritdoc />
    public async Task<bool> InitializeAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Initializing decision engine for market {MarketId}", marketId);

            // Initialize grid for any active state
            // Grid initialization is allowed in all states - capacity controls behavior
            var currentState = _stateService.CurrentState;
            var gridState = await _gridLifecycle.GetCurrentGridStateAsync(marketId, ct).ConfigureAwait(false);

            if (gridState is null && !_capacityService.IsDegradedState(currentState))
            {
                await _gridLifecycle.InitializeGridAsync(marketId, ct).ConfigureAwait(false);
            }
            else if (_capacityService.IsDegradedState(currentState))
            {
                _logger.LogInformation(
                    "Grid initialization deferred - state is {State}. Grid will initialize when conditions improve.",
                    currentState);
            }

            // Initialize counters
            _consecutiveTimeouts[marketId] = 0;
            _buysBlocked[marketId] = false;
            _sellsBlocked[marketId] = false;
            _lastRiskPositionMultiplier[marketId] = 1.0m;
            _lastRiskSpreadMultiplier[marketId] = 1.0m;
            _currentCapacity[marketId] = 100;

            _logger.LogInformation("Decision engine initialized for market {MarketId}", marketId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize decision engine for market {MarketId}", marketId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ShutdownAsync(int marketId, CancellationToken ct = default)
    {
        _logger.LogInformation("Shutdown initiated for market {MarketId}, acquiring lock...", marketId);

        var semaphore = GetMarketLock(marketId);
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        _logger.LogInformation("Shutdown lock acquired for market {MarketId}, beginning teardown...", marketId);

        try
        {
            // Teardown grid (cancels orders)
            await _gridLifecycle.TeardownGridAsync(marketId, ct).ConfigureAwait(false);

            // Clear state
            _consecutiveTimeouts.TryRemove(marketId, out _);
            _buysBlocked.TryRemove(marketId, out _);
            _sellsBlocked.TryRemove(marketId, out _);
            _lastRiskPositionMultiplier.TryRemove(marketId, out _);
            _lastRiskSpreadMultiplier.TryRemove(marketId, out _);
            _currentCapacity.TryRemove(marketId, out _);
            _previousPositionSize.TryRemove(marketId, out _);
            _wasInBootstrapMode.TryRemove(marketId, out _);
            _cachedSkewDeviation.TryRemove(marketId, out _);
            _priceCache.TryRemove(marketId, out _);
            _positionCache.TryRemove(marketId, out _);
            _orderBookCache.TryRemove(marketId, out _);

            _logger.LogInformation("Decision engine shutdown complete for market {MarketId}", marketId);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public RecoveryPhase GetCurrentRecoveryPhase(int marketId)
    {
        var state = _recoveryManager.GetRecoveryState(marketId);
        return state?.CurrentPhase ?? RecoveryPhase.None;
    }

    /// <inheritdoc />
    public decimal GetEffectivePositionMultiplier(int marketId)
    {
        var options = _options.DecisionEngine;
        var capacity = _currentCapacity.GetValueOrDefault(marketId, 100);

        // Start with capacity-based multiplier
        var multiplier = _capacityService.GetOrderSizeMultiplier(capacity);

        // Apply risk multiplier (multiplicative)
        multiplier *= _lastRiskPositionMultiplier.GetValueOrDefault(marketId, 1.0m);

        // Apply recovery phase multiplier (multiplicative stacking per spec CBR-005)
        var recoveryPhase = GetCurrentRecoveryPhase(marketId);
        if (recoveryPhase != RecoveryPhase.None)
        {
            var (phaseMultiplier, _, _) = _recoveryManager.GetPhaseMultipliers(recoveryPhase);
            multiplier *= phaseMultiplier;
        }

        // Apply timeout penalty
        var timeoutCount = _consecutiveTimeouts.GetValueOrDefault(marketId, 0);
        if (timeoutCount >= 3)
        {
            multiplier *= 0.5m;
        }

        // Apply floor
        return Math.Max(multiplier, options.PositionMultiplierFloor);
    }

    /// <inheritdoc />
    public decimal GetEffectiveSpreadMultiplier(int marketId)
    {
        var options = _options.DecisionEngine;
        var capacity = _currentCapacity.GetValueOrDefault(marketId, 100);

        // Start with capacity-based spread multiplier
        var multiplier = _capacityService.GetSpreadMultiplier(capacity);

        // Apply risk multiplier (additive)
        var riskSpread = _lastRiskSpreadMultiplier.GetValueOrDefault(marketId, 1.0m);
        if (riskSpread > 1.0m)
        {
            multiplier += riskSpread - 1.0m;
        }

        // Add recovery phase spread
        var recoveryPhase = GetCurrentRecoveryPhase(marketId);
        if (recoveryPhase != RecoveryPhase.None)
        {
            var (_, phaseSpread, _) = _recoveryManager.GetPhaseMultipliers(recoveryPhase);
            multiplier += phaseSpread - 1.0m;
        }

        // Add timeout penalty
        var timeoutCount = _consecutiveTimeouts.GetValueOrDefault(marketId, 0);
        if (timeoutCount >= 3)
        {
            multiplier += 0.5m;
        }

        // Apply ceiling
        return Math.Min(multiplier, options.SpreadMultiplierCeiling);
    }

    /// <inheritdoc />
    public bool CanPlaceOrder(int marketId, bool isBuy)
    {
        var capacity = _currentCapacity.GetValueOrDefault(marketId, 100);

        // At very low capacity, limit order placement
        if (capacity < 10)
            return false;

        // Check block states
        if (isBuy && _buysBlocked.GetValueOrDefault(marketId, false))
            return false;

        if (!isBuy && _sellsBlocked.GetValueOrDefault(marketId, false))
            return false;

        return true;
    }

    /// <inheritdoc />
    public int GetConsecutiveTimeoutCount(int marketId)
    {
        return _consecutiveTimeouts.GetValueOrDefault(marketId, 0);
    }

    /// <inheritdoc />
    public DecisionResult? GetLastDecisionResult(int marketId)
    {
        return _lastDecisionResults.TryGetValue(marketId, out var result) ? result : null;
    }

    /// <summary>
    /// Gets the current operational capacity for a market.
    /// </summary>
    public int GetCurrentCapacity(int marketId)
    {
        return _currentCapacity.GetValueOrDefault(marketId, 100);
    }

    private async Task<DecisionContext> CollectDataAsync(int marketId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromMilliseconds(_options.DecisionEngine.DataCollectionTimeoutMs);
        var cacheValidity = TimeSpan.FromMilliseconds(_options.DecisionEngine.CacheValidityMs);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        decimal currentPrice = 0;
        decimal? position = null;
        decimal? equity = null;
        OrderBookSnapshot? orderBook = null;
        GridState? gridState = null;

        // Thread-safe flags using int for Interlocked operations
        var isPriceStale = 0;
        var isPositionStale = 0;
        var isOrderBookStale = 0;
        var isGridStateStale = 0;
        var timedOut = 0;
        var failedSources = 0;

        // Parallel data collection
        var tasks = new List<Task>();

        // Price fetch
        var priceTask = Task.Run(async () =>
        {
            try
            {
                currentPrice = await _marketDataService.GetCurrentPriceAsync(marketId, timeoutCts.Token)
                    .ConfigureAwait(false);
                _priceCache[marketId] = (currentPrice, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref timedOut, 1);
                if (_priceCache.TryGetValue(marketId, out var cached) &&
                    DateTimeOffset.UtcNow - cached.Timestamp < cacheValidity)
                {
                    currentPrice = cached.Price;
                    Interlocked.Exchange(ref isPriceStale, 1);
                }
                else
                {
                    Interlocked.Increment(ref failedSources);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch price for market {MarketId}", marketId);
                Interlocked.Increment(ref failedSources);

                // Record API error for recovery advancement check (HIGH-005)
                _ = _recoveryManager.RecordApiErrorAsync(marketId, default);
            }
        }, ct);
        tasks.Add(priceTask);

        // Position/Account fetch - WebSocket first, REST fallback
        var positionTask = Task.Run(async () =>
        {
            // Try WebSocket first for account data (no API call needed)
            var wsAccount = _realtimeState.GetAccount();
            var isWsDataFresh = _realtimeState.OldestDataAge is null ||
                                _realtimeState.OldestDataAge.Value < cacheValidity;

            if (wsAccount is not null && _realtimeState.IsConnected && isWsDataFresh)
            {
                equity = wsAccount.Collateral;

                // Get position from WebSocket snapshot
                if (wsAccount.Positions.TryGetValue(marketId, out var wsPosition))
                {
                    // WebSocket PositionSnapshot.Size already includes sign (positive=long, negative=short)
                    position = wsPosition.Size;
                }
                else
                {
                    position = 0m;
                }

                _positionCache[marketId] = (position, DateTimeOffset.UtcNow);
                _logger.LogTrace(
                    "Account data from WebSocket: equity={Equity:F2}, position={Position:F4}",
                    equity, position);
                return;
            }

            // Fall back to REST if WebSocket not available or data is stale
            _logger.LogDebug(
                "Using REST for account data (WS connected={IsConnected}, has data={HasData}, data fresh={IsFresh})",
                _realtimeState.IsConnected,
                wsAccount is not null,
                isWsDataFresh);

            try
            {
                var account = await _lighterClient.GetAccountAsync(_lighterOptions.AccountIndex, timeoutCts.Token)
                    .ConfigureAwait(false);

                // Extract equity from account collateral (string to decimal)
                if (decimal.TryParse(account.Collateral, NumberStyles.Number, CultureInfo.InvariantCulture, out var collateralValue))
                {
                    equity = collateralValue;
                }

                // Extract position size from the specific market position
                var marketPosition = account.Positions?.FirstOrDefault(p => p.MarketId == marketId);
                if (marketPosition is not null && decimal.TryParse(marketPosition.Positionn, NumberStyles.Number, CultureInfo.InvariantCulture, out var positionSize))
                {
                    // CRITICAL: Sign indicates direction: 1=Long, -1=Short, 0=None
                    // Must multiply by Sign to get signed position (positive=long, negative=short)
                    position = positionSize * marketPosition.Sign;
                }
                else
                {
                    position = 0m;
                }

                _positionCache[marketId] = (position, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref timedOut, 1);
                if (_positionCache.TryGetValue(marketId, out var cached) &&
                    DateTimeOffset.UtcNow - cached.Timestamp < cacheValidity)
                {
                    position = cached.Position;
                    Interlocked.Exchange(ref isPositionStale, 1);
                }
                else
                {
                    Interlocked.Increment(ref failedSources);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch position for market {MarketId}", marketId);
                Interlocked.Increment(ref failedSources);

                // Record API error for recovery advancement check (HIGH-005)
                _ = _recoveryManager.RecordApiErrorAsync(marketId, default);
            }
        }, ct);
        tasks.Add(positionTask);

        // Order book fetch
        var orderBookTask = Task.Run(async () =>
        {
            try
            {
                orderBook = await _marketDataService.GetOrderBookSnapshotAsync(marketId, 20, timeoutCts.Token)
                    .ConfigureAwait(false);
                _orderBookCache[marketId] = (orderBook, DateTimeOffset.UtcNow);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Exchange(ref timedOut, 1);
                if (_orderBookCache.TryGetValue(marketId, out var cached) &&
                    DateTimeOffset.UtcNow - cached.Timestamp < cacheValidity)
                {
                    orderBook = cached.Book;
                    Interlocked.Exchange(ref isOrderBookStale, 1);
                }
                else
                {
                    Interlocked.Increment(ref failedSources);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch order book for market {MarketId}", marketId);
                Interlocked.Increment(ref failedSources);

                // Record API error for recovery advancement check (HIGH-005)
                _ = _recoveryManager.RecordApiErrorAsync(marketId, default);
            }
        }, ct);
        tasks.Add(orderBookTask);

        // Grid state fetch
        var gridStateTask = Task.Run(async () =>
        {
            try
            {
                gridState = await _gridLifecycle.GetCurrentGridStateAsync(marketId, timeoutCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch grid state for market {MarketId}", marketId);
                Interlocked.Exchange(ref isGridStateStale, 1);
                Interlocked.Increment(ref failedSources);

                // Record API error for recovery advancement check (HIGH-005)
                _ = _recoveryManager.RecordApiErrorAsync(marketId, default);
            }
        }, ct);
        tasks.Add(gridStateTask);

        await Task.WhenAll(tasks).ConfigureAwait(false);
        sw.Stop();

        return new DecisionContext
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            CurrentPrice = currentPrice,
            Position = position,
            Equity = equity,
            OrderBookSnapshot = orderBook,
            GridState = gridState,
            IsPriceStale = isPriceStale == 1,
            IsPositionStale = isPositionStale == 1,
            IsOrderBookStale = isOrderBookStale == 1,
            IsGridStateStale = isGridStateStale == 1,
            DataCollectionDuration = sw.Elapsed,
            DataCollectionTimedOut = timedOut == 1,
            FailedDataSources = failedSources
        };
    }

    private async Task HandleTimeoutEscalation(
        int marketId,
        int timeoutCount,
        List<string> warnings,
        List<string> actionsBlocked,
        CancellationToken ct)
    {
        var options = _options.DecisionEngine;

        // NEVER HALT - reduce capacity instead
        if (timeoutCount >= options.CriticalTimeoutThreshold)
        {
            // Critical - enter protective mode but don't halt
            _logger.LogCritical(
                "Critical timeout threshold reached for market {MarketId}. Count: {Count}. Entering protective mode.",
                marketId, timeoutCount);

            await _stateService.TransitionToAsync(
                TradingState.Degraded_ProtectiveMode,
                $"Critical API timeout threshold reached ({timeoutCount} consecutive)")
                .ConfigureAwait(false);

            actionsBlocked.Add("Operating in protective mode: Critical API timeout threshold");
        }
        else if (timeoutCount >= options.MaxConsecutiveTimeouts)
        {
            // Severe - reduce to low liquidity mode
            _logger.LogError(
                "Timeout threshold reached for market {MarketId}. Count: {Count}. Reducing capacity.",
                marketId, timeoutCount);

            await _gridLifecycle.PauseGridAsync(marketId, ct).ConfigureAwait(false);
            actionsBlocked.Add("Grid paused: API timeout threshold - will resume when connection stabilizes");
        }
        else if (timeoutCount >= 3)
        {
            // Warning - widen spreads and reduce position through capacity
            warnings.Add($"API timeouts: {timeoutCount} consecutive. Capacity reduced, spreads widened.");

            _logger.LogWarning(
                "Multiple consecutive timeouts for market {MarketId}. Count: {Count}. Reducing capacity.",
                marketId, timeoutCount);
        }
    }

    private async Task HandleEmergencyResponseAsync(
        int marketId,
        RiskAssessment assessment,
        DecisionContext context,
        List<string> actionsBlocked,
        CancellationToken ct)
    {
        // Flash crash handling - enter protective mode, don't halt
        // CRITICAL: Do NOT teardown grid - position must remain protected with sell orders
        if (assessment.FlashCrashStatus.CrashDetected)
        {
            var severity = assessment.FlashCrashStatus.Severity;

            _logger.LogWarning(
                "Flash crash detected for market {MarketId}. Severity: {Severity}",
                marketId, severity);

            if (severity == FlashCrashSeverity.Severe || severity == FlashCrashSeverity.Extreme)
            {
                // DO NOT teardown grid - keep sell orders alive for position protection
                // Only cancel buy orders to prevent adding to position
                _logger.LogWarning(
                    "Flash crash {Severity} detected for market {MarketId}. Entering protective mode (reduce-only).",
                    severity, marketId);

                // Enter protective mode - grid will switch to reduce-only mode
                await _stateService.TransitionToAsync(
                    TradingState.Degraded_ProtectiveMode,
                    $"Flash crash: {assessment.FlashCrashStatus.DropPercent:P2} drop")
                    .ConfigureAwait(false);

                actionsBlocked.Add($"Protective mode: Flash crash severity {severity}");
            }
        }

        // Loss limit handling - enter protective mode, don't halt
        // CRITICAL: Do NOT teardown grid - position must remain protected with sell orders
        if (assessment.LossStatus.AnyLimitBreached)
        {
            _logger.LogWarning(
                "Loss limit breached for market {MarketId}. Rolling24h: {Rolling24h}, Rolling7d: {Rolling7d}",
                marketId, assessment.LossStatus.Rolling24hBreached, assessment.LossStatus.Rolling7dBreached);

            // DO NOT teardown grid - keep sell orders alive for position protection
            // Enter protective mode - grid will switch to reduce-only mode
            await _stateService.TransitionToAsync(
                TradingState.Degraded_ProtectiveMode,
                "Loss limit breached - protective mode")
                .ConfigureAwait(false);

            actionsBlocked.Add("Protective mode: Loss limit breached");
        }

        // Handle recovery tracking for protective mode
        if (_stateService.CurrentState == TradingState.Degraded_ProtectiveMode)
        {
            var triggerType = GetTriggerType(assessment);

            // Record circuit breaker trigger metric
            TradingMetrics.CircuitBreakerTriggers.Add(1,
                TradingMetrics.CircuitBreakerTag(marketId, triggerType));

            var existingRecovery = _recoveryManager.GetRecoveryState(marketId);

            if (existingRecovery is not null)
            {
                // Reset recovery tracking
                await _recoveryManager.ResetRecoveryAsync(
                    marketId,
                    triggerType,
                    context.CurrentPrice,
                    context.Equity ?? 0m,
                    ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Protective mode triggered during recovery for market {MarketId}. " +
                    "Resetting recovery tracking. Trigger: {TriggerType}",
                    marketId, triggerType);
            }
            else
            {
                await _recoveryManager.StartRecoveryAsync(
                    marketId,
                    triggerType,
                    context.CurrentPrice,
                    context.Equity ?? 0m,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static string GetTriggerType(RiskAssessment assessment)
    {
        if (assessment.LossStatus.Rolling30dBreached)
            return "Rolling30dLossLimit";
        if (assessment.LossStatus.Rolling7dBreached)
            return "Rolling7dLossLimit";
        if (assessment.LossStatus.Rolling24hBreached)
            return "Rolling24hLossLimit";
        if (assessment.LossStatus.DrawdownBreached)
            return "MaxDrawdown";
        if (assessment.FlashCrashStatus.CrashDetected)
            return $"FlashCrash_{assessment.FlashCrashStatus.Severity}";
        if (assessment.LiquidityStatus.Level == LiquidityLevel.Halted)
            return "DeadMarket";

        return "Unknown";
    }

    private bool CanTrade(int marketId, RiskAssessment? assessment, int capacity)
    {
        // At very low capacity (protective mode), only allow monitoring
        if (capacity < 10)
            return false;

        // In protective mode, only allow position reduction
        var state = _stateService.CurrentState;
        if (state == TradingState.Degraded_ProtectiveMode)
        {
            // Allow trailing stops and position reduction, but not new grid orders
            return false;
        }

        // Risk assessment must allow trading
        if (assessment is not null && !assessment.TradingAllowed)
            return false;

        return true;
    }

    private async Task RecordMetricsAsync(int marketId, DecisionContext context, CancellationToken ct)
    {
        // Record price update
        if (context.CurrentPrice > 0)
        {
            await _riskSentinel.RecordPriceUpdateAsync(marketId, context.CurrentPrice, ct).ConfigureAwait(false);

            // Record for trailing grid if moon bag is active
            await _trailingGridService.RecordPriceAsync(marketId, context.CurrentPrice, ct).ConfigureAwait(false);

            // Update price gauge for telemetry
            TradingMetrics.SetCurrentPrice(marketId, context.CurrentPrice);
        }

        // Record equity update
        if (context.Equity.HasValue && context.Equity.Value > 0)
        {
            await _riskSentinel.RecordEquityUpdateAsync(marketId, context.Equity.Value, ct).ConfigureAwait(false);

            // Update equity gauge for telemetry
            TradingMetrics.SetCurrentEquity(marketId, context.Equity.Value);
        }

        // Update multiplier gauges for telemetry
        TradingMetrics.SetPositionMultiplier(marketId, GetEffectivePositionMultiplier(marketId));
        TradingMetrics.SetSpreadMultiplier(marketId, GetEffectiveSpreadMultiplier(marketId));

        // Update recovery phase gauge
        TradingMetrics.SetRecoveryPhase(marketId, (int)GetCurrentRecoveryPhase(marketId));

        // Update capacity gauge
        TradingMetrics.SetOperationalCapacity(marketId, _currentCapacity.GetValueOrDefault(marketId, 100));
    }

    private async Task CheckRecoveryAdvancementAsync(int marketId, DecisionContext context, CancellationToken ct)
    {
        // Update price tracking
        if (context.CurrentPrice > 0)
        {
            await _recoveryManager.UpdatePriceTrackingAsync(marketId, context.CurrentPrice, ct)
                .ConfigureAwait(false);
        }

        // Check if can advance
        var canAdvance = await _recoveryManager.CheckPhaseAdvancementAsync(
            marketId,
            context.CurrentPrice,
            context.Equity ?? 0m,
            context.OrderBookDepth,
            ct).ConfigureAwait(false);

        if (canAdvance)
        {
            await _recoveryManager.AdvancePhaseAsync(
                marketId,
                context.CurrentPrice,
                context.Equity ?? 0m,
                ct).ConfigureAwait(false);
        }
    }

    private void LogDecisionSummary(
        int marketId,
        TradingState previousState,
        TradingState currentState,
        int capacity,
        decimal positionMultiplier,
        decimal spreadMultiplier,
        int ordersPlaced,
        int ordersCancelled,
        TimeSpan duration)
    {
        _logger.LogInformation(
            "Decision cycle complete for market {MarketId}. State: {State}, Capacity: {Capacity}%, " +
            "PosMultiplier: {PosMult:F2}, SpreadMultiplier: {SpreadMult:F2}, " +
            "Orders +{Placed}/-{Cancelled}, Duration: {Duration}ms",
            marketId,
            currentState,
            capacity,
            positionMultiplier,
            spreadMultiplier,
            ordersPlaced,
            ordersCancelled,
            duration.TotalMilliseconds);

        if (previousState != currentState)
        {
            _logger.LogInformation(
                "State transition for market {MarketId}: {Previous} -> {Current}",
                marketId, previousState, currentState);
        }
    }

    private SemaphoreSlim GetMarketLock(int marketId)
    {
        return _marketLocks.GetOrAdd(marketId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Disposes all semaphore resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var semaphore in _marketLocks.Values)
        {
            semaphore.Dispose();
        }

        _marketLocks.Clear();
    }
}
