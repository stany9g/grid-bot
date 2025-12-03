using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Indicators;
using GridBot.ApiService.Services.MarketData;
using GridBot.ApiService.Services.State;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Trend;

/// <summary>
/// Implementation of trend detection using EMA, MACD, and ADX indicators.
/// Thread-safe for concurrent access across multiple markets.
/// </summary>
public sealed class TrendDetector : ITrendDetector
{
    private readonly IMarketDataService _marketDataService;
    private readonly IIndicatorService _indicatorService;
    private readonly IRiskConfiguration _riskConfig;
    private readonly ITradingStateService _tradingStateService;
    private readonly ILogger<TrendDetector> _logger;

    /// <summary>
    /// Tracks pending trend confirmations per market.
    /// Key: marketId, Value: (ProposedState, ConfirmationTime)
    /// </summary>
    private readonly ConcurrentDictionary<int, (TrendState State, DateTimeOffset ConfirmTime)> _pendingConfirmations = new();

    /// <summary>
    /// Tracks recent trend flip history per market for cooldown detection.
    /// Key: marketId, Value: thread-safe bag of (OldState, NewState, FlipTime)
    /// </summary>
    private readonly ConcurrentDictionary<int, ConcurrentBag<(TrendState From, TrendState To, DateTimeOffset At)>> _trendFlipHistory = new();

    /// <summary>
    /// Tracks cooldown expiry per market.
    /// </summary>
    private readonly ConcurrentDictionary<int, DateTimeOffset> _cooldownExpiry = new();

    public TrendDetector(
        IMarketDataService marketDataService,
        IIndicatorService indicatorService,
        IRiskConfiguration riskConfig,
        ITradingStateService tradingStateService,
        ILogger<TrendDetector> logger)
    {
        _marketDataService = marketDataService ?? throw new ArgumentNullException(nameof(marketDataService));
        _indicatorService = indicatorService ?? throw new ArgumentNullException(nameof(indicatorService));
        _riskConfig = riskConfig ?? throw new ArgumentNullException(nameof(riskConfig));
        _tradingStateService = tradingStateService ?? throw new ArgumentNullException(nameof(tradingStateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TrendAnalysis> AnalyzeTrendAsync(int marketId, CancellationToken ct = default)
    {
        var trendOptions = _riskConfig.Trend;
        var currentState = _tradingStateService.CurrentTrendState;
        var lastTrendChange = _tradingStateService.CurrentInventory.LastTrendChange;

        // Get candlestick data - need enough for both EMAs and ADX
        var requiredCandles = Math.Max(trendOptions.EmaSlowPeriod, 14) + 30; // Extra for ADX warmup
        var candles = await _marketDataService.GetCandlesticksAsync(marketId, "1h", requiredCandles, ct);

        if (candles.Count < trendOptions.EmaSlowPeriod)
        {
            _logger.LogWarning("Insufficient candle data for trend analysis on market {MarketId}. Got {Count}, need {Required}",
                marketId, candles.Count, trendOptions.EmaSlowPeriod);

            return CreateNeutralAnalysis(currentState, "Insufficient data for trend analysis");
        }

        // Extract close prices for EMA/MACD calculations
        var closePrices = candles.Select(c => c.Close).ToList();

        // Calculate indicators
        var ema20 = _indicatorService.CalculateEma(closePrices, trendOptions.EmaFastPeriod);
        var ema50 = _indicatorService.CalculateEma(closePrices, trendOptions.EmaSlowPeriod);
        var macd = _indicatorService.CalculateMacd(closePrices);
        var adx = _indicatorService.CalculateAdx(candles, 14);

        // Determine proposed trend state
        var proposedState = DetermineTrendState(ema20, ema50, macd, adx);
        var reason = BuildTrendReason(ema20, ema50, macd, adx, proposedState, trendOptions);

        // Check cooldown status
        var (inCooldown, cooldownExpiry) = CheckCooldownStatus(marketId);

        if (inCooldown)
        {
            _logger.LogInformation("Market {MarketId} is in trend flip cooldown until {Expiry}. Maintaining current state {State}",
                marketId, cooldownExpiry, currentState);

            return new TrendAnalysis
            {
                CurrentState = currentState,
                ProposedState = proposedState,
                ConfirmationRequired = false,
                Ema20 = ema20,
                Ema50 = ema50,
                Macd = macd,
                Adx = adx,
                TargetSkew = InventoryState.GetTargetSkewForTrend(currentState),
                Reason = $"In cooldown until {cooldownExpiry:HH:mm}. {reason}",
                InCooldown = true,
                CooldownExpiry = cooldownExpiry
            };
        }

        // Check if confirmation is needed
        var confirmationRequired = ShouldConfirmTrend(proposedState, currentState, lastTrendChange);
        DateTimeOffset? confirmationTime = null;

        if (confirmationRequired && proposedState != currentState)
        {
            confirmationTime = HandleConfirmation(marketId, proposedState, trendOptions);
        }
        else if (proposedState == currentState)
        {
            // Clear any pending confirmation since we're back to current state
            _pendingConfirmations.TryRemove(marketId, out _);
        }

        // Determine effective state (confirmed or not)
        var effectiveState = currentState;
        if (!confirmationRequired && proposedState != currentState)
        {
            // Check if pending confirmation has passed
            if (_pendingConfirmations.TryGetValue(marketId, out var pending) &&
                pending.State == proposedState &&
                DateTimeOffset.UtcNow >= pending.ConfirmTime)
            {
                effectiveState = proposedState;
                _pendingConfirmations.TryRemove(marketId, out _);
                RecordTrendFlip(marketId, currentState, proposedState);

                _logger.LogInformation("Trend confirmed on market {MarketId}: {OldState} -> {NewState}",
                    marketId, currentState, proposedState);
            }
        }

        return new TrendAnalysis
        {
            CurrentState = effectiveState,
            ProposedState = proposedState,
            ConfirmationRequired = confirmationRequired && proposedState != currentState,
            ConfirmationTime = confirmationTime,
            Ema20 = ema20,
            Ema50 = ema50,
            Macd = macd,
            Adx = adx,
            TargetSkew = InventoryState.GetTargetSkewForTrend(effectiveState),
            Reason = reason,
            InCooldown = false
        };
    }

    /// <inheritdoc />
    public TrendState DetermineTrendState(decimal ema20, decimal ema50, MacdResult macd, decimal adx)
    {
        var trendOptions = _riskConfig.Trend;

        // Check for neutral conditions first
        // EMA proximity check: if EMAs are within 1% of each other
        var emaProximityPercent = ema50 != 0
            ? Math.Abs((ema20 - ema50) / ema50) * 100
            : 0;

        if (emaProximityPercent < trendOptions.EmaNeutralProximityPercent)
        {
            return TrendState.Neutral;
        }

        // ADX check: if ADX is below neutral threshold, trend is weak
        if (adx < trendOptions.AdxNeutralThreshold)
        {
            return TrendState.Neutral;
        }

        var emaAbove = ema20 > ema50;
        var macdAboveSignal = macd.MacdLine > macd.SignalLine;
        var macdAboveZero = macd.MacdLine > 0;
        var strongTrend = adx > trendOptions.AdxStrongTrendThreshold;

        if (emaAbove)
        {
            // Bullish territory
            if (macdAboveSignal && macdAboveZero && strongTrend)
            {
                return TrendState.StrongBull;
            }

            if (macdAboveSignal || macdAboveZero)
            {
                return TrendState.MildBull;
            }

            // EMA bullish but MACD bearish - conflicting signals
            return TrendState.Neutral;
        }
        else
        {
            // Bearish territory (ema20 < ema50)
            var macdBelowSignal = macd.MacdLine < macd.SignalLine;
            var macdBelowZero = macd.MacdLine < 0;

            if (macdBelowSignal && macdBelowZero && strongTrend)
            {
                return TrendState.StrongBear;
            }

            if (macdBelowSignal || macdBelowZero)
            {
                return TrendState.MildBear;
            }

            // EMA bearish but MACD bullish - conflicting signals
            return TrendState.Neutral;
        }
    }

    /// <inheritdoc />
    public bool ShouldConfirmTrend(TrendState newState, TrendState currentState, DateTimeOffset? lastTrendChange)
    {
        if (newState == currentState)
        {
            return false;
        }

        // Major trend changes always require confirmation
        var majorChange = IsMajorTrendChange(currentState, newState);

        if (majorChange)
        {
            return true;
        }

        // Minor changes (e.g., StrongBull to MildBull) can be faster
        return false;
    }

    private static bool IsMajorTrendChange(TrendState from, TrendState to)
    {
        // Major changes are any of:
        // - Crossing from bullish to bearish or vice versa
        // - Going from neutral to strong (either direction)
        // - Going from strong to opposite mild

        var fromBullish = from is TrendState.StrongBull or TrendState.MildBull;
        var fromBearish = from is TrendState.StrongBear or TrendState.MildBear;
        var toBullish = to is TrendState.StrongBull or TrendState.MildBull;
        var toBearish = to is TrendState.StrongBear or TrendState.MildBear;

        // Crossing the neutral line
        if ((fromBullish && toBearish) || (fromBearish && toBullish))
        {
            return true;
        }

        // From neutral to strong
        if (from == TrendState.Neutral && (to == TrendState.StrongBull || to == TrendState.StrongBear))
        {
            return true;
        }

        return false;
    }

    private DateTimeOffset HandleConfirmation(int marketId, TrendState proposedState, TrendOptions options)
    {
        var confirmationDelay = TimeSpan.FromMinutes(options.ConfirmationDelayMinutes);
        var confirmationTime = DateTimeOffset.UtcNow.Add(confirmationDelay);

        // Store or update pending confirmation
        _pendingConfirmations.AddOrUpdate(
            marketId,
            (proposedState, confirmationTime),
            (_, existing) =>
            {
                // If same state is proposed, keep original confirmation time
                if (existing.State == proposedState)
                {
                    return existing;
                }
                // New state proposed, reset confirmation time
                return (proposedState, confirmationTime);
            });

        _logger.LogInformation(
            "Trend change pending confirmation on market {MarketId}: {ProposedState}. Will confirm at {ConfirmTime}",
            marketId, proposedState, confirmationTime);

        return confirmationTime;
    }

    private void RecordTrendFlip(int marketId, TrendState from, TrendState to)
    {
        var now = DateTimeOffset.UtcNow;
        var trendOptions = _riskConfig.Trend;
        var oneHourAgo = now.AddHours(-1);

        var history = _trendFlipHistory.GetOrAdd(marketId, _ => new ConcurrentBag<(TrendState, TrendState, DateTimeOffset)>());

        // Add the new flip (thread-safe)
        history.Add((from, to, now));

        // Count recent flips (within last hour) - thread-safe enumeration
        var recentFlips = history.Count(h => h.At >= oneHourAgo);

        // Check if we need to enter cooldown (2+ flips within 1 hour)
        if (recentFlips >= 2)
        {
            var cooldownExpiry = now.AddMinutes(trendOptions.TrendFlipCooldownMinutes);
            _cooldownExpiry[marketId] = cooldownExpiry;

            _logger.LogWarning(
                "Trend flip cooldown activated on market {MarketId}. {Flips} flips in last hour. Cooldown until {Expiry}",
                marketId, recentFlips, cooldownExpiry);
        }

        // Periodically clean up old entries by replacing with filtered bag
        // Only do this if bag is getting large (>10 entries)
        if (history.Count > 10)
        {
            var filteredHistory = new ConcurrentBag<(TrendState From, TrendState To, DateTimeOffset At)>(
                history.Where(h => h.At >= oneHourAgo));
            _trendFlipHistory.TryUpdate(marketId, filteredHistory, history);
        }
    }

    private (bool InCooldown, DateTimeOffset? Expiry) CheckCooldownStatus(int marketId)
    {
        if (_cooldownExpiry.TryGetValue(marketId, out var expiry))
        {
            if (DateTimeOffset.UtcNow < expiry)
            {
                return (true, expiry);
            }

            // Cooldown expired, remove it
            _cooldownExpiry.TryRemove(marketId, out _);
        }

        return (false, null);
    }

    private TrendAnalysis CreateNeutralAnalysis(TrendState currentState, string reason)
    {
        return new TrendAnalysis
        {
            CurrentState = currentState,
            ProposedState = TrendState.Neutral,
            ConfirmationRequired = false,
            Ema20 = 0,
            Ema50 = 0,
            Macd = new MacdResult { MacdLine = 0, SignalLine = 0, Histogram = 0 },
            Adx = 0,
            TargetSkew = InventoryState.GetTargetSkewForTrend(currentState),
            Reason = reason
        };
    }

    private static string BuildTrendReason(
        decimal ema20,
        decimal ema50,
        MacdResult macd,
        decimal adx,
        TrendState state,
        TrendOptions options)
    {
        var emaRelation = ema20 > ema50 ? "above" : ema20 < ema50 ? "below" : "equal to";
        var macdRelation = macd.MacdLine > macd.SignalLine ? "above" : "below";
        var macdZero = macd.MacdLine > 0 ? "positive" : "negative";
        var adxStrength = adx > options.AdxStrongTrendThreshold ? "strong" : adx < options.AdxNeutralThreshold ? "weak" : "moderate";

        return $"EMA20 {emaRelation} EMA50. MACD {macdRelation} signal, {macdZero}. ADX={adx:F1} ({adxStrength}). State: {state}";
    }
}
