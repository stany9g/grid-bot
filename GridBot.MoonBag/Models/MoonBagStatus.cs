namespace GridBot.MoonBag.Models;

/// <summary>
/// Current status of moon bag protection for a specific market.
/// </summary>
public sealed class MoonBagStatus
{
    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Current moon bag protection state.
    /// </summary>
    public MoonBagState State { get; set; } = MoonBagState.Inactive;

    /// <summary>
    /// Maximum position size achieved since position opened.
    /// Used as basis for moon bag threshold calculation.
    /// </summary>
    public decimal MaxPositionAchieved { get; set; }

    /// <summary>
    /// Highest price seen since tracking began.
    /// Used for trailing stop calculation.
    /// </summary>
    public decimal HighWatermarkPrice { get; set; }

    /// <summary>
    /// Current trailing stop price level.
    /// Position portion above moon bag will be sold if price falls to this level.
    /// </summary>
    public decimal TrailingStopPrice { get; set; }

    /// <summary>
    /// Quantity of the position that is locked as moon bag.
    /// This quantity is protected from automated selling.
    /// </summary>
    public decimal LockedQuantity { get; set; }

    /// <summary>
    /// Timestamp when warm-up period started.
    /// </summary>
    public DateTimeOffset? WarmUpStartedAt { get; set; }

    /// <summary>
    /// Timestamp when position was opened.
    /// </summary>
    public DateTimeOffset? PositionOpenedAt { get; set; }

    /// <summary>
    /// Current unrealized profit as percentage.
    /// Used to determine trailing stop tier.
    /// </summary>
    public decimal CurrentProfitPercent { get; set; }

    /// <summary>
    /// Whether moon bag release has been approved by operator.
    /// Required for HOLD_MODE -> RELEASED transition.
    /// </summary>
    public bool IsReleaseApproved { get; set; }

    /// <summary>
    /// Entry price for the position (average cost basis).
    /// </summary>
    public decimal EntryPrice { get; set; }

    /// <summary>
    /// Initial grid upper bound when moon bag tracking began.
    /// Used to determine trailing stop activation.
    /// </summary>
    public decimal InitialGridUpperBound { get; set; }

    /// <summary>
    /// Current trailing stop tier based on profit percentage.
    /// </summary>
    public TrailingStopTier CurrentTier { get; set; } = TrailingStopTier.Standard;

    /// <summary>
    /// Timestamp of last high watermark update.
    /// </summary>
    public DateTimeOffset? LastHighWatermarkUpdate { get; set; }

    /// <summary>
    /// Timestamp of last state transition.
    /// </summary>
    public DateTimeOffset LastStateTransition { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Number of consecutive price ticks below trailing stop.
    /// Used for stop trigger confirmation.
    /// </summary>
    public int ConsecutiveStopTriggerTicks { get; set; }

    /// <summary>
    /// Whether the trailing stop order is currently placed on the exchange.
    /// </summary>
    public bool HasActiveStopOrder { get; set; }

    /// <summary>
    /// Order ID of the trailing stop order if placed.
    /// </summary>
    public long? StopOrderId { get; set; }

    /// <summary>
    /// Position direction (true = long, false = short).
    /// Moon bag protection behavior differs by direction.
    /// </summary>
    public bool IsLongPosition { get; set; } = true;

    /// <summary>
    /// Reason for current state.
    /// </summary>
    public string? StateReason { get; set; }

    /// <summary>
    /// When StrongBear trend was first detected.
    /// Null if not in StrongBear trend.
    /// </summary>
    public DateTimeOffset? StrongBearStartTime { get; set; }

    /// <summary>
    /// Whether auto-release conditions are currently met.
    /// </summary>
    public bool AutoReleaseEligible { get; set; }

    /// <summary>
    /// Reason auto-release is blocked, if any.
    /// </summary>
    public string? AutoReleaseBlockedReason { get; set; }

    /// <summary>
    /// Operator has explicitly disabled auto-release for this market.
    /// </summary>
    public bool OperatorDisabledAutoRelease { get; set; }

    /// <summary>
    /// Creates a status instance indicating moon bag is inactive.
    /// </summary>
    public static MoonBagStatus Inactive(int marketId) => new()
    {
        MarketId = marketId,
        State = MoonBagState.Inactive,
        StateReason = "No active position or position too small"
    };
}
