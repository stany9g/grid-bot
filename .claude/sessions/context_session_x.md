# Session Context: Network Selection Feature - Made Functional

## Date: 2025-12-26

## Task Summary
Fixed the network selection feature to be functional (not just display-only) as identified in the trading audit at `.claude/doc/trading-audit-network-selection.md`.

## Problem Statement
The network selection was previously display-only:
- `NetworkSelectionService` tracked which network was "selected" but had NO mechanism to actually switch the underlying exchange client
- The bot would ALWAYS trade on whatever network was configured at startup via `AddLighterExchange()`
- WebSocket connections, SignerClient, and all trading operations were not updated when network changed

## Solution: Startup-Only Network Selection

Implemented a factory-based approach where:
- Network is selected before bot starts
- When network is selected, a new exchange client is initialized for that network
- Bot must be stopped to change network (already enforced)
- Previous network's exchange client is properly disposed before switching

## Implementation Details

### 1. Created INetworkExchangeFactory Interface
**File: `GridBot.Lighter/Factory/INetworkExchangeFactory.cs`**
```csharp
public interface INetworkExchangeFactory
{
    Task<IExchangeClient> CreateForNetworkAsync(LighterNetworkType network, CancellationToken ct = default);
    bool IsInitialized(LighterNetworkType network);
    IExchangeClient? GetCurrent();
    LighterNetworkType? GetCurrentNetwork();
    Task DisposeCurrentAsync();
}
```

### 2. Created NetworkExchangeFactory Implementation
**File: `GridBot.Lighter/Factory/NetworkExchangeFactory.cs`**
- Creates all services for a specific network (SignerClient, WebSocket, adapters, etc.)
- Caches the created client (only one network active at a time)
- `DisposeCurrentAsync()` properly cleans up WebSocket, SignerClient, and HTTP client
- Uses `NetworkExchangeContext` inner class to track all resources for proper disposal

Key responsibilities:
- Creates SignerClient with network-specific private key, chain ID, and account index
- Creates WebSocket client connected to network-specific API URL
- Creates all adapters (Connection, Realtime, Order, Account, MarketData, Auth, Scaling)
- Initializes market mapper with market data from the network
- Connects to WebSocket before returning

### 3. Modified NetworkSelectionService
**File: `GridBot.ApiService/Services/Network/NetworkSelectionService.cs`**
- Added injection of `INetworkExchangeFactory` and `IExchangeRegistry`
- `SelectNetworkAsync` now:
  1. Validates bot is stopped
  2. Validates network is configured
  3. If switching to different network, unregisters and disposes current client
  4. Creates new exchange client via factory
  5. Registers new client in exchange registry
  6. Fires `NetworkChanged` event

### 4. Modified ExchangeSelectionService
**File: `GridBot.ApiService/Services/Exchange/ExchangeSelectionService.cs`**
- Added subscription to `NetworkChanged` event
- When network changes, re-initializes from registry to pick up new client
- Fires `ExchangeChanged` event to notify UI components
- Implements `IDisposable` to unsubscribe from event

### 5. Updated LighterAbstractionsExtensions
**File: `GridBot.Lighter/Extensions/LighterAbstractionsExtensions.cs`**
- Modified `AddLighterNetworks` to register:
  - `LighterNetworksOptions` from configuration
  - `WebSocketOptions` from configuration
  - HTTP client factory
  - `IExchangeRegistry` (from Abstractions)
  - `INetworkExchangeFactory` (singleton)
  - Forwarding services for `IAccountClient`, `IMarketDataClient`, `IOrderClient`, etc.

The forwarding services resolve the primary exchange from the registry at call time, enabling dynamic network switching without re-resolving services.

### 6. Updated Program.cs
**File: `GridBot.ApiService/Program.cs`**
- Removed `AddLighterExchange` call (no longer needed)
- Only `AddLighterNetworks` is used now

### 7. Created NetworkInitializationService
**File: `GridBot.ApiService/Services/Network/NetworkInitializationService.cs`**
- Hosted service that runs on startup
- Calls `SelectNetworkAsync` for the default network
- Ensures exchange client is available before other hosted services start
- Gracefully handles initialization failures (logs error but doesn't crash app)

### 8. Updated TradingBotExtensions
**File: `GridBot.ApiService/Extensions/TradingBotExtensions.cs`**
- Added registration of `NetworkInitializationService` as hosted service

## Key Changes from Audit Recommendations

| Finding | Status | Implementation |
|---------|--------|----------------|
| Network selection has no effect on trading | FIXED | Factory creates real exchange clients per network |
| TOCTOU race condition | MITIGATED | Bot start checks `IsSwitchingNetwork` flag |
| No WebSocket cleanup on switch | FIXED | `DisposeCurrentAsync()` properly disconnects |
| Nonce sequences not isolated | FIXED | Each network gets its own SignerClient with independent nonce |
| Chain ID mismatch advisory only | UNCHANGED | (Low priority, can be enhanced later) |
| No mainnet confirmation dialog | UNCHANGED | (UI change, lower priority) |
| Startup defaults to testnet | KEPT | Safe default, unchanged |

## Files Modified

1. `GridBot.Lighter/Factory/INetworkExchangeFactory.cs` (NEW)
2. `GridBot.Lighter/Factory/NetworkExchangeFactory.cs` (NEW)
3. `GridBot.ApiService/Services/Network/NetworkSelectionService.cs` (MODIFIED)
4. `GridBot.ApiService/Services/Network/NetworkInitializationService.cs` (NEW)
5. `GridBot.ApiService/Services/Exchange/ExchangeSelectionService.cs` (MODIFIED)
6. `GridBot.Lighter/Extensions/LighterAbstractionsExtensions.cs` (MODIFIED)
7. `GridBot.ApiService/Extensions/TradingBotExtensions.cs` (MODIFIED)
8. `GridBot.ApiService/Program.cs` (MODIFIED)

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Testing Recommendations
1. Test network switching with bot stopped
2. Verify WebSocket connects to correct network URL
3. Verify account data shows correct account index for each network
4. Verify orders go to correct network
5. Verify switching back to same network doesn't recreate client unnecessarily
6. Test startup with only mainnet configured (fallback behavior)
7. Test startup with no networks configured (error handling)

## Next Steps
- Consider adding mainnet confirmation dialog (UI enhancement)
- Consider adding strict chain ID validation (block mismatched config)
- Add integration tests for network switching

---

# Session Continuation: Extended DEX Network Switching

## Date: 2026-01-01

## Task Summary
Implemented full Extended DEX network switching (Testnet/Mainnet) with the same factory pattern as Lighter.

## Implementation Details

### 1. Created ExtendedNetworkType Enum
**File: `GridBot.Extended/ExtendedNetworkType.cs`**
```csharp
public enum ExtendedNetworkType
{
    Testnet = 0,
    Mainnet = 1
}
```

### 2. Created ExtendedNetworksOptions Configuration
**File: `GridBot.Extended/ExtendedNetworksOptions.cs`**
- Contains `DefaultNetwork`, `Testnet`, and `Mainnet` properties
- Each network has full `ExtendedOptions` configuration
- Methods: `GetNetwork()`, `GetDefaultNetworkType()`, `IsNetworkConfigured()`
- Configuration section name: `"ExtendedNetworks"`

### 3. Created IExtendedNetworkExchangeFactory Interface
**File: `GridBot.Extended/Factory/IExtendedNetworkExchangeFactory.cs`**
```csharp
public interface IExtendedNetworkExchangeFactory
{
    Task<IExchangeClient> CreateForNetworkAsync(ExtendedNetworkType network, CancellationToken ct = default);
    bool IsInitialized(ExtendedNetworkType network);
    IExchangeClient? GetCurrent();
    ExtendedNetworkType? GetCurrentNetwork();
    Task DisposeCurrentAsync();
}
```

### 4. Created ExtendedNetworkExchangeFactory Implementation
**File: `GridBot.Extended/Factory/ExtendedNetworkExchangeFactory.cs`**
- Creates all Extended services for a specific network
- Uses `NetworkExchangeContext` inner class for resource management
- Properly disposes WebSocket, HTTP client, and all adapters
- Creates: RateLimiter, NonceManager, ExtendedMarketMapper, StarkSigner, all adapters, ExchangeClient

### 5. Created IExtendedNetworkSelectionService Interface
**File: `GridBot.ApiService/Services/Network/IExtendedNetworkSelectionService.cs`**
- Properties: `CurrentNetwork`, `IsSwitchingNetwork`, `AvailableNetworks`
- Event: `NetworkChanged`
- Methods: `SelectNetworkAsync()`, `RefreshNetworkStatusAsync()`

### 6. Created ExtendedNetworkSelectionService Implementation
**File: `GridBot.ApiService/Services/Network/ExtendedNetworkSelectionService.cs`**
- Mirrors `NetworkSelectionService` pattern for Extended DEX
- Validates bot is stopped before switching
- Validates network is configured
- Disposes previous client, creates new one, registers in exchange registry

### 7. Updated ExtendedServiceExtensions
**File: `GridBot.Extended/Extensions/ExtendedServiceExtensions.cs`**
- Added `AddExtendedNetworks()` extension method
- Registers `ExtendedNetworksOptions`, HTTP client factory, `IExchangeRegistry`, `IExtendedNetworkExchangeFactory`

### 8. Updated NetworkSelector.razor
**File: `GridBot.ApiService/Components/Dashboard/NetworkSelector.razor`**
- Added Extended network selector UI (shows when Extended DEX is selected)
- Added Extended-specific event handlers and helper methods
- Gets `IExtendedNetworkSelectionService` via `IServiceProvider` (optional)
- Updated `Dispose()` to unsubscribe from Extended events

### 9. Updated Program.cs
**File: `GridBot.ApiService/Program.cs`**
- Changed from `AddExtendedExchange` to `AddExtendedNetworks` (conditional)
- Registers `IExtendedNetworkSelectionService` when Extended is configured
- Initializes Extended network on startup

### 10. Updated appsettings.json
**File: `GridBot.ApiService/appsettings.json`**
- Changed `"Extended"` section to `"ExtendedNetworks"` section
- Added `DefaultNetwork`, `Testnet`, and `Mainnet` subsections
- Each network has full configuration including WebSocket settings

## Configuration Structure
```json
"ExtendedNetworks": {
  "DefaultNetwork": "Testnet",
  "Testnet": {
    "ApiUrl": "https://starknet.sepolia.extended.exchange/api/v1/",
    "ApiKey": "...",
    "StarkPrivateKey": "...",
    ...
    "WebSocket": { ... }
  },
  "Mainnet": {
    "ApiUrl": "https://starknet.extended.exchange/api/v1/",
    ...
  }
}
```

## Files Created
1. `GridBot.Extended/ExtendedNetworkType.cs`
2. `GridBot.Extended/ExtendedNetworksOptions.cs`
3. `GridBot.Extended/Factory/IExtendedNetworkExchangeFactory.cs`
4. `GridBot.Extended/Factory/ExtendedNetworkExchangeFactory.cs`
5. `GridBot.ApiService/Services/Network/IExtendedNetworkSelectionService.cs`
6. `GridBot.ApiService/Services/Network/ExtendedNetworkSelectionService.cs`

## Files Modified
1. `GridBot.Extended/Extensions/ExtendedServiceExtensions.cs`
2. `GridBot.ApiService/Components/Dashboard/NetworkSelector.razor`
3. `GridBot.ApiService/Program.cs`
4. `GridBot.ApiService/appsettings.json`

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Key Features
- Runtime network switching for Extended DEX (like Lighter)
- Exchange-aware Network Selection UI
- Proper resource cleanup when switching networks
- Validation that bot must be stopped before switching
- Mainnet warning indicators in UI

---

# Bug Fix: Extended DEX GetMarketsAsync JSON Deserialization

## Date: 2026-01-01

## Problem Statement
`GetMarketsAsync` in `ExtendedHttpClient` was failing with:
```
System.Text.Json.JsonException: The JSON value could not be converted to
System.Collections.Generic.IReadOnlyList`1[GridBot.Extended.Models.Api.MarketInfo]
```

**Root Cause**: The Extended DEX API returns a wrapped response:
```json
{"status": "OK", "data": [...market objects...]}
```

But the code was trying to deserialize directly to `IReadOnlyList<MarketInfo>`, expecting an array at the root.

Additionally, the `MarketInfo` model had incorrect property names:
- Old: `Market`, `BaseAsset`, `QuoteAsset`, `TickSize`, `StepSize`, `MaxLeverage`, `IsActive`
- Actual API: `name`, `assetName`, `collateralAssetName`, nested `tradingConfig.minPriceChange`, `tradingConfig.minOrderSizeChange`, `tradingConfig.maxLeverage`, `active`

## Solution

### 1. Created ApiResponse<T> Wrapper
**File: `GridBot.Extended/Models/Api/ApiResponse.cs`**
```csharp
public sealed record ApiResponse<T>
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; init; }
}
```

### 2. Updated MarketInfo Model
**File: `GridBot.Extended/Models/Api/MarketInfo.cs`**
- Changed `Market` to `Name`
- Changed `BaseAsset` to `AssetName`
- Changed `QuoteAsset` to `CollateralAssetName`
- Changed `IsActive` to `Active`
- Added `TradingConfig` nested object with `MinOrderSize`, `MinPriceChange`, `MinOrderSizeChange`, `MaxLeverage`
- Added `MarketStatsInfo`, `TradingConfigInfo`, `RiskFactorConfig`, `L2ConfigInfo` nested models

### 3. Updated GetMarketsAsync
**File: `GridBot.Extended/ExtendedHttpClient.cs`**
```csharp
var response = await SendAsync<ApiResponse<IReadOnlyList<MarketInfo>>>(
    HttpMethod.Get,
    "info/markets",
    RequestPriority.Low,
    ct);
return response?.Data ?? [];
```

### 4. Fixed Dependent Adapters
Updated all files that used the old `MarketInfo` properties:

**`ExtendedMarketMapper.cs`**:
- `market.Market` -> `market.Name`
- `info.TickSize` -> `info.TradingConfig?.MinPriceChange`
- `info.StepSize` -> `info.TradingConfig?.MinOrderSizeChange`
- `info.MinOrderSize` -> `info.TradingConfig?.MinOrderSize`
- `info?.IsActive` -> `info?.Active`
- `info?.MaxLeverage` -> parse `info?.TradingConfig?.MaxLeverage`
- `info?.PriceDecimals` -> `info?.CollateralAssetPrecision`
- `info?.SizeDecimals` -> `info?.AssetPrecision`

**`ExtendedMarketDataAdapter.cs`**:
- Same property mappings when converting API model to Abstraction model

**`ExtendedScalingAdapter.cs`**:
- Same property mappings for precision calculations

## Files Modified
1. `GridBot.Extended/Models/Api/ApiResponse.cs` (NEW)
2. `GridBot.Extended/Models/Api/MarketInfo.cs` (REWRITTEN)
3. `GridBot.Extended/ExtendedHttpClient.cs` (MODIFIED)
4. `GridBot.Extended/Adapters/ExtendedMarketMapper.cs` (MODIFIED)
5. `GridBot.Extended/Adapters/ExtendedMarketDataAdapter.cs` (MODIFIED)
6. `GridBot.Extended/Adapters/ExtendedScalingAdapter.cs` (MODIFIED)

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

---

# Bug Fix: Extended DEX 401 Unauthorized - Duplicate Headers

## Date: 2026-01-02

## Problem Statement
API calls to `/user/account/info` were failing with 401 Unauthorized.

**Root Cause**: The `ExtendedNetworkExchangeFactory` was adding `X-Api-Key` and `User-Agent` headers to the HttpClient, then passing it to `ExtendedHttpClient` constructor which tried to add the same headers again. This caused duplicate headers which could result in authentication failures.

## Solution
Removed duplicate header setting from `ExtendedNetworkExchangeFactory.CreateNetworkContextAsync()`. The `ExtendedHttpClient` constructor is responsible for setting all headers.

**File: `GridBot.Extended/Factory/ExtendedNetworkExchangeFactory.cs`**
```csharp
// Before (lines 173-177):
var httpClient = _httpClientFactory.CreateClient();
httpClient.BaseAddress = new Uri(options.ApiUrl);
httpClient.Timeout = TimeSpan.FromSeconds(30);
httpClient.DefaultRequestHeaders.Add(ExtendedConstants.ApiKeyHeader, options.ApiKey);  // DUPLICATE
httpClient.DefaultRequestHeaders.Add("User-Agent", options.UserAgent);  // DUPLICATE

// After:
var httpClient = _httpClientFactory.CreateClient();
httpClient.Timeout = TimeSpan.FromSeconds(30);
// Headers are added by ExtendedHttpClient constructor, not here
```

## Authentication Requirements (Extended DEX)
Per the docs:
- **X-Api-Key**: Required header with API key from UI management page
- **User-Agent**: Mandatory header for all REST and WebSocket requests

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

---

# Bug Fix: Extended DEX AccountInfoResponse Model Mismatch

## Date: 2026-01-02

## Problem Statement
`GetAccountInfoAsync` in `ExtendedHttpClient` was failing with:
```
System.Text.Json.JsonException: JSON deserialization for type
'GridBot.Extended.Models.Api.AccountInfoResponse' was missing required
properties including: 'address'.
```

**Root Cause**: The `AccountInfoResponse` model was designed for a different API structure. The actual Extended DEX API returns:
```json
{
  "status": "OK",
  "data": {
    "accountId": 201301,
    "description": "Test",
    "accountIndex": 2,
    "status": "ACTIVE",
    "l2Key": "0x...",
    "l2Vault": "301301",
    "bridgeStarknetAddress": "0x...",
    "apiKeys": ["..."],
    "accountIndexForKeyGeneration": 2
  }
}
```

But the old model expected: `address`, `starkKey`, `nonce`, `isMarketMaker`, `tier`, `makerFee`, `takerFee`.

## Solution

### 1. Updated AccountInfoResponse Model
**File: `GridBot.Extended/Models/Api/AccountInfoResponse.cs`**
- Rewrote the model to match actual API response:
  - `AccountId` (long)
  - `Description` (string?)
  - `AccountIndex` (int)
  - `Status` (string?)
  - `L2Key` (string?)
  - `L2Vault` (string?)
  - `BridgeStarknetAddress` (string?)
  - `ApiKeys` (IReadOnlyList<string>?)
  - `AccountIndexForKeyGeneration` (int)

### 2. Updated GetAccountInfoAsync
**File: `GridBot.Extended/ExtendedHttpClient.cs`**
- Changed to use `ApiResponse<AccountInfoResponse>` wrapper
- Extracts `Data` property from wrapped response

### 3. Fixed ExtendedConnectionAdapter
**File: `GridBot.Extended/Adapters/ExtendedConnectionAdapter.cs`**
- Removed nonce sync from account info (API doesn't provide it)
- Added initialization check for NonceManager
- Nonce is managed locally using `InitialNonce` from configuration

### 4. Fixed ExtendedAccountAdapter
**File: `GridBot.Extended/Adapters/ExtendedAccountAdapter.cs`**
- Removed nonce sync from `GetAccountAsync`
- Changed `AccountId` mapping from `accountInfo.Address` to `accountInfo.AccountId.ToString()`

### 5. Fixed ExtendedAuthAdapter
**File: `GridBot.Extended/Adapters/ExtendedAuthAdapter.cs`**
- Simplified `SyncNonceAsync` since Extended API doesn't return nonce
- Nonce is initialized from config (defaults to 0 if not set)

## Key Insight: Extended DEX Nonce Management
The Extended DEX API does NOT return nonce in account info. Nonce must be:
1. Configured via `InitialNonce` in `ExtendedOptions`
2. Managed locally by `NonceManager`
3. Incremented for each signed transaction

This is different from some other exchanges that return current nonce in account info.

## Files Modified
1. `GridBot.Extended/Models/Api/AccountInfoResponse.cs` (REWRITTEN)
2. `GridBot.Extended/ExtendedHttpClient.cs` (MODIFIED)
3. `GridBot.Extended/Adapters/ExtendedConnectionAdapter.cs` (MODIFIED)
4. `GridBot.Extended/Adapters/ExtendedAccountAdapter.cs` (MODIFIED)
5. `GridBot.Extended/Adapters/ExtendedAuthAdapter.cs` (MODIFIED)

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

---

# Bug Fix: Extended DEX WebSocket 404 Error - Incorrect URL Construction

## Date: 2026-01-02

## Problem Statement
WebSocket connection to Extended DEX was failing with:
```
System.Net.WebSockets.WebSocketException
Message=The server returned status code '404' when status code '101' was expected.
```

The failing URL was: `wss://api.starknet.extended.exchange/stream.extended.exchange/v1`

**Root Cause**: Extended DEX uses **endpoint-based WebSocket subscriptions**, NOT message-based subscriptions.

- **Wrong approach** (what the code was doing): Connect to a base URL, then send JSON subscribe messages
- **Correct approach** (what Extended expects): Connect directly to specific endpoints like:
  - `/stream.extended.exchange/v1/account` for private data
  - `/stream.extended.exchange/v1/orderbooks/{market}` for order books

The `/stream.extended.exchange/v1` path alone is NOT a valid WebSocket endpoint, hence the 404 error.

## Solution

### 1. Updated GetWebSocketUrl() Method
**File: `GridBot.Extended/ExtendedWebSocketClient.cs`**

Split into two methods:
- `GetBaseWebSocketUrl()`: Extracts just the host (e.g., `wss://api.starknet.extended.exchange`)
- `GetWebSocketUrl(endpoint?)`: Builds full URL with endpoint path (defaults to `/stream.extended.exchange/v1/account`)

```csharp
private string GetWebSocketUrl(string? endpoint = null)
{
    var baseUrl = GetBaseWebSocketUrl();
    var path = endpoint ?? "/stream.extended.exchange/v1/account";
    return baseUrl.TrimEnd('/') + path;
}

private string GetBaseWebSocketUrl()
{
    if (!string.IsNullOrEmpty(_wsOptions.WebSocketUrl))
    {
        var url = _wsOptions.WebSocketUrl;
        var streamIndex = url.IndexOf("/stream", StringComparison.OrdinalIgnoreCase);
        if (streamIndex > 0)
            return url[..streamIndex];
        return url;
    }
    return _options.IsTestnet
        ? "wss://starknet.sepolia.extended.exchange"
        : "wss://api.starknet.extended.exchange";
}
```

### 2. Updated SubscribeAccountAsync()
Changed to a no-op since Extended doesn't use subscribe messages - connecting to `/account` IS the subscription:
```csharp
public Task SubscribeAccountAsync(CancellationToken ct = default)
{
    // Extended DEX uses endpoint-based subscriptions.
    // Connecting to /stream.extended.exchange/v1/account IS the subscription.
    _logger.LogDebug("Account subscription active (connection-based, no message needed)");
    _subscriptions["/account"] = true;
    return Task.CompletedTask;
}
```

### 3. Updated SubscribeOrderBookAsync()
Added warning that order book streaming requires a separate WebSocket connection:
```csharp
public Task SubscribeOrderBookAsync(string market, CancellationToken ct = default)
{
    // Order book streaming requires connecting to a SEPARATE endpoint:
    // /stream.extended.exchange/v1/orderbooks/{market}
    _logger.LogWarning(
        "Order book subscription for {Market} not supported with current connection. " +
        "Extended requires separate WebSocket connection to /orderbooks/{Market} endpoint",
        market, market);
    return Task.CompletedTask;
}
```

### 4. Updated UnsubscribeAsync() and ResubscribeAllAsync()
Changed to no-ops since Extended doesn't use message-based subscriptions.

### 5. Updated appsettings.json
Fixed Testnet WebSocket URL to use just the base host:
```json
"WebSocket": {
    "WebSocketUrl": "wss://starknet.sepolia.extended.exchange",
    ...
}
```

## Key Insight: Extended DEX WebSocket Architecture
Extended DEX uses a **different WebSocket model** than most exchanges:

| Feature | Typical Exchange | Extended DEX |
|---------|-----------------|--------------|
| Connection | Single connection | Separate connection per stream type |
| Subscription | JSON message: `{"type":"subscribe","channel":"..."}` | Connect directly to endpoint URL |
| Unsubscription | JSON message: `{"type":"unsubscribe","channel":"..."}` | Disconnect from endpoint |
| Order books | Same connection, different channel | Separate `/orderbooks/{market}` endpoint |
| Account data | Same connection, different channel | Separate `/account` endpoint |

## Files Modified
1. `GridBot.Extended/ExtendedWebSocketClient.cs` (MODIFIED)
   - `GetWebSocketUrl()` - Now builds endpoint-specific URLs
   - `GetBaseWebSocketUrl()` - New method to extract base host
   - `SubscribeAccountAsync()` - Changed to no-op
   - `SubscribeOrderBookAsync()` - Changed to log warning
   - `UnsubscribeAsync()` - Changed to no-op
   - `ResubscribeAllAsync()` - Changed to no-op
2. `GridBot.ApiService/appsettings.json` (MODIFIED)
   - Fixed Testnet WebSocket URL

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Known Limitations
- Order book streaming is NOT currently supported (requires separate WebSocket connection)
- If order book streaming is needed, the architecture would need to support multiple WebSocket connections

## Sources
- [Extended API Documentation](https://api.docs.extended.exchange/)

---

# Bug Fix: Extended DEX List Endpoints Return Wrapped Response

## Date: 2026-01-02

## Problem Statement
`GetOrdersAsync` was failing with:
```
System.Text.Json.JsonException: The JSON value could not be converted to
System.Collections.Generic.IReadOnlyList`1[GridBot.Extended.Models.Api.OrderResponse]
```

**Root Cause**: ALL Extended DEX API responses are wrapped:
```json
{"status":"OK","data":[...]}  // for list endpoints
{"status":"OK","data":{...}}  // for object endpoints
```

The list methods (`GetBalancesAsync`, `GetPositionsAsync`, `GetOrdersAsync`) were trying to deserialize directly to `IReadOnlyList<T>` instead of using the `ApiResponse<T>` wrapper.

## Solution

Updated three methods in `ExtendedHttpClient.cs`:

### Before
```csharp
var response = await SendAsync<IReadOnlyList<OrderResponse>>(...);
return response ?? [];
```

### After
```csharp
var response = await SendAsync<ApiResponse<IReadOnlyList<OrderResponse>>>(...);
return response?.Data ?? [];
```

### Methods Fixed
1. `GetBalancesAsync()` - `user/balance`
2. `GetPositionsAsync()` - `user/positions`
3. `GetOrdersAsync()` - `user/orders`

## Key Pattern: Extended API Response Wrapper
**ALL Extended API responses** use this wrapper:
```csharp
public sealed record ApiResponse<T>
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; init; }
}
```

When implementing new endpoints, always deserialize to `ApiResponse<T>` and extract `Data`.

## Files Modified
1. `GridBot.Extended/ExtendedHttpClient.cs` (MODIFIED)
   - `GetBalancesAsync()`
   - `GetPositionsAsync()`
   - `GetOrdersAsync()`

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Note: Other Endpoints May Need Same Fix
The following methods may also need `ApiResponse<T>` wrapping if they fail:
- `CreateOrderAsync()` → `ApiResponse<CreateOrderResponse>`
- `MassCancelOrdersAsync()` → `ApiResponse<MassCancelResponse>`
- `GetLeverageAsync()` → `ApiResponse<LeverageResponse>`

---

# New Agent: Extended API Specialist

## Date: 2026-01-02

## Task Summary
Created a new agent `extended-api-specialist` to serve as the authoritative reference for Extended DEX (X10) implementations in C#, using the Python SDK as the source of truth.

## Agent Details

### Location
**File:** `C:\Users\stany\.claude\agents\extended-api-specialist.md`

### Purpose
This agent serves as a bridge between the Python SDK reference implementation and C# implementations in the GridBot project. It:
1. Uses the Python SDK at `.claude/repos/python_sdk` as the primary reference
2. Translates Python patterns to idiomatic C#
3. Provides exact API specifications based on SDK analysis
4. Documents signing flows, order creation, and WebSocket patterns

### Key Capabilities
- **Order Signing**: Understands StarkEx curve signing, `get_order_msg_hash()` parameters
- **API Structure**: Knows the `{"status": "OK", "data": ...}` wrapper pattern
- **WebSocket Model**: Endpoint-based subscriptions, not message-based
- **Data Models**: Translates Pydantic to C# records with proper JSON attributes
- **Amount Calculations**: StarkAmount handling for price/size

### Reference Files in Python SDK
| Topic | Python File(s) |
|-------|---------------|
| Order Creation | `perpetual/order_object.py`, `perpetual/order_object_settlement.py` |
| Signing | `perpetual/accounts.py` |
| Amount Calculations | `perpetual/amounts.py` |
| Trading Client | `perpetual/trading_client/trading_client.py` |
| WebSocket Streams | `perpetual/stream_client/stream_client.py` |
| Configuration | `perpetual/configuration.py` |
| HTTP Client | `utils/http.py` |

### Python SDK Location
`C:\Users\stany\.claude\repos\python_sdk` (package: `x10-python-trading-starknet` v0.0.17)

## Files Modified

1. **Created:** `C:\Users\stany\.claude\agents\extended-api-specialist.md`
   - Full agent definition with expertise areas
   - Python to C# translation guidelines
   - Reference file mapping

2. **Updated:** `C:\Users\stany\.claude\CLAUDE.md`
   - Added `extended-api-specialist` to Trading Agents section

3. **Updated:** `C:\Users\stany\source\repos\plan\GridBot\CLAUDE.md`
   - Added step 3 to workflow: leverage `extended-api-specialist` for Extended DEX
   - Renumbered subsequent steps (4-8)

## Usage

When implementing Extended DEX features in C#:
1. Invoke the `extended-api-specialist` agent with the Task tool
2. Agent will read relevant Python SDK files
3. Agent will provide C# equivalent patterns
4. Agent will create documentation at `.claude/doc/xxxxx.md`

Example invocation:
```
Task(subagent_type="extended-api-specialist", prompt="How do I sign an order for Extended DEX in C#?")
```

## Python SDK Structure Summary

```
python_sdk/
├── x10/
│   ├── perpetual/
│   │   ├── accounts.py              # StarkPerpetualAccount (signing)
│   │   ├── amounts.py               # StarkAmount calculations
│   │   ├── order_object.py          # create_order_object() factory
│   │   ├── order_object_settlement.py # Order hashing & signing
│   │   ├── trading_client/          # Modular client composition
│   │   ├── stream_client/           # WebSocket streaming
│   │   └── user_client/             # Onboarding & L2 key derivation
│   └── utils/
│       ├── http.py                  # HTTP client wrapper
│       └── model.py                 # X10BaseModel (Pydantic base)
```

## Key Differences from Lighter DEX

| Aspect | Extended DEX | Lighter DEX |
|--------|-------------|-------------|
| Response Format | Wrapped `{"status": "OK", "data": ...}` | Direct JSON |
| WebSocket | Endpoint-based subscriptions | Message-based subscriptions |
| Signing | StarkEx curve via `fast_stark_crypto` | Native signer DLL |
| Nonce | Tracked locally, not from API | Returned in account info |
| Key Derivation | EIP-712 → Stark key | Direct private key |

---

# Investigation: Extended DEX Balance Endpoint 404 Error

## Date: 2026-01-02

## Task Summary
Investigated 404 errors when calling `user/balance` endpoint in the C# Extended DEX integration.

## Key Findings

### 1. Wrong Testnet API URL (ROOT CAUSE)
**C# Configuration** (WRONG):
```
https://starknet.sepolia.extended.exchange/api/v1/
```

**Python SDK** (CORRECT):
```
https://api.starknet.sepolia.extended.exchange/api/v1
```

The testnet URL is missing the `api.` subdomain prefix! This is the primary cause of the 404 error.

### 2. Wrong Response Model
The C# `BalanceResponse` model has completely wrong fields compared to the Python SDK's `BalanceModel`.

**Python SDK Fields (Correct)**:
- `collateralName` (string)
- `balance` (Decimal)
- `equity` (Decimal)
- `availableForTrade` (Decimal)
- `availableForWithdrawal` (Decimal)
- `unrealisedPnl` (Decimal)
- `initialMargin` (Decimal)
- `marginRatio` (Decimal)
- `updatedTime` (int)

**C# Fields (Wrong)**:
- `asset`, `total`, `available`, `locked`, `inPositions`

### 3. Wrong Return Type
- **Python SDK**: Returns a **single** `BalanceModel` object
- **C# Implementation**: Expects `IReadOnlyList<BalanceResponse>` (a list)

The API returns `{"status": "OK", "data": {...}}` with a single object, not a list.

## Documentation Created
Full investigation documented at:
`C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-balance-endpoint-investigation.md`

## Required Fixes
1. **Fix Testnet URL**: Change from `starknet.sepolia.extended.exchange` to `api.starknet.sepolia.extended.exchange`
2. **Fix BalanceResponse Model**: Update field names to match Python SDK
3. **Fix Return Type**: Change from list to single object
4. **Remove Trailing Slash**: For consistency with Python SDK

## Files Affected
- `GridBot.ApiService/appsettings.json` - Wrong testnet URL
- `GridBot.Extended/Models/Api/BalanceResponse.cs` - Wrong model fields
- `GridBot.Extended/ExtendedHttpClient.cs` - Wrong return type (list vs single)

## Priority
**HIGH** - These issues will cause API calls to fail

---

# Bug Fix: Extended DEX Balance Endpoint 404 Error - All Issues Resolved

## Date: 2026-01-02

## Fixes Applied

### 1. Fixed Testnet API URL
**File: `GridBot.ApiService/appsettings.json`**

```
Before: https://starknet.sepolia.extended.exchange/api/v1/
After:  https://api.starknet.sepolia.extended.exchange/api/v1
```

Also fixed testnet WebSocket URL:
```
Before: wss://starknet.sepolia.extended.exchange
After:  wss://api.starknet.sepolia.extended.exchange
```

### 2. Fixed BalanceResponse Model
**File: `GridBot.Extended/Models/Api/BalanceResponse.cs`**

Completely rewrote to match Python SDK's `BalanceModel`:
```csharp
public sealed record BalanceResponse
{
    [JsonPropertyName("collateralName")]
    public required string CollateralName { get; init; }

    [JsonPropertyName("balance")]
    public required decimal Balance { get; init; }

    [JsonPropertyName("equity")]
    public required decimal Equity { get; init; }

    [JsonPropertyName("availableForTrade")]
    public required decimal AvailableForTrade { get; init; }

    [JsonPropertyName("availableForWithdrawal")]
    public required decimal AvailableForWithdrawal { get; init; }

    [JsonPropertyName("unrealisedPnl")]
    public required decimal UnrealisedPnl { get; init; }

    [JsonPropertyName("initialMargin")]
    public required decimal InitialMargin { get; init; }

    [JsonPropertyName("marginRatio")]
    public required decimal MarginRatio { get; init; }

    [JsonPropertyName("updatedTime")]
    public required long UpdatedTime { get; init; }
}
```

### 3. Fixed Return Type (List → Single Object)
**File: `GridBot.Extended/IExtendedHttpClient.cs`**
```csharp
// Before:
Task<IReadOnlyList<BalanceResponse>> GetBalancesAsync(CancellationToken ct = default);

// After:
Task<BalanceResponse?> GetBalanceAsync(CancellationToken ct = default);
```

**File: `GridBot.Extended/ExtendedHttpClient.cs`**
```csharp
// Before:
public async Task<IReadOnlyList<BalanceResponse>> GetBalancesAsync(CancellationToken ct = default)
{
    var response = await SendAsync<ApiResponse<IReadOnlyList<BalanceResponse>>>(...);
    return response?.Data ?? [];
}

// After:
public async Task<BalanceResponse?> GetBalanceAsync(CancellationToken ct = default)
{
    var response = await SendAsync<ApiResponse<BalanceResponse>>(...);
    return response?.Data;
}
```

### 4. Updated ExtendedAccountAdapter
**File: `GridBot.Extended/Adapters/ExtendedAccountAdapter.cs`**

Updated `GetAccountAsync` to use the new single-object response:
```csharp
// Before: Iterated over list, parsed strings
var balances = await _httpClient.GetBalancesAsync(ct);
foreach (var balance in balances)
{
    decimal.TryParse(balance.Total, ...);
}

// After: Uses single object with proper decimal fields
var balance = await _httpClient.GetBalanceAsync(ct);
var collateral = balance?.Balance ?? 0m;
var availableBalance = balance?.AvailableForTrade ?? 0m;
var equity = balance?.Equity ?? 0m;
```

## Files Modified
1. `GridBot.ApiService/appsettings.json` - Fixed testnet API and WebSocket URLs
2. `GridBot.Extended/Models/Api/BalanceResponse.cs` - Rewrote model fields
3. `GridBot.Extended/IExtendedHttpClient.cs` - Changed method signature
4. `GridBot.Extended/ExtendedHttpClient.cs` - Changed return type
5. `GridBot.Extended/Adapters/ExtendedAccountAdapter.cs` - Updated caller

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Key Insight
The Python SDK at `.claude/repos/python_sdk` serves as the authoritative reference for Extended DEX API. Always verify API structures against it when implementing C# equivalents.

---

# Investigation: Extended DEX Mainnet Balance Endpoint 404 Error

## Date: 2026-01-02

## Problem Statement
GET request to mainnet balance endpoint returning 404:
```
GET https://api.starknet.extended.exchange/api/v1/user/balance
Headers:
  X-Api-Key: 07d6d52da9805d3e744f3b6750ca2d9c
  User-Agent: GridBot/1.0
```

## Root Cause Analysis

After analyzing the Python SDK at `C:\Users\stany\.claude\repos\python_sdk`, I found **one critical issue** with the C# implementation:

### Issue: Missing Required `Accept` Header

**Python SDK sets these headers for ALL requests** (from `x10/utils/http.py` lines 238-250):
```python
headers = {
    "Accept": "application/json",       # MISSING IN C#
    "Content-Type": "application/json", # MISSING IN C# (for POST/PATCH)
    "User-Agent": USER_AGENT,
    "X-Api-Key": api_key
}
```

**C# only sets** (from `ExtendedHttpClient.cs` lines 41-42):
```csharp
_httpClient.DefaultRequestHeaders.Add(ExtendedConstants.ApiKeyHeader, _options.ApiKey);
_httpClient.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
// MISSING: Accept: application/json
```

### URL Construction Verification

Python SDK URL construction (from `base_module.py` and `http.py`):
- Base URL: `https://api.starknet.extended.exchange/api/v1` (NO trailing slash)
- Path: `/user/balance` (WITH leading slash)
- Result: `https://api.starknet.extended.exchange/api/v1/user/balance`

C# URL construction:
- BaseAddress: `https://api.starknet.extended.exchange/api/v1/` (WITH trailing slash in config)
- Path: `user/balance` (NO leading slash)
- HttpClient behavior: Appends correctly when BaseAddress has trailing slash

**Note**: The C# mainnet config has a trailing slash while Python SDK does not. While HttpClient should handle this correctly, removing the trailing slash would match the Python SDK exactly.

## Required Fixes

### Fix 1: Add Accept Header (HIGH PRIORITY)
In `ExtendedHttpClient.cs` constructor:
```csharp
_httpClient.DefaultRequestHeaders.Accept.Add(
    new MediaTypeWithQualityHeaderValue("application/json"));
```

### Fix 2: Remove Trailing Slash (LOW PRIORITY - for consistency)
In `appsettings.json`:
```json
"ApiUrl": "https://api.starknet.extended.exchange/api/v1"  // Remove trailing slash
```

### Fix 3: Verify API Key Access (EXTERNAL)
The mainnet API key may not have correct permissions. Test manually:
```bash
curl -v "https://api.starknet.extended.exchange/api/v1/user/balance" \
  -H "X-Api-Key: 07d6d52da9805d3e744f3b6750ca2d9c" \
  -H "User-Agent: GridBot/1.0" \
  -H "Accept: application/json"
```

## Documentation
Full investigation documented at:
`C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-balance-endpoint-404-mainnet.md`

## Files Affected
- `GridBot.Extended\ExtendedHttpClient.cs` - Add Accept header
- `GridBot.ApiService\appsettings.json` - Optionally remove trailing slash from ApiUrl

---

# Bug Fix: Extended DEX Missing Accept Header

## Date: 2026-01-02

## Fixes Applied

### 1. Added Accept Header
**File: `GridBot.Extended/ExtendedHttpClient.cs`**

Added the missing `Accept: application/json` header that the Python SDK sends with all requests:
```csharp
// Configure base address and default headers (matching Python SDK)
_httpClient.BaseAddress = new Uri(_options.ApiUrl);
_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
_httpClient.DefaultRequestHeaders.Add(ExtendedConstants.ApiKeyHeader, _options.ApiKey);
_httpClient.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
```

Also added required using:
```csharp
using System.Net.Http.Headers;
```

### 2. Removed Trailing Slash from Mainnet URL
**File: `GridBot.ApiService/appsettings.json`**
```
Before: https://api.starknet.extended.exchange/api/v1/
After:  https://api.starknet.extended.exchange/api/v1
```

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Note
If 404 persists, verify the API key has correct permissions by testing manually with curl.

---

# Investigation: Extended DEX Balance Endpoint 404 - Final Verification

## Date: 2026-01-02

## Task Summary
Verified that the Python SDK confirms `/user/balance` is the correct endpoint path.

## Python SDK Analysis

### Exact Code from `account_module.py` (Lines 39-45)
```python
async def get_balance(self) -> WrappedApiResponse[BalanceModel]:
    """
    https://api.docs.extended.exchange/#get-balance
    """

    url = self._get_url("/user/balance")
    return await send_get_request(await self.get_session(), url, BalanceModel, api_key=self._get_api_key())
```

### URL Construction from `base_module.py` (Lines 30-31)
```python
def _get_url(self, path: str, *, query: Optional[Dict] = None, **path_params) -> str:
    return get_url(f"{self.__endpoint_config.api_base_url}{path}", query=query, **path_params)
```

### Configuration from `configuration.py` (Lines 27-53)
```python
TESTNET_CONFIG = EndpointConfig(
    api_base_url="https://api.starknet.sepolia.extended.exchange/api/v1",
    ...
)

MAINNET_CONFIG = EndpointConfig(
    api_base_url="https://api.starknet.extended.exchange/api/v1",
    ...
)
```

## Final URL Construction

**Python SDK:**
- Base: `https://api.starknet.extended.exchange/api/v1` (NO trailing slash)
- Path: `/user/balance` (WITH leading slash)
- Result: `https://api.starknet.extended.exchange/api/v1/user/balance`

**C# Implementation:**
- Uses `user/balance` (no leading slash) with BaseAddress that has trailing slash
- HttpClient correctly appends to: `https://api.starknet.extended.exchange/api/v1/user/balance`

## Conclusion

**The endpoint path `/user/balance` IS CORRECT.**

The 404 error is likely caused by:
1. **API Key not associated with an account** - Need to complete onboarding first
2. **API Key permissions** - May be read-only or restricted
3. **Account not created on mainnet** - Testnet and mainnet accounts are separate

## Recommended Testing

```bash
# Test mainnet
curl -v "https://api.starknet.extended.exchange/api/v1/user/balance" \
  -H "X-Api-Key: YOUR_API_KEY" \
  -H "User-Agent: GridBot/1.0" \
  -H "Accept: application/json"

# Test testnet
curl -v "https://api.starknet.sepolia.extended.exchange/api/v1/user/balance" \
  -H "X-Api-Key: YOUR_API_KEY" \
  -H "User-Agent: GridBot/1.0" \
  -H "Accept: application/json"
```

If both return 404, the API key may need to be regenerated or linked to an account through the Extended Exchange web UI.

## Python SDK Reference Files

| File | Path |
|------|------|
| Account Module | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\trading_client\account_module.py` |
| Base Module | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\trading_client\base_module.py` |
| Configuration | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\configuration.py` |
| HTTP Utils | `C:\Users\stany\.claude\repos\python_sdk\x10\utils\http.py` |
| Balance Model | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\balances.py` |

---

# Analysis: Extended DEX Order Creation Requirements

## Date: 2026-01-02

## Task Summary
Analyzed Python SDK to understand order creation requirements for Extended DEX debug endpoint.

## Key Findings

### 1. Market Symbol for BTC
**Symbol: `BTC-USD`** (NOT `BTC-USDC`)

The market name format is `{BASE_ASSET}-USD`. Collateral is always USD (internally USDC with 6 decimals).

### 2. Minimum Order Size
Retrieved from market data: `trading_config.min_order_size`

Call `GET /info/markets`, find `BTC-USD`, read `trading_config.min_order_size`.
Typical value: 0.001 BTC (subject to change)

### 3. Required Fields for Limit Order

From `NewOrderModel` in `x10/perpetual/orders.py`:
- `id`: External order ID (string, can be order_hash)
- `market`: "BTC-USD"
- `type`: "LIMIT" (always LIMIT, even for IOC market-style orders)
- `side`: "BUY" or "SELL"
- `qty`: Amount in base asset (Decimal)
- `price`: Price in USD (Decimal)
- `reduceOnly`: bool (default false)
- `postOnly`: bool (default false)
- `timeInForce`: "GTT" or "IOC" (FOK not supported for new orders!)
- `expiryEpochMillis`: Expiration timestamp in milliseconds
- `fee`: Fee rate (e.g., 0.0006)
- `nonce`: Unique nonce (Decimal)
- `selfTradeProtectionLevel`: "ACCOUNT", "CLIENT", or "DISABLED"
- `settlement`: Required signature data

### 4. Settlement Model (Signature)
```json
{
  "signature": { "r": "0x...", "s": "0x..." },
  "starkKey": "0x...",
  "collateralPosition": 123456
}
```

### 5. Order Hash Calculation

Parameters for `get_order_msg_hash()`:
1. `position_id` - Vault ID (from account info `l2_vault`)
2. `base_asset_id` - BTC's `l2_config.synthetic_id` (hex to int)
3. `base_amount` - Stark amount (positive for BUY, negative for SELL)
4. `quote_asset_id` - USD's `l2_config.collateral_id` (hex to int)
5. `quote_amount` - Stark amount (NEGATIVE for BUY, positive for SELL)
6. `fee_amount` - Max fee in Stark amount (always positive)
7. `fee_asset_id` - Same as quote_asset_id
8. `expiration` - Order expiry + 14 days (in seconds!)
9. `salt` - Nonce
10. `user_public_key` - Stark public key
11. `domain_name` - "Perpetuals"
12. `domain_version` - "v0"
13. `domain_chain_id` - "SN_SEPOLIA" (testnet) or "SN_MAIN" (mainnet)
14. `domain_revision` - "1"

### 6. Market Order Simulation
Extended DEX does not have a separate MARKET order type. To simulate:
- Use `type: "LIMIT"`
- Use `timeInForce: "IOC"` (Immediate-Or-Cancel)
- Set price aggressively (best ask + slippage for BUY, best bid - slippage for SELL)

### 7. Critical Notes for $10 Balance Test
- Order value = qty * price
- With 0.001 BTC at $95,000 = $95 order value
- Need leverage to trade with $10 balance (10x = $9.50 margin required)
- Check `trading_config.max_leverage` for market limits

## Documentation Created
Full analysis at: `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-order-creation-analysis.md`

## Python SDK Files Analyzed
| Topic | Path |
|-------|------|
| Order Object Factory | `x10/perpetual/order_object.py` |
| Settlement & Signing | `x10/perpetual/order_object_settlement.py` |
| Order Models | `x10/perpetual/orders.py` |
| Market Models | `x10/perpetual/markets.py` |
| Amount Conversions | `x10/perpetual/amounts.py` |
| Asset Conversions | `x10/perpetual/assets.py` |
| Account/Signing | `x10/perpetual/accounts.py` |
| Configuration | `x10/perpetual/configuration.py` |
| API Endpoint | `x10/perpetual/trading_client/order_management_module.py` |

---

# Feature: Extended DEX Debug API Endpoints

## Date: 2026-01-02

## Task Summary
Created debug API endpoints for testing Extended DEX order placement directly. These endpoints bypass the abstraction layers to allow direct debugging of the order creation and signing flow.

## Endpoints Created

### 1. GET /api/debug/extended/status
Returns comprehensive status including:
- Account info (accountId, l2Key, l2Vault, status)
- Balance (collateral, equity, availableForTrade)
- BTC market info (trading config, min order size)
- Open orders
- Available markets list

### 2. POST /api/debug/extended/limit-order
Places a hardcoded limit BUY order:
- Market: `BTC-USD`
- Quantity: `0.001 BTC` (minimum order size)
- Price: `$80,000` (below market, won't fill immediately)
- Side: `BUY`
- Time-in-Force: `GTT` (Good-Till-Time)
- Expiry: 7 days

### 3. POST /api/debug/extended/market-order
Places a hardcoded market BUY order using IOC:
- Market: `BTC-USD`
- Quantity: `0.001 BTC`
- Price: Best ask + 1% slippage
- Side: `BUY`
- Time-in-Force: `IOC` (Immediate-Or-Cancel)
- Expiry: 1 minute

**Note**: Extended DEX doesn't have true market orders. We simulate by using LIMIT + IOC.

### 4. DELETE /api/debug/extended/cancel-all
Mass cancels all orders on BTC-USD market.

---

# Bug Fix: Extended DEX BalanceResponse String-to-Decimal Deserialization

## Date: 2026-01-02

## Problem Statement
`GetBalanceAsync` was failing with:
```
System.Text.Json.JsonException: The JSON value could not be converted to System.Decimal.
Path: $.data.balance | LineNumber: 0 | BytePositionInLine: 60.
```

**Root Cause**: The Extended DEX API returns numeric values as **strings**:
```json
{"status":"OK","data":{"collateralName":"USD","balance":"20","equity":"20",...}}
```

But the C# model had `decimal` types expecting numeric JSON values:
```csharp
public required decimal Balance { get; init; }  // Expects: 20
                                                 // Got:     "20"
```

## Solution

Added `[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]` attribute to the `BalanceResponse` class to allow System.Text.Json to parse string values as decimals.

Also added missing fields from the API response:
- `status` (string)
- `spotEquity` (decimal)
- `spotEquityForAvailableForTrade` (decimal)
- `exposure` (decimal)
- `leverage` (decimal)

**File: `GridBot.Extended/Models/Api/BalanceResponse.cs`**
```csharp
[JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
public sealed record BalanceResponse
{
    // ... all decimal fields now accept string JSON values
}
```

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Key Pattern: Extended API Numeric Values
**ALL Extended API numeric values are returned as strings.** Other models may need the same `[JsonNumberHandling]` attribute if they fail to deserialize.

---

## Implementation Details

### Order Signing Flow
1. Get nonce from NonceManager
2. Build ExtendedOrderMessage with all parameters
3. Sign using StarkSigner.SignOrder()
4. Build CreateOrderRequest with settlement object containing signature
5. POST to /user/order endpoint

### Key Dependencies
- `IExtendedHttpClient` - HTTP communication
- `NonceManager` - Nonce tracking
- `StarkSigner` - Order signing
- `IOptions<ExtendedOptions>` - Configuration

### File Modified
**`GridBot.ApiService/Program.cs`**
- Added debug endpoint group at `/api/debug/extended`
- 4 new endpoints for status, limit order, market order, and cancel all

## Testing Instructions

1. Start the application
2. Select Extended network (testnet or mainnet)
3. Access Scalar API docs at `/scalar/v1`
4. Call `/api/debug/extended/status` to verify connectivity
5. Call `/api/debug/extended/limit-order` to test limit order
6. Call `/api/debug/extended/market-order` to test market order
7. Call `/api/debug/extended/cancel-all` to clean up

## Important Notes

### Balance Requirements
- 0.001 BTC at ~$95,000 = $95 notional value
- With $10 balance, you need leverage enabled
- Check market's `max_leverage` setting
- May get "insufficient balance" error without leverage

### Hardcoded Values
All values are intentionally hardcoded for debugging:
- Market: BTC-USD
- Quantity: 0.001 BTC
- Limit price: $80,000 (won't fill)
- Fee: 0.02% (ExtendedConstants.DefaultFeeRate)

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

---

# Analysis: CreateOrderRequest Model Comparison with Python SDK

## Date: 2026-01-02

## Task Summary
Analyzed the C# `CreateOrderRequest` model against the Python SDK to identify why orders are failing on Extended DEX mainnet.

## CRITICAL ISSUES FOUND

### 1. Wrong Signature Structure (CRITICAL)
**C# sends r/s at settlement root level:**
```json
{ "settlement": { "starkKey": "...", "r": "...", "s": "...", "nonce": 123 } }
```

**Python SDK nests signature in a sub-object:**
```json
{ "settlement": { "signature": { "r": "...", "s": "..." }, "starkKey": "...", "collateralPosition": "..." } }
```

### 2. `nonce` in Wrong Location (CRITICAL)
- **C#**: `nonce` is inside `settlement` object
- **Python SDK**: `nonce` is at the **order root level**, NOT inside settlement

### 3. Missing `collateralPosition` (CRITICAL)
- **C#**: Has `collateral` and `positionType` (wrong fields)
- **Python SDK**: Requires `collateralPosition` = vault ID (from account info `l2Vault`)

### 4. Missing Required Fields (HIGH)
- `selfTradeProtectionLevel` - Required, defaults to "ACCOUNT"
- `postOnly` - Required, defaults to false

### 5. Wrong Type Casing (HIGH)
- **C#**: Uses `"limit"` (lowercase)
- **Python SDK**: Uses `"LIMIT"` (uppercase)

## Correct Request Structure

```json
{
  "id": "123456789",
  "market": "BTC-USD",
  "type": "LIMIT",
  "side": "BUY",
  "qty": "0.001",
  "price": "80000",
  "reduceOnly": false,
  "postOnly": false,
  "timeInForce": "GTT",
  "expiryEpochMillis": 1704067200000,
  "fee": "0.0006",
  "nonce": "1234567890123456789",
  "selfTradeProtectionLevel": "ACCOUNT",
  "settlement": {
    "signature": {
      "r": "0x...",
      "s": "0x..."
    },
    "starkKey": "0x...",
    "collateralPosition": "301301"
  }
}
```

## Files Affected
- `GridBot.Extended/Models/Api/CreateOrderRequest.cs` - Needs complete rewrite

## Documentation Created
Full analysis at: `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-order-creation-request-analysis.md`

## Python SDK Reference
| File | Purpose |
|------|---------|
| `x10/perpetual/orders.py` (line 134-157) | `NewOrderModel` definition |
| `x10/perpetual/orders.py` (line 106-109) | `StarkSettlementModel` |
| `x10/utils/model.py` (line 64-66) | `SettlementSignatureModel` |
| `x10/perpetual/order_object.py` | Order creation factory |

## Priority: CRITICAL
Orders will fail until these issues are fixed.

---

# Analysis: Extended DEX Quantity Precision Error

## Date: 2026-01-02

## Problem Statement
Extended DEX returning error: `{"status":"ERROR","error":{"code":1123,"message":"Invalid quantity precision"}}`

Sent quantity: `"0.000125"` for BTC-USD.

## Root Cause

The quantity `0.000125` violates the market's **step size** (`min_order_size_change`). Extended DEX requires quantities to be exact multiples of the step size.

For BTC-USD, the typical `min_order_size_change` is `0.001` (3 decimal places).

`0.000125 / 0.001 = 0.125` - NOT an integer, so it's rejected.

## Python SDK Rounding Logic

From `x10/perpetual/markets.py` (lines 64-68):

```python
def round_order_size(self, order_size: Decimal, rounding_direction: str = ROUND_CEILING) -> Decimal:
    order_size = (order_size / self.min_order_size_change).to_integral_exact(
        rounding_direction
    ) * self.min_order_size_change
    return order_size
```

This ensures quantities are exact multiples of `min_order_size_change`.

## C# Equivalent

```csharp
public static decimal RoundToStepSize(decimal quantity, decimal stepSize)
{
    // Round to nearest multiple of stepSize
    var steps = quantity / stepSize;
    var roundedSteps = Math.Round(steps, 0, MidpointRounding.ToPositiveInfinity);
    return roundedSteps * stepSize;
}
```

## Valid vs Invalid Quantities for BTC-USD

| Quantity | Is Multiple of 0.001? | Valid? |
|----------|----------------------|--------|
| 0.001 | Yes (1 * 0.001) | Valid |
| 0.002 | Yes (2 * 0.001) | Valid |
| 0.0015 | No (1.5 * 0.001) | INVALID |
| 0.000125 | No (0.125 * 0.001) | INVALID |

## Fix Required

Before sending order:
1. Get market info to retrieve `min_order_size_change`
2. Round quantity to valid step size
3. Ensure quantity >= `min_order_size`

## Documentation Created
Full analysis at: `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-quantity-precision-analysis.md`

## Python SDK Files Analyzed
- `x10/perpetual/markets.py` - TradingConfigModel, round_order_size(), quantity_precision
- `x10/perpetual/amounts.py` - Rounding contexts (ROUND_UP, ROUND_DOWN)
- `x10/perpetual/order_object.py` - Order creation factory
- `x10/perpetual/order_object_settlement.py` - Stark amount conversion

---

# Analysis: Extended DEX "Trading fees are invalid" Error (Code 1128)

## Date: 2026-01-02

## Task Summary
Investigated error `{"status":"ERROR","error":{"code":1128,"message":"Trading fees are invalid"}}` when placing orders on Extended DEX.

## Root Cause

The C# implementation uses the **wrong default fee rate**:
- **C# Default**: `0.0002m` (0.02%) - This is the MAKER fee
- **Correct Value**: `0.0005m` (0.05%) - This is the TAKER fee

When placing orders, the Python SDK always uses the **taker_fee_rate**, not the maker_fee_rate.

## Python SDK Fee Handling

### 1. Fee Model (`x10/perpetual/fees.py`)
```python
class TradingFeeModel(X10BaseModel):
    market: str
    maker_fee_rate: Decimal   # 0.0002 (0.02%)
    taker_fee_rate: Decimal   # 0.0005 (0.05%)
    builder_fee_rate: Decimal # 0

DEFAULT_FEES = TradingFeeModel(
    market="BTC-USD",
    maker_fee_rate=(Decimal("2") / Decimal("10000")),   # 0.0002
    taker_fee_rate=(Decimal("5") / Decimal("10000")),   # 0.0005
    builder_fee_rate=Decimal("0"),
)
```

### 2. Fee Usage in Order Creation (`x10/perpetual/order_object.py`)
```python
fees = account.trading_fee.get(market.name, DEFAULT_FEES)
fee_rate = fees.taker_fee_rate  # ALWAYS uses taker fee!

order = NewOrderModel(
    ...
    fee=fee_rate,  # This is taker_fee_rate
    ...
)
```

### 3. Fees Should Be Fetched from API
The `/user/fees` endpoint returns account-specific fees:
```
GET /user/fees?market=BTC-USD
```

Response:
```json
{
  "status": "OK",
  "data": [{
    "market": "BTC-USD",
    "makerFeeRate": "0.0002",
    "takerFeeRate": "0.0005",
    "builderFeeRate": "0"
  }]
}
```

## Key Findings

1. **Maker vs Taker**: Always use **taker fee** when creating orders
2. **Default Taker Fee**: `0.0005` (0.05%)
3. **Per-Account Fees**: Fees can vary by account tier/volume
4. **API Validation**: Server validates fee matches expected value

## Documentation Created
Full analysis at: `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-trading-fees-analysis.md`

## Fix Required
Change `ExtendedConstants.DefaultFeeRate` from `0.0002m` to `0.0005m`:
```csharp
// GridBot.Extended/ExtendedConstants.cs
public const decimal DefaultFeeRate = 0.0005m;  // Taker fee rate
```

## Python SDK Files Analyzed
| File | Purpose |
|------|---------|
| `x10/perpetual/fees.py` | TradingFeeModel, DEFAULT_FEES |
| `x10/perpetual/order_object.py` | Order creation with fee lookup |
| `x10/perpetual/accounts.py` | Account stores trading_fee dict |
| `x10/perpetual/trading_client/account_module.py` | get_fees() API endpoint |

---

# CRITICAL Analysis: Extended DEX Order Hash Computation - "Invalid StarkEx Signature"

## Date: 2026-01-02

## Task Summary
Investigated "Invalid StarkEx signature" error by analyzing the Python SDK's order hash computation algorithm versus the C# implementation.

## Executive Summary

**The C# implementation uses a COMPLETELY WRONG hash algorithm.**

- **C#**: Uses custom SNIP-12 style Pedersen hash chain
- **Python SDK**: Uses `fast_stark_crypto.get_order_msg_hash()` - a specialized StarkEx perpetual order hash

## Root Cause: Wrong Hash Algorithm

### C# Implementation (StarkSigner.cs) - WRONG
```csharp
// Uses Pedersen hash chain with wrong parameters
var domainHash = ComputeDomainHash();  // "Extended DEX", chainId, version
var hash1 = PedersenHash(domainHash, starkPublicKey);
var marketHash = ComputeStringHash(order.Market);  // "BTC-USD" string hash
var hash2 = PedersenHash(hash1, marketHash);
// ... more Pedersen hash chains
```

### Python SDK (order_object_settlement.py) - CORRECT
```python
get_order_msg_hash(
    position_id=position_id,                    # vault ID
    base_asset_id=int(synthetic_id, 16),       # BTC asset ID from l2Config
    base_amount=stark_synthetic_amount.value,   # SIGNED Stark amount
    quote_asset_id=int(collateral_id, 16),     # USD asset ID from l2Config
    quote_amount=stark_collateral_amount.value, # SIGNED Stark amount
    fee_amount=stark_fee_amount.value,          # positive max fee
    fee_asset_id=int(collateral_id, 16),       # same as quote
    expiration=expire_time_seconds,             # order expiry + 14 days (SECONDS)
    salt=nonce,                                 # nonce
    user_public_key=public_key,
    domain_name="Perpetuals",                   # NOT "Extended DEX"
    domain_version="v0",                        # NOT "1"
    domain_chain_id="SN_SEPOLIA" or "SN_MAIN",
    domain_revision="1",
)
```

## Key Parameters from Python SDK

### 1. `get_order_msg_hash()` Parameters (14 total)

| # | Parameter | Type | Source | Notes |
|---|-----------|------|--------|-------|
| 1 | `position_id` | int | Account `l2_vault` | Vault ID |
| 2 | `base_asset_id` | int | Market `l2_config.synthetic_id` | Hex to int |
| 3 | `base_amount` | int | Calculated | **SIGNED** - positive for BUY |
| 4 | `quote_asset_id` | int | Market `l2_config.collateral_id` | Hex to int |
| 5 | `quote_amount` | int | Calculated | **SIGNED** - negative for BUY |
| 6 | `fee_amount` | int | Calculated | **ALWAYS positive** |
| 7 | `fee_asset_id` | int | Same as quote_asset_id | |
| 8 | `expiration` | int | Order expiry + 14 days | **IN SECONDS** |
| 9 | `salt` | int | Nonce | Random 32-bit |
| 10 | `user_public_key` | int | Stark public key | |
| 11 | `domain_name` | str | `"Perpetuals"` | Fixed |
| 12 | `domain_version` | str | `"v0"` | Fixed |
| 13 | `domain_chain_id` | str | `"SN_SEPOLIA"` / `"SN_MAIN"` | Network-specific |
| 14 | `domain_revision` | str | `"1"` | Fixed |

### 2. Sign Convention (CRITICAL)

From `order_object_settlement.py`:

```python
if is_buying_synthetic:  # BUY
    stark_collateral_amount = stark_collateral_amount.negate()  # quote is NEGATIVE
else:  # SELL
    stark_synthetic_amount = stark_synthetic_amount.negate()    # base is NEGATIVE
```

| Order Side | base_amount (synthetic) | quote_amount (collateral) |
|------------|------------------------|---------------------------|
| **BUY**    | POSITIVE (receive BTC) | NEGATIVE (pay USD) |
| **SELL**   | NEGATIVE (pay BTC)     | POSITIVE (receive USD) |

Fee amount is **ALWAYS positive**.

### 3. Settlement Resolution (Amount Conversion)

From `assets.py`:

```python
def convert_human_readable_to_stark_quantity(self, internal: Decimal, rounding_context: Context) -> int:
    return int(rounding_context.multiply(internal, Decimal(self.settlement_resolution)).to_integral(
        context=rounding_context
    ))
```

**Example for BUY 0.001 BTC @ $80,000:**
- BTC resolution: 1,000,000,000 (from l2_config.synthetic_resolution)
- USD resolution: 1,000,000 (from l2_config.collateral_resolution)

```
base_amount = 0.001 * 1,000,000,000 = 1,000,000 (POSITIVE)
quote_amount = -(0.001 * 80000) * 1,000,000 = -80,000,000 (NEGATIVE)
fee_amount = (0.0005 * 80) * 1,000,000 = 40,000 (POSITIVE)
```

### 4. Expiration Calculation (CRITICAL)

From `order_object_settlement.py`:

```python
def __calc_settlement_expiration(expiration_timestamp: datetime):
    expire_time_with_buffer = expiration_timestamp + timedelta(days=14)
    expire_time_as_seconds = math.ceil(expire_time_with_buffer.timestamp())
    return expire_time_as_seconds
```

**Important:**
1. Add **14 days buffer** to order expiry
2. Convert to **SECONDS** (NOT milliseconds)
3. Use `math.ceil()` (round up)

### 5. Nonce Generation

From `utils/nonce.py`:

```python
def generate_nonce() -> int:
    return random.randint(0, 2**32 - 1)
```

Nonce should be a random 32-bit unsigned integer.

## Your Debug Data Analysis

```json
{
  "baseAmount": {"_value": 100},       // WRONG: Should be ~1,000,000
  "quoteAmount": {"_value": -8000000}, // Close but calculation looks off
  "feeAmount": "0x7d0",                // = 2000, should be ~40,000
  "salt": "0x1",                       // = 1, should be random 32-bit
  "expiration": {"seconds": "0x6973e579"} // Check if +14 days buffer applied
}
```

**Issues:**
1. `baseAmount` missing settlement resolution multiplier
2. `feeAmount` calculated incorrectly
3. `salt` is hardcoded to 1 instead of random

## Documentation Created

Full analysis at: `C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-order-hash-signing-analysis.md`

## Action Items

1. **Replace hash algorithm** - Cannot use custom Pedersen chain, must use native `get_order_msg_hash()`
2. **Fix amount conversions** - Apply settlement_resolution from market l2_config
3. **Apply sign convention** - Negate correct amount based on order side
4. **Fix expiration** - Add 14 day buffer, convert to seconds
5. **Generate random nonce** - Use `Random.Shared.Next(0, int.MaxValue)`
6. **Use correct domain** - "Perpetuals", "v0", chain-specific ID, "1"

## Python SDK Reference

| File | Purpose |
|------|---------|
| `order_object_settlement.py` | Hash computation, sign rules, expiry |
| `amounts.py` | StarkAmount class, rounding |
| `assets.py` | settlement_resolution conversion |
| `markets.py` | Asset from l2_config |
| `configuration.py` | Domain values |
| `utils/nonce.py` | Nonce generation |

## Rust Library Reference

From `x10xchange/stark-crypto-wrapper-py`:

| Function | File | Purpose |
|----------|------|---------|
| `get_order_msg_hash` | `lib.py` | Python wrapper |
| `rs_get_order_msg` | `lib.rs` | Rust implementation |

## Priority: CRITICAL

The signature will ALWAYS fail until the hash algorithm is replaced.

---

# Deep Analysis: Extended DEX StarkEx Order Hash Algorithm

## Date: 2026-01-02

## Task Summary
Performed comprehensive analysis of the Python SDK to document the exact algorithm for computing the StarkEx perpetual order message hash.

## Documentation Created
Full specification document created at:
`C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-starkex-order-hash-algorithm.md`

## Key Findings Summary

### 1. Hash Function
The Python SDK uses `fast_stark_crypto.get_order_msg_hash()` - a **native Rust function**, NOT a custom Pedersen hash chain. The C# code's custom SNIP-12 style hash is completely wrong.

### 2. Function Parameters (14 total)
```
get_order_msg_hash(
    position_id,        # int - Account l2Vault
    base_asset_id,      # int - l2Config.syntheticId (hex to int)
    base_amount,        # int - SIGNED Stark amount
    quote_asset_id,     # int - l2Config.collateralId (hex to int)
    quote_amount,       # int - SIGNED Stark amount
    fee_amount,         # int - ALWAYS positive
    fee_asset_id,       # int - same as quote_asset_id
    expiration,         # int - order expiry + 14 DAYS (IN SECONDS)
    salt,               # int - random 32-bit nonce
    user_public_key,    # int - Stark public key
    domain_name,        # str - "Perpetuals" (FIXED)
    domain_version,     # str - "v0" (FIXED)
    domain_chain_id,    # str - "SN_SEPOLIA" or "SN_MAIN"
    domain_revision,    # str - "1" (FIXED)
)
```

### 3. Sign Convention (CRITICAL)
| Order Side | base_amount (synthetic) | quote_amount (collateral) |
|------------|-------------------------|---------------------------|
| BUY        | POSITIVE                | NEGATIVE                  |
| SELL       | NEGATIVE                | POSITIVE                  |

Fee is ALWAYS positive.

### 4. Amount Conversion
```
stark_amount = human_amount * settlement_resolution
```

For BTC-USD:
- syntheticResolution = 1,000,000
- collateralResolution = 1,000,000

### 5. Expiration Calculation
1. Take order expiry timestamp
2. Add 14 days buffer
3. Convert to Unix seconds (NOT milliseconds)
4. Round UP (ceiling)

### 6. Domain Values
| Network | chain_id |
|---------|----------|
| Testnet | "SN_SEPOLIA" |
| Mainnet | "SN_MAIN" |

Domain name is always "Perpetuals", version is "v0", revision is "1".

### 7. Test Case Verification
From Python SDK test (BUY 0.001 BTC @ $43,445.1168):
```
syntheticAmount: 1000 (positive)
collateralAmount: -43445117 (negative, rounded UP)
feeAmount: 21723 (rounded UP)
```

## Issues in Current C# Code

| Aspect | Current C# | Correct |
|--------|------------|---------|
| Hash algorithm | Custom Pedersen | Native get_order_msg_hash |
| Domain name | "Extended DEX" | "Perpetuals" |
| Domain version | 1 (int) | "v0" (string) |
| Chain ID | 300/304 (int) | "SN_SEPOLIA"/"SN_MAIN" |
| Amounts | 10^8 scale | settlement_resolution |
| Sign convention | Missing | base/quote signs per side |
| Expiration | Raw millis | Seconds + 14 days |
| Nonce | Sequential | Random 32-bit |

## Python SDK Files Analyzed

| File | Path |
|------|------|
| Hash computation | `x10/perpetual/order_object_settlement.py` |
| Order factory | `x10/perpetual/order_object.py` |
| Amount rounding | `x10/perpetual/amounts.py` |
| Resolution conversion | `x10/perpetual/assets.py` |
| Market model | `x10/perpetual/markets.py` |
| Domain config | `x10/perpetual/configuration.py` |
| Account signing | `x10/perpetual/accounts.py` |
| Nonce generation | `x10/utils/nonce.py` |
| Test cases | `tests/perpetual/test_order_object.py` |
| Test fixtures | `tests/fixtures/markets.py`, `tests/fixtures/accounts.py` |

## Next Steps Required

1. Research if StarkNativeMethods has `get_order_msg_hash` equivalent
2. If not, implement the algorithm in C# or wrap the Rust library
3. Update StarkSigner.cs with correct hash computation
4. Create StarkAmountCalculator helper for resolution/rounding
5. Add unit tests matching Python SDK test cases

---

# Bug Fix: Extended DEX StarkEx Signature Hash Algorithm

## Date: 2026-01-02

## Task Summary
Completely rewrote the StarkSigner.cs to use the correct StarkEx perpetual order hash algorithm instead of the incorrect custom SNIP-12 style hash.

## Root Cause
The previous implementation used a custom SNIP-12 style Pedersen hash chain that was completely wrong. The correct algorithm is the StarkEx perpetual limit order with fees hash, which uses a 5-parameter Pedersen hash.

## Solution

### 1. Implemented StarkEx Perpetual Order Hash
**File: `GridBot.Extended/StarkSigner.cs`**

Replaced `ComputeOrderMessageHash()` with `ComputeStarkExPerpetualOrderHash()` that follows the StarkEx documentation:

```csharp
// Hash = Pedersen(asset_id_sell, asset_id_buy, asset_id_fee, packed_message0, packed_message1)
//
// packed_message0 (224 bits):
// - [223:160] amount_sell (64 bits)
// - [159:96] amount_buy (64 bits)
// - [95:32] max_amount_fee (64 bits)
// - [31:0] nonce (32 bits)
//
// packed_message1 (251 bits):
// - [250:241] type (10 bits, value 3 for limit)
// - [240:177] position_id (64 bits) x3
// - [48:17] expiration_timestamp (32 bits, in seconds)
// - [16:0] padding (17 bits zeros)
```

### 2. Created StarkExOrderParams Class
New parameter class for signing:
- `PositionId` - Account vault ID
- `BaseAmount` - Synthetic amount (SIGNED: +BUY, -SELL)
- `QuoteAmount` - Collateral amount (SIGNED: -BUY, +SELL)
- `FeeAmount` - Max fee (always positive)
- `Nonce` - Random 32-bit value
- `ExpirationSeconds` - Order expiry + 14 days in seconds

### 3. Created StarkAmountCalculator Helper
Static helper class for amount calculations:
- `CalculateStarkAmounts()` - Converts human amounts to Stark amounts with proper rounding and sign convention
- `CalcSettlementExpiration()` - Adds 14-day buffer and converts to seconds
- `GenerateNonce()` - Generates random 32-bit nonce

### 4. Updated SignOrder Method
Changed signature to: `SignOrder(StarkExOrderParams order, L2ConfigInfo marketInfo)`
- Now takes L2ConfigInfo for asset IDs and resolutions
- Uses proper sign convention for buy/sell
- Packs amounts into 224-bit and 251-bit values
- Uses 5-parameter Pedersen hash

### 5. Updated ExtendedOrderAdapter
**File: `GridBot.Extended/Adapters/ExtendedOrderAdapter.cs`**
- Added market info caching
- Changed `BuildOrderRequest` to async `BuildOrderRequestAsync`
- Fetches market L2Config for signing
- Uses `StarkAmountCalculator` for proper amount conversion

### 6. Updated Debug Endpoints
**File: `GridBot.ApiService/Program.cs`**
- Updated `/api/debug/extended/limit-order` to use new signing approach
- Updated `/api/debug/extended/market-order` to use new signing approach
- Both endpoints now:
  - Fetch market L2Config
  - Calculate Stark amounts with proper resolutions
  - Use random nonce
  - Add 14-day expiration buffer

## Sign Convention
| Order Side | base_amount (synthetic) | quote_amount (collateral) |
|------------|-------------------------|---------------------------|
| BUY        | POSITIVE (receive BTC)  | NEGATIVE (pay USD)        |
| SELL       | NEGATIVE (pay BTC)      | POSITIVE (receive USD)    |

## Amount Calculation
```csharp
var (baseAmount, quoteAmount, feeAmount) = StarkAmountCalculator.CalculateStarkAmounts(
    quantity,           // e.g., 0.0001 BTC
    price,              // e.g., $80,000
    feeRate,            // e.g., 0.00025 (0.025%)
    isBuy,
    l2Config.SyntheticResolution,   // 1,000,000
    l2Config.CollateralResolution); // 1,000,000
```

## Expiration Calculation
```csharp
// Order expiry + 14 days, in seconds
var settlementExpiration = StarkAmountCalculator.CalcSettlementExpiration(orderExpiryMillis);
```

## Files Modified
1. `GridBot.Extended/StarkSigner.cs` - Complete rewrite with correct hash algorithm
2. `GridBot.Extended/Adapters/ExtendedOrderAdapter.cs` - Updated signing flow
3. `GridBot.ApiService/Program.cs` - Updated debug endpoints

## Build Status
- Solution compiles successfully with 0 errors and 0 warnings

## Reference
- StarkEx Documentation: https://docs.starkware.co/starkex/perpetual/signature_construction_perpetual.html
- Extended API Specialist Analysis: `.claude/doc/extended-starkex-order-hash-algorithm.md`

## Next Steps
Test the limit order placement on Extended mainnet to verify the signature is now accepted.

---

# CRITICAL Investigation: Extended DEX Order Hash Algorithm - SNIP-12 Discovery

## Date: 2026-01-02

## Task Summary
Deep investigation of why the "Invalid StarkEx signature" (error 1101) persists after implementing the standard StarkEx perpetual order hash.

## Root Cause Discovered

**The Extended DEX does NOT use the standard StarkEx perpetual order hash.**

Instead, it uses a **SNIP-12 typed domain hash** implemented in the `fast_stark_crypto.get_order_msg_hash()` Rust function.

## Evidence

### Python SDK Hash Function Call
From `x10/perpetual/order_object_settlement.py`:

```python
get_order_msg_hash(
    position_id=position_id,
    base_asset_id=int(synthetic_asset.settlement_external_id, 16),
    base_amount=amount_synthetic.value,          # SIGNED amount
    quote_asset_id=int(collateral_asset.settlement_external_id, 16),
    quote_amount=amount_collateral.value,        # SIGNED amount
    fee_amount=max_fee.value,
    fee_asset_id=int(collateral_asset.settlement_external_id, 16),
    expiration=__calc_settlement_expiration(expiration_timestamp),
    salt=nonce,
    user_public_key=public_key,                  # INCLUDED IN HASH
    domain_name=starknet_domain.name,            # "Perpetuals"
    domain_version=starknet_domain.version,      # "v0"
    domain_chain_id=starknet_domain.chain_id,    # "SN_SEPOLIA" or "SN_MAIN"
    domain_revision=starknet_domain.revision,    # "1"
)
```

### Domain Configuration
From `x10/perpetual/configuration.py`:

**Testnet:**
```python
starknet_domain=StarknetDomain(name="Perpetuals", version="v0", chain_id="SN_SEPOLIA", revision="1")
```

**Mainnet:**
```python
starknet_domain=StarknetDomain(name="Perpetuals", version="v0", chain_id="SN_MAIN", revision="1")
```

## Key Differences from Standard StarkEx

| Aspect | Standard StarkEx | Extended DEX (Python SDK) |
|--------|-----------------|---------------------------|
| Hash algorithm | 5-element Pedersen | SNIP-12 typed domain hash |
| Domain params | None | name, version, chain_id, revision |
| User public key | Not in hash | Included in hash |
| Library | Custom Pedersen chain | `fast_stark_crypto` Rust lib |

## Current C# Implementation (WRONG)

```csharp
// StarkSigner.cs line 184
var hash = StarkNativeMethods.PedersenHashMany(
    ToHex(assetIdSell),
    ToHex(assetIdBuy),
    ToHex(assetIdFee),
    ToHex(packed0),
    ToHex(packed1)
);
```

This is the standard StarkEx hash, but Extended DEX expects the SNIP-12 typed domain hash.

## Required Fix Options

### Option A: Port Rust Algorithm to C#
Implement the SNIP-12 order hash algorithm in C#. This requires:
1. Understanding the exact Poseidon/Pedersen hash chain
2. Implementing type_hash computation
3. Implementing domain separator encoding
4. Implementing message encoding

### Option B: Wrap Rust Library
Compile `fast_stark_crypto` as a native DLL and P/Invoke it from C#.

### Option C: Request Specification
Contact Extended Exchange team for exact algorithm specification.

## Test Case Verification

From `tests/perpetual/test_order_object.py` (BUY 0.001 BTC @ $43,445.1168):

```json
{
  "debuggingAmounts": {
    "collateralAmount": "-43445117",  // Negative for BUY
    "feeAmount": "21723",             // Always positive
    "syntheticAmount": "1000"         // Positive for BUY
  }
}
```

Note: resolution = 1,000,000, so `0.001 * 1,000,000 = 1000`

## Sign Convention

| Order Side | base_amount (synthetic) | quote_amount (collateral) |
|------------|-------------------------|---------------------------|
| BUY        | POSITIVE                | NEGATIVE                  |
| SELL       | NEGATIVE                | POSITIVE                  |

Fee is ALWAYS positive.

## Documentation Created
Full investigation documented at:
`C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-order-hash-algorithm-investigation.md`

## Python SDK Files Analyzed
- `x10/perpetual/order_object_settlement.py` - Hash computation
- `x10/perpetual/configuration.py` - Domain configuration
- `x10/perpetual/amounts.py` - Rounding rules
- `x10/perpetual/assets.py` - Resolution conversion
- `tests/perpetual/test_order_object.py` - Test cases
- `tests/fixtures/markets.py` - Market L2Config
- `tests/fixtures/accounts.py` - Test account setup

## External References
- [fast-stark-crypto PyPI](https://pypi.org/project/fast-stark-crypto/)
- [x10xchange/stark-crypto-wrapper-py GitHub](https://github.com/x10xchange/stark-crypto-wrapper-py)
- [SNIP-12 Specification](https://github.com/starknet-io/SNIPs/blob/main/SNIPS/snip-12.md)

## Priority
**CRITICAL** - The order hash algorithm must be completely replaced. The current standard StarkEx hash will NEVER work.

## Next Steps Required
1. Research if there's a C# SNIP-12 implementation available
2. Consider wrapping the Rust library as a native DLL
3. Or implement SNIP-12 order hash from scratch in C#
4. Update StarkSigner.cs with correct algorithm
5. Verify with Python SDK test cases

---

# DEFINITIVE Analysis: Extended DEX Order Hash Algorithm - Poseidon SNIP-12

## Date: 2026-01-02

## Task Summary
Deep traced the Python SDK to the underlying Rust implementation to document the EXACT hash algorithm used by Extended DEX.

## CRITICAL DISCOVERY: Uses Poseidon SNIP-12, NOT Pedersen StarkEx

The Extended DEX uses a **completely different hashing algorithm** than standard StarkEx:

| Aspect | Standard StarkEx | Extended DEX (X10) |
|--------|-----------------|-------------------|
| Hash function | Pedersen | **Poseidon** |
| Message format | Bit-packed fields | **SNIP-12 typed data** |
| Domain | Not used | **Required** |
| Type selectors | Not used | **Required** |
| User public key | Not in hash | **Included** |

## Algorithm Traced from Rust Source

### Final Message Hash Formula
```rust
message_hash = Poseidon(
    cairo_short_string("StarkNet Message"),
    domain_hash,
    user_public_key,
    order_hash
)
```

### Order Hash Formula
```rust
order_hash = Poseidon(
    Order_SELECTOR,          // starknet_keccak of type string
    position_id,             // u32 as Felt
    base_asset_id,           // Felt (hex converted)
    base_amount,             // i64 as Felt (SIGNED!)
    quote_asset_id,          // Felt (hex converted)
    quote_amount,            // i64 as Felt (SIGNED!)
    fee_asset_id,            // Felt (hex converted)
    fee_amount,              // u64 as Felt
    expiration_seconds,      // u64 as Felt
    salt                     // Felt (nonce)
)
```

### Domain Hash Formula
```rust
domain_hash = Poseidon(
    Domain_SELECTOR,
    cairo_short_string("Perpetuals"),
    cairo_short_string("v0"),
    cairo_short_string("SN_SEPOLIA"),  // or "SN_MAIN"
    1                                   // revision
)
```

### Type Selector Strings
```rust
Order_SELECTOR = starknet_keccak(
    '"Order"("position_id":"felt","base_asset_id":"AssetId",' +
    '"base_amount":"i64","quote_asset_id":"AssetId",' +
    '"quote_amount":"i64","fee_asset_id":"AssetId",' +
    '"fee_amount":"u64","expiration":"Timestamp","salt":"felt")' +
    '"PositionId"("value":"u32")"AssetId"("value":"felt")' +
    '"Timestamp"("seconds":"u64")'
)

Domain_SELECTOR = starknet_keccak(
    '"StarknetDomain"("name":"shortstring","version":"shortstring",' +
    '"chainId":"shortstring","revision":"shortstring")'
)
```

## Test Vector from Rust Source

**Input:**
```
position_id      = 100
base_asset_id    = 0x2
base_amount      = 100         // i64, can be negative
quote_amount     = -156        // i64, NEGATIVE for BUY
fee_amount       = 74
expiration       = 100
salt             = 123
user_public_key  = 0x5d05989e9302dcebc74e241001e3e3ac3f4402ccf2f8e6f74b034b07ad6a904
domain           = (Perpetuals, v0, SN_SEPOLIA, 1)
```

**Expected Output:**
```
0x4de4c009e0d0c5a70a7da0e2039fb2b99f376d53496f89d9f437e736add6b48
```

## Python SDK `get_order_msg_hash` Function

From `stark-crypto-wrapper-py/python/fast_stark_crypto/lib.py` (branch: `add_limit_orders`):

```python
def get_order_msg_hash(
    position_id: int,
    base_asset_id: int,
    base_amount: int,
    quote_asset_id: int,
    quote_amount: int,
    fee_asset_id: int,
    fee_amount: int,
    expiration: int,
    salt: int,
    user_public_key: int,
    domain_name: str,
    domain_version: str,
    domain_chain_id: str,
    domain_revision: str,
) -> int:
    return int(
        rs_get_order_msg(
            str(position_id),
            hex(base_asset_id),
            str(base_amount),
            hex(quote_asset_id),
            str(quote_amount),
            hex(fee_asset_id),
            str(fee_amount),
            str(expiration),
            str(salt),
            hex(user_public_key),
            domain_name,
            domain_version,
            domain_chain_id,
            domain_revision,
        ),
        16,
    )
```

## Required C# Components

1. **Poseidon Hash** - Starknet's 3-element Poseidon
2. **Starknet Keccak** - Keccak-256 masked to 250 bits (for selectors)
3. **Cairo Short String** - ASCII to Felt conversion
4. **Signed i64 to Felt** - Two's complement field conversion

## Documentation Created

Full specification with all implementation details at:
`C:\Users\stany\source\repos\plan\GridBot\.claude\doc\extended-order-hash-algorithm-complete.md`

## Source Repositories

- **Python SDK:** https://github.com/x10xchange/python_sdk
- **Crypto Wrapper:** https://github.com/x10xchange/stark-crypto-wrapper-py (branch: `add_limit_orders`)
- **Rust Base:** https://github.com/x10xchange/rust-crypto-lib-base

## Priority

**CRITICAL** - The entire StarkSigner.cs must be rewritten to use Poseidon SNIP-12 instead of the current Pedersen-based approach.

---

# Native Signer Replacement - COMPLETED (2026-01-03)

## Summary
Replaced the old Pedersen-based native signer with a new library that implements Poseidon hash with SNIP-12 typed structured data, matching the Python SDK exactly.

## What Was Done

### Phase 1: Clone rust-crypto-lib-base and add C ABI exports ✓
- Backed up old `stark-signer` to `stark-signer-old`
- Cloned `x10xchange/rust-crypto-lib-base` as new `stark-signer`
- Created `src/ffi.rs` with C ABI wrapper functions:
  - `stark_get_order_hash` - Computes order hash using Poseidon + SNIP-12
  - `stark_sign` - Signs message hash with ECDSA
  - `stark_get_public_key` - Derives public key from private key
  - `stark_get_error_message` - Returns error descriptions
- All 19 unit tests pass (including FFI tests with known test vectors)

### Phase 2: Build native libraries ✓
- Built Windows x64 release DLL (285KB)
- Copied to `GridBot.Extended/Native/stark-signer-windows-amd64.dll`

### Phase 3: Update C# P/Invoke bindings ✓
- Updated `StarkNativeMethods.cs` with new function imports
- Added `GetOrderHash` wrapper with 14 parameters for SNIP-12 domain data
- Updated error codes (added `ErrComputationFailed = -4`)
- Removed old Pedersen hash functions (no longer exported)

### Phase 4: Update StarkSigner.cs ✓
- Replaced `ComputeStarkExPerpetualOrderHash` (Pedersen) with `ComputeOrderHash` (Poseidon + SNIP-12)
- Now calls `StarkNativeMethods.GetOrderHash` with domain parameters:
  - `domainName: "Perpetuals"`
  - `domainVersion: "v0"`
  - `domainChainId: "SN_MAIN"` or `"SN_SEPOLIA"`
  - `domainRevision: "1"`
- Added validation for required L2Config fields
- Removed unused helper methods

### Phase 5: Cleanup ✓
- Removed `stark-signer-old` backup directory
- Solution builds successfully with 0 errors

## Files Changed/Created

| File | Change |
|------|--------|
| `stark-signer/src/lib.rs` | Added `pub mod ffi;` |
| `stark-signer/src/ffi.rs` | NEW - C ABI wrapper functions |
| `stark-signer/Cargo.toml` | Already had cdylib (from previous attempt) |
| `GridBot.Extended/Native/stark-signer-windows-amd64.dll` | REPLACED - New 285KB DLL |
| `GridBot.Extended/Native/StarkNativeMethods.cs` | UPDATED - New P/Invoke bindings |
| `GridBot.Extended/StarkSigner.cs` | UPDATED - Uses new native hash function |

## Test Vector (from Python SDK fixtures)
```
position_id: 100
base_asset_id: 0x2
base_amount: 100
quote_asset_id: 0x1
quote_amount: -156
fee_asset_id: 0x1
fee_amount: 74
expiration: 100
salt: 123
public_key: 0x5d05989e9302dcebc74e241001e3e3ac3f4402ccf2f8e6f74b034b07ad6a904
domain: Perpetuals/v0/SN_SEPOLIA/1

Expected hash: 0x04de4c009e0d0c5a70a7da0e2039fb2b99f376d53496f89d9f437e736add6b48
```

## Test Results - SUCCESS ✓

### Test Execution
Ran application and tested limit order on Extended mainnet:

```bash
curl -sk -X POST https://localhost:7452/api/debug/extended/limit-order
```

### Response
```json
{
  "request": {
    "market": "BTC-USD",
    "side": "BUY",
    "type": "LIMIT",
    "quantity": 0.0001,
    "price": 80000,
    "nonce": 1349560246,
    "baseAmount": 100,
    "quoteAmount": -8000000,
    "feeAmount": 2000
  },
  "response": {
    "id": 2007398264881913856,
    "externalId": "c81ba90beaa04de594f90fea8e9563cd"
  }
}
```

### Result
**Order accepted!** Server returned order ID `2007398264881913856`, confirming the signature is now valid.

The "Invalid StarkEx signature" (error 1101) issue is **RESOLVED**.

## Summary
The native signer replacement is complete and working:
- Poseidon hash + SNIP-12 typed structured data (matching Python SDK exactly)
- Successfully placing orders on Extended DEX mainnet