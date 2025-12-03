namespace GridBot.ApiService.Models.Trading;

/// <summary>
/// Data context gathered for a decision cycle.
/// Contains all market data and state information needed for decision making.
/// </summary>
public sealed record DecisionContext
{
    /// <summary>
    /// Lighter DEX market ID.
    /// </summary>
    public required int MarketId { get; init; }

    /// <summary>
    /// When this context was collected.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current market price.
    /// </summary>
    public decimal CurrentPrice { get; init; }

    /// <summary>
    /// Current position size (null if unable to fetch).
    /// Positive for long, negative for short.
    /// </summary>
    public decimal? Position { get; init; }

    /// <summary>
    /// Current equity value (null if unable to fetch).
    /// </summary>
    public decimal? Equity { get; init; }

    /// <summary>
    /// Order book snapshot (null if unable to fetch).
    /// </summary>
    public OrderBookSnapshot? OrderBookSnapshot { get; init; }

    /// <summary>
    /// Current grid state (null if grid not initialized).
    /// </summary>
    public GridState? GridState { get; init; }

    /// <summary>
    /// Risk assessment from the Risk Sentinel.
    /// </summary>
    public RiskAssessment? RiskAssessment { get; init; }

    /// <summary>
    /// Whether the price data is stale (from cache due to fetch failure).
    /// </summary>
    public bool IsPriceStale { get; init; }

    /// <summary>
    /// Whether the position data is stale (from cache due to fetch failure).
    /// </summary>
    public bool IsPositionStale { get; init; }

    /// <summary>
    /// Whether the order book data is stale (from cache due to fetch failure).
    /// </summary>
    public bool IsOrderBookStale { get; init; }

    /// <summary>
    /// Whether the grid state is stale (from cache due to fetch failure).
    /// </summary>
    public bool IsGridStateStale { get; init; }

    /// <summary>
    /// Time taken to collect all data.
    /// </summary>
    public TimeSpan DataCollectionDuration { get; init; }

    /// <summary>
    /// Whether any data collection timed out.
    /// </summary>
    public bool DataCollectionTimedOut { get; init; }

    /// <summary>
    /// Number of data sources that failed to fetch.
    /// </summary>
    public int FailedDataSources { get; init; }

    /// <summary>
    /// Order book depth (total USD value on both sides).
    /// </summary>
    public decimal OrderBookDepth => OrderBookSnapshot is not null
        ? OrderBookSnapshot.TotalBidDepth + OrderBookSnapshot.TotalAskDepth
        : 0m;

    /// <summary>
    /// Whether this context has sufficient data for trading decisions.
    /// Requires at minimum: current price and no critical stale data.
    /// Note: RiskAssessment is checked separately after data collection.
    /// </summary>
    public bool HasSufficientData =>
        CurrentPrice > 0 &&
        !IsPriceStale;

    /// <summary>
    /// Whether any data is stale.
    /// </summary>
    public bool HasStaleData =>
        IsPriceStale || IsPositionStale || IsOrderBookStale || IsGridStateStale;

    /// <summary>
    /// Creates a context with minimal data for error conditions.
    /// </summary>
    public static DecisionContext CreateMinimal(int marketId, TimeSpan collectionDuration)
    {
        return new DecisionContext
        {
            MarketId = marketId,
            Timestamp = DateTimeOffset.UtcNow,
            CurrentPrice = 0,
            IsPriceStale = true,
            IsPositionStale = true,
            IsOrderBookStale = true,
            IsGridStateStale = true,
            DataCollectionDuration = collectionDuration,
            DataCollectionTimedOut = true,
            FailedDataSources = 4
        };
    }
}
