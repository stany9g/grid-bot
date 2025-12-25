using GridBot.Core.Models;

namespace GridBot.Core.Services.Risk;

/// <summary>
/// Basic risk monitoring - flash crash detection and daily loss limit only.
/// </summary>
public interface IBasicRiskMonitor
{
    /// <summary>
    /// Checks if trading is safe to continue.
    /// </summary>
    /// <param name="currentPrice">Current market price.</param>
    /// <param name="accountEquity">Current account equity in USDC.</param>
    /// <param name="todayPnl">Today's realized P&L in USDC.</param>
    /// <returns>Risk status indicating if trading is safe.</returns>
    RiskStatus Check(decimal currentPrice, decimal accountEquity, decimal todayPnl);

    /// <summary>
    /// Records a price sample for flash crash detection.
    /// </summary>
    /// <param name="price">Current price.</param>
    /// <param name="timestamp">Timestamp of the price.</param>
    void RecordPrice(decimal price, DateTimeOffset timestamp);

    /// <summary>
    /// Resets daily statistics (call at day rollover).
    /// </summary>
    void ResetDaily();
}
