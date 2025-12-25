namespace GridBot.Abstractions.Models.Enums;

/// <summary>
/// Specifies how long an order remains active before it is executed or expires.
/// </summary>
public enum TimeInForce
{
    /// <summary>
    /// Immediate or Cancel - execute immediately what can be filled, cancel the rest.
    /// </summary>
    ImmediateOrCancel = 0,

    /// <summary>
    /// Good Till Cancel - order remains active until filled or explicitly cancelled.
    /// </summary>
    GoodTillCancel = 1,

    /// <summary>
    /// Post Only - order will only be placed if it would be added to the order book (maker only).
    /// </summary>
    PostOnly = 2,

    /// <summary>
    /// Fill or Kill - order must be filled completely immediately or cancelled entirely.
    /// </summary>
    FillOrKill = 3
}
