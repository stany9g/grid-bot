# MarketDataService Trading Safety Audit

**Audit Date:** December 12, 2025
**Auditor Role:** Trading Systems Auditor
**File Under Review:** `GridBot.ApiService/Services/MarketData/MarketDataService.cs`
**Audit Type:** Crypto Trading Correctness & Safety

---

## Executive Summary

This audit evaluates the recent optimizations to `MarketDataService` from a trading correctness perspective. The changes introduce WebSocket-first data retrieval with REST fallback, and 5-minute caching for candlestick data.

**Overall Assessment: CONDITIONAL PASS**

The changes are generally acceptable for the trading bot's use case (grid trading with ~2 second decision cycles), but several issues require attention before production deployment with significant capital.

---

## FINDINGS

### FINDING 1: Stale Candlestick Data During Market Regime Changes

**Risk Level:** MEDIUM
**Category:** Stale Data / Safety
**Location:** `MarketDataService.cs:100-143` (GetCandlesticksAsync with cache)
**Financial Impact:** Could delay trend detection by up to 5 minutes, causing incorrect inventory skew

**Problem:**

The 5-minute cache TTL for 1-hour candlesticks creates a potential blind spot during rapid market regime changes. The `TrendDetector` uses these candlesticks to calculate EMA(20), EMA(50), MACD, and ADX indicators. If a major market event occurs immediately after cache refresh, the bot operates on stale data for up to 5 minutes.

**Evidence:**

```csharp
// MarketDataService.cs:29
private const int CandlestickCacheTtlSeconds = 300; // 5 minutes

// TrendDetector.cs:63 - Consumes cached data
var candles = await _marketDataService.GetCandlesticksAsync(marketId, "1h", requiredCandles, ct);
```

**Analysis:**

For 1-hour candlestick data used in trend analysis:

| Scenario | Impact |
|----------|--------|
| Normal market | ACCEPTABLE - 5 min staleness on 1h candles is <8% of candle duration |
| Flash crash -20% | LOW - Flash crash detector operates on real-time price, not candlesticks |
| Trend reversal | MEDIUM - Could delay trend flip detection by 5 minutes |
| Grid spacing | LOW - ATR uses these candles but updates every cycle anyway |

The 1-hour candlesticks inherently lag by definition. The most recent candle closes at most 59 minutes ago, plus the 5-minute cache means worst case is ~64 minutes of lag on the newest data point. For EMA/MACD calculations using 50+ candles, this is acceptable.

**Verdict:** ACCEPTABLE

**Rationale:**
- Trend detection already has 15-minute confirmation delay built in
- Flash crash and risk sentinel use real-time WebSocket price, not candlesticks
- 5-minute cache on hourly data is <8% additional staleness
- The session context notes this was intentional to reduce REST calls from ~5 to ~1 per 5 minutes

---

### FINDING 2: MidPrice vs LastTradePrice Semantic Difference

**Risk Level:** HIGH
**Category:** Price Data Accuracy
**Location:** `MarketDataService.cs:248-257` (ConvertWebSocketOrderBook)
**Financial Impact:** Could place orders at incorrect prices relative to market

**Problem:**

WebSocket order book returns `MidPrice` (average of best bid/ask), while REST returns `LastTradePrice`. These are semantically different and can diverge significantly:

1. **In illiquid markets:** Spread could be 1%+. MidPrice might be $100.50, but LastTradePrice was $99.00 (on the bid side).
2. **During rapid moves:** LastTradePrice reflects actual execution, MidPrice reflects current quotes (which may gap).

**Evidence:**

```csharp
// MarketDataService.cs:248-257 - WebSocket path
var lastPrice = wsOrderBook.MidPrice;
if (lastPrice == 0m)
{
    var marketStats = _realtimeState.GetMarketStats(marketId);
    if (marketStats != null && marketStats.MarkPrice > 0)
        lastPrice = marketStats.MarkPrice;
}

// MarketDataService.cs:200-206 - REST path
var marketData = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);
var lastPrice = marketData.LastTradePrice;  // Different semantic!
```

**Scenario Analysis:**

| Consumer | Uses LastPrice For | Impact of MidPrice |
|----------|-------------------|-------------------|
| Grid order placement | Reference price | MEDIUM - grid levels calculated from this |
| P&L calculation | Position valuation | HIGH - could misvalue position |
| Trailing stop | Trigger comparison | LOW - TrailingStopService uses real-time data |
| Risk sentinel | Price monitoring | LOW - uses separate price feed |

**Recommendation:**

For grid trading, MidPrice is actually MORE appropriate than LastTradePrice because:
1. Grid orders are limit orders placed around current market
2. MidPrice represents "fair value" better than last trade (which could be stale)
3. Lighter DEX uses MarkPrice for liquidation/funding calculations

**Verdict:** ACCEPTABLE WITH MONITORING

**Rationale:**
- MidPrice is arguably better for grid order placement
- The `OrderBookSnapshot.LastPrice` field name is misleading but functionally correct
- Recommend renaming field to `ReferencePrice` or documenting the semantic difference

---

### FINDING 3: No Staleness Check on WebSocket Data

**Risk Level:** CRITICAL
**Category:** Stale Data Guard
**Location:** `MarketDataService.cs:52-58, 161-166`
**Financial Impact:** Could place orders on stale data after WebSocket reconnection

**Problem:**

The code checks if WebSocket price/order book EXISTS (`HasValue && > 0`), but does NOT check if the data is STALE. After a WebSocket reconnection, there's a window where old cached data is served before new data arrives.

**Evidence:**

```csharp
// MarketDataService.cs:52-58 - No staleness check!
var wsPrice = _realtimeState.GetCurrentPrice(marketId);
if (wsPrice.HasValue && wsPrice.Value > 0)
{
    _logger.LogDebug("Using WebSocket price for market {MarketId}: {Price}", marketId, wsPrice.Value);
    return wsPrice.Value;  // Could be minutes old after reconnect!
}

// MarketDataService.cs:161-166 - No staleness check!
var wsOrderBook = _realtimeState.GetOrderBook(marketId);
if (wsOrderBook != null && wsOrderBook.Bids.Count > 0)
{
    return ConvertWebSocketOrderBook(marketId, wsOrderBook, depth);  // Could be stale!
}
```

The `ILighterRealtimeState` interface DOES expose staleness information:

```csharp
// ILighterRealtimeState.cs:73-74
TimeSpan? OldestDataAge { get; }
```

**Scenario - Flash Crash During WebSocket Reconnection:**

1. T+0: WebSocket disconnects
2. T+1: Price cached at $100
3. T+2: Price drops 10% to $90 (bot doesn't see)
4. T+3: WebSocket reconnects
5. T+4: `GetCurrentPriceAsync` returns cached $100
6. T+5: Grid places buy orders at $99.50 (1% below "current" price)
7. T+6: Orders fill immediately at $99.50 when market is at $90
8. **Result:** 10% immediate loss per filled order

**Recommendation:**

Add staleness validation before accepting WebSocket data:

```csharp
// Recommended threshold for trading operations
private const int MaxDataStalenessMs = 5000; // 5 seconds

var wsOrderBook = _realtimeState.GetOrderBook(marketId);
var dataAge = _realtimeState.OldestDataAge;

// Only use WebSocket if connected AND data is fresh
if (wsOrderBook != null &&
    wsOrderBook.Bids.Count > 0 &&
    _realtimeState.IsConnected &&
    dataAge.HasValue &&
    dataAge.Value.TotalMilliseconds < MaxDataStalenessMs)
{
    return ConvertWebSocketOrderBook(marketId, wsOrderBook, depth);
}
```

**Verdict:** FAIL - REQUIRES FIX BEFORE PRODUCTION

---

### FINDING 4: WebSocket Order Book Depth May Differ from REST

**Risk Level:** MEDIUM
**Category:** Data Integrity
**Location:** `MarketDataService.cs:238-246`
**Financial Impact:** Liquidity calculations may be inaccurate

**Problem:**

The WebSocket order book subscription may return a different depth than the REST API. The conversion applies a `Take(depth)` limit, but if WebSocket provides less depth than requested, liquidity calculations will be understated.

**Evidence:**

```csharp
// MarketDataService.cs:238-246
var bids = wsOrderBook.Bids
    .Take(depth)  // Takes minimum of available and requested
    .Select(b => new PriceLevel { Price = b.Price, Size = b.Size })
    .ToList();
```

**Analysis:**

From the session context, the WebSocket order book subscription depth is not explicitly documented. If the WebSocket provides only top 10 levels but REST provides 20:

| Requested Depth | WS Actual | REST Actual | Impact |
|-----------------|-----------|-------------|--------|
| 20 | 10 | 20 | WS returns 50% less depth data |

This affects:
1. `TotalBidDepth` / `TotalAskDepth` calculations
2. Liquidity monitoring thresholds
3. Order book imbalance analysis

**Recommendation:**

1. Verify WebSocket subscription depth matches REST (check Lighter docs)
2. Add logging if WebSocket returns less than requested depth
3. Consider falling back to REST if depth is insufficient for liquidity analysis

**Verdict:** ACCEPTABLE - LOW IMPACT

**Rationale:**
- Grid operations primarily use best bid/ask
- Liquidity monitoring has wide thresholds ($50k warning, $25k critical)
- Depth 10 vs 20 unlikely to affect overall liquidity assessment

---

### FINDING 5: REST Fallback Latency During High Load

**Risk Level:** LOW
**Category:** Performance
**Location:** `MarketDataService.cs:60-90` (GetCurrentPriceAsync REST fallback)
**Financial Impact:** Grid operations delayed during WebSocket unavailability

**Problem:**

When WebSocket is unavailable, the REST fallback makes up to 2 API calls:
1. `GetOrderBookDetailsAsync` for last trade price
2. `GetOrderBookOrdersAsync` if last trade price is 0

In the decision cycle context (2-second intervals), this adds latency.

**Evidence:**

```csharp
// MarketDataService.cs:62-81
// Fall back to REST - get market metadata for last trade price
var marketData = await _queryClient.GetOrderBookDetailsAsync(marketId, cancellationToken: cancellationToken);

if (marketData.LastTradePrice > 0)
    return marketData.LastTradePrice;

// Fall back to mid price from best bid/ask
var orderBook = await _queryClient.GetOrderBookOrdersAsync(marketId, limit: 1, cancellationToken);
```

**Analysis:**

The decision engine (`TradingDecisionEngine.cs:687-688`) has a 2-second timeout for data collection:
```csharp
var timeout = TimeSpan.FromMilliseconds(_options.DecisionEngine.DataCollectionTimeoutMs);
```

Two sequential REST calls could exceed this, causing:
1. Timeout escalation (3+ consecutive = capacity reduction)
2. Stale cache usage
3. Degraded state transition

**Verdict:** ACCEPTABLE

**Rationale:**
- WebSocket should be primary path 99%+ of time
- REST fallback is safety net, not primary path
- Timeout handling exists in decision engine

---

### FINDING 6: No Validation of Order Book Spread Sanity

**Risk Level:** MEDIUM
**Category:** Data Integrity / Safety
**Location:** `MarketDataService.cs:259-271`
**Financial Impact:** Could use corrupted/manipulated order book data

**Problem:**

The converted order book snapshot is returned without validating that the data makes sense. A corrupted WebSocket message or exchange issue could return inverted book (bid > ask) or extreme spreads.

**Evidence:**

```csharp
// MarketDataService.cs:259-271 - No validation
return new OrderBookSnapshot
{
    MarketId = marketId,
    Timestamp = wsOrderBook.LastUpdate,
    LastPrice = lastPrice,
    BestBid = wsOrderBook.BestBidPrice,  // Could be > BestAsk
    BestAsk = wsOrderBook.BestAskPrice,  // Could be < BestBid
    Spread = wsOrderBook.Spread,          // Could be negative
    // ...
};
```

**Scenario - Corrupted Order Book:**

1. Exchange sends malformed data: BestBid = $101, BestAsk = $99
2. Spread calculated as -$2 (negative)
3. Grid places sell orders at $99 thinking it's the ask
4. Grid places buy orders at $101 thinking it's the bid
5. **Result:** Immediate loss on every fill

**Recommendation:**

Add sanity validation:

```csharp
// Validate order book sanity
if (wsOrderBook.BestBidPrice >= wsOrderBook.BestAskPrice)
{
    _logger.LogWarning(
        "Invalid order book for market {MarketId}: Bid {Bid} >= Ask {Ask}. Falling back to REST.",
        marketId, wsOrderBook.BestBidPrice, wsOrderBook.BestAskPrice);
    // Fall through to REST fallback
}

if (wsOrderBook.SpreadPercent > 5m) // 5% spread = illiquid or corrupted
{
    _logger.LogWarning(
        "Excessive spread for market {MarketId}: {SpreadPercent}%. Flagging for review.",
        marketId, wsOrderBook.SpreadPercent);
}
```

**Verdict:** SHOULD FIX

---

### FINDING 7: Potential Cache Key Collision for Different Markets

**Risk Level:** LOW
**Category:** Race Condition
**Location:** `MarketDataService.cs:24, 102`
**Financial Impact:** None - cache key includes marketId

**Problem:** None - This was a false positive during initial analysis.

**Evidence:**

```csharp
// Cache key correctly includes marketId
private readonly ConcurrentDictionary<(int MarketId, string Resolution, int Count), ...> _candlestickCache = new();

var cacheKey = (marketId, resolution, count);  // Correct - includes marketId
```

**Verdict:** PASS

---

## STALENESS SCENARIO ANALYSIS

### Scenario 1: 5-Minute Old Candle Data During Flash Crash

**Timeline:**
- T+0:00 - Cache refreshed with 1h candles showing normal market
- T+0:30 - Flash crash begins (-3%)
- T+1:00 - Flash crash detector triggers on real-time price
- T+2:00 - Protective mode activated
- T+4:59 - Cache still shows pre-crash candles
- T+5:00 - Cache expires, fresh data fetched

**Impact:** MINIMAL
- Flash crash detection uses real-time WebSocket price
- Protective mode is triggered by `FlashCrashDetector`, not trend analysis
- Candlesticks only affect trend detection (which has 15-min confirmation delay)

### Scenario 2: WebSocket Reconnection During Volatile Period

**Timeline:**
- T+0:00 - WebSocket connected, price at $100
- T+0:05 - WebSocket disconnects (network issue)
- T+0:06 - Price moves to $105 (bot doesn't see)
- T+0:10 - WebSocket reconnects
- T+0:11 - `GetCurrentPriceAsync` called
- **CURRENT BEHAVIOR:** Returns cached $100 from `_realtimeState`
- T+0:12 - Grid places orders around $100
- T+0:13 - Orders fill at $100 when market is at $105

**Impact:** HIGH
- Without staleness check, bot trades on 10+ second old data
- This is the critical gap identified in FINDING 3

### Scenario 3: Price Data During Trend Analysis

**Question:** Could stale candlestick data cause incorrect trend detection and wrong trading decisions?

**Analysis:**
- Trend detection uses 1h candles for EMA(20), EMA(50), MACD, ADX
- A 5-minute cache on 1h data means latest candle is at most 64 minutes old
- EMA calculations inherently smooth out short-term noise
- Trend confirmation delay (15 min) further buffers against false signals

**Impact:** LOW
- Trend detection is designed to be lagging indicator
- Major trend reversals take hours to confirm
- 5-minute additional cache staleness is negligible

---

## FINANCIAL RISK QUANTIFICATION

### Risk: Placing Orders at Wrong Prices (FINDING 3)

**Worst Case Scenario:**
- WebSocket disconnects for 60 seconds during -10% move
- Bot places $10,000 worth of buy orders 10% above market
- All orders fill immediately
- **Loss:** $1,000 (10% of order value)

**Probability:** LOW (requires simultaneous WebSocket failure AND rapid move)
**Severity:** HIGH (direct P&L impact)
**Risk Score:** MEDIUM-HIGH

### Risk: Missing Trend Reversals (FINDING 1)

**Worst Case Scenario:**
- Market reverses from StrongBull to StrongBear
- 5-minute cache delay + 15-minute confirmation = 20-minute reaction
- Position held 20 minutes too long in bear reversal
- At -2% per hour trend: **Loss:** ~0.67% of position

**Probability:** MEDIUM
**Severity:** LOW
**Risk Score:** LOW

### Risk: Corrupted Order Book (FINDING 6)

**Worst Case Scenario:**
- Exchange sends inverted book (bid > ask)
- Grid places orders on both sides
- Immediate arbitrage fills both sides
- **Loss:** Spread amount x quantity

**Probability:** VERY LOW (exchange bug required)
**Severity:** HIGH
**Risk Score:** LOW-MEDIUM

---

## AUDIT SUMMARY

```
===================================================================
AUDIT SUMMARY - MarketDataService Trading Safety
===================================================================
Total Findings: 7
+-- CRITICAL Risk: 1 (REQUIRES FIX)
+-- HIGH Risk: 1 (ACCEPTABLE WITH MONITORING)
+-- MEDIUM Risk: 3 (ACCEPTABLE / SHOULD FIX)
+-- LOW Risk: 2 (PASS)

Critical Finding:
- FINDING 3: No staleness check on WebSocket data

Overall Verdict: CONDITIONAL PASS

Deployment Recommendation:
- NOT RECOMMENDED for production with significant capital until
  FINDING 3 is addressed
- ACCEPTABLE for testnet/paper trading
- ACCEPTABLE for production with small position sizes (<$1000)
  and manual monitoring
===================================================================
```

---

## REQUIRED ACTIONS BEFORE PRODUCTION

### MUST FIX (BLOCKING)

1. **Add WebSocket Data Staleness Validation (FINDING 3)**
   - Check `_realtimeState.OldestDataAge` before using WebSocket data
   - Threshold: 5 seconds for grid operations
   - Fall back to REST if stale

### SHOULD FIX (NON-BLOCKING)

2. **Add Order Book Sanity Validation (FINDING 6)**
   - Validate bid < ask
   - Validate spread < 5%
   - Log and fall back on invalid data

### RECOMMENDED (NICE TO HAVE)

3. **Rename `LastPrice` to `ReferencePrice` (FINDING 2)**
   - Or document that WebSocket returns MidPrice, REST returns LastTradePrice

4. **Log When WebSocket Depth < Requested (FINDING 4)**
   - For debugging liquidity monitoring discrepancies

---

## TESTING RECOMMENDATIONS

### Critical Path Tests

1. **Staleness Injection Test:**
   - Disconnect WebSocket mid-test
   - Verify fallback to REST occurs
   - Verify stale data not used for order placement

2. **Order Book Corruption Test:**
   - Inject inverted bid/ask
   - Verify validation catches it
   - Verify fallback occurs

3. **Cache Expiration Test:**
   - Verify cache expires after 5 minutes
   - Verify fresh data fetched
   - No stale data persists

### Integration Tests

1. **WebSocket Reconnection Sequence:**
   - Connect -> Disconnect -> Reconnect
   - Verify data freshness after reconnection
   - Verify no trading on stale data

2. **Flash Crash Simulation:**
   - Rapid price movement
   - Verify real-time data used (not cached)
   - Verify protective mode triggers correctly

---

## APPENDIX: Code Locations

| Component | File | Lines |
|-----------|------|-------|
| Candlestick Cache | MarketDataService.cs | 24-29, 100-143 |
| WebSocket Price | MarketDataService.cs | 52-58 |
| WebSocket Order Book | MarketDataService.cs | 160-166 |
| Order Book Conversion | MarketDataService.cs | 232-272 |
| Staleness Property | ILighterRealtimeState.cs | 73-74 |
| Decision Engine Data Collection | TradingDecisionEngine.cs | 684-906 |
| Trend Detection | TrendDetector.cs | 55-171 |
