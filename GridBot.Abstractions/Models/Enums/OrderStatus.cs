namespace GridBot.Abstractions.Models.Enums;

/// <summary>
/// Represents the status of an order on the exchange.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Order status is unknown or unrecognized.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Order is open and waiting to be filled.
    /// </summary>
    Open = 1,

    /// <summary>
    /// Order has been partially filled.
    /// </summary>
    PartiallyFilled = 2,

    /// <summary>
    /// Order has been fully filled.
    /// </summary>
    Filled = 3,

    /// <summary>
    /// Order has been cancelled.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Order was rejected by the exchange.
    /// </summary>
    Rejected = 5,

    /// <summary>
    /// Order has expired.
    /// </summary>
    Expired = 6
}
