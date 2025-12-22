using GridBot.ApiService.Configuration;
using GridBot.ApiService.Models.Trading;
using GridBot.Lighter;
using GridBot.Lighter.Models.WebSocket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GridBot.ApiService.Services.Validation;

/// <summary>
/// Validates orders against market depth before submission.
/// Uses WebSocket order book data for real-time validation.
/// Thread-safe for concurrent access.
/// </summary>
/// <remarks>
/// Fail-closed design: If depth check fails or throws, reject the order (don't proceed with unknown risk).
/// </remarks>
public sealed class PreTradeValidator : IPreTradeValidator
{
    private readonly ILighterRealtimeState _realtimeState;
    private readonly IOptions<TradingBotOptions> _options;
    private readonly ILogger<PreTradeValidator> _logger;

    public PreTradeValidator(
        ILighterRealtimeState realtimeState,
        IOptions<TradingBotOptions> options,
        ILogger<PreTradeValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(realtimeState);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _realtimeState = realtimeState;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PreTradeValidation> ValidateOrderAsync(
        int marketId,
        bool isBuy,
        decimal orderSizeUsd,
        CancellationToken ct = default)
    {
        var preTrade = _options.Value.PreTrade;

        // Get order book from WebSocket state
        var orderBook = _realtimeState.GetOrderBook(marketId);

        // Rule: Fail closed - if no order book data, reject
        if (orderBook == null)
        {
            _logger.LogWarning(
                "PRE-TRADE FAIL: Order book not available for market {MarketId}",
                marketId);

            return Task.FromResult(PreTradeValidation.Invalid(
                "Order book not available - not subscribed or no data received",
                availableDepth: 0,
                totalDepth: 0,
                spreadPercent: 0,
                dataAge: TimeSpan.MaxValue));
        }

        var dataAge = DateTimeOffset.UtcNow - orderBook.LastUpdate;

        // Rule 3: Check data freshness (must be < 5 seconds)
        if (dataAge.TotalSeconds > preTrade.MaxDataAgeSeconds)
        {
            _logger.LogWarning(
                "PRE-TRADE FAIL: Order book data stale ({DataAge:F1}s > {Max}s) for market {MarketId}",
                dataAge.TotalSeconds, preTrade.MaxDataAgeSeconds, marketId);

            return Task.FromResult(PreTradeValidation.Invalid(
                $"Order book data too stale ({dataAge.TotalSeconds:F1}s > {preTrade.MaxDataAgeSeconds}s)",
                availableDepth: 0,
                totalDepth: 0,
                spreadPercent: orderBook.SpreadPercent,
                dataAge: dataAge));
        }

        // Calculate depth on relevant side
        // For BUY orders: check ASK depth (liquidity to buy from)
        // For SELL orders: check BID depth (liquidity to sell into)
        var relevantSide = isBuy ? orderBook.Asks : orderBook.Bids;
        var sideDepthUsd = CalculateSideDepthUsd(relevantSide);
        var bidDepthUsd = CalculateSideDepthUsd(orderBook.Bids);
        var askDepthUsd = CalculateSideDepthUsd(orderBook.Asks);
        var totalDepthUsd = bidDepthUsd + askDepthUsd;

        // Rule 5: Check spread (reject if > 1%)
        if (orderBook.SpreadPercent > preTrade.MaxAcceptableSpreadPercent)
        {
            _logger.LogWarning(
                "PRE-TRADE FAIL: Spread {Spread:F2}% > {Max:F2}% for market {MarketId} - order rejected",
                orderBook.SpreadPercent, preTrade.MaxAcceptableSpreadPercent, marketId);

            return Task.FromResult(PreTradeValidation.Invalid(
                $"Spread too wide ({orderBook.SpreadPercent:F2}% > {preTrade.MaxAcceptableSpreadPercent:F2}%)",
                availableDepth: sideDepthUsd,
                totalDepth: totalDepthUsd,
                spreadPercent: orderBook.SpreadPercent,
                dataAge: dataAge));
        }

        // Rule 2: Check critical depth threshold ($10k)
        if (totalDepthUsd < preTrade.CriticalDepthThresholdUsd)
        {
            _logger.LogError(
                "PRE-TRADE FAIL: CRITICAL depth ${Depth:F0} < ${Critical:F0} for market {MarketId} - ALL ORDERS REJECTED",
                totalDepthUsd, preTrade.CriticalDepthThresholdUsd, marketId);

            return Task.FromResult(PreTradeValidation.Invalid(
                $"CRITICAL: Order book depth ${totalDepthUsd:F0} < ${preTrade.CriticalDepthThresholdUsd:F0}",
                availableDepth: sideDepthUsd,
                totalDepth: totalDepthUsd,
                spreadPercent: orderBook.SpreadPercent,
                dataAge: dataAge));
        }

        // Rule 4: Check side depth (need 2x the order size available)
        if (sideDepthUsd < orderSizeUsd * 2)
        {
            var recommendedSize = sideDepthUsd / 2 * 0.9m; // 90% of half the available depth
            var side = isBuy ? "ask" : "bid";

            _logger.LogWarning(
                "PRE-TRADE FAIL: {Side} depth ${Depth:F0} < 2x order ${Order:F0} for market {MarketId}. Recommended: ${Recommended:F0}",
                side, sideDepthUsd, orderSizeUsd, marketId, recommendedSize);

            return Task.FromResult(PreTradeValidation.Invalid(
                $"Insufficient {side} depth: ${sideDepthUsd:F0} < 2x ${orderSizeUsd:F0}",
                availableDepth: sideDepthUsd,
                totalDepth: totalDepthUsd,
                spreadPercent: orderBook.SpreadPercent,
                dataAge: dataAge,
                recommendedSize: recommendedSize > 0 ? recommendedSize : null));
        }

        // Rule 1: Check order size vs total depth (10% max)
        var maxOrderSize = totalDepthUsd * preTrade.MaxOrderToDepthRatio;
        if (orderSizeUsd > maxOrderSize)
        {
            var recommendedSize = maxOrderSize * 0.9m; // 90% of max

            _logger.LogWarning(
                "PRE-TRADE FAIL: Order ${Order:F0} exceeds {Ratio:P0} of depth ${Depth:F0} for market {MarketId}. Recommended: ${Recommended:F0}",
                orderSizeUsd, preTrade.MaxOrderToDepthRatio, totalDepthUsd, marketId, recommendedSize);

            return Task.FromResult(PreTradeValidation.Invalid(
                $"Order size ${orderSizeUsd:F0} > {preTrade.MaxOrderToDepthRatio:P0} of depth ${totalDepthUsd:F0}",
                availableDepth: sideDepthUsd,
                totalDepth: totalDepthUsd,
                spreadPercent: orderBook.SpreadPercent,
                dataAge: dataAge,
                recommendedSize: recommendedSize));
        }

        // Warning: Check minimum depth (not rejection, just warning)
        if (totalDepthUsd < preTrade.MinOrderBookDepthUsd)
        {
            _logger.LogWarning(
                "PRE-TRADE WARNING: Low depth ${Depth:F0} < ${Min:F0} for market {MarketId} - proceeding with caution",
                totalDepthUsd, preTrade.MinOrderBookDepthUsd, marketId);
        }

        _logger.LogDebug(
            "PRE-TRADE PASS: Market {MarketId}, {Side} ${Size:F0}, depth=${Depth:F0}, spread={Spread:F2}%, age={Age:F1}s",
            marketId, isBuy ? "BUY" : "SELL", orderSizeUsd, totalDepthUsd, orderBook.SpreadPercent, dataAge.TotalSeconds);

        return Task.FromResult(PreTradeValidation.Valid(
            availableDepth: sideDepthUsd,
            totalDepth: totalDepthUsd,
            spreadPercent: orderBook.SpreadPercent,
            dataAge: dataAge));
    }

    /// <inheritdoc />
    public Task<decimal> GetMaxSafeOrderSizeAsync(
        int marketId,
        bool isBuy,
        CancellationToken ct = default)
    {
        var preTrade = _options.Value.PreTrade;

        var orderBook = _realtimeState.GetOrderBook(marketId);
        if (orderBook == null)
        {
            _logger.LogWarning(
                "Cannot calculate max safe order size: order book not available for market {MarketId}",
                marketId);
            return Task.FromResult(0m);
        }

        var dataAge = DateTimeOffset.UtcNow - orderBook.LastUpdate;
        if (dataAge.TotalSeconds > preTrade.MaxDataAgeSeconds)
        {
            _logger.LogWarning(
                "Cannot calculate max safe order size: order book data stale for market {MarketId}",
                marketId);
            return Task.FromResult(0m);
        }

        var relevantSide = isBuy ? orderBook.Asks : orderBook.Bids;
        var sideDepthUsd = CalculateSideDepthUsd(relevantSide);
        var bidDepthUsd = CalculateSideDepthUsd(orderBook.Bids);
        var askDepthUsd = CalculateSideDepthUsd(orderBook.Asks);
        var totalDepthUsd = bidDepthUsd + askDepthUsd;

        // Max safe is the smaller of:
        // - 10% of total depth
        // - 50% of side depth (to satisfy the 2x requirement)
        var maxFromTotal = totalDepthUsd * preTrade.MaxOrderToDepthRatio;
        var maxFromSide = sideDepthUsd / 2; // Half because we need 2x depth

        var maxSafe = Math.Min(maxFromTotal, maxFromSide);

        _logger.LogDebug(
            "Max safe order size for market {MarketId} ({Side}): ${MaxSafe:F0} (total limit=${FromTotal:F0}, side limit=${FromSide:F0})",
            marketId, isBuy ? "BUY" : "SELL", maxSafe, maxFromTotal, maxFromSide);

        return Task.FromResult(maxSafe);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreTradeValidation>> ValidateBatchAsync(
        int marketId,
        IReadOnlyList<(bool IsBuy, decimal SizeUsd)> orders,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orders);

        if (orders.Count == 0)
        {
            return [];
        }

        var results = new List<PreTradeValidation>(orders.Count);

        // Validate each order sequentially (they share the same order book state)
        foreach (var (isBuy, sizeUsd) in orders)
        {
            var result = await ValidateOrderAsync(marketId, isBuy, sizeUsd, ct)
                .ConfigureAwait(false);
            results.Add(result);
        }

        var validCount = results.Count(r => r.IsValid);
        var invalidCount = results.Count - validCount;

        if (invalidCount > 0)
        {
            _logger.LogWarning(
                "PRE-TRADE BATCH: {Valid}/{Total} orders passed validation for market {MarketId}",
                validCount, orders.Count, marketId);
        }

        return results;
    }

    /// <inheritdoc />
    public Task<PostOnlyValidation> ValidatePostOnlyPriceAsync(
        int marketId,
        bool isBuy,
        decimal orderPrice,
        CancellationToken ct = default)
    {
        var preTrade = _options.Value.PreTrade;

        // Get order book from WebSocket state
        var orderBook = _realtimeState.GetOrderBook(marketId);

        // Rule: Fail closed - if no order book data, reject
        if (orderBook == null)
        {
            _logger.LogWarning(
                "POST-ONLY FAIL: Order book not available for market {MarketId}",
                marketId);

            return Task.FromResult(PostOnlyValidation.NoData(
                "Order book not available - cannot validate PostOnly price",
                orderPrice));
        }

        var dataAge = DateTimeOffset.UtcNow - orderBook.LastUpdate;

        // Rule: Check data freshness (must be < 5 seconds)
        if (dataAge.TotalSeconds > preTrade.MaxDataAgeSeconds)
        {
            _logger.LogWarning(
                "POST-ONLY FAIL: Order book data stale ({DataAge:F1}s > {Max}s) for market {MarketId}",
                dataAge.TotalSeconds, preTrade.MaxDataAgeSeconds, marketId);

            return Task.FromResult(PostOnlyValidation.NoData(
                $"Order book data too stale ({dataAge.TotalSeconds:F1}s)",
                orderPrice));
        }

        var bestBid = orderBook.BestBidPrice;
        var bestAsk = orderBook.BestAskPrice;

        // Validate we have valid bid/ask
        if (bestBid <= 0 || bestAsk <= 0)
        {
            _logger.LogWarning(
                "POST-ONLY FAIL: Invalid best bid/ask ({BestBid}/{BestAsk}) for market {MarketId}",
                bestBid, bestAsk, marketId);

            return Task.FromResult(PostOnlyValidation.NoData(
                "Invalid order book state - no valid bid/ask",
                orderPrice));
        }

        // PostOnly crossing validation
        // BUY order: must be strictly < best ask (can't match with sellers)
        // SELL order: must be strictly > best bid (can't match with buyers)
        if (isBuy)
        {
            var distanceFromCrossing = bestAsk - orderPrice;

            if (orderPrice >= bestAsk)
            {
                // Buy price would cross - calculate safe price (1 tick below best ask)
                // Use 0.01% buffer below best ask to be safe
                var safePrice = bestAsk * 0.9999m;

                _logger.LogWarning(
                    "POST-ONLY CROSS: BUY at {OrderPrice:F2} >= bestAsk {BestAsk:F2} for market {MarketId}. " +
                    "Order would cross spread. SafePrice: {SafePrice:F2}",
                    orderPrice, bestAsk, marketId, safePrice);

                return Task.FromResult(PostOnlyValidation.WouldCross(
                    $"BUY price {orderPrice:F2} >= best ask {bestAsk:F2} - would cross spread",
                    orderPrice,
                    bestBid,
                    bestAsk,
                    distanceFromCrossing,
                    dataAge,
                    recommendedPrice: safePrice));
            }

            _logger.LogDebug(
                "POST-ONLY PASS: BUY at {OrderPrice:F2} < bestAsk {BestAsk:F2} (distance: {Distance:F2})",
                orderPrice, bestAsk, distanceFromCrossing);

            return Task.FromResult(PostOnlyValidation.Valid(
                orderPrice,
                bestBid,
                bestAsk,
                distanceFromCrossing,
                dataAge));
        }
        else
        {
            // SELL order
            var distanceFromCrossing = orderPrice - bestBid;

            if (orderPrice <= bestBid)
            {
                // Sell price would cross - calculate safe price (1 tick above best bid)
                // Use 0.01% buffer above best bid to be safe
                var safePrice = bestBid * 1.0001m;

                _logger.LogWarning(
                    "POST-ONLY CROSS: SELL at {OrderPrice:F2} <= bestBid {BestBid:F2} for market {MarketId}. " +
                    "Order would cross spread. SafePrice: {SafePrice:F2}",
                    orderPrice, bestBid, marketId, safePrice);

                return Task.FromResult(PostOnlyValidation.WouldCross(
                    $"SELL price {orderPrice:F2} <= best bid {bestBid:F2} - would cross spread",
                    orderPrice,
                    bestBid,
                    bestAsk,
                    distanceFromCrossing,
                    dataAge,
                    recommendedPrice: safePrice));
            }

            _logger.LogDebug(
                "POST-ONLY PASS: SELL at {OrderPrice:F2} > bestBid {BestBid:F2} (distance: {Distance:F2})",
                orderPrice, bestBid, distanceFromCrossing);

            return Task.FromResult(PostOnlyValidation.Valid(
                orderPrice,
                bestBid,
                bestAsk,
                distanceFromCrossing,
                dataAge));
        }
    }

    /// <summary>
    /// Calculates total depth in USD for one side of the order book.
    /// </summary>
    private static decimal CalculateSideDepthUsd(IReadOnlyList<(decimal Price, decimal Size)> levels)
    {
        var total = 0m;
        foreach (var (price, size) in levels)
        {
            total += price * size;
        }
        return total;
    }
}
