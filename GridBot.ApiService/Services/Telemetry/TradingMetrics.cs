using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GridBot.ApiService.Services.Telemetry;

/// <summary>
/// Centralized trading metrics using System.Diagnostics.Metrics.
/// Thread-safe static class for recording ALTE trading bot telemetry.
/// All observable gauges are per-market to support multi-market operation.
/// </summary>
public static class TradingMetrics
{
    /// <summary>
    /// The meter name for the trading bot metrics.
    /// </summary>
    public const string MeterName = "GridBot.Trading";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // Thread-safe per-market gauge storage using double to avoid torn reads (decimal is 128-bit, not atomic)
    private static readonly ConcurrentDictionary<int, double> _positionMultiplierByMarket = new();
    private static readonly ConcurrentDictionary<int, double> _spreadMultiplierByMarket = new();
    private static readonly ConcurrentDictionary<int, int> _recoveryPhaseByMarket = new();
    private static readonly ConcurrentDictionary<int, double> _currentPriceByMarket = new();
    private static readonly ConcurrentDictionary<int, double> _currentEquityByMarket = new();
    private static readonly ConcurrentDictionary<int, int> _consecutiveTimeoutsByMarket = new();
    private static readonly ConcurrentDictionary<int, int> _operationalCapacityByMarket = new();

    // Histograms - for measuring distributions of values

    /// <summary>
    /// Duration of decision loop execution in milliseconds.
    /// </summary>
    public static readonly Histogram<double> DecisionLoopDuration = Meter.CreateHistogram<double>(
        "alte.decision_loop.duration",
        unit: "ms",
        description: "Duration of decision loop execution");

    /// <summary>
    /// Latency of data collection phase in milliseconds.
    /// </summary>
    public static readonly Histogram<double> DataCollectionLatency = Meter.CreateHistogram<double>(
        "alte.data_collection.latency",
        unit: "ms",
        description: "Latency of data collection phase");

    // Counters - for cumulative counts (monotonically increasing)

    /// <summary>
    /// Total orders placed.
    /// </summary>
    public static readonly Counter<long> OrdersPlaced = Meter.CreateCounter<long>(
        "alte.orders.placed",
        unit: "{orders}",
        description: "Total orders placed");

    /// <summary>
    /// Total orders cancelled.
    /// </summary>
    public static readonly Counter<long> OrdersCancelled = Meter.CreateCounter<long>(
        "alte.orders.cancelled",
        unit: "{orders}",
        description: "Total orders cancelled");

    /// <summary>
    /// Circuit breaker trigger count by type.
    /// </summary>
    public static readonly Counter<long> CircuitBreakerTriggers = Meter.CreateCounter<long>(
        "alte.circuit_breaker.triggers",
        unit: "{triggers}",
        description: "Circuit breaker trigger count by type");

    /// <summary>
    /// State transition count.
    /// </summary>
    public static readonly Counter<long> StateTransitions = Meter.CreateCounter<long>(
        "alte.state.transitions",
        unit: "{transitions}",
        description: "State transition count");

    /// <summary>
    /// Total decision cycles executed.
    /// </summary>
    public static readonly Counter<long> DecisionCyclesTotal = Meter.CreateCounter<long>(
        "alte.decision_cycles.total",
        unit: "{cycles}",
        description: "Total decision cycles executed");

    /// <summary>
    /// Failed decision cycles.
    /// </summary>
    public static readonly Counter<long> DecisionCyclesFailed = Meter.CreateCounter<long>(
        "alte.decision_cycles.failed",
        unit: "{cycles}",
        description: "Failed decision cycles");

    /// <summary>
    /// Skipped decision cycles.
    /// </summary>
    public static readonly Counter<long> DecisionCyclesSkipped = Meter.CreateCounter<long>(
        "alte.decision_cycles.skipped",
        unit: "{cycles}",
        description: "Skipped decision cycles");

    /// <summary>
    /// Trailing stop trigger count.
    /// </summary>
    public static readonly Counter<long> TrailingStopTriggers = Meter.CreateCounter<long>(
        "alte.trailing_stop.triggers",
        unit: "{triggers}",
        description: "Trailing stop trigger count");

    /// <summary>
    /// Grid shift count.
    /// </summary>
    public static readonly Counter<long> GridShifts = Meter.CreateCounter<long>(
        "alte.grid.shifts",
        unit: "{shifts}",
        description: "Grid shift count");

    // Observable gauges - values that can go up and down, read via callbacks
    // Each gauge emits measurements for all tracked markets

    /// <summary>
    /// Current effective position multiplier per market.
    /// </summary>
    public static readonly ObservableGauge<double> PositionMultiplier = Meter.CreateObservableGauge(
        "alte.multiplier.position",
        () => _positionMultiplierByMarket.Select(kvp =>
            new Measurement<double>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        description: "Current effective position multiplier per market");

    /// <summary>
    /// Current effective spread multiplier per market.
    /// </summary>
    public static readonly ObservableGauge<double> SpreadMultiplier = Meter.CreateObservableGauge(
        "alte.multiplier.spread",
        () => _spreadMultiplierByMarket.Select(kvp =>
            new Measurement<double>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        description: "Current effective spread multiplier per market");

    /// <summary>
    /// Current recovery phase per market (0=None, 1=Phase1, 2=Phase2, 3=Phase3, 4=Phase4).
    /// </summary>
    public static readonly ObservableGauge<int> RecoveryPhase = Meter.CreateObservableGauge(
        "alte.recovery.phase",
        () => _recoveryPhaseByMarket.Select(kvp =>
            new Measurement<int>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        description: "Current recovery phase per market (0=None, 1-4=Phase1-4)");

    /// <summary>
    /// Current market price in USD per market.
    /// </summary>
    public static readonly ObservableGauge<double> CurrentPrice = Meter.CreateObservableGauge(
        "alte.market.price",
        () => _currentPriceByMarket.Select(kvp =>
            new Measurement<double>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        unit: "USD",
        description: "Current market price per market");

    /// <summary>
    /// Current account equity in USD per market.
    /// </summary>
    public static readonly ObservableGauge<double> CurrentEquity = Meter.CreateObservableGauge(
        "alte.account.equity",
        () => _currentEquityByMarket.Select(kvp =>
            new Measurement<double>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        unit: "USD",
        description: "Current account equity per market");

    /// <summary>
    /// Consecutive API timeout count per market.
    /// </summary>
    public static readonly ObservableGauge<int> ConsecutiveTimeouts = Meter.CreateObservableGauge(
        "alte.api.consecutive_timeouts",
        () => _consecutiveTimeoutsByMarket.Select(kvp =>
            new Measurement<int>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        description: "Consecutive API timeout count per market");

    /// <summary>
    /// Operational capacity per market (10-100%).
    /// </summary>
    public static readonly ObservableGauge<int> OperationalCapacity = Meter.CreateObservableGauge(
        "alte.capacity.operational",
        () => _operationalCapacityByMarket.Select(kvp =>
            new Measurement<int>(kvp.Value, new KeyValuePair<string, object?>(Tags.MarketId, kvp.Key))),
        unit: "%",
        description: "Operational capacity per market (10-100%)");

    // Thread-safe per-market setters (use double storage to avoid torn reads for decimal)

    /// <summary>
    /// Sets the current position multiplier gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetPositionMultiplier(int marketId, decimal value) =>
        _positionMultiplierByMarket[marketId] = (double)value;

    /// <summary>
    /// Sets the current spread multiplier gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetSpreadMultiplier(int marketId, decimal value) =>
        _spreadMultiplierByMarket[marketId] = (double)value;

    /// <summary>
    /// Sets the current recovery phase gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetRecoveryPhase(int marketId, int phase) =>
        _recoveryPhaseByMarket[marketId] = phase;

    /// <summary>
    /// Sets the current price gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetCurrentPrice(int marketId, decimal price) =>
        _currentPriceByMarket[marketId] = (double)price;

    /// <summary>
    /// Sets the current equity gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetCurrentEquity(int marketId, decimal equity) =>
        _currentEquityByMarket[marketId] = (double)equity;

    /// <summary>
    /// Sets the consecutive timeouts gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetConsecutiveTimeouts(int marketId, int count) =>
        _consecutiveTimeoutsByMarket[marketId] = count;

    /// <summary>
    /// Sets the operational capacity gauge value for a specific market.
    /// Thread-safe: ConcurrentDictionary ensures atomic updates.
    /// </summary>
    public static void SetOperationalCapacity(int marketId, int capacity) =>
        _operationalCapacityByMarket[marketId] = capacity;

    // Helper methods for common tag combinations to reduce allocations on hot paths

    /// <summary>
    /// Creates a TagList with just the market ID tag.
    /// Use this to avoid allocating new KeyValuePair arrays on every metric call.
    /// </summary>
    public static TagList MarketTag(int marketId) => new()
    {
        { Tags.MarketId, marketId }
    };

    /// <summary>
    /// Creates a TagList with market ID and result tags.
    /// Use this for decision loop duration and similar metrics.
    /// </summary>
    public static TagList MarketResultTag(int marketId, string result) => new()
    {
        { Tags.MarketId, marketId },
        { Tags.Result, result }
    };

    /// <summary>
    /// Creates a TagList for state transition metrics.
    /// </summary>
    public static TagList StateTransitionTag(int marketId, string fromState, string toState) => new()
    {
        { Tags.MarketId, marketId },
        { Tags.FromState, fromState },
        { Tags.ToState, toState }
    };

    /// <summary>
    /// Creates a TagList for circuit breaker trigger metrics.
    /// </summary>
    public static TagList CircuitBreakerTag(int marketId, string triggerType) => new()
    {
        { Tags.MarketId, marketId },
        { Tags.TriggerType, triggerType }
    };

    /// <summary>
    /// Common tag names for multi-dimensional metrics.
    /// </summary>
    public static class Tags
    {
        /// <summary>Market identifier tag.</summary>
        public const string MarketId = "market_id";

        /// <summary>Circuit breaker trigger type tag.</summary>
        public const string TriggerType = "trigger_type";

        /// <summary>Previous state for state transitions.</summary>
        public const string FromState = "from_state";

        /// <summary>New state for state transitions.</summary>
        public const string ToState = "to_state";

        /// <summary>Order side (buy/sell).</summary>
        public const string OrderSide = "side";

        /// <summary>Operation result (success/failed/skipped).</summary>
        public const string Result = "result";
    }
}
