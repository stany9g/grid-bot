namespace GridBot.Extended.Models;

/// <summary>
/// Order state machine states for Extended DEX.
/// Critical: Never trust HTTP 200 alone - must wait for WebSocket confirmation.
/// </summary>
public enum OrderState
{
    /// <summary>
    /// Order has been created locally but not yet submitted.
    /// </summary>
    Created = 0,

    /// <summary>
    /// Order submitted to API and awaiting WebSocket confirmation.
    /// HTTP 200 received but order not yet confirmed by matching engine.
    /// </summary>
    PendingConfirmation = 1,

    /// <summary>
    /// Order confirmed active by WebSocket (open or partially filled).
    /// </summary>
    Active = 2,

    /// <summary>
    /// Order rejected by matching engine (after HTTP 200).
    /// Check rejection reason and trigger inventory recalculation.
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// Order cancelled by user or system.
    /// </summary>
    Cancelled = 4,

    /// <summary>
    /// Order fully filled.
    /// </summary>
    Filled = 5,

    /// <summary>
    /// Order state unknown (e.g., after reconnection with missing data).
    /// Excluded from inventory calculations until reconciled.
    /// </summary>
    Unknown = 6
}
