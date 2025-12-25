using GridBot.ApiService.Configuration;
using GridBot.MoonBag.Services;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Adapters;

/// <summary>
/// Adapter that bridges MoonBag IMoonBagConfiguration to ApiService's TradingBotOptions.
/// </summary>
public sealed class MoonBagConfigurationAdapter : IMoonBagConfiguration
{
    private readonly MoonBagOptions _options;

    public MoonBagConfigurationAdapter(IOptions<TradingBotOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value.MoonBag;
    }

    public decimal MoonBagPercentage => _options.MoonBagPercentage;
    public decimal TrailingGridStep => _options.TrailingGridStep;
    public decimal MaxTrailDistance => _options.MaxTrailDistance;
    public decimal InitialTrailingStopPercent => _options.InitialTrailingStopPercent;
    public decimal TightenedStopPercent => _options.TightenedStopPercent;
    public decimal AggressiveStopPercent => _options.AggressiveStopPercent;
    public decimal EmergencyStopPercent => _options.EmergencyStopPercent;
    public decimal TightenAtProfitPercent50 => _options.TightenAtProfitPercent50;
    public decimal FlashSpikeThreshold => _options.FlashSpikeThreshold;
    public int FlashSpikeCooldownMinutes => _options.FlashSpikeCooldownMinutes;
    public int WarmUpPeriodMinutes => _options.WarmUpPeriodMinutes;
    public decimal MinimumMoonBagUsd => _options.MinimumMoonBagUsd;
    public int ShiftCooldownSeconds => _options.ShiftCooldownSeconds;
    public decimal MaxShiftPercent => _options.MaxShiftPercent;
    public decimal MaxCumulativeShift1h => _options.MaxCumulativeShift1h;
    public bool EnableShortMoonBag => _options.EnableShortMoonBag;
    public decimal TrailingStopActivationThreshold => _options.TrailingStopActivationThreshold;
    public int TrailingStopConfirmationTicks => _options.TrailingStopConfirmationTicks;
    public int TrailingStopUpdateIntervalSeconds => _options.TrailingStopUpdateIntervalSeconds;
    public decimal HighWatermarkUpdateThreshold => _options.HighWatermarkUpdateThreshold;
    public bool AutoReleaseEnabled => _options.AutoReleaseEnabled;
    public int AutoReleaseConfirmationHours => _options.AutoReleaseConfirmationHours;
    public decimal AutoReleaseUnrealizedLossPercent => _options.AutoReleaseUnrealizedLossPercent;
    public bool AllowOperatorOverride => _options.AllowOperatorOverride;
}
