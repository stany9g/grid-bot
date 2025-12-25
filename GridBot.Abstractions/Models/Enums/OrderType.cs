namespace GridBot.Abstractions.Models.Enums;

/// <summary>
/// Specifies the type of order to be placed on the exchange.
/// </summary>
public enum OrderType
{
    /// <summary>
    /// A limit order that will only execute at the specified price or better.
    /// </summary>
    Limit = 0,

    /// <summary>
    /// A market order that executes immediately at the best available price.
    /// </summary>
    Market = 1,

    /// <summary>
    /// A stop-loss order that triggers a market order when the stop price is reached.
    /// </summary>
    StopLoss = 2,

    /// <summary>
    /// A stop-loss limit order that triggers a limit order when the stop price is reached.
    /// </summary>
    StopLossLimit = 3,

    /// <summary>
    /// A take-profit order that triggers a market order when the target price is reached.
    /// </summary>
    TakeProfit = 4,

    /// <summary>
    /// A take-profit limit order that triggers a limit order when the target price is reached.
    /// </summary>
    TakeProfitLimit = 5
}
