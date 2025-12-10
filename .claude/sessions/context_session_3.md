# Session 3: WebSocket Transaction Submission

## Status: COMPLETED

## Objective
Replace HTTP-based transaction submission in `WsLighterCommandClient` with WebSocket-based submission for both single and batch transactions.

## Background
Previously `WsLighterCommandClient` used HTTP POST to `sendTx` and `sendTxBatch` endpoints. This has been replaced with WebSocket-based submission which provides:
- Lower latency (no HTTP handshake overhead)
- Consistent connection usage (already connected for market data)
- Better for high-frequency trading scenarios

## Implementation Summary

### Files Modified

1. **`GridBot.Lighter/Models/WebSocket/WebSocketMessages.cs`**
   - Added `SendTxWsResponse` record for single transaction responses
   - Added `SendTxBatchWsResponse` record for batch transaction responses
   - Both include request ID correlation, code, message, tx_hash(es), and predicted execution time

2. **`GridBot.Lighter/ILighterWebSocketClient.cs`**
   - Added `SendTransactionAsync(int txType, string txInfo, bool? priceProtection, CancellationToken)` method
   - Added `SendTransactionBatchAsync(int[] txTypes, string[] txInfos, CancellationToken)` method

3. **`GridBot.Lighter/LighterWebSocketClient.cs`**
   - Added `_pendingRequests` ConcurrentDictionary for request-response correlation
   - Added `TransactionTimeoutMs` constant (30 seconds)
   - Implemented `SendTransactionAsync` with request ID tracking and timeout
   - Implemented `SendTransactionBatchAsync` with JSON stringified arrays (as required by API)
   - Added `HandleSendTxResponse` and `HandleSendTxBatchResponse` helper methods
   - Added routing for `jsonapi/sendtx` and `jsonapi/sendtxbatch` message types in `RouteMessageAsync`

4. **`GridBot.Lighter/WsLighterCommandClient.cs`**
   - Changed dependency from `HttpClient` to `ILighterWebSocketClient`
   - Updated constructor to accept `ILighterWebSocketClient wsClient`
   - Replaced `SendTransactionAsync` to use WebSocket instead of HTTP POST
   - Replaced `SendTransactionBatchAsync` to use WebSocket instead of HTTP POST
   - Removed `EnsureSuccessStatusCodeAsync` method (no longer needed)
   - Removed unused `using System.Net.Http.Json;` and `using System.Globalization;`
   - Updated logging from "POST sendTx" to "WS sendTx"

5. **`GridBot.Lighter/LighterServiceCollectionExtensions.cs`**
   - Updated `WsLighterCommandClient` registration to inject `ILighterWebSocketClient` instead of `HttpClient`
   - Renamed HTTP client from "LighterCommandClient" to "LighterRestClient" (used only for REST operations like markets list)

## WebSocket Message Formats

### Single Transaction Request
```json
{
    "type": "jsonapi/sendtx",
    "data": {
        "id": "tx_<guid>",
        "tx_type": 14,
        "tx_info": "<signed tx info string>",
        "price_protection": true  // optional
    }
}
```

### Single Transaction Response
```json
{
    "type": "jsonapi/sendtx",
    "id": "tx_<guid>",
    "code": 200,
    "message": "Success",
    "tx_hash": "0xabc123...",
    "predicted_execution_time_ms": 150
}
```

### Batch Transaction Request
```json
{
    "type": "jsonapi/sendtxbatch",
    "data": {
        "id": "txbatch_<guid>",
        "tx_types": "[14, 14, ...]",     // JSON stringified array
        "tx_infos": "[{...}, {...}]"     // JSON stringified array
    }
}
```

### Batch Transaction Response
```json
{
    "type": "jsonapi/sendtxbatch",
    "id": "txbatch_<guid>",
    "code": 200,
    "message": "Success",
    "tx_hashes": "0xabc123...,0xdef456...",
    "predicted_execution_time_ms": "150,160"
}
```

## Key Design Decisions

1. **Request-Response Correlation**: Uses `TaskCompletionSource<object>` with unique request IDs to correlate responses with pending requests
2. **Timeout Handling**: 30-second timeout with `CancellationTokenSource.CreateLinkedTokenSource` for proper cancellation
3. **JSON Stringification for Batch**: Batch requests require `tx_types` and `tx_infos` to be JSON stringified strings, not raw arrays
4. **Maximum 50 transactions per batch**: Enforced by validation in `SendTransactionBatchAsync`
5. **Response Conversion**: WebSocket responses are converted to existing `RespSendTx`/`RespSendTxBatch` types for API compatibility

## Build Status
- **Build Succeeded**: 0 warnings, 0 errors

## Testing Considerations
- Need to test WebSocket disconnection during transaction (will throw exception)
- Request timeout handling (30 seconds)
- Concurrent request correlation (multiple in-flight transactions)
- Batch size limits (50 max)

---

## Code Review Results (2025-12-10)

**Reviewer:** csharp-code-reviewer
**Verdict:** APPROVED WITH RECOMMENDATIONS

### Summary
- **Critical Issues:** 0
- **Important Issues (WARNING):** 4
- **Minor Suggestions:** 4

### Important Issues to Address

1. **Pending Request Cleanup on Disconnect** - When WebSocket disconnects or `DisposeAsync` is called, pending `TaskCompletionSource` instances are not cancelled, leaving callers hanging until timeout. Fix: iterate through `_pendingRequests` and call `TrySetCanceled()` in `DisposeAsync`.

2. **#region Directive Usage** - `WebSocketMessages.cs` uses multiple `#region` blocks which violates project guidelines. File has grown to 1262 lines. Consider splitting into separate files per message type.

3. **Unnecessary Async State Machines** - Several handler methods (`HandleAccountMessageAsync`, `HandleOrdersMessageAsync`, etc.) are marked `async` but don't await anything, creating unnecessary state machines.

4. **Inconsistent Error Response Handling** - Error responses complete the `TaskCompletionSource` with result instead of exception. Consider failing fast with `TrySetException` for error codes.

### Thread Safety
The implementation is thread-safe:
- `ConcurrentDictionary` for pending requests
- `SemaphoreSlim` for send serialization
- `TaskCompletionSource` with `RunContinuationsAsynchronously`
- GUID-based request IDs eliminate collisions
- Cleanup in `finally` blocks

### Recommended Actions
1. **Before deployment:** Fix pending request cleanup on disconnect
2. **Tech debt:** Remove #region directives in follow-up PR
3. **Optional:** Address async state machine and error handling improvements

**Full review document:** `.claude/doc/code-review-websocket-tx-submission.md`

---

## Trading Systems Audit Results (2025-12-10)

**Auditor:** trading-bot-auditor
**Verdict:** FAIL - DO NOT DEPLOY TO PRODUCTION

### Summary
- **HIGH Risk Issues:** 5 (BLOCKING)
- **MEDIUM Risk Issues:** 3
- **LOW Risk Issues:** 2

### Critical Trading Risks (Must Fix Before Production)

#### 1. Pending Requests Not Canceled on Disconnect
When WebSocket disconnects, `_pendingRequests` is NOT cleared. In-flight transactions hang for 30 seconds even though connection is dead. **Financial Impact:** Unknown transaction state can lead to duplicate order submissions if caller retries, potentially doubling position size.

#### 2. Transaction Status Unknown After Timeout
30-second timeout does not mean transaction failed - it means status is unknown. If original transaction succeeded but response was lost, retry creates duplicate order with new nonce. **Financial Impact:** 2x intended position size.

#### 3. Batch Partial Execution Not Handled
If batch partially executes (3 of 5 orders succeed), code returns success with only 3 tx_hashes but assumes all 5 succeeded. **Financial Impact:** Inventory tracking becomes incorrect.

#### 4. Messages Silently Dropped When WS Not Open
`SendMessageAsync` silently drops messages if WebSocket state is not Open. Caller's TCS times out after 30 seconds with no indication message was never sent.

#### 5. Stale Order Book Used for Market Orders
`CreateMarketOrderAsync` uses cached order book without timestamp validation. If WebSocket lagged or reconnecting, market order executes at stale prices. **Financial Impact:** Significant price slippage.

### Important Concerns

6. **Nonce Retry Ineffective:** WS-only mode cannot sync nonce from server. All 3 retries may fail if server nonce is significantly ahead.

7. **30-Second Timeout Too Long:** Market conditions change dramatically in 30 seconds during volatility.

8. **No Duplicate Request ID Check:** While GUIDs make collision unlikely, no defensive check exists.

### Recommendations

1. Add `CancelAllPendingRequests()` call in `HandleDisconnectAsync`
2. Create `TransactionStatusUnknownException` for timeout scenarios
3. Compare `tx_hashes.Length` vs `signedOrders.Length` for batch operations
4. Throw exception in `SendMessageAsync` when WebSocket not open
5. Add timestamp validation to `OrderBookSnapshot` before market order execution

### Testing Requirements Before Production

1. Disconnect during transaction - verify old requests canceled
2. Batch partial execution - mock exchange returning fewer tx_hashes
3. Stale order book - verify market order rejection
4. Concurrent requests - 100 simultaneous orders
5. Timeout handling - mock non-responding exchange
6. Nonce desync - verify retry behavior

**Full audit report:** `.claude/doc/websocket_tx_audit_report.md`

---

## Critical Fixes Applied (2025-12-10)

Based on the code review and trading audit findings, the following critical fixes were implemented:

### Fix 1: Cancel Pending Requests on Disconnect (HIGH PRIORITY)

**Location:** `LighterWebSocketClient.cs`

Added `CancelAllPendingRequests(Exception)` helper method that:
- Iterates through all pending requests in `_pendingRequests`
- Calls `TrySetException` on each `TaskCompletionSource` to fail with the provided exception
- Removes entries from dictionary and logs count

This method is now called in:
- `HandleDisconnectAsync` - Before setting connection state to Reconnecting
- `DisposeAsync` - Before completing channels

**Impact:** Prevents 30-second stalls when WebSocket disconnects during transaction. Callers receive immediate failure notification instead of hanging.

### Fix 2: Throw Exception When WebSocket Not Open

**Location:** `LighterWebSocketClient.SendMessageAsync`

Changed from silently dropping messages to throwing `InvalidOperationException`:
```csharp
if (_webSocket?.State != WebSocketState.Open)
{
    throw new InvalidOperationException(
        $"Cannot send message: WebSocket state is {_webSocket?.State ?? WebSocketState.None}");
}
```

**Impact:** Immediate failure feedback instead of 30-second timeout for messages sent to closed socket.

### Fix 3: Detect Batch Partial Execution

**Location:** `WsLighterCommandClient.SubmitOrderBatchAsync`

Added detection logic that compares `txHashes.Length` vs `signedOrders.Length`:
- If counts differ, logs `CRITICAL` error message
- Sets `IsSuccess = false` for partial execution
- Returns actual `OrdersSubmitted` count (not assumed)
- Sets descriptive error message for partial execution

**Impact:** Prevents incorrect inventory tracking when only some batch orders succeed.

### Fix 4: Use TryAdd for Defensive Request ID Check

**Location:** `LighterWebSocketClient.SendTransactionAsync` and `SendTransactionBatchAsync`

Changed from direct dictionary assignment to `TryAdd`:
```csharp
if (!_pendingRequests.TryAdd(requestId, tcs))
{
    throw new InvalidOperationException($"Duplicate request ID: {requestId}");
}
```

**Impact:** Defensive programming against extremely unlikely GUID collision. Prevents response correlation bugs.

---

## Remaining Considerations (Not Fixed - Lower Priority)

1. **Stale Order Book for Market Orders** (Finding 9) - Existing code, not part of this WS transaction change. Should be addressed separately with timestamp validation on `OrderBookSnapshot`.

2. **Transaction Status Unknown After Timeout** (Finding 2) - This is inherent to distributed systems. Consider adding `TransactionStatusUnknownException` type for explicit handling.

3. **Nonce Retry Ineffective in WS-Only Mode** (Finding 4) - Design limitation. Consider REST fallback for nonce sync only.

4. **30-Second Timeout** (Finding 8) - Make timeout configurable based on operation type.

5. **#region Directives** - Tech debt. Split `WebSocketMessages.cs` into separate files in follow-up PR.

---

## Additional Critical Fixes (2025-12-10) - Stale Order Book & Timeout Handling

Based on the trading audit findings, the following remaining critical issues were addressed:

### Fix 5: Stale Order Book Validation for Market Orders

**Problem:** `CreateMarketOrderAsync` used cached order book without checking data freshness. If WebSocket feed was delayed, market orders could execute at stale prices, causing significant slippage.

**Files Modified:**
- `GridBot.Lighter/Models/WebSocket/ChannelEvents.cs`
- `GridBot.Lighter/WsLighterCommandClient.cs`

**Implementation:**

1. Added `LastUpdate` timestamp property to `OrderBookSnapshot`:
```csharp
public DateTimeOffset LastUpdate { get; init; } = DateTimeOffset.UtcNow;
```

2. Added staleness constant and validation in `WsLighterCommandClient`:
```csharp
private const int MaxOrderBookAgeMs = 2000; // 2 seconds

// In CreateMarketOrderAsync:
var ageMs = (DateTimeOffset.UtcNow - orderBook.LastUpdate).TotalMilliseconds;
if (ageMs > MaxOrderBookAgeMs)
{
    throw new LighterApiException(
        $"Order book data is stale ({ageMs:F0}ms old, max {MaxOrderBookAgeMs}ms). " +
        "Cannot execute market order safely. Wait for fresh data or use limit order.",
        code: 0);
}
```

**Impact:** Market orders are now rejected if order book data is older than 2 seconds, preventing execution at potentially incorrect prices.

### Fix 6: TransactionStatusUnknownException for Timeout Scenarios

**Problem:** When a transaction timed out, it was unclear if it succeeded or failed. Callers had no way to distinguish between actual failure and unknown status, potentially leading to duplicate orders on retry.

**Files Created:**
- `GridBot.Lighter/TransactionStatusUnknownException.cs`

**Files Modified:**
- `GridBot.Lighter/LighterApiException.cs` (changed from `sealed` to allow inheritance)
- `GridBot.Lighter/LighterWebSocketClient.cs`

**Implementation:**

1. Created `TransactionStatusUnknownException`:
```csharp
public sealed class TransactionStatusUnknownException : LighterApiException
{
    public string RequestId { get; }

    public TransactionStatusUnknownException(string requestId, string message)
        : base(message)
    {
        RequestId = requestId;
    }
}
```

2. Updated timeout handling in `SendTransactionAsync` and `SendTransactionBatchAsync`:
```csharp
catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
{
    // Timeout occurred (not caller cancellation)
    _logger.LogError(
        "Transaction {RequestId} timed out after {Timeout}ms - STATUS UNKNOWN. " +
        "Verify transaction state before retrying.",
        requestId, _options.TransactionTimeoutMs);
    throw new TransactionStatusUnknownException(
        requestId,
        $"Transaction timed out after {_options.TransactionTimeoutMs}ms. Status unknown - verify before retrying.");
}
```

**Impact:** Callers can now distinguish between definite failures and unknown status, enabling proper retry logic that verifies transaction state before retrying.

### Fix 7: Configurable Transaction Timeouts

**Problem:** The 30-second timeout was hardcoded and too long for volatile market conditions. Different operations may need different timeout values.

**Files Modified:**
- `GridBot.Lighter/WebSocketOptions.cs`
- `GridBot.Lighter/LighterWebSocketClient.cs`

**Implementation:**

1. Added timeout settings to `WebSocketOptions`:
```csharp
/// <summary>
/// Default timeout for single transaction operations in milliseconds.
/// </summary>
public int TransactionTimeoutMs { get; set; } = 30000;

/// <summary>
/// Timeout for batch transaction operations in milliseconds.
/// </summary>
public int BatchTransactionTimeoutMs { get; set; } = 60000;
```

2. Removed hardcoded `TransactionTimeoutMs` constant from `LighterWebSocketClient`

3. Updated `SendTransactionAsync` to use `_options.TransactionTimeoutMs`

4. Updated `SendTransactionBatchAsync` to use `_options.BatchTransactionTimeoutMs`

**Impact:** Timeouts are now configurable via `appsettings.json` under the `LighterWebSocket` section. Batch operations get a longer default timeout (60s) than single transactions (30s).

---

## Fix 8: REST Fallback for Nonce Synchronization (2025-12-10)

**Problem:** The nonce retry mechanism in `WsLighterCommandClient.ExecuteWithNonceRetryAsync` waited 100ms and retried, but without actually syncing the nonce from the server. In WS-only mode, `SyncNonceAsync` threw `NotSupportedException`. If the server nonce was significantly ahead (e.g., due to external transactions or service restart), all 3 retries would fail.

**Files Modified:**
- `GridBot.Lighter/WsLighterCommandClient.cs`
- `GridBot.Lighter/LighterServiceCollectionExtensions.cs`

**Implementation:**

### 1. Updated Constructor to Accept Optional HttpClient

```csharp
private readonly HttpClient? _httpClient;

public WsLighterCommandClient(
    SignerClient signer,
    ILighterRealtimeState state,
    ILighterWebSocketClient wsClient,
    ILogger<WsLighterCommandClient> logger,
    HttpClient? httpClient = null)  // Optional for nonce sync
{
    // ...
    _httpClient = httpClient;
}
```

### 2. Implemented SyncNonceAsync with HTTP Fallback

```csharp
public async Task<long> SyncNonceAsync(
    long accountIndex,
    int apiKeyIndex,
    CancellationToken cancellationToken = default)
{
    if (_httpClient == null)
    {
        throw new NotSupportedException(
            "SyncNonceAsync requires HTTP client. Configure HttpClient in DI registration.");
    }

    var url = $"nextNonce?account_index={accountIndex}&api_key_index={apiKeyIndex}";
    var response = await _httpClient.GetAsync(url, cancellationToken);

    // Parse NextNonce response and update SignerClient
    var nonceResponse = await response.Content.ReadFromJsonAsync<NextNonce>(...);
    _signer.SetNonce(nonceResponse.Nonce);

    return nonceResponse.Nonce;
}
```

### 3. Updated ExecuteWithNonceRetryAsync to Sync on Nonce Error

```csharp
catch (LighterApiException ex) when (ex.Code == InvalidNonceErrorCode && retryCount < MaxNonceRetries)
{
    retryCount++;

    // Try to sync nonce from server if HTTP client is available
    if (_httpClient != null)
    {
        try
        {
            await SyncNonceAsync(
                _signer.AccountIndex,
                _signer.ApiKeyIndex,
                cancellationToken);
            _logger.LogInformation("Successfully synced nonce from server");
        }
        catch (Exception syncEx)
        {
            _logger.LogWarning(syncEx, "Failed to sync nonce from server");
        }
    }

    await Task.Delay(NonceRetryDelayMs, cancellationToken);
}
```

### 4. Updated DI Registration to Provide HttpClient

```csharp
services.AddSingleton<ILighterCommandClient>(serviceProvider =>
{
    var signer = serviceProvider.GetRequiredService<SignerClient>();
    var state = serviceProvider.GetRequiredService<ILighterRealtimeState>();
    var wsClient = serviceProvider.GetRequiredService<ILighterWebSocketClient>();
    var logger = serviceProvider.GetRequiredService<ILogger<WsLighterCommandClient>>();
    var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient("LighterRestClient");

    return new WsLighterCommandClient(signer, state, wsClient, logger, httpClient);
});
```

**Impact:**
- When a nonce error occurs (code 21104), the system now attempts to sync the nonce from the server via REST API before retrying
- Uses the existing `nextNonce` endpoint: `GET /api/v1/nextNonce?account_index={id}&api_key_index={id}`
- Falls back gracefully if HTTP sync fails (logs warning, continues with local nonce increment)
- Existing `LighterRestClient` HttpClient is reused (already configured with correct base URL)
- Backward compatible: if no HttpClient provided, behavior matches previous implementation

**API Endpoint Used:**
```
GET /api/v1/nextNonce?account_index={accountIndex}&api_key_index={apiKeyIndex}

Response:
{
    "code": 200,
    "message": "Success",
    "nonce": 42
}
```

---

## Code Review: Fixes 5-8 (2025-12-10)

**Reviewer:** csharp-code-reviewer
**Verdict:** APPROVED

### Summary
| Severity | Count |
|----------|-------|
| CRITICAL | 0 |
| WARNING | 3 |
| SUGGESTION | 4 |

### Warnings (Non-Blocking)

1. **Async Methods Without Await** - Handler methods (`HandleOrderBookMessageAsync`, etc.) are marked `async` but contain no `await`, creating unnecessary state machine overhead. Recommend removing `async` keyword.

2. **CancellationToken Not Used in Handlers** - The `cancellationToken` parameter is accepted but not checked. Minor responsiveness concern.

3. **HttpClient Disposal Documentation** - `WsLighterCommandClient.Dispose()` does not dispose `_httpClient`, which is correct (owned by `IHttpClientFactory`), but a comment would clarify intent.

### Verified Correct

- **Thread Safety**: All concurrent structures properly implemented
- **Resource Cleanup**: `CancelAllPendingRequests` called in both `HandleDisconnectAsync` and `DisposeAsync`
- **File Split**: WebSocket messages split into 9 logical files, no `#region` directives
- **Async Patterns**: Correct use of `CreateLinkedTokenSource`, `RunContinuationsAsynchronously`
- **DI Registration**: Correct `IHttpClientFactory` pattern for `HttpClient` injection

### Files Reviewed
- `ChannelEvents.cs` - LastUpdate property added correctly
- `WsLighterCommandClient.cs` - Staleness validation at 2000ms, REST nonce fallback
- `TransactionStatusUnknownException.cs` - Proper inheritance from LighterApiException
- `LighterWebSocketClient.cs` - Timeout handling throws TransactionStatusUnknownException
- `WebSocketOptions.cs` - TransactionTimeoutMs and BatchTransactionTimeoutMs configurable
- `LighterServiceCollectionExtensions.cs` - HttpClient injected for nonce sync

**Full review:** `.claude/doc/code-review-websocket-fixes-5-8.md`

---

## Final Build Status
- **Build Succeeded**: 0 warnings, 0 errors
- **Critical Issues Fixed**: 8 of 8 blocking/important issues addressed
- **Code Review Status**: APPROVED (3 minor warnings for future cleanup)
- **Deployment Status**: Ready for production testing

---

## Re-Audit Results (2025-12-10)

**Auditor:** trading-bot-auditor
**Verdict:** PASS - APPROVED FOR PRODUCTION

### HIGH Risk Finding Verification

| Finding | Description | Status |
|---------|-------------|--------|
| F1 | Pending Requests Not Canceled on Disconnect | FIXED |
| F2 | Transaction Status Unknown After Timeout | FIXED |
| F3 | Batch Partial Execution Not Handled | FIXED |
| F4 | Messages Silently Dropped When WS Not Open | FIXED |
| F5 | Stale Order Book Used for Market Orders | FIXED |

### Additional Fixes Verified

1. **Nonce Retry with REST Fallback** - Properly implemented with `SyncNonceAsync` and DI registration
2. **Configurable Timeouts** - Via `WebSocketOptions.TransactionTimeoutMs` and `BatchTransactionTimeoutMs`
3. **Defensive Request ID Check** - Using `TryAdd` to detect GUID collisions

### New Risks Identified (All LOW)

1. Order book timestamp uses parse time, not exchange time (acceptable for 2s staleness threshold)
2. No circuit breaker pattern (future enhancement)
3. Batch size validation only in WebSocket layer (acceptable)

### Thread Safety Verification

All concurrent access patterns properly synchronized:
- `ConcurrentDictionary` for pending requests
- `SemaphoreSlim` for send/connect/auth locks
- `volatile` for connection state
- `TaskCompletionSource` with `RunContinuationsAsynchronously`

### Deployment Recommendation

**APPROVED FOR PRODUCTION** after completing recommended testing scenarios:
1. Disconnect during transaction
2. Transaction timeout handling
3. Batch partial execution
4. Stale order book rejection
5. Nonce desync recovery

**Full re-audit report:** `.claude/doc/websocket_tx_reaudit_report.md`

---

## Fix 10: LighterRealtimeStateService Refactoring (2025-12-10)

**Problem:** `LighterRealtimeStateService` was a `BackgroundService`. Its `ExecuteAsync` (which connects to WebSocket) runs AFTER all `StartAsync` methods complete. But in `Program.cs`, there was code calling `SubscribeMarketAsync` BEFORE the background execution started, so WebSocket wasn't connected yet.

**Solution:** Refactored `LighterRealtimeStateService` from a `BackgroundService` to a regular service with explicit `InitializeAsync` method.

### Files Modified

1. **`GridBot.Lighter/ILighterRealtimeState.cs`**
   - Added `InitializeAsync(CancellationToken)` method
   - Extended interface to implement `IAsyncDisposable`

2. **`GridBot.Lighter/LighterRealtimeStateService.cs`**
   - Removed `BackgroundService` inheritance
   - Added state tracking: `_processingCts`, `_processingTask`, `_initialized`, `_disposed`
   - Added `InitializeAsync` that connects WS and starts channel processing
   - Added `DisposeAsync` for proper cleanup with 5-second timeout
   - Added guards for disposed/uninitialized state

3. **`GridBot.Lighter/LighterServiceCollectionExtensions.cs`**
   - Removed `AddHostedService` registration
   - Service is now a singleton that must be explicitly initialized

4. **`GridBot.ApiService/Program.cs`**
   - Removed premature initialization code (lines 1093-1099)

5. **`GridBot.ApiService/Services/TradingBotHostedService.cs`**
   - Added `IMarketResolver` and `ILighterRealtimeState` dependencies
   - Updated `StartAsync` to initialize in correct order:
     1. MarketResolver (REST, discovers MarketId)
     2. RealtimeState (WebSocket connects, account subscriptions)
     3. Market subscriptions (uses known MarketId)
     4. Decision engine initialization

### Initialization Sequence (Fixed)

```
TradingBotHostedService.StartAsync
  ├── MarketResolver.InitializeAsync()     // REST call to discover MarketId
  ├── RealtimeState.InitializeAsync()      // WebSocket connects, subscribes to account
  │     ├── wsClient.ConnectAsync()
  │     ├── wsClient.SubscribeAccountAsync()
  │     ├── wsClient.SubscribeOrdersAsync()
  │     ├── wsClient.SubscribeNotificationsAsync()
  │     └── Start channel processing task
  ├── RealtimeState.SubscribeMarketAsync() // Market-specific data streams
  └── DecisionEngine.InitializeAsync()     // Trading engine setup
```

### Code Review: APPROVED

- Thread safety: Proper use of `volatile`, `ObjectDisposedException.ThrowIf` guards
- Resource management: Idempotent disposal with 5-second timeout
- No leaked resources: Background task properly cancelled and awaited
- Correct initialization order: Fixes the original race condition

**Full review:** `.claude/doc/code-review-realtime-state-refactoring.md`
