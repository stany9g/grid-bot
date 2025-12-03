namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Record of a moon bag related event for logging and auditing.
/// </summary>
public sealed record MoonBagEvent
{
    /// <summary>
    /// Unique event identifier.
    /// </summary>
    public required string EventId { get; init; }

    /// <summary>
    /// Type of moon bag event.
    /// </summary>
    public required MoonBagEventType EventType { get; init; }

    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// Timestamp when event occurred.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Previous state before the event.
    /// </summary>
    public MoonBagState? PreviousState { get; init; }

    /// <summary>
    /// New state after the event.
    /// </summary>
    public MoonBagState? NewState { get; init; }

    /// <summary>
    /// Price at time of event.
    /// </summary>
    public decimal? Price { get; init; }

    /// <summary>
    /// High watermark at time of event.
    /// </summary>
    public decimal? HighWatermark { get; init; }

    /// <summary>
    /// Trailing stop price at time of event.
    /// </summary>
    public decimal? TrailingStopPrice { get; init; }

    /// <summary>
    /// Position size at time of event.
    /// </summary>
    public decimal? PositionSize { get; init; }

    /// <summary>
    /// Moon bag locked quantity at time of event.
    /// </summary>
    public decimal? LockedQuantity { get; init; }

    /// <summary>
    /// Profit percentage at time of event.
    /// </summary>
    public decimal? ProfitPercent { get; init; }

    /// <summary>
    /// Human-readable description of the event.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Additional details or context.
    /// </summary>
    public string? Details { get; init; }

    /// <summary>
    /// Creates a new event ID.
    /// </summary>
    public static string NewEventId() => $"MB-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";

    /// <summary>
    /// Creates a state transition event.
    /// </summary>
    public static MoonBagEvent StateTransition(
        int marketId,
        MoonBagState from,
        MoonBagState to,
        string reason,
        decimal? price = null) => new()
    {
        EventId = NewEventId(),
        EventType = MoonBagEventType.StateTransition,
        MarketId = marketId,
        PreviousState = from,
        NewState = to,
        Price = price,
        Description = $"State transition: {from} -> {to}",
        Details = reason
    };

    /// <summary>
    /// Creates a high watermark update event.
    /// </summary>
    public static MoonBagEvent HighWatermarkUpdated(
        int marketId,
        decimal oldWatermark,
        decimal newWatermark) => new()
    {
        EventId = NewEventId(),
        EventType = MoonBagEventType.HighWatermarkUpdated,
        MarketId = marketId,
        HighWatermark = newWatermark,
        Price = newWatermark,
        Description = $"High watermark updated: {oldWatermark:F4} -> {newWatermark:F4}"
    };

    /// <summary>
    /// Creates a trailing stop tightened event.
    /// </summary>
    public static MoonBagEvent TrailingStopTightened(
        int marketId,
        TrailingStopTier newTier,
        decimal newStopPrice,
        decimal profitPercent) => new()
    {
        EventId = NewEventId(),
        EventType = MoonBagEventType.TrailingStopTightened,
        MarketId = marketId,
        TrailingStopPrice = newStopPrice,
        ProfitPercent = profitPercent,
        Description = $"Trailing stop tightened to {newTier} tier at {newStopPrice:F4}",
        Details = $"Profit: {profitPercent:P1}"
    };

    /// <summary>
    /// Creates a trailing stop triggered event.
    /// </summary>
    public static MoonBagEvent TrailingStopTriggered(
        int marketId,
        decimal triggerPrice,
        decimal positionSize,
        decimal moonBagQuantity) => new()
    {
        EventId = NewEventId(),
        EventType = MoonBagEventType.TrailingStopTriggered,
        MarketId = marketId,
        Price = triggerPrice,
        TrailingStopPrice = triggerPrice,
        PositionSize = positionSize,
        LockedQuantity = moonBagQuantity,
        Description = $"Trailing stop triggered at {triggerPrice:F4}",
        Details = $"Selling {positionSize - moonBagQuantity:F4}, preserving moon bag of {moonBagQuantity:F4}"
    };

    /// <summary>
    /// Creates a grid shift event.
    /// </summary>
    public static MoonBagEvent GridShifted(
        int marketId,
        decimal shiftAmount,
        decimal newUpperBound,
        decimal newLowerBound) => new()
    {
        EventId = NewEventId(),
        EventType = MoonBagEventType.GridShifted,
        MarketId = marketId,
        Description = $"Grid shifted up by {shiftAmount:P2}",
        Details = $"New bounds: {newLowerBound:F4} - {newUpperBound:F4}"
    };

    /// <summary>
    /// Creates a moon bag release event.
    /// </summary>
    public static MoonBagEvent MoonBagReleased(
        int marketId,
        decimal quantity,
        string reason) => new()
    {
        EventId = NewEventId(),
        EventType = MoonBagEventType.MoonBagReleased,
        MarketId = marketId,
        LockedQuantity = quantity,
        Description = "Moon bag released for sale",
        Details = reason
    };
}

/// <summary>
/// Types of moon bag events for logging.
/// </summary>
public enum MoonBagEventType
{
    /// <summary>
    /// State machine transition occurred.
    /// </summary>
    StateTransition,

    /// <summary>
    /// High watermark price was updated.
    /// </summary>
    HighWatermarkUpdated,

    /// <summary>
    /// Trailing stop distance was tightened.
    /// </summary>
    TrailingStopTightened,

    /// <summary>
    /// Trailing stop was triggered.
    /// </summary>
    TrailingStopTriggered,

    /// <summary>
    /// Trailing stop order was placed or updated.
    /// </summary>
    TrailingStopOrderUpdated,

    /// <summary>
    /// Grid was shifted upward.
    /// </summary>
    GridShifted,

    /// <summary>
    /// Flash spike detected, grid shift paused.
    /// </summary>
    FlashSpikeDetected,

    /// <summary>
    /// Moon bag protection activated.
    /// </summary>
    MoonBagActivated,

    /// <summary>
    /// Moon bag was released for sale.
    /// </summary>
    MoonBagReleased,

    /// <summary>
    /// Sell order blocked due to moon bag protection.
    /// </summary>
    SellOrderBlocked,

    /// <summary>
    /// Warm-up period started.
    /// </summary>
    WarmUpStarted,

    /// <summary>
    /// Warm-up period completed.
    /// </summary>
    WarmUpCompleted,

    /// <summary>
    /// Early activation due to rapid price movement.
    /// </summary>
    EarlyActivation,

    /// <summary>
    /// Hold mode entered - only moon bag remains.
    /// </summary>
    HoldModeEntered,

    /// <summary>
    /// Release conditions check performed.
    /// </summary>
    ReleaseConditionsChecked,

    /// <summary>
    /// Operator approved moon bag release.
    /// </summary>
    OperatorApprovedRelease
}
