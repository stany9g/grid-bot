# Session 1: WebSocket-First Architecture Refactoring

## Status: COMPLETED (Updated)

## Objective
Refactor GridBot.Lighter to use WebSocket as the primary data source for queries. Move real-time state management from ApiService to Lighter library and create thin client implementations.

## Changes Made

### New Files in GridBot.Lighter
| File | Description |
|------|-------------|
| `ILighterRealtimeState.cs` | Interface for thread-safe WebSocket state access + snapshot types |
| `LighterRealtimeStateService.cs` | BackgroundService that processes WebSocket channels and maintains state |
| `WsLighterQueryClient.cs` | WebSocket + REST hybrid (WS for real-time, REST for market list & candlesticks) |
| `WsLighterCommandClient.cs` | WebSocket-integrated command client (uses HTTP for tx submission) |

### New Files in GridBot.ApiService
| File | Description |
|------|-------------|
| `WsMarketDataService.cs` | Pure WebSocket market data service (replaces HybridMarketDataService) |

### Modified Files
| File | Change |
|------|--------|
| `LighterServiceCollectionExtensions.cs` | Complete rewrite - registers WS-based clients with HTTP for REST fallback |
| `GridBot.Lighter.csproj` | Added Hosting.Abstractions and Logging.Abstractions packages |
| `TradingDecisionEngine.cs` | Removed old Realtime namespace import |
| `Program.cs` | Simplified - uses WsMarketDataService, subscribes to market after MarketResolver |

### Deleted Files
| File | Reason |
|------|--------|
| `GridBot.ApiService/Services/Realtime/ILighterRealtimeState.cs` | Moved to GridBot.Lighter |
| `GridBot.ApiService/Services/Realtime/LighterRealtimeStateService.cs` | Moved to GridBot.Lighter |
| `GridBot.ApiService/Services/Realtime/` folder | Empty after moves |
| `GridBot.ApiService/Services/MarketData/HybridMarketDataService.cs` | Replaced by WsMarketDataService |

### Commented Out (by user before session)
| File | Reason |
|------|--------|
| `LighterQueryClient.cs` | Replaced by WsLighterQueryClient |
| `LighterCommandClient.cs` | Replaced by WsLighterCommandClient |

## Architecture Summary

```
┌─────────────────────────────────────────────────────────────┐
│                     GridBot.Lighter                          │
├─────────────────────────────────────────────────────────────┤
│  LighterWebSocketClient (existing)                           │
│  └── Channels: OrderBook, Account, Orders, MarketStats       │
│                           ▼                                  │
│  LighterRealtimeStateService (BackgroundService)             │
│  ├── ConcurrentDictionary<marketId, snapshot>                │
│  ├── Implements ILighterRealtimeState                        │
│  ├── Started automatically as HostedService                  │
│  └── NO auto-subscribe - waits for SubscribeMarketAsync      │
│                           ▼                                  │
│  WsLighterQueryClient (ILighterQueryClient)                  │
│  ├── Reads from ILighterRealtimeState for real-time data     │
│  ├── Uses HTTP for GetOrderBooksAsync (market list)          │
│  └── Throws NotSupportedException for historical data        │
│                                                              │
│  WsLighterCommandClient (ILighterCommandClient)              │
│  ├── SignerClient for local signing                          │
│  ├── ILighterRealtimeState for market order pricing          │
│  └── HTTP POST for transaction submission                    │
└─────────────────────────────────────────────────────────────┘
```

## Startup Flow

```
Program.cs startup:
1. Services configured (AddLighterClient)
2. LighterRealtimeStateService starts as HostedService
   └── Connects WebSocket
   └── Subscribes to account/orders/notifications
   └── Does NOT subscribe to market data yet
3. MarketResolver.InitializeAsync()
   └── Calls REST GetOrderBooksAsync to get market list
   └── Resolves Symbol (e.g., "BTC") → MarketId (e.g., 0)
4. realtimeState.SubscribeMarketAsync(marketId)
   └── Subscribes to order book & market stats for the market
5. App starts running
```

## Operations Matrix

### Supported via WebSocket (real-time)
| Operation | Notes |
|-----------|-------|
| GetAccountAsync | From AccountSnapshot |
| GetActiveOrdersAsync | From OrderSnapshot list |
| GetOrderBookOrdersAsync | From OrderBookSnapshot |
| GetCurrentPrice | Mid price from order book |
| GetPositionSize | From AccountSnapshot.Positions |
| GetMarketStats | From MarketStatsSnapshot |

### Supported via REST (historical/discovery)
| Operation | Notes |
|-----------|-------|
| GetOrderBooksAsync | Market list discovery (used by MarketResolver) |
| GetCandlesticksAsync | Historical OHLCV (via WsMarketDataService) |

### Not Supported (throws NotSupportedException)
| Operation | Reason |
|-----------|--------|
| GetAccountMetadataAsync | Metadata not in WS |
| GetOrderBookDetailsAsync | Market metadata not in WS |
| GetTransactionAsync | Historical data |
| GetNextNonceAsync | Use local nonce tracking |
| GetFundingRatesAsync | Full list not available |
| GetRecentTradesAsync | Historical data |

## Configuration

Market is discovered from Symbol at startup (no DefaultMarketId):
```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "AccountIndex": 123,
    "ApiKeyIndex": 0,
    "PrivateKey": "...",
    "ChainId": 304,
    "InitialNonce": 0
  },
  "TradingBot": {
    "Symbol": "BTC"
  }
}
```

## Notes
- Commands still use HTTP POST (WebSocket is read-only for exchanges)
- Nonce is tracked locally by SignerClient after initialization
- **WebSocket for real-time data**: Price, OrderBook, FundingRate, Account, Orders
- **REST for discovery**: GetOrderBooksAsync (market list) - used at startup by MarketResolver
- **REST for historical data**: Candlesticks only (historical OHLCV not available via WS)
- **Symbol-based market discovery**: Configure `TradingBot:Symbol` (e.g., "BTC"), MarketResolver finds the market ID
- Build successful with 0 errors

## Code Review Completed (2025-12-10)

A code review was performed on the WebSocket refactoring. See `.claude/doc/websocket-refactoring-code-review.md` for full details.

### Key Findings Summary

| Priority | Issue | Action Required |
|----------|-------|-----------------|
| CRITICAL | JsonSerializerOptions duplicated in 4 classes | Create shared static LighterJsonOptions.Default |
| IMPORTANT | 8 interface methods throw NotSupportedException | Split interface or document WS vs REST support |
| IMPORTANT | Obsolete AddLighterWebSocket method | Remove dead code |
| IMPORTANT | Console.WriteLine in production code | Remove or use proper logging |
| MINOR | Excessive trace logging | Wrap in IsEnabled check |
| MINOR | Unused _options field in WsLighterQueryClient | Remove |

### Files Reviewed
- WsLighterCommandClient.cs - Approved with minor issues
- WsLighterQueryClient.cs - Has dead interface methods, unused field
- LighterRealtimeStateService.cs - Minor logging optimization needed
- LighterServiceCollectionExtensions.cs - Remove obsolete method and Console.WriteLine
- ILighterRealtimeState.cs - Approved, no changes needed
- WsMarketDataService.cs - Approved with minor JsonSerializerOptions duplication

---

## WebSocket Transaction Format Research (2025-12-10)

Research was conducted on the exact WebSocket message format for sending transactions on Lighter DEX.

**Full documentation**: `.claude/doc/lighter-websocket-transaction-format.md`

### Key Findings

#### Single Transaction (`jsonapi/sendtx`)
```json
{
    "type": "jsonapi/sendtx",
    "data": {
        "id": "my_random_id_{random}",
        "tx_type": INTEGER,
        "tx_info": OBJECT  // Parsed JSON, NOT string!
    }
}
```

#### Batch Transactions (`jsonapi/sendtxbatch`)
```json
{
    "type": "jsonapi/sendtxbatch",
    "data": {
        "id": "my_random_id_{random}",
        "tx_types": "[1, 1, ...]",  // Stringified array!
        "tx_infos": "[{...}, {...}]"  // Stringified array!
    }
}
```

#### Critical Details
1. **tx_hash is NOT included in requests** - it's computed client-side for verification only
2. **Single TX**: `tx_info` is a **parsed JSON object**
3. **Batch TX**: Both `tx_types` and `tx_infos` are **JSON stringified strings**
4. **Max 50 transactions** per batch
5. **All batch TX must use same API key**
6. **id field** required for request/response correlation

#### Current Implementation Status
- `WsLighterCommandClient.cs` uses HTTP POST for transactions (valid approach)
- WebSocket-based TX submission is an alternative (not currently implemented)
- Both approaches have same rate limits and volume quotas

### Sources
- [Lighter WebSocket Reference](https://apidocs.lighter.xyz/docs/websocket-reference)
- [Lighter Python SDK utils.py](https://github.com/elliottech/lighter-python/blob/main/examples/utils.py)
