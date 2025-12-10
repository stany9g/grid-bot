# WebSocket Transaction Submission - Trading System Audit Report

**Audit Date:** 2025-12-10
**Auditor:** Trading Systems Auditor
**Files Reviewed:**
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\LighterWebSocketClient.cs`
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\WsLighterCommandClient.cs`
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\SignerClient.cs`
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Lighter\Models\WebSocket\WebSocketMessages.cs`

---

## FINDING 1: Pending Requests Not Canceled on WebSocket Disconnect

**Risk Level:** HIGH
**Category:** Safety | Race Condition
**Location:** `LighterWebSocketClient.cs:825-866` (HandleDisconnectAsync)
**Financial Impact:** Orders may appear to timeout when they actually succeeded, leading to duplicate order submissions on retry. Could result in 2x intended position size.
**Performance Impact:** 30-second stall waiting for response that will never arrive.

**Problem:**
When the WebSocket disconnects (either via server close or network failure), the `_pendingRequests` dictionary is NOT cleared and pending `TaskCompletionSource` objects are NOT canceled. Any in-flight transaction requests will hang for the full 30-second timeout even though the connection is already dead, and the caller will not know if the transaction was submitted successfully before disconnect.

**Evidence:**
```csharp
private async Task HandleDisconnectAsync(string reason, CancellationToken cancellationToken)
{
    if (_connectionState == ConnectionState.Reconnecting || _connectionState == ConnectionState.Failed)
        return;

    await SetConnectionStateAsync(ConnectionState.Reconnecting, reason, cancellationToken);
    // ... reconnection logic ...
    // NOTE: _pendingRequests is NEVER touched - all pending TCS objects remain
}
```

**Fix:**
```csharp
private async Task HandleDisconnectAsync(string reason, CancellationToken cancellationToken)
{
    if (_connectionState == ConnectionState.Reconnecting || _connectionState == ConnectionState.Failed)
        return;

    // CRITICAL: Cancel all pending requests immediately
    CancelAllPendingRequests(new InvalidOperationException($"WebSocket disconnected: {reason}"));

    await SetConnectionStateAsync(ConnectionState.Reconnecting, reason, cancellationToken);
    // ... reconnection logic ...
}

private void CancelAllPendingRequests(Exception exception)
{
    var pendingIds = _pendingRequests.Keys.ToArray();
    foreach (var id in pendingIds)
    {
        if (_pendingRequests.TryRemove(id, out var tcs))
        {
            tcs.TrySetException(exception);
        }
    }
    _logger.LogWarning("Canceled {Count} pending transaction requests due to disconnect", pendingIds.Length);
}
```

**Verdict:** FAIL

---

## FINDING 2: Transaction Status Unknown After Timeout - No Idempotency

**Risk Level:** HIGH
**Category:** Safety | Trading Logic
**Location:** `LighterWebSocketClient.cs:911-951` (SendTransactionAsync) and `WsLighterCommandClient.cs:444-468` (ExecuteWithNonceRetryAsync)
**Financial Impact:** If transaction times out, the order may have been accepted by the exchange. Retry logic will create a NEW nonce and submit a duplicate order, potentially doubling position size.
**Performance Impact:** N/A

**Problem:**
The 30-second timeout does not mean the transaction failed - it means we don't know if it succeeded. The current implementation throws `OperationCanceledException` on timeout, and the caller has no way to verify if the original transaction was processed. The nonce retry logic in `ExecuteWithNonceRetryAsync` assumes failure and will retry, but if the original succeeded with a consumed nonce, retry will either:
1. Succeed with a NEW nonce = duplicate order
2. Fail if exchange rejects (but original is already live)

**Evidence:**
```csharp
// LighterWebSocketClient.cs - timeout throws without transaction status
using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
cts.CancelAfter(TransactionTimeoutMs);

var result = await tcs.Task.WaitAsync(cts.Token); // Throws OperationCanceledException on timeout
return (SendTxWsResponse)result;

// WsLighterCommandClient.cs - retry uses NEW nonce (lambda re-signs)
return await ExecuteWithNonceRetryAsync(
    async () =>
    {
        var (txInfo, error) = await _signer.CreateOrderAsync(request); // Gets NEXT nonce
        // ... if original actually succeeded, this is a DUPLICATE ORDER
    },
    "CreateOrder",
    cancellationToken);
```

**Fix:**
```csharp
// 1. Add transaction state tracking with request ID
public enum TransactionState { Unknown, Submitted, Confirmed, Failed, Timeout }

// 2. On timeout, mark as UNKNOWN not FAILED
catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
{
    _logger.LogError("Transaction {RequestId} timed out - STATUS UNKNOWN, manual verification required", requestId);
    throw new TransactionStatusUnknownException(requestId, "Transaction timed out, status unknown");
}

// 3. Caller should NOT retry timeout errors automatically
// Instead: query order book or positions to verify if order exists
```

**Verdict:** FAIL

---

## FINDING 3: Race Condition in Request-Response Correlation on Duplicate IDs

**Risk Level:** MEDIUM
**Category:** Race Condition
**Location:** `LighterWebSocketClient.cs:922-949` (SendTransactionAsync)
**Financial Impact:** Extremely unlikely in practice due to GUID usage, but theoretically possible: wrong response delivered to wrong caller.
**Performance Impact:** N/A

**Problem:**
While GUIDs make collision extremely unlikely, there is no defensive check if a request ID already exists in `_pendingRequests`. If it did, the new TCS would overwrite the old one, and the old caller would never receive a response.

**Evidence:**
```csharp
var requestId = $"tx_{Guid.NewGuid():N}";
var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
_pendingRequests[requestId] = tcs; // Direct assignment, not TryAdd
```

**Fix:**
```csharp
var requestId = $"tx_{Guid.NewGuid():N}";
var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
if (!_pendingRequests.TryAdd(requestId, tcs))
{
    // Should never happen with GUIDs, but defensive
    throw new InvalidOperationException($"Duplicate request ID generated: {requestId}");
}
```

**Verdict:** CONDITIONAL PASS (Low risk due to GUID uniqueness)

---

## FINDING 4: Nonce Retry Logic Ineffective for WS-Only Mode

**Risk Level:** MEDIUM
**Category:** Trading Logic | Safety
**Location:** `WsLighterCommandClient.cs:444-468` (ExecuteWithNonceRetryAsync)
**Financial Impact:** After nonce error, retry waits 100ms but SignerClient simply increments local nonce. If server nonce is significantly ahead, all 3 retries will fail with the same error.
**Performance Impact:** 300ms wasted on doomed retries.

**Problem:**
The nonce retry mechanism was designed for HTTP mode where `SyncNonceAsync` could fetch the correct server nonce. In WS-only mode, `SyncNonceAsync` throws `NotSupportedException`. The retry simply waits 100ms and tries again with the next local nonce, but if server nonce is N and local is N-5, it will fail repeatedly.

**Evidence:**
```csharp
catch (LighterApiException ex) when (ex.Code == InvalidNonceErrorCode && retryCount < MaxNonceRetries)
{
    retryCount++;
    // In WS-only mode, we can't sync from server, so just wait and retry
    // The SignerClient's nonce will be incremented on the next signing attempt
    await Task.Delay(NonceRetryDelayMs, cancellationToken);
}
```

```csharp
// SyncNonceAsync throws in WS mode
public Task<long> SyncNonceAsync(...)
{
    throw new NotSupportedException(
        "SyncNonceAsync is not supported in WebSocket-only mode. " +
        "Nonce is tracked locally by SignerClient after initialization.");
}
```

**Fix:**
```csharp
// Option 1: Parse nonce from error message if API provides expected value
// Option 2: Implement WebSocket-based nonce query
// Option 3: Increase retry count and use exponential backoff
// Option 4: Add REST fallback for nonce sync only

catch (LighterApiException ex) when (ex.Code == InvalidNonceErrorCode && retryCount < MaxNonceRetries)
{
    retryCount++;
    // Extract expected nonce from error if available
    var expectedNonce = ParseExpectedNonceFromError(ex.Message);
    if (expectedNonce.HasValue)
    {
        _signer.SetNonce(expectedNonce.Value - 1); // -1 because signing increments
    }
    await Task.Delay(NonceRetryDelayMs * (int)Math.Pow(2, retryCount), cancellationToken);
}
```

**Verdict:** FAIL

---

## FINDING 5: Batch Transaction Partial Execution Not Handled

**Risk Level:** HIGH
**Category:** Trading Logic | Safety
**Location:** `WsLighterCommandClient.cs:284-338` (SubmitOrderBatchAsync)
**Financial Impact:** If batch partially executes (3 of 5 orders succeed), the return value shows success but only 3 tx_hashes. Caller assumes all 5 succeeded. Inventory tracking becomes incorrect.
**Performance Impact:** N/A

**Problem:**
The `SendTxBatchWsResponse` only has a single `Code` and `Message`. If the exchange partially executes a batch (some orders succeed, some fail), the current code has no way to know which specific orders failed. The `TxHashArray` may have fewer elements than submitted orders.

**Evidence:**
```csharp
return new BatchOrderResult
{
    IsSuccess = response.IsSuccess,
    OrdersSubmitted = response.IsSuccess ? signedOrders.Length : 0, // Assumes ALL or NONE
    TxHashes = response.TxHashArray, // May have fewer elements than signedOrders.Length
    ErrorMessage = response.IsSuccess ? null : response.Message,
    Code = response.Code
};
```

**Fix:**
```csharp
var txHashes = response.TxHashArray;
var partialSuccess = response.IsSuccess && txHashes.Length < signedOrders.Length;

if (partialSuccess)
{
    _logger.LogError(
        "CRITICAL: Batch partial execution! Submitted {Submitted}, Executed {Executed}. " +
        "Orders {FailedIndices} may have failed.",
        signedOrders.Length,
        txHashes.Length,
        string.Join(",", Enumerable.Range(txHashes.Length, signedOrders.Length - txHashes.Length)));
}

return new BatchOrderResult
{
    IsSuccess = response.IsSuccess && !partialSuccess,
    OrdersSubmitted = txHashes.Length, // Actual count, not assumed
    TxHashes = txHashes,
    ErrorMessage = partialSuccess
        ? $"Partial execution: {txHashes.Length}/{signedOrders.Length} orders succeeded"
        : (response.IsSuccess ? null : response.Message),
    Code = response.Code,
    PartialExecution = partialSuccess
};
```

**Verdict:** FAIL

---

## FINDING 6: SendMessageAsync Silently Drops Messages When WebSocket Not Open

**Risk Level:** MEDIUM
**Category:** Safety | Trading Logic
**Location:** `LighterWebSocketClient.cs:403-424` (SendMessageAsync)
**Financial Impact:** Transaction request appears to be sent but is silently dropped. Caller waits 30 seconds for response that will never come.
**Performance Impact:** 30-second stall.

**Problem:**
If WebSocket state is not `Open` at send time (e.g., during reconnection), the message is silently dropped. No exception is thrown, no indication given. The caller's `TaskCompletionSource` will timeout.

**Evidence:**
```csharp
private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(message, LighterJsonOptions.Default);
    var bytes = Encoding.UTF8.GetBytes(json);

    await _sendLock.WaitAsync(cancellationToken);
    try
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            await _webSocket.SendAsync(...);
        }
        // ELSE: Message is silently dropped! No exception, no logging.
    }
    finally
    {
        _sendLock.Release();
    }
}
```

**Fix:**
```csharp
private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(message, LighterJsonOptions.Default);
    var bytes = Encoding.UTF8.GetBytes(json);

    await _sendLock.WaitAsync(cancellationToken);
    try
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException(
                $"Cannot send message: WebSocket state is {_webSocket?.State ?? WebSocketState.None}");
        }

        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            endOfMessage: true,
            cancellationToken);
    }
    finally
    {
        _sendLock.Release();
    }
}
```

**Verdict:** FAIL

---

## FINDING 7: Response Handler Swallows Deserialization Errors

**Risk Level:** LOW
**Category:** Safety
**Location:** `LighterWebSocketClient.cs:1002-1021` (HandleSendTxResponse)
**Financial Impact:** If exchange sends malformed response, error is logged but caller never receives response. 30-second timeout occurs.
**Performance Impact:** 30-second stall on malformed response.

**Problem:**
Deserialization errors in response handlers are caught and logged, but the pending TCS is not completed with an error. The caller will timeout instead of getting immediate failure feedback.

**Evidence:**
```csharp
private void HandleSendTxResponse(string json)
{
    try
    {
        var response = JsonSerializer.Deserialize<SendTxWsResponse>(json, LighterJsonOptions.Default);
        if (response == null) return; // Silent failure

        if (_pendingRequests.TryGetValue(response.Id, out var tcs))
        {
            tcs.TrySetResult(response);
        }
        // ...
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to handle SendTx response");
        // TCS left hanging!
    }
}
```

**Fix:**
```csharp
private void HandleSendTxResponse(string json)
{
    SendTxWsResponse? response = null;
    try
    {
        response = JsonSerializer.Deserialize<SendTxWsResponse>(json, LighterJsonOptions.Default);
    }
    catch (JsonException ex)
    {
        _logger.LogError(ex, "Failed to deserialize SendTx response: {Json}", TruncateJson(json));
        // Try to extract ID from raw JSON to fail the correct request
        var id = TryExtractIdFromJson(json);
        if (id != null && _pendingRequests.TryRemove(id, out var failedTcs))
        {
            failedTcs.TrySetException(new LighterApiException($"Invalid response format: {ex.Message}"));
        }
        return;
    }

    if (response == null)
    {
        _logger.LogWarning("Received null SendTx response");
        return;
    }

    if (_pendingRequests.TryGetValue(response.Id, out var tcs))
    {
        tcs.TrySetResult(response);
    }
    else
    {
        _logger.LogWarning("Received SendTx response for unknown request ID: {Id}", response.Id);
    }
}
```

**Verdict:** CONDITIONAL PASS (Low probability, but should be fixed)

---

## FINDING 8: 30-Second Timeout May Be Too Long for Trading

**Risk Level:** MEDIUM
**Category:** Performance | Trading Logic
**Location:** `LighterWebSocketClient.cs:44` (TransactionTimeoutMs)
**Financial Impact:** In fast-moving markets, waiting 30 seconds for order confirmation could mean significant price slippage or missed opportunities.
**Performance Impact:** Blocks strategy execution for 30 seconds per failed transaction.

**Problem:**
The 30-second timeout is appropriate for ensuring transactions have time to be processed, but for a trading system, this is an eternity. Market conditions can change dramatically in 30 seconds during volatility.

**Evidence:**
```csharp
private const int TransactionTimeoutMs = 30000; // 30 seconds
```

**Fix:**
```csharp
// Make timeout configurable and use tiered approach
public class WebSocketTransactionOptions
{
    public int DefaultTimeoutMs { get; set; } = 10000;  // 10 seconds for most operations
    public int MarketOrderTimeoutMs { get; set; } = 5000;  // 5 seconds for market orders
    public int CriticalOperationTimeoutMs { get; set; } = 30000;  // 30 seconds for critical ops
}

// Usage:
var timeout = request.OrderType == OrderType.Market
    ? _options.MarketOrderTimeoutMs
    : _options.DefaultTimeoutMs;
```

**Verdict:** CONDITIONAL PASS (Operational concern, not a bug)

---

## FINDING 9: Market Order Uses Stale Order Book Data Without Timestamp Validation

**Risk Level:** HIGH
**Category:** Trading Logic | Safety
**Location:** `WsLighterCommandClient.cs:79-141` (CreateMarketOrderAsync)
**Financial Impact:** If order book is stale (WebSocket lagged or reconnecting), market order uses old prices. Could execute at prices significantly worse than current market.
**Performance Impact:** N/A

**Problem:**
The `CreateMarketOrderAsync` method uses the cached order book from `_state.GetOrderBook()` without checking how old that data is. If the WebSocket feed is delayed or was recently reconnected, the order book could be seconds or minutes old.

**Evidence:**
```csharp
public async Task<RespSendTx> CreateMarketOrderAsync(...)
{
    var orderBook = _state.GetOrderBook(request.MarketIndex);
    if (orderBook == null)
    {
        throw new LighterApiException(...);
    }

    // NO TIMESTAMP CHECK! Order book could be minutes old.
    decimal idealPrice;
    if (request.IsAsk)
    {
        idealPrice = orderBook.BestBidPrice; // Could be stale
    }
    // ...
}
```

**Fix:**
```csharp
public async Task<RespSendTx> CreateMarketOrderAsync(...)
{
    var orderBook = _state.GetOrderBook(request.MarketIndex);
    if (orderBook == null)
    {
        throw new LighterApiException(...);
    }

    // CRITICAL: Validate order book freshness
    var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - orderBook.Timestamp;
    if (ageMs > MaxOrderBookAgeMs) // e.g., 500ms
    {
        throw new LighterApiException(
            $"Order book is stale ({ageMs}ms old). Cannot execute market order safely.");
    }
    // ...
}
```

**Verdict:** FAIL

---

## FINDING 10: Memory Allocation in Hot Path

**Risk Level:** LOW
**Category:** Performance | Memory
**Location:** `LighterWebSocketClient.cs:403-406` (SendMessageAsync)
**Financial Impact:** N/A
**Performance Impact:** Allocations cause GC pressure. In high-frequency scenarios, could add microseconds of latency due to GC pauses.

**Problem:**
Every message send allocates a new byte array via `Encoding.UTF8.GetBytes()`. For high-frequency trading, this creates GC pressure.

**Evidence:**
```csharp
private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
{
    var json = JsonSerializer.Serialize(message, LighterJsonOptions.Default); // String allocation
    var bytes = Encoding.UTF8.GetBytes(json); // Byte array allocation
    // ...
}
```

**Fix:**
```csharp
// Use ArrayPool for byte buffers
private static readonly ArrayPool<byte> _bytePool = ArrayPool<byte>.Shared;

private async Task SendMessageAsync(object message, CancellationToken cancellationToken)
{
    using var ms = new PooledMemoryStream(); // Or use recyclable memory stream
    await JsonSerializer.SerializeAsync(ms, message, LighterJsonOptions.Default, cancellationToken);

    var length = (int)ms.Length;
    var buffer = _bytePool.Rent(length);
    try
    {
        ms.Position = 0;
        ms.Read(buffer, 0, length);

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(buffer, 0, length),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
    finally
    {
        _bytePool.Return(buffer);
    }
}
```

**Verdict:** CONDITIONAL PASS (Acceptable for current volume, optimize if HFT)

---

## ADDITIONAL OBSERVATIONS

### Positive Findings

1. **Thread-Safe Nonce Management**: `SignerClient.GetNextNonce()` uses proper locking.
2. **Bounded Channels**: Using `BoundedChannelOptions` with `DropOldest` prevents memory exhaustion.
3. **Auth Token Refresh**: Proactive refresh 60 seconds before expiry prevents auth failures.
4. **CancellationToken Support**: Properly threaded throughout async operations.
5. **Request ID Uniqueness**: GUID-based IDs are practically collision-free.

### Areas Requiring Manual Review

1. **Native Library Thread Safety**: `NativeMethods` calls should be verified for thread safety.
2. **Exact Error Code Semantics**: Need to verify what exchange returns for partial batch execution.
3. **Order Book Timestamp Field**: Verify `OrderBookSnapshot` has timestamp populated from WebSocket feed.

---

## AUDIT SUMMARY

```
======================================================================
AUDIT SUMMARY
======================================================================
Total Findings: 10
+-- HIGH Risk: 5 (BLOCKING)
|   - FINDING 1: Pending requests not canceled on disconnect
|   - FINDING 2: Transaction timeout leads to unknown state
|   - FINDING 5: Batch partial execution not detected
|   - FINDING 6: Messages silently dropped when WS not open
|   - FINDING 9: Stale order book used for market orders
+-- MEDIUM Risk: 3
|   - FINDING 3: No defensive check for duplicate request IDs
|   - FINDING 4: Nonce retry ineffective in WS-only mode
|   - FINDING 8: 30-second timeout may be too long
+-- LOW Risk: 2
    - FINDING 7: Response handler swallows errors
    - FINDING 10: Memory allocation in hot path
======================================================================

Overall Verdict: FAIL

Deployment Recommendation: DO NOT DEPLOY TO PRODUCTION
======================================================================
```

---

## REQUIRED ACTIONS BEFORE PRODUCTION

### Critical (Must Fix)

1. **Cancel pending requests on disconnect** - Prevents 30-second stalls and potential duplicate orders
2. **Handle unknown transaction state** - Add explicit handling for timeout vs failure
3. **Detect batch partial execution** - Compare tx_hashes count to submitted count
4. **Throw exception when WebSocket not open** - Don't silently drop messages
5. **Validate order book freshness** - Add timestamp check before market order execution

### Important (Should Fix)

6. **Improve nonce sync for WS-only mode** - Either add REST fallback or parse expected nonce from error
7. **Add configurable timeouts** - Different operations need different timeout strategies

### Optional (Nice to Have)

8. **Reduce memory allocations** - Use ArrayPool for byte buffers
9. **Complete TCS on deserialization error** - Better error handling for malformed responses

---

## TESTING RECOMMENDATIONS

Before production deployment, implement these test scenarios:

1. **Disconnect during transaction**: Kill network while order is in flight
2. **Reconnect with pending requests**: Verify old requests are properly canceled
3. **Batch partial execution**: Mock exchange returning fewer tx_hashes than submitted
4. **Stale order book**: Pause WebSocket, submit market order, verify rejection
5. **Concurrent requests**: Submit 100 orders simultaneously, verify no response mismatch
6. **Timeout handling**: Mock exchange that never responds, verify graceful timeout
7. **Nonce desync**: Manually desync nonce, verify retry behavior

---

*Report generated by Trading Systems Auditor*
