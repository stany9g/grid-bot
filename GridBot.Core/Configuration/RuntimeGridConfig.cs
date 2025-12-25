using System.Text.Json.Serialization;

namespace GridBot.Core.Configuration;

/// <summary>
/// Wrapper for configuration values that support hybrid auto-tuning.
/// Engine suggests values, user can override with manual value.
/// </summary>
/// <typeparam name="T">The value type (must be a struct).</typeparam>
public sealed class ConfigValue<T> where T : struct
{
    /// <summary>
    /// User-set value (manual override).
    /// </summary>
    public T Value { get; set; }

    /// <summary>
    /// Auto-tuned suggestion from adaptive parameter service.
    /// </summary>
    public T? SuggestedValue { get; set; }

    /// <summary>
    /// True = use suggested value, false = use manual value.
    /// </summary>
    public bool IsAuto { get; set; }

    /// <summary>
    /// Returns SuggestedValue when IsAuto=true and suggestion exists, otherwise returns Value.
    /// </summary>
    [JsonIgnore]
    public T EffectiveValue => IsAuto && SuggestedValue.HasValue ? SuggestedValue.Value : Value;

    /// <summary>
    /// Creates a ConfigValue with default value and manual mode.
    /// </summary>
    public ConfigValue() { }

    /// <summary>
    /// Creates a ConfigValue with specified initial value.
    /// </summary>
    public ConfigValue(T initialValue)
    {
        Value = initialValue;
    }

    /// <summary>
    /// Creates a ConfigValue in auto mode with initial value as fallback.
    /// </summary>
    public static ConfigValue<T> Auto(T fallbackValue) => new()
    {
        Value = fallbackValue,
        IsAuto = true
    };

    /// <summary>
    /// Creates a deep copy of this ConfigValue.
    /// </summary>
    public ConfigValue<T> Clone() => new()
    {
        Value = Value,
        SuggestedValue = SuggestedValue,
        IsAuto = IsAuto
    };
}

/// <summary>
/// Runtime-editable grid configuration with hybrid auto-tuning support.
/// Persisted to Redis and editable via dashboard.
/// </summary>
public sealed class RuntimeGridConfig
{
    /// <summary>
    /// Redis key for configuration persistence.
    /// </summary>
    public const string CacheKey = "gridbot:config:v1";

    // ===== Grid Strategy (Auto-Tunable) =====

    /// <summary>
    /// Spacing between grid levels as percentage of price.
    /// Auto-tuned based on ATR.
    /// Hard limits: 0.15% - 5.0%
    /// </summary>
    public ConfigValue<decimal> GridSpacingPercent { get; set; } = new(0.5m);

    /// <summary>
    /// Number of buy orders below current price.
    /// Auto-tuned based on market conditions.
    /// Hard limits: 2 - 30 per side (total 4-60)
    /// </summary>
    public ConfigValue<int> BuyLevels { get; set; } = new(10);

    /// <summary>
    /// Number of sell orders above current price.
    /// Auto-tuned based on market conditions.
    /// Hard limits: 2 - 30 per side (total 4-60)
    /// </summary>
    public ConfigValue<int> SellLevels { get; set; } = new(10);

    /// <summary>
    /// Size of each grid order in USDC.
    /// Auto-tuned based on equity and levels.
    /// Hard limits: 5 USDC minimum
    /// </summary>
    public ConfigValue<decimal> OrderSizeUsdc { get; set; } = new(100m);

    // ===== Risk Configuration (Fixed - Not Auto-Tunable) =====

    /// <summary>
    /// Maximum allowed daily loss as percentage of account value.
    /// Trading pauses when limit reached.
    /// Hard limits: 1% - 20%
    /// </summary>
    public decimal MaxDailyLossPercent { get; set; } = 5m;

    /// <summary>
    /// Flash crash detection threshold.
    /// If price drops this percentage in 1 minute, trading pauses.
    /// Hard limits: 3% - 15%
    /// </summary>
    public decimal FlashCrashThresholdPercent { get; set; } = 8m;

    /// <summary>
    /// How long to pause after a risk event (in minutes).
    /// </summary>
    public int PauseCooldownMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum position size as percentage of account equity.
    /// </summary>
    public decimal MaxPositionPercent { get; set; } = 10m;

    // ===== Exchange Configuration (Fixed) =====

    /// <summary>
    /// Trading market symbol (BTC or ETH).
    /// Index is resolved via API.
    /// </summary>
    public string Market { get; set; } = "BTC";

    /// <summary>
    /// Resolved market index from Lighter DEX.
    /// Set by MarketResolver, not user-editable.
    /// </summary>
    public int MarketIndex { get; set; } = 4;

    /// <summary>
    /// Position leverage multiplier.
    /// Hard limits: 1x - 10x
    /// </summary>
    public int Leverage { get; set; } = 3;

    // ===== Timing Configuration (Fixed) =====

    /// <summary>
    /// How often to run the trading loop (in seconds).
    /// </summary>
    public int LoopIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Use post-only orders (maker orders that don't cross spread).
    /// </summary>
    public bool UsePostOnlyOrders { get; set; } = true;

    /// <summary>
    /// Validates configuration against hard limits.
    /// </summary>
    /// <returns>List of validation errors, empty if valid.</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // Grid spacing limits
        var spacing = GridSpacingPercent.EffectiveValue;
        if (spacing < HardLimits.MinGridSpacingPercent)
            errors.Add($"Grid spacing {spacing}% is below minimum {HardLimits.MinGridSpacingPercent}%");
        if (spacing > HardLimits.MaxGridSpacingPercent)
            errors.Add($"Grid spacing {spacing}% exceeds maximum {HardLimits.MaxGridSpacingPercent}%");

        // Level limits
        var buyLevels = BuyLevels.EffectiveValue;
        var sellLevels = SellLevels.EffectiveValue;
        var totalLevels = buyLevels + sellLevels;

        if (totalLevels < HardLimits.MinTotalLevels)
            errors.Add($"Total levels {totalLevels} is below minimum {HardLimits.MinTotalLevels}");
        if (totalLevels > HardLimits.MaxTotalLevels)
            errors.Add($"Total levels {totalLevels} exceeds maximum {HardLimits.MaxTotalLevels}");

        // Order size limits
        var orderSize = OrderSizeUsdc.EffectiveValue;
        if (orderSize < HardLimits.MinOrderSizeUsdc)
            errors.Add($"Order size {orderSize} USDC is below minimum {HardLimits.MinOrderSizeUsdc} USDC");

        // Risk limits
        if (MaxDailyLossPercent < HardLimits.MinDailyLossPercent)
            errors.Add($"Max daily loss {MaxDailyLossPercent}% is below minimum {HardLimits.MinDailyLossPercent}%");
        if (MaxDailyLossPercent > HardLimits.MaxDailyLossPercent)
            errors.Add($"Max daily loss {MaxDailyLossPercent}% exceeds maximum {HardLimits.MaxDailyLossPercent}%");

        if (FlashCrashThresholdPercent < HardLimits.MinFlashCrashThresholdPercent)
            errors.Add($"Flash crash threshold {FlashCrashThresholdPercent}% is below minimum {HardLimits.MinFlashCrashThresholdPercent}%");
        if (FlashCrashThresholdPercent > HardLimits.MaxFlashCrashThresholdPercent)
            errors.Add($"Flash crash threshold {FlashCrashThresholdPercent}% exceeds maximum {HardLimits.MaxFlashCrashThresholdPercent}%");

        // Leverage limits
        if (Leverage < HardLimits.MinLeverage)
            errors.Add($"Leverage {Leverage}x is below minimum {HardLimits.MinLeverage}x");
        if (Leverage > HardLimits.MaxLeverage)
            errors.Add($"Leverage {Leverage}x exceeds maximum {HardLimits.MaxLeverage}x");

        return errors;
    }

    /// <summary>
    /// Returns true if any auto-tunable setting has IsAuto enabled.
    /// </summary>
    [JsonIgnore]
    public bool HasAutoSettings =>
        GridSpacingPercent.IsAuto ||
        BuyLevels.IsAuto ||
        SellLevels.IsAuto ||
        OrderSizeUsdc.IsAuto;

    /// <summary>
    /// Creates a RuntimeGridConfig from a SimpleGridConfig.
    /// Used for initial load from appsettings.
    /// </summary>
    public static RuntimeGridConfig FromSimpleConfig(SimpleGridConfig simple)
    {
        ArgumentNullException.ThrowIfNull(simple);

        return new RuntimeGridConfig
        {
            GridSpacingPercent = new ConfigValue<decimal>(simple.GridSpacingPercent),
            BuyLevels = new ConfigValue<int>(simple.BuyLevels),
            SellLevels = new ConfigValue<int>(simple.SellLevels),
            OrderSizeUsdc = new ConfigValue<decimal>(simple.OrderSizeUsdc),
            MaxDailyLossPercent = simple.MaxDailyLossPercent,
            FlashCrashThresholdPercent = simple.FlashCrashThresholdPercent,
            PauseCooldownMinutes = simple.PauseCooldownMinutes,
            MaxPositionPercent = simple.MaxPositionPercent,
            Market = ExtractMarketSymbol(simple.Market),
            MarketIndex = simple.MarketIndex,
            Leverage = simple.Leverage,
            LoopIntervalSeconds = simple.LoopIntervalSeconds,
            UsePostOnlyOrders = simple.UsePostOnlyOrders
        };
    }

    private static string ExtractMarketSymbol(string market)
    {
        // "BTC-USDC" -> "BTC"
        var dashIndex = market.IndexOf('-');
        return dashIndex > 0 ? market[..dashIndex] : market;
    }

    /// <summary>
    /// Creates a deep copy of this configuration.
    /// Used to return defensive copies from the configuration service.
    /// </summary>
    public RuntimeGridConfig Clone() => new()
    {
        // Auto-tunable (deep clone ConfigValue)
        GridSpacingPercent = GridSpacingPercent.Clone(),
        BuyLevels = BuyLevels.Clone(),
        SellLevels = SellLevels.Clone(),
        OrderSizeUsdc = OrderSizeUsdc.Clone(),

        // Fixed risk settings (value types)
        MaxDailyLossPercent = MaxDailyLossPercent,
        FlashCrashThresholdPercent = FlashCrashThresholdPercent,
        PauseCooldownMinutes = PauseCooldownMinutes,
        MaxPositionPercent = MaxPositionPercent,

        // Exchange settings
        Market = Market,
        MarketIndex = MarketIndex,
        Leverage = Leverage,

        // Timing settings
        LoopIntervalSeconds = LoopIntervalSeconds,
        UsePostOnlyOrders = UsePostOnlyOrders
    };
}

/// <summary>
/// Non-negotiable hard limits for configuration values.
/// These cannot be overridden by user, even with warnings.
/// </summary>
public static class HardLimits
{
    // Grid Spacing
    public const decimal MinGridSpacingPercent = 0.15m;
    public const decimal MaxGridSpacingPercent = 5.0m;

    // Risk
    public const decimal MinDailyLossPercent = 1m;
    public const decimal MaxDailyLossPercent = 20m;
    public const decimal MinFlashCrashThresholdPercent = 3m;
    public const decimal MaxFlashCrashThresholdPercent = 15m;

    // Leverage
    public const int MinLeverage = 1;
    public const int MaxLeverage = 10;

    // Order Size
    public const decimal MinOrderSizeUsdc = 5m;

    // Levels
    public const int MinTotalLevels = 4;
    public const int MaxTotalLevels = 60;
    public const int MinLevelsPerSide = 2;
    public const int MaxLevelsPerSide = 30;

    // Adaptive tuning
    public const decimal MinSpacingClamp = 0.3m;
    public const decimal MaxSpacingClamp = 2.0m;
    public const int MinEffectiveLevels = 4;
    public const int MaxEffectiveLevels = 40;
    public const decimal MaxOrderSizeEquityPercent = 0.05m;
    public const decimal MaxOrderSizeAbsolute = 5000m;
    public const decimal MinEquityForTrading = 100m;
}
