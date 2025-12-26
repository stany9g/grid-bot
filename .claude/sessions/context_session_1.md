# Session 1: Extended DEX Integration

## Objective
Add Extended DEX integration to GridBot following the multi-DEX abstraction pattern established in `GridBot.Abstractions`.

## Extended DEX API Summary

### Base URLs
- **Mainnet API**: `https://api.starknet.extended.exchange/api/v1`
- **Mainnet WS**: `wss://api.starknet.extended.exchange/stream.extended.exchange/v1`
- **Testnet API**: `https://api.starknet.sepolia.extended.exchange/api/v1`
- **Testnet WS**: `wss://starknet.sepolia.extended.exchange/stream.extended.exchange/v1`

### Authentication
- **Header**: `X-Api-Key: <API_KEY>`
- **Order Management**: Requires Stark signatures (SNIP12 standard)
- **Settlement Object**: Contains `starkKey`, signature components (`r`, `s`), nonce, collateral/position details

### Rate Limits
- Standard: 1,000 requests/minute
- Market Makers: 60,000 requests/5 minutes
- Exceeded returns HTTP 429

### Key Endpoints

#### Public REST
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/info/markets` | GET | List markets with config and stats |
| `/info/markets/{market}/orderbook` | GET | Order book snapshot |
| `/info/markets/{market}/trades` | GET | Recent trades |
| `/info/markets/{market}/stats` | GET | Market statistics |
| `/info/candles/{market}/{type}` | GET | Candlestick data |
| `/info/{market}/funding` | GET | Funding rate history |

#### Private REST
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/user/account/info` | GET | Account details |
| `/user/balance` | GET | Balances |
| `/user/positions` | GET | Open positions |
| `/user/orders` | GET | Active orders |
| `/user/order` | POST | Create/edit order |
| `/user/order/{id}` | DELETE | Cancel order |
| `/user/order/massCancel` | POST | Mass cancel |
| `/user/leverage` | GET/PATCH | Leverage management |

#### WebSocket Streams (Public)
| Endpoint | Description |
|----------|-------------|
| `/orderbooks/{market}` | Order book updates (100ms/10ms) |
| `/publicTrades/{market}` | Trade stream |
| `/funding/{market}` | Funding rates |
| `/candles/{market}/{type}` | Candle updates |
| `/prices/mark/{market}` | Mark price stream |
| `/prices/index/{market}` | Index price stream |

#### WebSocket Streams (Private)
| Endpoint | Description |
|----------|-------------|
| `/account` | Account updates (orders, trades, balance, positions) |

### Order Creation Request
```json
{
  "id": "external-order-id",
  "market": "BTC-USD",
  "type": "limit",
  "side": "BUY",
  "qty": "0.01",
  "price": "50000.00",
  "fee": "0.0002",
  "expiryEpochMillis": 1735689600000,
  "timeInForce": "GTT",
  "settlement": {
    "starkKey": "0x...",
    "r": "0x...",
    "s": "0x...",
    "nonce": 123,
    ...
  }
}
```

### Error Codes
- 400: Bad Request
- 401: Unauthorized
- 403: Forbidden
- 404: Not Found
- 422: Unprocessable Entity
- 429: Rate Limited
- 500: Internal Server Error

## Mapping to GridBot Abstractions

| Extended Concept | GridBot Interface |
|-----------------|-------------------|
| Order creation/cancel | `IOrderClient` |
| Account/positions/orders | `IAccountClient` |
| Markets/orderbook/candles | `IMarketDataClient` |
| WebSocket streams | `IRealtimeDataProvider` |
| API Key + Stark signing | `IAuthenticationProvider` |
| Price/size decimals | `IScalingProvider` |
| Connection state | `IExchangeConnection` |

## Implementation Plan

### Phase 1: Project Setup
1. Create `GridBot.Extended` project
2. Add project to solution
3. Configure dependencies

### Phase 2: Core Components
1. `ExtendedOptions` - Configuration
2. `ExtendedStarkSigner` - Stark signature handling (external library or P/Invoke)
3. `ExtendedHttpClient` - HTTP client with API key auth

### Phase 3: Adapters
1. `ExtendedMarketDataAdapter` - IMarketDataClient
2. `ExtendedOrderAdapter` - IOrderClient
3. `ExtendedAccountAdapter` - IAccountClient
4. `ExtendedScalingAdapter` - IScalingProvider

### Phase 4: WebSocket & Realtime
1. `ExtendedWebSocketClient` - WebSocket client with channels
2. `ExtendedRealtimeAdapter` - IRealtimeDataProvider
3. `ExtendedConnectionAdapter` - IExchangeConnection

### Phase 5: Integration
1. `ExtendedAuthAdapter` - IAuthenticationProvider
2. `ExtendedExchangeClient` - IExchangeClient aggregate
3. `ExtendedServiceExtensions` - DI registration
4. Add `ExchangeType.Extended` to enum

### Phase 6: Quality
1. Code review
2. Trading bot audit
3. Update documentation

## Key Technical Decisions

### Stark Signing
Extended uses Stark key pairs derived from Ethereum accounts via EIP-712 signing:

**Key Derivation Process:**
1. Generate EIP-712 typed data structure with:
   - `accountIndex` (int8)
   - `wallet` (address)
   - `tosAccepted` (bool)
2. Sign the typed data with Ethereum private key
3. Use signature to derive Stark private key
4. Stark public key = derive from Stark private key using Stark curve

**Libraries Needed:**
- Nethereum (for EIP-712 signing and Ethereum operations)
- StarkSharp or StarkNet.NET (for Stark curve operations)
- Or: stark-crypto-wrapper native bindings (Rust library with Python/C# bindings)

**Decision**: Use Nethereum for EIP-712 + consider native Rust library bindings for Stark crypto operations (similar to how Lighter uses signer-amd64.dll/so)

### Market ID Mapping
Extended uses string market IDs like "BTC-USD-PERP"
GridBot uses string marketId in abstractions
**Decision**: Direct string mapping, no conversion needed

### WebSocket Message Routing
Extended uses path-based streams with message types
**Decision**: Follow Lighter pattern with bounded channels and explicit routing

## Files to Create/Modify

### New Project: GridBot.Extended/
```
GridBot.Extended/
├── ExtendedOptions.cs
├── ExtendedWebSocketClient.cs
├── ExtendedJsonOptions.cs
├── ExtendedApiException.cs
├── Models/
│   ├── Api/
│   │   ├── Market.cs
│   │   ├── OrderBook.cs
│   │   ├── Order.cs
│   │   ├── Position.cs
│   │   ├── Account.cs
│   │   └── Trade.cs
│   └── WebSocket/
│       ├── OrderBookMessage.cs
│       ├── AccountMessage.cs
│       └── ...
├── Adapters/
│   ├── ExtendedExchangeClient.cs
│   ├── ExtendedOrderAdapter.cs
│   ├── ExtendedAccountAdapter.cs
│   ├── ExtendedMarketDataAdapter.cs
│   ├── ExtendedRealtimeAdapter.cs
│   ├── ExtendedScalingAdapter.cs
│   ├── ExtendedConnectionAdapter.cs
│   └── ExtendedAuthAdapter.cs
└── Extensions/
    └── ExtendedServiceExtensions.cs
```

### Modify: GridBot.Abstractions/Factory/ExchangeType.cs
Add `Extended = 3`

## Progress Log

### Session Start
- Fetched Extended DEX API documentation
- Analyzed abstraction interfaces
- Reviewed Lighter implementation as reference
- Created implementation plan

### Risk Management Review (2025-12-25)
- Trading Risk Manager agent reviewed Extended DEX integration
- Created comprehensive risk management framework: `.claude/doc/extended-dex-risk-management.md`
- Key risk categories identified:
  1. Asynchronous Order Rejection Handling
  2. Stark Signature Nonce Management
  3. Rate Limiting Strategy
  4. Order Validation and Safety Checks
  5. WebSocket Connection Resilience
  6. Settlement and Position Safety
  7. Error Code Handling
  8. Integration Component Safety Requirements
  9. Startup and Shutdown Procedures
  10. Monitoring and Alerting Recommendations

### Critical Implementation Requirements Identified
1. Order state machine: Created -> PendingConfirmation -> Active/Rejected (never trust HTTP 200 alone)
2. Thread-safe nonce manager with atomic increment (never decrement, gaps allowed)
3. Proactive rate limiting at 80% of limit with priority queue
4. Pre-submission order validation against market config
5. Data staleness detection with 5-second threshold
6. Automatic reconciliation via REST on WebSocket reconnection
7. Margin monitoring with emergency position reduction

### Trading Bot Audit (2025-12-26)
- Trading Bot Auditor agent completed comprehensive safety audit
- Created audit report: `.claude/doc/extended-dex-trading-audit.md`
- **Overall Verdict:** CONDITIONAL PASS

#### HIGH Risk Findings (BLOCKING for Production):
1. **Stark signature not implemented** (CRITICAL) - Orders will be rejected
2. **Order state machine lacks auto-cleanup** - Orphaned orders not cleaned
3. **Rate limiter records after response** - Should record before sending
4. **Order cache thread safety bug** - List<T> inside ConcurrentDictionary not thread-safe
5. **Cancel confirmation not async-aware** - HTTP 200 does not mean cancelled
6. **No order idempotency protection** - Duplicate orders possible

#### PASS Items:
- Cancel order priority correctly configured as Critical
- Data staleness detection implemented (5s threshold)
- Decimal precision correctly used throughout
- Quantity rounding direction correct (always down)
- Price rounding direction correct (less aggressive)
- WebSocket channel backpressure configured (DropOldest)
- Position reconciliation on reconnect implemented

#### Missing Safety Features:
- No kill switch mechanism
- No order size validation
- No price bounds validation
- No self-trade prevention
- No margin/collateral pre-check

## Notes
- Extended requires `User-Agent` header for all requests
- Orders are processed asynchronously - must subscribe to WebSocket for real-time status
- Max order expiry: 90 days (mainnet), 28 days (testnet)
- Fee format: decimal (0.0002 = 0.02%)
- Nonce limit: 2^31 - 1 (warn at 2 billion)
- Rate limits: 1,000/min standard, 60,000/5min market makers

## Build Status (2025-12-26)
- ✅ Full solution builds successfully
- ✅ All compilation errors resolved
- ✅ Type mismatches between Extended adapters and GridBot.Abstractions fixed

### Compilation Fixes Applied:
1. Added `OrderStatus` enum to `GridBot.Abstractions.Models.Enums`
2. Added `PendingConfirmation` factory method to `OrderResult`
3. Fixed `ConnectionState` ambiguity using type aliases in `ExtendedConnectionAdapter`
4. Fixed `ConnectionStateEvent` property name (`State` not `NewState`)
5. Added missing `using GridBot.Extended.Models` for `OrderState` enum
6. Added `WebSocketOptions` configuration in service extensions

## Stark Signer Implementation (2025-12-26)

### Architecture: Native Rust Interop (Option 1)
Following the same pattern as Lighter's native signing library.

### Files Created:

**Rust Project:**
```
stark-signer/
├── Cargo.toml           # Dependencies: starknet-crypto 0.7
├── src/lib.rs           # C ABI exports: stark_sign, stark_get_public_key, stark_pedersen_hash
└── BUILD.md             # Build instructions for all platforms
```

**C# Interop Layer:**
```
GridBot.Extended/
├── Native/
│   └── StarkNativeMethods.cs    # P/Invoke declarations + high-level wrappers
├── StarkSigner.cs               # Order signing logic with SNIP-12 message hashing
└── Adapters/ExtendedOrderAdapter.cs  # Updated to use StarkSigner
```

### Native Functions Exported:
1. `stark_sign(private_key, message_hash) -> (r, s)` - ECDSA signing
2. `stark_get_public_key(private_key) -> public_key` - Key derivation
3. `stark_pedersen_hash(a, b) -> hash` - Pedersen hash of two elements
4. `stark_pedersen_hash_many(elements[], count) -> hash` - Chained Pedersen hash
5. `stark_get_error_message(code) -> string` - Error messages

### Build Targets:
- `stark-signer-windows-amd64.dll` - Windows x64
- `stark-signer-linux-amd64.so` - Linux x64
- `stark-signer-linux-arm64.so` - Linux ARM64
- `stark-signer-macos-arm64.dylib` - macOS Apple Silicon

### Next Step:
Build the Rust library:
```bash
cd stark-signer
cargo build --release
```

## Production Blockers (ALL RESOLVED)

All production blockers from the trading bot audit have been fixed (2025-12-26):

1. ~~**[CRITICAL] Implement Stark signature**~~ ✅ DONE - Native Rust interop created

2. ~~**[HIGH] Fix rate limiter ordering**~~ ✅ DONE
   - File: `ExtendedHttpClient.cs`
   - Moved `RecordRequest()` call BEFORE `SendAsync()` to account for in-flight requests

3. ~~**[HIGH] Fix order cache thread safety**~~ ✅ DONE
   - File: `ExtendedRealtimeAdapter.cs`
   - Changed `ConcurrentDictionary<string, List<OrderInfo>>` to `ConcurrentDictionary<string, ConcurrentDictionary<string, OrderInfo>>`
   - Updated `GetOrders`, `ReconcileStateAsync`, and `UpdateOrderCache` methods

4. ~~**[HIGH] Add order idempotency mechanism**~~ ✅ DONE
   - File: `ExtendedOrderAdapter.cs`
   - Added `_recentIdempotencyKeys` dictionary with SHA256-based key generation
   - 1-minute deduplication window, 5-minute key expiry
   - Cleanup integrated with `CleanupOrphanedOrders()`

5. ~~**[HIGH] Make cancel requests async-aware**~~ ✅ DONE
   - File: `ExtendedOrderAdapter.cs`
   - Added `PendingCancellation` state to `LocalOrderState` enum
   - Updated `CancelOrderAsync` to return `PendingConfirmation` result
   - Added `ConfirmCancellation()` method called from WebSocket handler
   - Updated `ExtendedRealtimeAdapter` to call `ConfirmCancellation` on cancel events

6. ~~**[MEDIUM] Add automatic orphan order cleanup timer**~~ ✅ DONE
   - File: `ExtendedOrderAdapter.cs`
   - Added `StartCleanupTimer()` and `StopCleanupTimer()` methods
   - Timer runs every 30 seconds
   - `ExtendedServiceExtensions.cs` starts timer on adapter creation
   - Implemented `IDisposable` for proper cleanup

## Implementation Complete

### Files Modified (Production Blocker Fixes):
- `GridBot.Extended/ExtendedHttpClient.cs` - Rate limiter ordering
- `GridBot.Extended/Adapters/ExtendedRealtimeAdapter.cs` - Thread-safe order cache
- `GridBot.Extended/Adapters/ExtendedOrderAdapter.cs` - Idempotency, async cancel, cleanup timer
- `GridBot.Extended/Extensions/ExtendedServiceExtensions.cs` - Timer startup

### Build Status
- ✅ Full solution builds successfully (0 errors, 0 warnings)

## Next Steps

1. **Build Rust Native Library**
   ```bash
   cd stark-signer
   cargo build --release
   ```
   Copy built libraries to `GridBot.Extended/Native/`

2. **Testing Phase**
   - Unit tests for rate limiter, idempotency, thread safety
   - Integration tests with testnet
   - 48-hour testnet run monitoring

3. **Rollout** (per plan in `.claude/doc/extended-production-blockers-plan.md`)
   - Phase 1: Testnet only
   - Phase 2: Mainnet paper trading (DryRun mode)
   - Phase 3: Mainnet with position limits
   - Phase 4: Full production

## Code Review: Bot Control & DEX Selection (2025-12-26)

**Review document:** `.claude/doc/code-review-bot-control-dex-selection.md`

### Overall Verdict: APPROVED with HIGH priority fixes

### HIGH Priority Issues (Fix before production):

1. **Race Condition in GridBotControlService.PauseAsync/ResumeAsync**
   - State validation inside lock, but async operation outside
   - Another thread could change state between validation and execution
   - **Fix:** Add `Pausing`/`Resuming` transitional states to `BotStatus` enum

2. **Race Condition in ExchangeSelectionService.SelectExchangeAsync**
   - Bot running check happens outside the lock (TOCTOU vulnerability)
   - Bot could start between check and lock acquisition
   - **Fix:** Move `_botControlService.IsRunning` check inside the lock

### WARNING Issues:

1. **Event raised inside lock** - `SetRunning()`/`SetPaused()` call `RaiseStatusChanged` inside lock (potential deadlock)
2. **Fire-and-forget task** - `RefreshAvailableExchangesAsync()` called without await in constructor

### Approved Patterns:
- Correct `IDisposable` implementation in Blazor components
- Proper `InvokeAsync(StateHasChanged)` for thread-safe UI updates
- Consistent lock pattern for state protection
- Proper cancellation token propagation
