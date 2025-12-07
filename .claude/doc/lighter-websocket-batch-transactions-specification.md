# Lighter DEX WebSocket Batch Transaction Specification

## Overview

This document specifies how to submit batch transactions via WebSocket on Lighter DEX. The WebSocket API provides an alternative to REST for transaction submission with potentially lower latency.

## Key Finding: WebSocket Transaction Support

**YES, transactions CAN be submitted via WebSocket**, not just via REST. The Lighter API supports two WebSocket message types for transaction submission:

1. **Single Transaction**: `jsonapi/sendtx`
2. **Batch Transaction**: `jsonapi/sendtxbatch`

## 1. WebSocket Batch Transaction Message Format

### Message Structure

```json
{
  "type": "jsonapi/sendtxbatch",
  "data": {
    "id": "unique_request_id_123",
    "tx_types": "[1,1,5]",
    "tx_infos": "[{...},{...},{...}]"
  }
}
```

### Field Details

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | Must be `"jsonapi/sendtxbatch"` |
| `data.id` | string | No | Client-provided request ID for response correlation |
| `data.tx_types` | string | Yes | JSON-encoded array of transaction type integers |
| `data.tx_infos` | string | Yes | JSON-encoded array of transaction info objects |

### CRITICAL: JSON Encoding Requirements

Both `tx_types` and `tx_infos` are **double-encoded**:
- The outer message is JSON
- `tx_types` and `tx_infos` are JSON strings containing JSON arrays

Example in Python:
```python
await ws_client.send(
    json.dumps({
        "type": "jsonapi/sendtxbatch",
        "data": {
            "id": f"batch_{uuid.uuid4()}",
            "tx_types": json.dumps(tx_types),    # Note: json.dumps on an array
            "tx_infos": json.dumps(tx_infos),    # Note: json.dumps on an array
        },
    })
)
```

Example in C#:
```csharp
var message = new
{
    type = "jsonapi/sendtxbatch",
    data = new
    {
        id = $"batch_{Guid.NewGuid()}",
        tx_types = JsonSerializer.Serialize(txTypes),  // Array -> JSON string
        tx_infos = JsonSerializer.Serialize(txInfos),  // Array -> JSON string
    }
};
await SendMessageAsync(JsonSerializer.Serialize(message));
```

## 2. Single Transaction Message Format (for reference)

```json
{
  "type": "jsonapi/sendtx",
  "data": {
    "id": "unique_request_id_456",
    "tx_type": 1,
    "tx_info": {...}
  }
}
```

**Note**: For single transactions, `tx_type` is an integer and `tx_info` is an object (not double-encoded).

## 3. Transaction Types (tx_types)

Transaction types are uint8 values returned by the native signing library. Based on analysis of the signing functions:

| Sign Method | Description | Notes |
|-------------|-------------|-------|
| `SignCreateOrder` | Create new order | Limit, Market, Stop-Loss, Take-Profit, etc. |
| `SignCancelOrder` | Cancel single order | By market and order index |
| `SignCancelAllOrders` | Cancel all orders | Across ALL markets |
| `SignModifyOrder` | Modify existing order | Update price/size/trigger |
| `SignTransfer` | Transfer funds | Between accounts |
| `SignWithdraw` | Withdraw funds | From exchange |
| `SignCreateSubAccount` | Create sub-account | |
| `SignUpdateLeverage` | Update leverage | Initial margin fraction |
| `SignUpdateMargin` | Update margin | Add/remove collateral |
| `SignCreateGroupedOrders` | Create multiple orders | With grouping (OCO, etc.) |
| `SignCreatePublicPool` | Create liquidity pool | |
| `SignUpdatePublicPool` | Update liquidity pool | |
| `SignMintShares` | Mint pool shares | |
| `SignBurnShares` | Burn pool shares | |
| `SignChangePubKey` | Change API key | |

**IMPORTANT**: The exact numeric values are determined by the native signer library and returned in `SignedTxResponse.TxType`. Do NOT hardcode these values - always use the value returned by the signing operation.

## 4. Transaction Info (tx_infos) Format

Each `tx_info` is a JSON object returned by the signing operation. The structure varies by transaction type.

### Example: Create Order tx_info

```json
{
  "market_index": 0,
  "client_order_index": 1001,
  "base_amount": "1000",
  "price": "405000",
  "is_ask": true,
  "order_type": 0,
  "time_in_force": 1,
  "reduce_only": false,
  "trigger_price": "0",
  "order_expiry": -1,
  "nonce": 123456789,
  "api_key_index": 0,
  "account_index": 12345,
  "signature": "0x..."
}
```

### Example: Cancel Order tx_info

```json
{
  "market_index": 0,
  "order_index": 1001,
  "nonce": 123456790,
  "api_key_index": 0,
  "account_index": 12345,
  "signature": "0x..."
}
```

## 5. Batch Transaction Constraints

### All Transactions Must Use Same API Key

**CRITICAL**: All transactions in a batch MUST originate from the same API key. The nonce manager increments for each transaction while maintaining the same `api_key_index`.

```python
# Correct - same api_key_index for all
nonce = client.nonce_manager.next_nonce(api_key_index)
tx1_type, tx1_info, tx1_hash, _ = client.sign_create_order(..., api_key_index=0)

nonce = client.nonce_manager.next_nonce(api_key_index)
tx2_type, tx2_info, tx2_hash, _ = client.sign_cancel_order(..., api_key_index=0)
```

### Maximum Batch Size

While the user mentioned 50 transactions, I could not find explicit documentation confirming this limit. The batch size limit should be tested empirically or confirmed with Lighter support.

**Recommended approach**: Start with smaller batches (10-20 transactions) and increase gradually while monitoring for errors.

### Nonce Requirements

Each transaction in the batch requires a unique, incrementing nonce:
- Nonces must be sequential
- No gaps allowed
- Cannot reuse nonces

## 6. Response Format

### Expected Response Structure

Based on the REST API response format (`RespSendTxBatch`), the WebSocket response should include:

```json
{
  "type": "jsonapi/sendtxbatch/response",
  "data": {
    "code": 200,
    "message": "Success",
    "tx_hashes": "0xabc123...,0xdef456...,0xghi789...",
    "predicted_execution_time_ms": "50,52,55"
  }
}
```

| Field | Type | Description |
|-------|------|-------------|
| `code` | int | 200 = success |
| `message` | string | "Success" or error description |
| `tx_hashes` | string | Comma-separated transaction hashes |
| `predicted_execution_time_ms` | string | Comma-separated execution times |

### Error Response

```json
{
  "type": "error",
  "code": 400,
  "message": "Invalid transaction format"
}
```

## 7. Complete Example: Batch Order Creation and Cancellation

### Python Example (from official SDK)

```python
import json
import asyncio
import websockets
import lighter

async def send_batch():
    # Setup
    client = lighter.SignerClient(
        url="https://mainnet.zklighter.elliot.ai",
        account_index=12345,
        api_private_keys={0: "your_private_key"}
    )

    api_key_index = 0
    tx_types = []
    tx_infos = []
    tx_hashes = []

    # Transaction 1: Create buy order
    nonce = client.nonce_manager.next_nonce(api_key_index)
    tx_type, tx_info, tx_hash, err = client.sign_create_order(
        market_index=0,
        client_order_index=1001,
        base_amount=1000,
        price=4050_00,
        is_ask=False,  # Buy
        order_type=client.ORDER_TYPE_LIMIT,
        time_in_force=client.ORDER_TIME_IN_FORCE_GOOD_TILL_TIME,
        nonce=nonce,
        api_key_index=api_key_index
    )
    if err:
        raise Exception(f"Sign error: {err}")

    tx_types.append(tx_type)
    tx_infos.append(json.loads(tx_info))  # Parse to object
    tx_hashes.append(tx_hash)

    # Transaction 2: Create sell order
    nonce = client.nonce_manager.next_nonce(api_key_index)
    tx_type, tx_info, tx_hash, err = client.sign_create_order(
        market_index=0,
        client_order_index=1002,
        base_amount=1000,
        price=4100_00,
        is_ask=True,  # Sell
        order_type=client.ORDER_TYPE_LIMIT,
        time_in_force=client.ORDER_TIME_IN_FORCE_GOOD_TILL_TIME,
        nonce=nonce,
        api_key_index=api_key_index
    )
    if err:
        raise Exception(f"Sign error: {err}")

    tx_types.append(tx_type)
    tx_infos.append(json.loads(tx_info))
    tx_hashes.append(tx_hash)

    # Send via WebSocket
    ws_url = "wss://mainnet.zklighter.elliot.ai/stream"
    async with websockets.connect(ws_url) as ws:
        await ws.send(json.dumps({
            "type": "jsonapi/sendtxbatch",
            "data": {
                "id": "my_batch_123",
                "tx_types": json.dumps(tx_types),
                "tx_infos": json.dumps(tx_infos),
            }
        }))

        response = await ws.recv()
        print(f"Response: {response}")
        print(f"Expected hashes: {tx_hashes}")

asyncio.run(send_batch())
```

## 8. C# Implementation Guidance

### Required WebSocket Message Models

```csharp
// Models/WebSocket/SendTxBatchMessage.cs
public sealed record SendTxBatchMessage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "jsonapi/sendtxbatch";

    [JsonPropertyName("data")]
    public required SendTxBatchData Data { get; init; }
}

public sealed record SendTxBatchData
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// JSON-encoded array of transaction types.
    /// </summary>
    [JsonPropertyName("tx_types")]
    public required string TxTypes { get; init; }

    /// <summary>
    /// JSON-encoded array of transaction info objects.
    /// </summary>
    [JsonPropertyName("tx_infos")]
    public required string TxInfos { get; init; }
}
```

### Interface Extension

```csharp
// ILighterWebSocketClient.cs - Add methods:

/// <summary>
/// Sends a single transaction via WebSocket.
/// </summary>
Task<SendTxResponse> SendTransactionAsync(
    int txType,
    string txInfo,
    CancellationToken cancellationToken = default);

/// <summary>
/// Sends a batch of transactions via WebSocket.
/// </summary>
Task<SendTxBatchResponse> SendTransactionBatchAsync(
    int[] txTypes,
    string[] txInfos,
    CancellationToken cancellationToken = default);
```

### Implementation Pattern

```csharp
// LighterWebSocketClient.cs

public async Task<SendTxBatchResponse> SendTransactionBatchAsync(
    int[] txTypes,
    string[] txInfos,
    CancellationToken cancellationToken = default)
{
    ObjectDisposedException.ThrowIf(_disposed, this);

    if (txTypes.Length != txInfos.Length)
        throw new ArgumentException("Arrays must have equal length");

    if (txTypes.Length == 0)
        throw new ArgumentException("At least one transaction required");

    var requestId = $"batch_{Guid.NewGuid():N}";

    var message = new SendTxBatchMessage
    {
        Data = new SendTxBatchData
        {
            Id = requestId,
            TxTypes = JsonSerializer.Serialize(txTypes),
            TxInfos = JsonSerializer.Serialize(
                txInfos.Select(i => JsonSerializer.Deserialize<JsonElement>(i))
            )
        }
    };

    var tcs = new TaskCompletionSource<SendTxBatchResponse>();

    // Register pending request
    _pendingBatchRequests[requestId] = tcs;

    try
    {
        await SendMessageAsync(message, cancellationToken);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        return await tcs.Task.WaitAsync(cts.Token);
    }
    finally
    {
        _pendingBatchRequests.TryRemove(requestId, out _);
    }
}
```

## 9. WebSocket vs REST Comparison

| Aspect | REST | WebSocket |
|--------|------|-----------|
| Endpoint | POST `/api/v1/sendTxBatch` | Message type `jsonapi/sendtxbatch` |
| Connection | New TCP per request | Persistent connection |
| Latency | Higher (connection overhead) | Lower (reused connection) |
| Format | Form-data or JSON body | JSON message |
| tx_types | Comma-separated string | JSON-encoded array string |
| tx_infos | Comma-separated string | JSON-encoded array string |
| Response | HTTP response body | WebSocket message |

### Current REST Implementation (for reference)

```csharp
// LighterCommandClient.cs:481-486
var request = new
{
    TxTypes = string.Join(",", txTypes),  // REST: comma-separated
    TxInfos = string.Join(",", txInfos)   // REST: comma-separated
};
```

### WebSocket Format Difference

```csharp
// WebSocket: JSON-encoded arrays
var data = new
{
    tx_types = JsonSerializer.Serialize(txTypes),  // WS: JSON array string
    tx_infos = JsonSerializer.Serialize(txInfoObjects)  // WS: JSON array string
};
```

## 10. Error Handling Recommendations

### Common Errors

| Error Code | Cause | Recovery |
|------------|-------|----------|
| 400 | Invalid format | Check double-encoding of tx_types/tx_infos |
| 401 | Authentication failed | Refresh auth token |
| 403 | API key mismatch | All tx in batch must use same key |
| 429 | Rate limited | Implement exponential backoff |
| 500 | Server error | Retry with exponential backoff |

### Nonce Errors

If a transaction fails due to nonce issues:
1. Fetch current nonce from server
2. Reset local nonce manager
3. Re-sign all transactions with corrected nonces

## 11. Limitations and Uncertainties

### Not Confirmed

1. **Exact batch size limit**: Documentation mentions potential 50 tx limit but not confirmed
2. **Response message type**: Exact WebSocket response `type` field value
3. **Numeric tx_type values**: These are internal to the native signer library

### Recommendations

1. **Test incrementally**: Start with 5-10 transactions per batch
2. **Implement fallback**: Use REST if WebSocket fails
3. **Monitor latency**: Track performance difference vs REST
4. **Handle partial failures**: Some transactions may succeed while others fail

## 12. Order Type and Time-in-Force Constants

From the official SDK:

```python
# Order Types
ORDER_TYPE_LIMIT = 0
ORDER_TYPE_MARKET = 1
ORDER_TYPE_STOP_LOSS = 2
ORDER_TYPE_STOP_LOSS_LIMIT = 3
ORDER_TYPE_TAKE_PROFIT = 4
ORDER_TYPE_TAKE_PROFIT_LIMIT = 5
ORDER_TYPE_TWAP = 6

# Time in Force
ORDER_TIME_IN_FORCE_IMMEDIATE_OR_CANCEL = 0  # IOC
ORDER_TIME_IN_FORCE_GOOD_TILL_TIME = 1       # GTC
ORDER_TIME_IN_FORCE_POST_ONLY = 2            # Post-Only
```

---

## Summary

WebSocket batch transactions are supported via `jsonapi/sendtxbatch` message type. Key implementation points:

1. **Double-encode** `tx_types` and `tx_infos` as JSON strings
2. **Same API key** for all transactions in a batch
3. **Sequential nonces** for each transaction
4. **tx_type values** come from native signer - do not hardcode
5. **Response correlation** via `id` field
6. **Format differs from REST** - arrays vs comma-separated strings
