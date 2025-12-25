using GridBot.ApiService.Configuration;
using GridBot.TrendIntelligence.Services;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Adapters;

/// <summary>
/// Adapter that bridges TrendIntelligence ITrendConfiguration to ApiService's TradingBotOptions.
/// </summary>
public sealed class TrendConfigurationAdapter : ITrendConfiguration
{
    private readonly TrendOptions _options;

    public TrendConfigurationAdapter(IOptions<TradingBotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.Trend;
    }

    public int EmaFastPeriod => _options.EmaFastPeriod;
    public int EmaSlowPeriod => _options.EmaSlowPeriod;
    public int ConfirmationDelayMinutes => _options.ConfirmationDelayMinutes;
    public int TrendFlipCooldownMinutes => _options.TrendFlipCooldownMinutes;
    public decimal AdxStrongTrendThreshold => _options.AdxStrongTrendThreshold;
    public decimal AdxNeutralThreshold => _options.AdxNeutralThreshold;
    public decimal EmaNeutralProximityPercent => _options.EmaNeutralProximityPercent;
    public decimal RebalanceTolerancePercent => _options.RebalanceTolerancePercent;
    public decimal MaxRebalanceRatePercent => _options.MaxRebalanceRatePercent;
    public int MinRebalanceIntervalMinutes => _options.MinRebalanceIntervalMinutes;
    public decimal EmergencyRebalanceThresholdPercent => _options.EmergencyRebalanceThresholdPercent;
}
