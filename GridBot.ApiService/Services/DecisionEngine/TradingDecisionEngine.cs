using System.Collections.Concurrent;
using System.Diagnostics;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
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
/// Implements the 7-step decision loop with fail-fast, safety-first philosophy.
/// </summary>
public sealed class TradingDecisionEngine : ITradingDecisionEngine, IDisposable
{
    private readonly ILogger<TradingDecisionEngine> _logger;
    private readonly TradingBotOptions _options;
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

    // Per-market state tracking
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _marketLocks = new();
    private bool _disposed;
    private readonly ConcurrentDictionary<int, int> _consecutiveTimeouts = new();
    private readonly ConcurrentDictionary<int, bool> _buysBlocked = new();
    private readonly ConcurrentDictionary<int, bool> _sellsBlocked = new();
    private readonly ConcurrentDictionary<int, decimal> _lastRiskPositionMultiplier = new();
    private readonly ConcurrentDictionary<int, decimal> _lastRiskSpreadMultiplier = new();

    // Cached data for fallback
    private readonly ConcurrentDictionary<int, (decimal Price, DateTimeOffset Timestamp)> _priceCache = new();
    private readonly ConcurrentDictionary<int, (decimal? Position, DateTimeOffset Timestamp)> _positionCache = new();
    private readonly ConcurrentDictionary<int, (OrderBookSnapshot? Book, DateTimeOffset Timestamp)> _orderBookCache = new();

    /// <summary>
    /// Creates a new TradingDecisionEngine instance.
    /// </summary>
    public TradingDecisionEngine(
        ILogger<TradingDecisionEngine> logger,
        IOptions<TradingBotOptions> options,
        ITradingStateService stateService,
        IRiskSentinel riskSentinel,
        IMoonBagManager moonBagManager,
        ITrailingStopService trailingStopService,
        ITrailingGridService trailingGridService,
        ITrendIntelligenceService trendIntelligence,
        IGridLifecycleService gridLifecycle,
        IMarketDataService marketDataService,
        IRecoveryManager recoveryManager,
        ILighterQueryClient lighterClient)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
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

        _logger = logger;
        _options = options.Value;
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

            // Skip if Paused state
            if (previousState == TradingState.Paused)
            {
                TradingMetrics.DecisionCyclesSkipped.Add(1,
                    new KeyValuePair<string, object?>(TradingMetrics.Tags.MarketId, marketId));

                sw.Stop();
                return DecisionResult.Skipped(
                    marketId,
                    previousState,
                    "Trading is paused",
                    sw.Elapsed);
            }

            // STEP 1: DATA COLLECTION (Parallel with timeout)
            var context = await CollectDataAsync(marketId, ct).ConfigureAwait(false);

            // Record data collection latency
            TradingMetrics.DataCollectionLatency.Record(
                context.DataCollectionDuration.TotalMilliseconds,
                TradingMetrics.MarketTag(marketId));

            // Handle consecutive timeouts
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

            // Check if we have sufficient data to proceed
            if (!context.HasSufficientData && previousState != TradingState.Halted)
            {
                TradingMetrics.DecisionCyclesSkipped.Add(1,
                    new KeyValuePair<string, object?>(TradingMetrics.Tags.MarketId, marketId));

                sw.Stop();
                warnings.Add("Insufficient data for decision cycle");
                return DecisionResult.Skipped(
                    marketId,
                    previousState,
                    "Insufficient data collected",
                    sw.Elapsed);
            }

            RiskAssessment? riskAssessment = null;
            MoonBagStatus? moonBagStatus = null;
            TrendIntelligenceResult? trendResult = null;
            GridUpdateResult? gridResult = null;
            var ordersPlaced = 0;
            var ordersCancelled = 0;

            // STEP 2: RISK SENTINEL CHECK (CRITICAL)
            if (previousState != TradingState.Halted)
            {
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
            }

            // Handle Halted state - only monitoring
            if (_stateService.CurrentState == TradingState.Halted)
            {
                sw.Stop();
                return DecisionResult.Succeeded(
                    marketId,
                    previousState,
                    _stateService.CurrentState,
                    riskAssessment,
                    moonBagStatus,
                    trendResult,
                    gridResult,
                    GetEffectivePositionMultiplier(marketId),
                    GetEffectiveSpreadMultiplier(marketId),
                    RecoveryPhase.None,
                    null,
                    warnings,
                    actionsBlocked,
                    sw.Elapsed);
            }

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

            // STEP 4: TREND INTELLIGENCE CYCLE (MEDIUM)
            var currentState = _stateService.CurrentState;
            if (currentState == TradingState.Active || currentState == TradingState.Recovering)
            {
                trendResult = await _trendIntelligence.ProcessTrendCycleAsync(marketId, ct).ConfigureAwait(false);

                if (!trendResult.Success)
                {
                    warnings.Add($"Trend intelligence failed: {trendResult.ErrorMessage}");
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

            // STEP 6: GRID OPERATIONS (if trading allowed)
            if (CanTrade(marketId, riskAssessment))
            {
                // Initialize grid if not already done (handles transition from Paused to Active)
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
            LogDecisionSummary(marketId, previousState, _stateService.CurrentState,
                GetEffectivePositionMultiplier(marketId), GetEffectiveSpreadMultiplier(marketId),
                ordersPlaced, ordersCancelled, sw.Elapsed);

            // Record decision loop duration for successful completion
            sw.Stop();
            TradingMetrics.DecisionLoopDuration.Record(sw.Elapsed.TotalMilliseconds,
                TradingMetrics.MarketResultTag(marketId, "success"));

            return DecisionResult.Succeeded(
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Decision cycle failed for market {MarketId}", marketId);

            // Record failed cycle metrics
            TradingMetrics.DecisionCyclesFailed.Add(1, TradingMetrics.MarketTag(marketId));

            sw.Stop();
            TradingMetrics.DecisionLoopDuration.Record(sw.Elapsed.TotalMilliseconds,
                TradingMetrics.MarketResultTag(marketId, "failed"));

            return DecisionResult.Failed(marketId, previousState, ex.Message, sw.Elapsed);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> InitializeAsync(int marketId, CancellationToken ct = default)
    {
        try
        {
            _logger.LogInformation("Initializing decision engine for market {MarketId}", marketId);

            // Only initialize grid if trading state is Active
            // Grid initialization requires Active state; when Paused, grid will be initialized
            // when the bot transitions to Active via the decision cycle
            if (_stateService.CurrentState == TradingState.Active)
            {
                var gridState = await _gridLifecycle.GetCurrentGridStateAsync(marketId, ct).ConfigureAwait(false);
                if (gridState is null)
                {
                    await _gridLifecycle.InitializeGridAsync(marketId, ct).ConfigureAwait(false);
                }
            }
            else
            {
                _logger.LogInformation(
                    "Skipping grid initialization - trading state is {State}. Grid will initialize when activated.",
                    _stateService.CurrentState);
            }

            // Initialize counters
            _consecutiveTimeouts[marketId] = 0;
            _buysBlocked[marketId] = false;
            _sellsBlocked[marketId] = false;
            _lastRiskPositionMultiplier[marketId] = 1.0m;
            _lastRiskSpreadMultiplier[marketId] = 1.0m;

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

        // Start with risk multiplier
        var multiplier = _lastRiskPositionMultiplier.GetValueOrDefault(marketId, 1.0m);

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

        // Start with risk multiplier
        var multiplier = _lastRiskSpreadMultiplier.GetValueOrDefault(marketId, 1.0m);

        // Add recovery phase spread
        var recoveryPhase = GetCurrentRecoveryPhase(marketId);
        if (recoveryPhase != RecoveryPhase.None)
        {
            var (_, phaseSpread, _) = _recoveryManager.GetPhaseMultipliers(recoveryPhase);
            // Additive stacking per spec
            multiplier = multiplier + phaseSpread - 1.0m;
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
        // Check trading state
        var state = _stateService.CurrentState;
        if (state == TradingState.Paused || state == TradingState.Halted)
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

        // Position/Account fetch
        var positionTask = Task.Run(async () =>
        {
            try
            {
                var account = await _lighterClient.GetAccountAsync(_options.MarketId, timeoutCts.Token)
                    .ConfigureAwait(false);

                // Extract equity from account collateral (string to decimal)
                if (decimal.TryParse(account.Collateral, out var collateralValue))
                {
                    equity = collateralValue;
                }

                // Extract position size from the specific market position
                var marketPosition = account.Positions?.FirstOrDefault(p => p.MarketId == marketId);
                if (marketPosition is not null && decimal.TryParse(marketPosition.Size, out var positionSize))
                {
                    position = positionSize;
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

        if (timeoutCount >= options.CriticalTimeoutThreshold)
        {
            // Critical - transition to Halted
            _logger.LogCritical(
                "Critical timeout threshold reached for market {MarketId}. Count: {Count}. Halting.",
                marketId, timeoutCount);

            await _stateService.TransitionToAsync(
                TradingState.Halted,
                $"Critical API timeout threshold reached ({timeoutCount} consecutive)")
                .ConfigureAwait(false);

            actionsBlocked.Add("All trading halted: Critical API timeout threshold");
        }
        else if (timeoutCount >= options.MaxConsecutiveTimeouts)
        {
            // Severe - pause new orders
            _logger.LogError(
                "Timeout threshold reached for market {MarketId}. Count: {Count}. Pausing new orders.",
                marketId, timeoutCount);

            await _gridLifecycle.PauseGridAsync(marketId, ct).ConfigureAwait(false);
            actionsBlocked.Add("New orders paused: API timeout threshold");
        }
        else if (timeoutCount >= 3)
        {
            // Warning - widen spreads and reduce position
            warnings.Add($"API timeouts: {timeoutCount} consecutive. Spreads widened, positions reduced.");

            _logger.LogWarning(
                "Multiple consecutive timeouts for market {MarketId}. Count: {Count}. Applying penalties.",
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
        // Flash crash handling
        if (assessment.FlashCrashStatus.CrashDetected)
        {
            var severity = assessment.FlashCrashStatus.Severity;

            _logger.LogWarning(
                "Flash crash detected for market {MarketId}. Severity: {Severity}",
                marketId, severity);

            if (severity == FlashCrashSeverity.Severe || severity == FlashCrashSeverity.Extreme)
            {
                // Cancel all pending orders FIRST per spec Section 4.1:
                // "Cancel ALL pending grid orders" THEN transition to Halted
                _logger.LogWarning(
                    "Flash crash {Severity} detected for market {MarketId}. Cancelling all orders before halt.",
                    severity, marketId);

                await _gridLifecycle.TeardownGridAsync(marketId, ct).ConfigureAwait(false);

                // Transition to Halted
                await _stateService.TransitionToAsync(
                    TradingState.Halted,
                    $"Flash crash: {assessment.FlashCrashStatus.DropPercent:P2} drop")
                    .ConfigureAwait(false);

                actionsBlocked.Add($"Trading halted: Flash crash severity {severity}");
            }
        }

        // Loss limit handling
        if (assessment.LossStatus.AnyLimitBreached)
        {
            _logger.LogWarning(
                "Loss limit breached for market {MarketId}. Daily: {Daily}, Weekly: {Weekly}",
                marketId, assessment.LossStatus.DailyLimitBreached, assessment.LossStatus.WeeklyLimitBreached);

            // Cancel all pending orders before transitioning to Halted
            await _gridLifecycle.TeardownGridAsync(marketId, ct).ConfigureAwait(false);

            // Transition to Halted
            await _stateService.TransitionToAsync(
                TradingState.Halted,
                "Loss limit breached")
                .ConfigureAwait(false);

            actionsBlocked.Add("Trading halted: Loss limit breached");
        }

        // If transitioning from Active to Halted, need to start recovery process
        if (_stateService.CurrentState == TradingState.Halted)
        {
            var triggerType = GetTriggerType(assessment);

            // Record circuit breaker trigger metric
            TradingMetrics.CircuitBreakerTriggers.Add(1,
                TradingMetrics.CircuitBreakerTag(marketId, triggerType));

            var existingRecovery = _recoveryManager.GetRecoveryState(marketId);

            if (existingRecovery is not null)
            {
                // Reset to Phase 1 per spec RCB-001: circuit breaker during recovery resets progress
                await _recoveryManager.ResetRecoveryAsync(
                    marketId,
                    triggerType,
                    context.CurrentPrice,
                    context.Equity ?? 0m,
                    ct).ConfigureAwait(false);

                _logger.LogWarning(
                    "Circuit breaker triggered during recovery for market {MarketId}. " +
                    "Resetting to Phase 1 per spec RCB-001. Trigger: {TriggerType}",
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
        if (assessment.LossStatus.MonthlyLimitBreached)
            return "MonthlyLossLimit";
        if (assessment.LossStatus.WeeklyLimitBreached)
            return "WeeklyLossLimit";
        if (assessment.LossStatus.DailyLimitBreached)
            return "DailyLossLimit";
        if (assessment.LossStatus.DrawdownLimitBreached)
            return "MaxDrawdown";
        if (assessment.FlashCrashStatus.CrashDetected)
            return $"FlashCrash_{assessment.FlashCrashStatus.Severity}";
        if (assessment.LiquidityStatus.Level == LiquidityLevel.Halted)
            return "DeadMarket";

        return "Unknown";
    }

    private bool CanTrade(int marketId, RiskAssessment? assessment)
    {
        var state = _stateService.CurrentState;

        // Must be Active or Recovering
        if (state != TradingState.Active && state != TradingState.Recovering)
            return false;

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
        decimal positionMultiplier,
        decimal spreadMultiplier,
        int ordersPlaced,
        int ordersCancelled,
        TimeSpan duration)
    {
        _logger.LogInformation(
            "Decision cycle complete for market {MarketId}. State: {State}, " +
            "PosMultiplier: {PosMult:F2}, SpreadMultiplier: {SpreadMult:F2}, " +
            "Orders +{Placed}/-{Cancelled}, Duration: {Duration}ms",
            marketId,
            currentState,
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
