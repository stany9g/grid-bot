using GridBot.Core.Configuration;
using GridBot.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.Core.Services.Risk;

/// <summary>
/// Basic risk monitor with flash crash detection and daily loss limit.
/// Thread-safe implementation.
/// </summary>
public sealed class BasicRiskMonitor : IBasicRiskMonitor
{
    private readonly SimpleGridConfig _config;
    private readonly ILogger<BasicRiskMonitor> _logger;
    private readonly object _lock = new();

    // Price history for flash crash detection (1-minute window)
    private readonly Queue<(decimal Price, DateTimeOffset Time)> _priceHistory = new();
    private static readonly TimeSpan FlashCrashWindow = TimeSpan.FromMinutes(1);

    // Daily tracking
    private decimal _startOfDayEquity;
    private DateOnly _currentDay;

    public BasicRiskMonitor(
        IOptions<SimpleGridConfig> config,
        ILogger<BasicRiskMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);

        _config = config.Value;
        _logger = logger;
        _currentDay = DateOnly.FromDateTime(DateTime.UtcNow);
    }

    /// <inheritdoc />
    public RiskStatus Check(decimal currentPrice, decimal accountEquity, decimal todayPnl)
    {
        lock (_lock)
        {
            // Check for day rollover
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            if (today != _currentDay)
            {
                ResetDailyInternal(accountEquity);
            }

            // Initialize start of day equity if needed
            if (_startOfDayEquity == 0)
            {
                _startOfDayEquity = accountEquity;
            }

            // 1. Flash crash check
            var flashCrashStatus = CheckFlashCrash(currentPrice);
            if (!flashCrashStatus.IsSafe)
            {
                _logger.LogWarning("Risk check failed: {Reason}", flashCrashStatus.Reason);
                return flashCrashStatus;
            }

            // 2. Daily loss limit check
            var lossLimitStatus = CheckDailyLossLimit(accountEquity);
            if (!lossLimitStatus.IsSafe)
            {
                _logger.LogWarning("Risk check failed: {Reason}", lossLimitStatus.Reason);
                return lossLimitStatus;
            }

            return RiskStatus.Safe;
        }
    }

    /// <inheritdoc />
    public void RecordPrice(decimal price, DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            // Add new price
            _priceHistory.Enqueue((price, timestamp));

            // Remove old prices outside the window
            var cutoff = timestamp - FlashCrashWindow;
            while (_priceHistory.Count > 0 && _priceHistory.Peek().Time < cutoff)
            {
                _priceHistory.Dequeue();
            }
        }
    }

    /// <inheritdoc />
    public void ResetDaily()
    {
        lock (_lock)
        {
            ResetDailyInternal(0);
        }
    }

    private void ResetDailyInternal(decimal currentEquity)
    {
        _currentDay = DateOnly.FromDateTime(DateTime.UtcNow);
        _startOfDayEquity = currentEquity;
        _logger.LogInformation("Daily reset - new day: {Day}, equity: {Equity:F2}",
            _currentDay, currentEquity);
    }

    private RiskStatus CheckFlashCrash(decimal currentPrice)
    {
        if (_priceHistory.Count == 0)
            return RiskStatus.Safe;

        // Get the highest price in the window
        var highestPrice = _priceHistory.Max(p => p.Price);

        if (highestPrice <= 0)
            return RiskStatus.Safe;

        // Calculate drop percentage
        var dropPercent = (highestPrice - currentPrice) / highestPrice * 100;

        if (dropPercent >= _config.FlashCrashThresholdPercent)
        {
            return RiskStatus.FlashCrash(dropPercent);
        }

        return RiskStatus.Safe;
    }

    private RiskStatus CheckDailyLossLimit(decimal currentEquity)
    {
        if (_startOfDayEquity <= 0)
            return RiskStatus.Safe;

        // Calculate loss percentage
        var lossPercent = (_startOfDayEquity - currentEquity) / _startOfDayEquity * 100;

        if (lossPercent >= _config.MaxDailyLossPercent)
        {
            return RiskStatus.DailyLossLimit(lossPercent, _config.MaxDailyLossPercent);
        }

        return RiskStatus.Safe;
    }
}
