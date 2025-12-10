# Lighter DEX WebSocket Transaction Format Specification

## Overview

This document describes the exact WebSocket message formats for sending transactions on Lighter DEX. The information is derived from the official Lighter API documentation and the Python SDK (`lighter-python`).

**Key Finding**: The current GridBot implementation uses HTTP POST for transaction submission (`WsLighterCommandClient.cs`). WebSocket-based transaction submission is an alternative that can be implemented using the formats documented below.

---

## 1. Single Transaction via WebSocket (`jsonapi/sendtx`)

### Message Format (Outgoing)

```json
{
    "type": "jsonapi/sendtx",
    "data": {
        "id": "my_random_id_{random_number}",
        "tx_type": INTEGER,
        "tx_info": OBJECT
    }
}
```

### Field Details

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | Must be `"jsonapi/sendtx"` |
| `data.id` | string | Yes | Unique request ID for correlation (e.g., `"my_random_id_12345678"`) |
| `data.tx_type` | integer | Yes | Transaction type from `TransactionTypes` enum |
| `data.tx_info` | object | Yes | **Parsed JSON object** (NOT a string!) - the signed transaction payload |

### Important Notes

1. **`tx_info` is a parsed JSON object**, not a stringified JSON. The Python SDK explicitly calls `json.loads(tx_info)` before sending.
2. **`tx_hash` is NOT included in the request**. It is used client-side for verification but not transmitted.
3. The `id` field allows correlating responses with requests.

### Example (Create Order)

```json
{
    "type": "jsonapi/sendtx",
    "data": {
        "id": "my_random_id_87654321",
        "tx_type": 1,
        "tx_info": {
            "account_index": 123,
            "api_key_index": 0,
            "market_index": 0,
            "client_order_index": 1,
            "base_amount": "10000",
            "price": "450000",
            "is_ask": false,
            "order_type": 0,
            "time_in_force": 0,
            "reduce_only": false,
            "trigger_price": "0",
            "order_expiry": -1,
            "nonce": 42,
            "signature": "0x..."
        }
    }
}
```

---

## 2. Batch Transactions via WebSocket (`jsonapi/sendtxbatch`)

### Message Format (Outgoing)

```json
{
    "type": "jsonapi/sendtxbatch",
    "data": {
        "id": "my_random_id_{random_number}",
        "tx_types": "[INTEGER, INTEGER, ...]",
        "tx_infos": "[{...}, {...}, ...]"
    }
}
```

### Field Details

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | string | Yes | Must be `"jsonapi/sendtxbatch"` |
| `data.id` | string | Yes | Unique request ID for correlation |
| `data.tx_types` | string | Yes | **JSON stringified array** of transaction type integers |
| `data.tx_infos` | string | Yes | **JSON stringified array** of transaction info objects |

### Critical Differences from Single TX

1. **Both `tx_types` and `tx_infos` are JSON stringified strings**, NOT native arrays/objects.
2. The Python SDK uses `json.dumps(tx_types)` and `json.dumps(tx_infos)`.
3. **`tx_hashes` is NOT included in the request** - used client-side for verification only.
4. **Maximum 50 transactions** per batch.
5. **All transactions in a batch must come from the same API key**.

### Example (2 Orders)

```json
{
    "type": "jsonapi/sendtxbatch",
    "data": {
        "id": "my_random_id_12345678",
        "tx_types": "[1, 1]",
        "tx_infos": "[{\"account_index\":123,\"api_key_index\":0,\"market_index\":0,\"base_amount\":\"10000\",\"price\":\"450000\",\"is_ask\":false,\"nonce\":42,\"signature\":\"0x...\"},{\"account_index\":123,\"api_key_index\":0,\"market_index\":0,\"base_amount\":\"10000\",\"price\":\"449500\",\"is_ask\":true,\"nonce\":43,\"signature\":\"0x...\"}]"
    }
}
```

---

## 3. Response Format

### Single Transaction Response

```json
{
    "type": "jsonapi/sendtx",
    "id": "my_random_id_87654321",
    "code": 200,
    "message": "Success",
    "tx_hash": "0xabc123...",
    "predicted_execution_time_ms": 150
}
```

### Batch Transaction Response

```json
{
    "type": "jsonapi/sendtxbatch",
    "id": "my_random_id_12345678",
    "code": 200,
    "message": "Success",
    "tx_hashes": "0xabc123...,0xdef456...",
    "predicted_execution_time_ms": "150,160"
}
```

**Note**: The `tx_hashes` and `predicted_execution_time_ms` fields in batch responses can be:
- Comma-separated strings
- JSON arrays
- Single values (for single-transaction batches)

The existing `RespSendTxBatch.cs` already handles this polymorphism via `JsonElement`.

---

## 4. Transaction Types Reference

From `TransactionTypes.cs`:

| Constant | Value | Description |
|----------|-------|-------------|
| `CreateOrder` | 1 | Create a new order |
| `CancelOrder` | 2 | Cancel an existing order |
| `ModifyOrder` | 3 | Modify an existing order |
| `CreateGroupedOrders` | 4 | Create multiple linked orders |
| `CancelAllOrders` | 5 | Cancel all orders |
| `UpdateLeverage` | 6 | Update position leverage |

---

## 5. tx_hash Field Clarification

**Q: Do we need to include `tx_hash` in the WebSocket message?**

**A: NO.** The `tx_hash` is:
1. **Computed client-side** by the signing process (returned alongside `tx_info` from `SignerClient`)
2. **Used for client-side verification** to match responses
3. **NOT transmitted in the request message**

The server generates and returns the transaction hash in the response.

---

## 6. Comparison: HTTP vs WebSocket Transaction Submission

### Current HTTP Implementation (WsLighterCommandClient.cs)

```csharp
// Single TX - uses multipart/form-data
using var formContent = new MultipartFormDataContent();
formContent.Add(new StringContent(txType.ToString()), "tx_type");
formContent.Add(new StringContent(txInfo), "tx_info");  // txInfo is JSON string
await _httpClient.PostAsync("sendTx", formContent, cancellationToken);

// Batch TX - uses multipart/form-data
formContent.Add(new StringContent(JsonSerializer.Serialize(txTypes)), "tx_types");
formContent.Add(new StringContent(JsonSerializer.Serialize(txInfos)), "tx_infos");
await _httpClient.PostAsync("sendTxBatch", formContent, cancellationToken);
```

### WebSocket Implementation (would be new)

```csharp
// Single TX - JSON message
var message = new {
    type = "jsonapi/sendtx",
    data = new {
        id = GenerateRequestId(),
        tx_type = txType,
        tx_info = JsonSerializer.Deserialize<JsonElement>(txInfo)  // Parse to object!
    }
};
await SendWebSocketMessageAsync(message);

// Batch TX - JSON message with stringified arrays
var message = new {
    type = "jsonapi/sendtxbatch",
    data = new {
        id = GenerateRequestId(),
        tx_types = JsonSerializer.Serialize(txTypes),  // Stringify!
        tx_infos = JsonSerializer.Serialize(txInfos)   // Stringify!
    }
};
await SendWebSocketMessageAsync(message);
```

---

## 7. Implementation Considerations

### If implementing WebSocket-based transaction submission:

1. **Add response handling** - Need to listen for responses with matching `id` field
2. **Add request correlation** - Track pending requests by `id` for timeout/error handling
3. **Consider hybrid approach** - Use WebSocket for subscriptions, HTTP for transactions (current approach is valid)
4. **Rate limits** - Same rate limits apply: sendTx and sendTxBatch have volume-based quotas

### Advantages of WebSocket TX submission:
- Lower latency (connection already established)
- Unified connection for reads and writes
- Real-time response streaming

### Advantages of current HTTP approach:
- Simpler request/response model
- Easier error handling
- No need for request correlation
- Already implemented and working

---

## 8. Sources

- [Lighter WebSocket Reference](https://apidocs.lighter.xyz/docs/websocket-reference)
- [Lighter sendTxBatch Reference](https://apidocs.lighter.xyz/reference/sendtxbatch)
- [Lighter Python SDK - utils.py](https://github.com/elliottech/lighter-python/blob/main/examples/utils.py)
- [Lighter Python SDK - send_batch_tx_ws.py](https://github.com/elliottech/lighter-python/blob/main/examples/send_batch_tx_ws.py)

---

## Summary

| Aspect | Single TX (`jsonapi/sendtx`) | Batch TX (`jsonapi/sendtxbatch`) |
|--------|------------------------------|----------------------------------|
| `tx_info` format | Parsed JSON object | Stringified JSON array |
| `tx_types` format | Integer | Stringified JSON array |
| Max transactions | 1 | 50 |
| `tx_hash` in request | NO | NO |
| `id` field | Required for correlation | Required for correlation |
| Same API key required | N/A | Yes - all TX must use same API key |
