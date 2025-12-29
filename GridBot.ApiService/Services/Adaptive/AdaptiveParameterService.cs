using GridBot.Abstractions.Trading;
using GridBot.Core.Configuration;
using GridBot.Core.Models;
using GridBot.Core.Services.Adaptive;
using GridBot.Core.Services.Configuration;
using GridBot.TrendIntelligence.Models;
using GridBot.TrendIntelligence.Services.Indicators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GridBot.ApiService.Services.Adaptive;

/// <summary>
/// Calculates auto-tuned parameter suggestions based on market conditions.
/// Uses ATR with EMA smoothing and applies change limiting for stability.
/// </summary>
/// <remarks>
/// This is a singleton service that maintains ATR history for smoothing.
/// It uses IServiceScopeFactory to resolve scoped exchange client dependencies,
/// supporting dynamic network switching.
/// </remarks>
public sealed class AdaptiveParameterService : IAdaptiveParameterService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIndicatorService _indicatorService;
    private readonly IGridConfigurationService _configService;
    private readonly ILogger<AdaptiveParameterService> _logger;

    // ATR smoothing: EMA of last 3 ATR values
    private readonly Queue<decimal> _atrHistory = new(3);
    private readonly object _atrLock = new();

    // Cadence: Only recalculate every 60 seconds
    private DateTimeOffset _lastCalculation = DateTimeOffset.MinValue;
    private AdaptiveSuggestions? _cachedSuggestions;
    private static readonly TimeSpan CalculationInterval = TimeSpan.FromSeconds(60);

    public AdaptiveParameterService(
        IServiceScopeFactory scopeFactory,
        IIndicatorService indicatorService,
        IGridConfigurationService configService,
        ILogger<AdaptiveParameterService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _indicatorService = indicatorService ?? throw new ArgumentNullException(nameof(indicatorService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<AdaptiveSuggestions> CalculateSuggestionsAsync(
        int marketId,
        decimal equity,
        CancellationToken ct = default)
    {
        // Check cadence - return cached if within interval
        var now = DateTimeOffset.UtcNow;
        if (_cachedSuggestions is not null && now - _lastCalculation < CalculationInterval)
        {
            return _cachedSuggestions;
        }

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var marketDataClient = scope.ServiceProvider.GetRequiredService<IMarketDataClient>();

            // Fetch 1-hour candlesticks for ATR calculation (need at least 15 for 14-period ATR)
            var marketIdStr = marketId.ToString();
            var candles = await marketDataClient.GetCandlesticksAsync(
                marketIdStr,
                resolution: "1h",
                count: 20,
                ct).ConfigureAwait(false);

            if (candles is null || candles.Count < 3)
            {
                _logger.LogWarning("Insufficient candlestick data for ATR calculation");
                return AdaptiveSuggestions.Empty("Insufficient market data");
            }

            // Convert to TrendIntelligence model
            var candleData = candles
                .Select(c => new CandlestickData
                {
                    Timestamp = c.Timestamp,
                    Open = c.Open,
                    High = c.High,
                    Low = c.Low,
                    Close = c.Close,
                    Volume = c.Volume
                })
                .OrderBy(c => c.Timestamp)
                .ToList();

            // Calculate ATR
            var rawAtr = _indicatorService.CalculateAtr(candleData, period: 14);
            if (rawAtr <= 0)
            {
                _logger.LogWarning("ATR calculation returned zero or negative");
                return AdaptiveSuggestions.Empty("ATR calculation failed");
            }

            // Get current price for ATR percentage
            var currentPrice = candleData[^1].Close;
            if (currentPrice <= 0)
            {
                _logger.LogWarning("Invalid current price");
                return AdaptiveSuggestions.Empty("Invalid price data");
            }

            var rawAtrPercent = rawAtr / currentPrice * 100m;

            // Apply EMA smoothing over last 3 ATR values
            var smoothedAtrPercent = ApplyAtrSmoothing(rawAtrPercent);

            // Calculate spacing with change limiting
            var suggestions = CalculateSuggestions(smoothedAtrPercent, equity);

            _cachedSuggestions = suggestions;
            _lastCalculation = now;

            _logger.LogDebug(
                "Calculated adaptive suggestions: ATR={AtrPercent:F3}%, Spacing={Spacing:F3}%, OrderSize={OrderSize:F2}",
                smoothedAtrPercent,
                suggestions.SuggestedSpacing,
                suggestions.SuggestedOrderSize);

            return suggestions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate adaptive suggestions");
            return AdaptiveSuggestions.Empty($"Calculation error: {ex.Message}");
        }
    }

    private decimal ApplyAtrSmoothing(decimal rawAtrPercent)
    {
        lock (_atrLock)
        {
            _atrHistory.Enqueue(rawAtrPercent);
            while (_atrHistory.Count > 3)
            {
                _atrHistory.Dequeue();
            }

            if (_atrHistory.Count == 0)
                return rawAtrPercent;

            // EMA with 3-period smoothing: k = 2 / (3 + 1) = 0.5
            // For simplicity, use weighted average: newest = 40%, middle = 35%, oldest = 25%
            var values = _atrHistory.ToArray();
            return values.Length switch
            {
                1 => values[0],
                2 => values[0] * 0.4m + values[1] * 0.6m,
                _ => values[0] * 0.25m + values[1] * 0.35m + values[2] * 0.4m
            };
        }
    }

    private AdaptiveSuggestions CalculateSuggestions(decimal smoothedAtrPercent, decimal equity)
    {
        var current = _configService.Current;
        var reasonBuilder = new System.Text.StringBuilder();

        // === Grid Spacing Calculation ===
        // Formula: spacing = clamp(ATR% * 0.5, 0.3%, 2.0%)
        var rawSpacing = smoothedAtrPercent * 0.5m;
        var clampedSpacing = Math.Clamp(
            rawSpacing,
            HardLimits.MinSpacingClamp,
            HardLimits.MaxSpacingClamp);

        // Apply change limiting: max 20% change from current
        var currentSpacing = current.GridSpacingPercent.EffectiveValue;
        var maxChange = currentSpacing * 0.20m;
        var proposedChange = clampedSpacing - currentSpacing;
        var actualChange = Math.Clamp(proposedChange, -maxChange, maxChange);
        var suggestedSpacing = currentSpacing + actualChange;

        // Ensure within hard limits
        suggestedSpacing = Math.Clamp(
            suggestedSpacing,
            HardLimits.MinGridSpacingPercent,
            HardLimits.MaxGridSpacingPercent);

        reasonBuilder.AppendLine($"ATR: {smoothedAtrPercent:F2}% (smoothed)");
        reasonBuilder.AppendLine($"Spacing: {suggestedSpacing:F2}% (ATR * 0.5, clamped {HardLimits.MinSpacingClamp}-{HardLimits.MaxSpacingClamp}%)");

        // === Order Size Calculation ===
        // Formula: orderSize = clamp((equity * maxPosition%) / effectiveLevels, 10, min(equity * 5%, 5000))
        var currentBuyLevels = current.BuyLevels.EffectiveValue;
        var currentSellLevels = current.SellLevels.EffectiveValue;
        var totalLevels = currentBuyLevels + currentSellLevels;

        // Clamp effective levels
        var effectiveLevels = Math.Clamp(
            totalLevels,
            HardLimits.MinEffectiveLevels,
            HardLimits.MaxEffectiveLevels);

        var maxPositionPercent = current.MaxPositionPercent / 100m;
        var rawOrderSize = equity * maxPositionPercent / effectiveLevels;

        // Apply order size caps
        var maxOrderSize = Math.Min(
            equity * HardLimits.MaxOrderSizeEquityPercent,
            HardLimits.MaxOrderSizeAbsolute);

        var suggestedOrderSize = Math.Clamp(
            rawOrderSize,
            HardLimits.MinOrderSizeUsdc,
            maxOrderSize);

        reasonBuilder.AppendLine($"Order size: {suggestedOrderSize:F2} USDC (equity {equity:F0} / {effectiveLevels} levels)");

        // For now, keep current levels (can be enhanced later)
        var suggestedBuyLevels = currentBuyLevels;
        var suggestedSellLevels = currentSellLevels;

        // Round spacing to 2 decimal places for cleaner display
        suggestedSpacing = Math.Round(suggestedSpacing, 2);
        suggestedOrderSize = Math.Round(suggestedOrderSize, 2);

        return new AdaptiveSuggestions(
            SuggestedSpacing: suggestedSpacing,
            SuggestedBuyLevels: suggestedBuyLevels,
            SuggestedSellLevels: suggestedSellLevels,
            SuggestedOrderSize: suggestedOrderSize,
            Reasoning: reasonBuilder.ToString().TrimEnd());
    }
}
