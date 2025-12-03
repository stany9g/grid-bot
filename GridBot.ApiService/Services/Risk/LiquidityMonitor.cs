using System.Collections.Concurrent;
using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.ApiService.Services.Metrics;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Risk;

/// <summary>
/// Monitors market liquidity conditions for risk management.
/// Thread-safe singleton implementation.
/// </summary>
public sealed class LiquidityMonitor : ILiquidityMonitor
{
    private readonly ILogger<LiquidityMonitor> _logger;
    private readonly IRiskConfiguration _riskConfig;
    private readonly IMarketMetricsService _metricsService;
    private readonly IRiskEventLogger _eventLogger;

    private readonly ConcurrentDictionary<int, MarketLiquidityState> _marketStates = new();

    /// <summary>
    /// Creates a new LiquidityMonitor instance.
    /// </summary>
    public LiquidityMonitor(
        ILogger<LiquidityMonitor> logger,
        IRiskConfiguration riskConfig,
        IMarketMetricsService metricsService,
        IRiskEventLogger eventLogger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(riskConfig);
        ArgumentNullException.ThrowIfNull(metricsService);
        ArgumentNullException.ThrowIfNull(eventLogger);

        _logger = logger;
        _riskConfig = riskConfig;
        _metricsService = metricsService;
        _eventLogger = eventLogger;
    }

    /// <inheritdoc />
    public async Task<LiquidityStatus> CheckLiquidityAsync(int marketId, CancellationToken ct = default)
    {
        var metrics = await _metricsService.GetMarketMetricsAsync(marketId, ct).ConfigureAwait(false);
        var config = _riskConfig.Liquidity;
        var state = GetOrCreateState(marketId);

        var volume24h = metrics.Volume24h;
        var volume7dAvg = metrics.Volume7dAvg;
        var volumeRatio = volume7dAvg > 0 ? (volume24h / volume7dAvg) * 100m : 100m;
        var orderBookDepth = metrics.OrderBookBidDepth + metrics.OrderBookAskDepth;
        var fundingRate = metrics.FundingRate ?? 0m;
        var bidAskSpread = metrics.BidAskSpread;

        // Calculate warning/critical flags
        var volumeWarning = volumeRatio < config.VolumeWarningPercent;
        var volumeCritical = volumeRatio < config.VolumeCriticalPercent;
        var depthWarning = orderBookDepth < 100_000m; // $100K warning
        var depthCritical = orderBookDepth < config.MinBookDepthUsd;
        var fundingWarning = Math.Abs(fundingRate) > config.MaxFundingRatePercent;
        var fundingCritical = Math.Abs(fundingRate) > config.CriticalFundingRatePercent;

        // Track consecutive low volume hours
        if (volumeWarning)
        {
            if (state.LowVolumeStartTime == null)
            {
                state.LowVolumeStartTime = DateTimeOffset.UtcNow;
            }
            state.ConsecutiveLowVolumeHours = (int)(DateTimeOffset.UtcNow - state.LowVolumeStartTime.Value).TotalHours;
        }
        else
        {
            state.LowVolumeStartTime = null;
            state.ConsecutiveLowVolumeHours = 0;
        }

        // Determine liquidity level
        var level = LiquidityLevel.Normal;
        var warnings = new List<string>();
        var spreadMultiplier = 1.0m;
        var tradingAllowed = true;

        // Volume checks
        if (volumeCritical)
        {
            level = LiquidityLevel.Critical;
            spreadMultiplier = Math.Max(spreadMultiplier, 2.0m); // 100% wider
            warnings.Add($"Critical volume: {volumeRatio:F1}% of 7d average");

            await LogLiquidityEventIfNewAsync(
                marketId, "LIQ-001", AlertSeverity.High,
                $"Critical volume: {volumeRatio:F1}% of 7d average (threshold: {config.VolumeCriticalPercent}%)",
                "Spreads widened by 100%", state, ct).ConfigureAwait(false);
        }
        else if (volumeWarning)
        {
            if (level < LiquidityLevel.Low) level = LiquidityLevel.Low;
            spreadMultiplier = Math.Max(spreadMultiplier, 1.25m); // 25% wider
            warnings.Add($"Low volume: {volumeRatio:F1}% of 7d average");
        }

        // Check for prolonged low volume
        if (state.ConsecutiveLowVolumeHours >= config.LowVolumeHaltHours)
        {
            level = LiquidityLevel.Halted;
            tradingAllowed = false;
            warnings.Add($"Trading halted: {state.ConsecutiveLowVolumeHours} hours of low volume");

            await LogLiquidityEventIfNewAsync(
                marketId, "LIQ-002", AlertSeverity.Critical,
                $"Trading halted due to {state.ConsecutiveLowVolumeHours} hours of low volume",
                "Trading halted", state, ct).ConfigureAwait(false);
        }

        // Order book depth checks
        if (depthCritical)
        {
            level = LiquidityLevel.Halted;
            tradingAllowed = false;
            warnings.Add($"Critical depth: ${orderBookDepth:N0} (min: ${config.MinBookDepthUsd:N0})");

            await LogLiquidityEventIfNewAsync(
                marketId, "LIQ-003", AlertSeverity.Critical,
                $"Order book depth critical: ${orderBookDepth:N0} < ${config.MinBookDepthUsd:N0}",
                "Trading halted", state, ct).ConfigureAwait(false);
        }
        else if (depthWarning)
        {
            if (level < LiquidityLevel.Low) level = LiquidityLevel.Low;
            warnings.Add($"Low depth: ${orderBookDepth:N0}");

            await LogLiquidityEventIfNewAsync(
                marketId, "LIQ-004", AlertSeverity.Medium,
                $"Order book depth warning: ${orderBookDepth:N0} < $100,000",
                "Alert sent", state, ct).ConfigureAwait(false);
        }

        // Funding rate checks
        if (fundingCritical)
        {
            if (level < LiquidityLevel.Critical) level = LiquidityLevel.Critical;
            warnings.Add($"Critical funding rate: {fundingRate:F4}%");

            await LogLiquidityEventIfNewAsync(
                marketId, "LIQ-005", AlertSeverity.High,
                $"Funding rate critical: {fundingRate:F4}% > {config.CriticalFundingRatePercent}%",
                "Close position recommended", state, ct).ConfigureAwait(false);
        }
        else if (fundingWarning)
        {
            if (level < LiquidityLevel.Low) level = LiquidityLevel.Low;
            warnings.Add($"High funding rate: {fundingRate:F4}%");

            await LogLiquidityEventIfNewAsync(
                marketId, "LIQ-006", AlertSeverity.Medium,
                $"Funding rate warning: {fundingRate:F4}% > {config.MaxFundingRatePercent}%",
                "Reduce position by 25%", state, ct).ConfigureAwait(false);
        }

        // Bid-ask spread check
        if (bidAskSpread > config.MaxBidAskSpreadPercent)
        {
            if (level < LiquidityLevel.Low) level = LiquidityLevel.Low;
            warnings.Add($"Wide spread: {bidAskSpread:F3}%");
        }

        // Update state
        state.LastLevel = level;
        state.LastSpreadMultiplier = spreadMultiplier;
        state.LastTradingAllowed = tradingAllowed;
        state.LastFundingReduction = fundingCritical ? 100m : (fundingWarning ? 25m : 0m);

        var status = new LiquidityStatus
        {
            Volume24h = volume24h,
            Volume7dAvg = volume7dAvg,
            VolumeRatio = volumeRatio,
            OrderBookDepth = orderBookDepth,
            BidDepth = metrics.OrderBookBidDepth,
            AskDepth = metrics.OrderBookAskDepth,
            FundingRate = fundingRate,
            BidAskSpread = bidAskSpread,
            Level = level,
            RecommendedSpreadMultiplier = spreadMultiplier,
            TradingAllowed = tradingAllowed,
            Warning = string.Join("; ", warnings),
            ConsecutiveLowVolumeHours = state.ConsecutiveLowVolumeHours,
            VolumeWarning = volumeWarning,
            VolumeCritical = volumeCritical,
            DepthWarning = depthWarning,
            DepthCritical = depthCritical,
            FundingWarning = fundingWarning,
            FundingCritical = fundingCritical
        };

        if (warnings.Count > 0)
        {
            _logger.LogWarning(
                "Liquidity concerns for market {MarketId}: {Warnings}. Level: {Level}, Trading allowed: {TradingAllowed}",
                marketId, string.Join(", ", warnings), level, tradingAllowed);
        }

        return status;
    }

    /// <inheritdoc />
    public decimal GetRecommendedSpreadMultiplier(int marketId)
    {
        return _marketStates.TryGetValue(marketId, out var state) ? state.LastSpreadMultiplier : 1.0m;
    }

    /// <inheritdoc />
    public bool IsTradingAllowed(int marketId)
    {
        return !_marketStates.TryGetValue(marketId, out var state) || state.LastTradingAllowed;
    }

    /// <inheritdoc />
    public decimal GetFundingRateReduction(int marketId)
    {
        return _marketStates.TryGetValue(marketId, out var state) ? state.LastFundingReduction : 0m;
    }

    private MarketLiquidityState GetOrCreateState(int marketId)
    {
        return _marketStates.GetOrAdd(marketId, _ => new MarketLiquidityState());
    }

    private async Task LogLiquidityEventIfNewAsync(
        int marketId,
        string ruleId,
        AlertSeverity severity,
        string description,
        string action,
        MarketLiquidityState state,
        CancellationToken ct)
    {
        // Debounce: Don't log the same event more than once per 5 minutes
        var key = $"{marketId}_{ruleId}";
        var now = DateTimeOffset.UtcNow;

        if (state.LastEventTimes.TryGetValue(key, out var lastTime) &&
            now - lastTime < TimeSpan.FromMinutes(5))
        {
            return;
        }

        state.LastEventTimes[key] = now;

        var riskEvent = RiskEvent.Create(ruleId, severity, description, action);
        await _eventLogger.LogEventAsync(riskEvent, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Internal state tracking for a single market.
    /// </summary>
    private sealed class MarketLiquidityState
    {
        public LiquidityLevel LastLevel { get; set; } = LiquidityLevel.Normal;
        public decimal LastSpreadMultiplier { get; set; } = 1.0m;
        public bool LastTradingAllowed { get; set; } = true;
        public decimal LastFundingReduction { get; set; }
        public DateTimeOffset? LowVolumeStartTime { get; set; }
        public int ConsecutiveLowVolumeHours { get; set; }
        public Dictionary<string, DateTimeOffset> LastEventTimes { get; } = [];
    }
}
