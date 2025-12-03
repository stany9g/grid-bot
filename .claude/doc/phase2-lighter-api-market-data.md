# Phase 2: Lighter DEX Market Data API Research

## Summary

This document provides comprehensive research on Lighter DEX API endpoints for Phase 2 (Market Data) implementation of the ALTE trading bot.

**Base URL:** `https://mainnet.zklighter.elliot.ai/api/v1`

**WebSocket URL:** `wss://mainnet.zklighter.elliot.ai/stream`

---

## 1. Price Data / Candlesticks

### 1.1 Candlesticks Endpoint (OHLCV)

**Endpoint:** `GET /api/v1/candlesticks`

**Parameters:**
| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `market_id` | int | Yes | - | Market ID (0=BTC, 1=ETH, etc.) |
| `resolution` | string | No | `1h` | Candle interval: `1m`, `5m`, `15m`, `1h`, `4h`, `1d` |
| `start_timestamp` | long | No | - | Unix timestamp (seconds) for range start |
| `end_timestamp` | long | No | - | Unix timestamp (seconds) for range end |
| `count_back` | int | No | 2 | Number of candles to fetch |
| `set_timestamp_to_end` | bool | No | false | Align to end timestamp |

**Response Schema (Expected):**
```json
{
  "code": 200,
  "data": [
    {
      "timestamp": 1732600800,
      "open": "2900.50",
      "high": "2950.25",
      "low": "2880.10",
      "close": "2920.75",
      "volume": "1234.56"
    }
  ]
}
```

**Implementation Notes:**
- Use `market_id` NOT symbol string (must map symbols to IDs)
- Resolution strings are lowercase: `1m`, `5m`, `15m`, `1h`, `4h`, `1d`
- For ATR calculation, recommend fetching 14-20 candles minimum

### 1.2 Current/Last Price

**Available via:** `GET /api/v1/orderBookDetails?market_id={id}`

**Response Fields:**
```json
{
  "last_trade_price": "2902.75",
  "daily_price_change": "-0.1918514698298092",
  "daily_high": "2984.51",
  "daily_low": "2857.58"
}
```

**Alternative:** WebSocket `market_stats/{MARKET_INDEX}` channel provides real-time updates.

### 1.3 24h Price Change

**Source:** `orderBookDetails` endpoint

**Field:** `daily_price_change` - Returns decimal percentage (e.g., `-0.19` = -19%)

**IMPORTANT:** The value appears to be a ratio, not a percentage. Verify during implementation.

---

## 2. Volume Data

### 2.1 24h Trading Volume

**Source:** `GET /api/v1/orderBookDetails?market_id={id}`

**Response Fields:**
```json
{
  "daily_base_token_volume": "471791.67",
  "daily_quote_token_volume": "1380731860.77",
  "daily_trades_count": 1017981
}
```

### 2.2 Exchange-Wide Statistics

**Endpoint:** `GET /api/v1/exchangeStats`

**Response Fields:**
```json
{
  "code": 200,
  "total": 110,
  "daily_usd_volume": "9213267970.002752",
  "daily_trades_count": 7000000,
  "order_book_stats": [
    {
      "symbol": "ETH",
      "last_trade_price": "2902.81",
      "daily_trades_count": 1017457,
      "daily_base_token_volume": "471616.66",
      "daily_quote_token_volume": "1380000000",
      "daily_price_change": "-0.19"
    }
  ]
}
```

### 2.3 Historical Volume

**NOT directly available** - Must calculate from:
1. Candlestick data (each candle has volume)
2. Trade history aggregation

---

## 3. Funding Rate

### 3.1 Current Funding Rates (All Exchanges)

**Endpoint:** `GET /api/v1/funding-rates`

**Response Schema:**
```json
{
  "code": 200,
  "data": [
    {
      "market_id": 0,
      "exchange": "lighter",
      "symbol": "BTC",
      "rate": "0.00004234"
    },
    {
      "market_id": 0,
      "exchange": "binance",
      "symbol": "BTC",
      "rate": "0.00003500"
    }
  ]
}
```

**Key Notes:**
- Returns funding rates across multiple exchanges (binance, bybit, hyperliquid, lighter)
- **Does NOT include next_funding_time**
- **Does NOT include historical funding rates**
- Rate is decimal (0.0001 = 0.01% = 1 basis point)

### 3.2 Historical Funding (Fundings Endpoint)

**Endpoint:** `GET /api/v1/fundings`

**Parameters:**
| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `market_id` | int | Yes | - | Market ID |
| `resolution` | string | No | `1h` | Funding interval |
| `start_timestamp` | long | No | - | Range start |
| `end_timestamp` | long | No | - | Range end |
| `count_back` | int | No | 2 | Number of funding periods |

**Response Schema (Expected):**
```json
{
  "code": 200,
  "data": [
    {
      "timestamp": 1732600800,
      "funding_rate": "0.0001"
    }
  ]
}
```

### 3.3 Limitations

| Data Point | Available | Notes |
|------------|-----------|-------|
| Current funding rate | Yes | Via `/funding-rates` |
| Historical funding | Yes | Via `/fundings` |
| Next funding time | **NO** | Not exposed in API |
| Funding interval | **NO** | Typically 8h, hardcode or derive |

---

## 4. Market Statistics

### 4.1 Mark Price vs Index Price

**STATUS: NOT AVAILABLE**

The Lighter API does not expose:
- Mark price
- Index price

**Workaround:** Use `last_trade_price` from `orderBookDetails` as approximation.

### 4.2 Open Interest

**Source:** `GET /api/v1/orderBookDetails?market_id={id}`

**Response Field:**
```json
{
  "open_interest": "64122.1511"
}
```

Open interest is in base asset units (e.g., 64,122 ETH).

### 4.3 24h High/Low

**Source:** `GET /api/v1/orderBookDetails?market_id={id}`

**Response Fields:**
```json
{
  "daily_high": "2984.51",
  "daily_low": "2857.58"
}
```

### 4.4 Margin Parameters

**Source:** `GET /api/v1/orderBookDetails?market_id={id}`

**Response Fields:**
```json
{
  "default_initial_margin_e4": 500,
  "maintenance_margin_e4": 120,
  "closeout_margin_e4": 80
}
```

**Note:** Values are in basis points (e4 = divide by 10000):
- 500 = 5% initial margin (20x max leverage)
- 120 = 1.2% maintenance margin
- 80 = 0.8% closeout margin

---

## 5. Trades Data

### 5.1 Recent Trades

**Endpoint:** `GET /api/v1/recentTrades`

**Parameters:**
| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `market_id` | int | Yes | - | Market ID |
| `limit` | int | No | 100 | Max trades to return |

### 5.2 Trade History

**Endpoint:** `GET /api/v1/trades`

**Parameters:**
| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `market_id` | int | No | - | Filter by market |
| `limit` | int | No | 100 | Max trades to return |
| `sort_by` | string | No | `timestamp` | Sort field |

---

## 6. WebSocket Streams (Real-Time Data)

### 6.1 Connection

```
wss://mainnet.zklighter.elliot.ai/stream
```

### 6.2 Available Channels

| Channel | Subscription Format | Data |
|---------|---------------------|------|
| **Market Stats** | `market_stats/{MARKET_INDEX}` or `market_stats/all` | Real-time price, volume, OI |
| **Order Book** | `order_book/{MARKET_INDEX}` | Bid/ask updates |
| **Trade** | `trade/{MARKET_INDEX}` | Trade executions |
| **Account All** | `account_all/{ACCOUNT_ID}` | Account data all markets |
| **Height** | `height` | Block height updates |

### 6.3 Subscription Message Format

```json
{
  "type": "subscribe",
  "channel": "market_stats/0"
}
```

### 6.4 Market Stats Channel Response (Expected)

```json
{
  "channel": "market_stats/0",
  "data": {
    "last_price": "2902.75",
    "daily_volume": "471791.67",
    "daily_change": "-0.19",
    "open_interest": "64122.15"
  }
}
```

---

## 7. Data Availability Summary

| Data Point | REST Endpoint | WebSocket | Notes |
|------------|---------------|-----------|-------|
| Candlesticks (OHLCV) | `/candlesticks` | No | Historical data |
| Last Price | `/orderBookDetails` | `market_stats` | Real-time via WS |
| 24h Price Change | `/orderBookDetails` | `market_stats` | |
| 24h Volume | `/orderBookDetails` | `market_stats` | |
| 24h High/Low | `/orderBookDetails` | `market_stats` | |
| Open Interest | `/orderBookDetails` | `market_stats` | |
| Funding Rate (Current) | `/funding-rates` | No | Multi-exchange |
| Funding Rate (Historical) | `/fundings` | No | |
| Next Funding Time | **NOT AVAILABLE** | No | Hardcode interval |
| Mark Price | **NOT AVAILABLE** | No | Use last_trade_price |
| Index Price | **NOT AVAILABLE** | No | Not exposed |
| Trades | `/trades`, `/recentTrades` | `trade` | |
| Order Book | `/orderBookDetails` | `order_book` | |

---

## 8. Implementation Recommendations

### 8.1 New Endpoints to Add to ILighterQueryClient

```csharp
public interface ILighterQueryClient
{
    // Existing methods...

    // NEW: Candlestick data for ATR calculation
    Task<List<Candlestick>> GetCandlesticksAsync(
        int marketId,
        string resolution = "1h",
        int countBack = 20,
        CancellationToken cancellationToken = default);

    // NEW: Exchange-wide statistics
    Task<ExchangeStats> GetExchangeStatsAsync(
        CancellationToken cancellationToken = default);

    // NEW: Funding rates (all exchanges)
    Task<List<FundingRate>> GetFundingRatesAsync(
        CancellationToken cancellationToken = default);

    // NEW: Historical funding for a market
    Task<List<FundingHistory>> GetFundingsAsync(
        int marketId,
        string resolution = "1h",
        int countBack = 20,
        CancellationToken cancellationToken = default);

    // NEW: Recent trades for a market
    Task<List<Trade>> GetRecentTradesAsync(
        int marketId,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
```

### 8.2 New Models Required

```csharp
// Candlestick.cs
public sealed class Candlestick
{
    public long Timestamp { get; set; }
    public string Open { get; set; }
    public string High { get; set; }
    public string Low { get; set; }
    public string Close { get; set; }
    public string Volume { get; set; }
}

// FundingRate.cs
public sealed class FundingRate
{
    public int MarketId { get; set; }
    public string Exchange { get; set; }  // "lighter", "binance", etc.
    public string Symbol { get; set; }
    public string Rate { get; set; }
}

// FundingHistory.cs
public sealed class FundingHistory
{
    public long Timestamp { get; set; }
    public string FundingRate { get; set; }
}

// Trade.cs
public sealed class Trade
{
    public long TradeId { get; set; }
    public int MarketId { get; set; }
    public string Price { get; set; }
    public string Size { get; set; }
    public string Side { get; set; }  // "buy" or "sell"
    public long Timestamp { get; set; }
}

// ExchangeStats.cs
public sealed class ExchangeStats
{
    public int TotalMarkets { get; set; }
    public string DailyUsdVolume { get; set; }
    public long DailyTradesCount { get; set; }
    public List<MarketStats> MarketStats { get; set; }
}

public sealed class MarketStats
{
    public string Symbol { get; set; }
    public string LastTradePrice { get; set; }
    public long DailyTradesCount { get; set; }
    public string DailyBaseTokenVolume { get; set; }
    public string DailyQuoteTokenVolume { get; set; }
    public string DailyPriceChange { get; set; }
}
```

### 8.3 Extend OrderBookDetail Model

The existing `OrderBookDetail` model is missing critical fields. Add:

```csharp
// Add to OrderBookDetail.cs
[JsonPropertyName("last_trade_price")]
public string? LastTradePrice { get; set; }

[JsonPropertyName("daily_price_change")]
public string? DailyPriceChange { get; set; }

[JsonPropertyName("daily_high")]
public string? DailyHigh { get; set; }

[JsonPropertyName("daily_low")]
public string? DailyLow { get; set; }

[JsonPropertyName("open_interest")]
public string? OpenInterest { get; set; }

[JsonPropertyName("daily_base_token_volume")]
public string? DailyBaseTokenVolume { get; set; }

[JsonPropertyName("daily_quote_token_volume")]
public string? DailyQuoteTokenVolume { get; set; }

[JsonPropertyName("daily_trades_count")]
public long? DailyTradesCount { get; set; }

[JsonPropertyName("default_initial_margin_e4")]
public int? DefaultInitialMarginE4 { get; set; }

[JsonPropertyName("maintenance_margin_e4")]
public int? MaintenanceMarginE4 { get; set; }

[JsonPropertyName("closeout_margin_e4")]
public int? CloseoutMarginE4 { get; set; }
```

### 8.4 WebSocket Client (Future Phase)

For Phase 2, REST endpoints are sufficient for:
- ATR calculation (candlesticks)
- Trend detection (price changes)
- Volume monitoring

Consider WebSocket for Phase 3+ when real-time order book updates are needed.

---

## 9. Rate Limits and Considerations

### 9.1 Known Limits

| Aspect | Value | Notes |
|--------|-------|-------|
| Rate limit | Not documented | Use conservative polling (1-5 seconds) |
| Batch size | 50 transactions max | For WebSocket sendtxbatch |
| Candlestick history | Unknown max | Start with count_back=100 |

### 9.2 Recommended Polling Intervals

| Data Type | Interval | Justification |
|-----------|----------|---------------|
| Price/Volume | 5-10 seconds | Balance freshness vs rate limits |
| Candlesticks | On-demand | Fetch when calculating ATR |
| Funding Rates | 1-5 minutes | Rates change slowly |
| Order Book | 1-2 seconds | For order placement decisions |

### 9.3 Error Handling

- HTTP 400: Bad request (check parameters)
- HTTP 429: Rate limited (implement exponential backoff)
- HTTP 500/502/503: Server error (retry with backoff)

---

## 10. Gaps and Workarounds

### 10.1 Mark Price / Index Price

**Problem:** Not available in API

**Workaround:**
- Use `last_trade_price` as proxy
- For more accuracy, calculate mid-price from best bid/ask

```csharp
decimal midPrice = (bestBid + bestAsk) / 2;
```

### 10.2 Next Funding Time

**Problem:** Not exposed

**Workaround:**
- Lighter uses 8-hour funding intervals (typical for perps)
- Calculate next funding time: `current_time - (current_time % 8h) + 8h`

```csharp
var currentUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
var fundingInterval = 8 * 60 * 60; // 8 hours in seconds
var nextFunding = currentUtc - (currentUtc % fundingInterval) + fundingInterval;
```

### 10.3 Historical Volume

**Problem:** No dedicated historical volume endpoint

**Workaround:**
- Use candlestick volume data
- Aggregate from trade history

---

## 11. Sources

- [Lighter API Documentation](https://apidocs.lighter.xyz)
- [Lighter Docs](https://docs.lighter.xyz)
- [Unofficial Python SDK](https://github.com/hangukquant/lighter_sdk)
- Live API testing against `mainnet.zklighter.elliot.ai`
