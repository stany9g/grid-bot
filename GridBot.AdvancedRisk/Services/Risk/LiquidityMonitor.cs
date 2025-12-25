using System.Collections.Concurrent;
using GridBot.AdvancedRisk.Models;
using Microsoft.Extensions.Logging;
namespace GridBot.AdvancedRisk.Services.Risk;
/// <summary>
/// Monitors order book liquidity depth.
/// Thread-safe implementation with per-market caching.
/// </summary>
public sealed class LiquidityMonitor : ILiquidityMonitor
{
    private readonly IAdvancedRiskConfiguration _config;
    private readonly IAdvancedRiskMarketDataProvider _marketData;
    private readonly IRiskEventLogger _eventLogger;
    private readonly ILogger<LiquidityMonitor> _logger;
    private readonly ConcurrentDictionary<int, LiquidityStatus> _statusCache = new();
    public LiquidityMonitor(
        IAdvancedRiskConfiguration config,
        IAdvancedRiskMarketDataProvider marketData,
        IRiskEventLogger eventLogger,
        ILogger<LiquidityMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(marketData);
        ArgumentNullException.ThrowIfNull(eventLogger);
        ArgumentNullException.ThrowIfNull(logger);
        _config = config;
        _marketData = marketData;
        _eventLogger = eventLogger;
        _logger = logger;
    }
    public Task<LiquidityStatus> GetLiquidityStatusAsync(int marketId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (_statusCache.TryGetValue(marketId, out var status))
        {
            return Task.FromResult(status);
        }
        return Task.FromResult(LiquidityStatus.Unknown(marketId));
    }
    public async Task RefreshLiquidityAsync(int marketId, CancellationToken ct = default)
    {
        var depth = await _marketData.GetOrderBookDepthAsync(marketId, _config.LiquidityScanRangePercent, ct).ConfigureAwait(false);
        if (!depth.HasValue)
        {
            var unknownStatus = LiquidityStatus.Unknown(marketId);
            _statusCache[marketId] = unknownStatus;
            return;
        }
        var (bidDepth, askDepth, spreadBps) = depth.Value;
        var totalDepth = bidDepth + askDepth;
        LiquidityHealth health;
        decimal spreadMult;
        decimal posMult;
        if (totalDepth >= _config.MinimumHealthyDepthUsd)
        {
            health = LiquidityHealth.Healthy;
            spreadMult = 1.0m;
            posMult = 1.0m;
        }
        else if (totalDepth >= _config.CriticalDepthUsd)
        {
            health = LiquidityHealth.Thin;
            spreadMult = 1.5m;
            posMult = 0.75m;
        }
        else
        {
            health = LiquidityHealth.Critical;
            spreadMult = 2.0m;
            posMult = 0.25m;
            var evt = RiskEvent.LowLiquidity(marketId, bidDepth, askDepth, _config.CriticalDepthUsd);
            await _eventLogger.LogEventAsync(evt, ct).ConfigureAwait(false);
        }
        var status = new LiquidityStatus(
            marketId,
            health,
            bidDepth,
            askDepth,
            spreadBps,
            DateTimeOffset.UtcNow,
            spreadMult,
            posMult);
        _statusCache[marketId] = status;
        _logger.LogDebug(
            "Liquidity for market {MarketId}: {Health} (Bid: {Bid:F0}, Ask: {Ask:F0}, Spread: {Spread:F1}bps)",
            marketId, health, bidDepth, askDepth, spreadBps);
    }
}
