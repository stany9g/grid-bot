# Lighter DEX REST API sendTxBatch Format Specification

## Research Summary

This document details the exact format requirements for the Lighter DEX `sendTxBatch` REST endpoint based on official documentation, Python SDK analysis, and WebSocket API reference.

## Endpoint Details

- **URL**: `POST https://mainnet.zklighter.elliot.ai/api/v1/sendTxBatch`
- **Content-Type**: `multipart/form-data`
- **Parameters**: `tx_types` (string), `tx_infos` (string)
- **Max batch size**: 50 transactions

## Current Error

```json
{"code":21501,"message":"invalid tx info"}
```

The error code `21501` indicates the `tx_info` structure is not being correctly parsed by the server.

---

## 1. Parameter Format: tx_types

### Answer to Question 1: What is the exact string format for tx_types?

Based on the API documentation and Python SDK:

**Format**: JSON array as string - `"[14,14,14]"`

The Python SDK uses:
```python
_form_params.append(('tx_types', tx_types))
```

Where `tx_types` is already a **string representation** of a JSON array of integers.

**Your current format `"[14,14,14,14,14,14,14,14]"` appears CORRECT.**

---

## 2. Parameter Format: tx_infos

### Answer to Question 2: What is the exact string format for tx_infos?

**Format**: JSON array of objects as string - `"[{...},{...},{...}]"`

The Python SDK sends this as a form parameter where each tx_info object is a complete JSON object.

**Your current format (JSON array) appears CORRECT in structure.**

---

## 3. Property Names in tx_info

### Answer to Question 3: Should property names be snake_case or PascalCase?

**CRITICAL FINDING: PascalCase is required**

From the official WebSocket documentation and transaction examples:

```json
{
  "hash": "0xabc123456789def",
  "type": 15,
  "info": "{\"AccountIndex\":1,\"ApiKeyIndex\":2,\"MarketIndex\":3,\"Index\":404,\"ExpiredAt\":1700000000000,\"Nonce\":1234,\"Sig\":\"0xsigexample\"}"
}
```

The `info` field (which is `tx_info`) uses **PascalCase** property names:
- `AccountIndex` (NOT `account_index`)
- `ApiKeyIndex` (NOT `api_key_index`)
- `MarketIndex` (NOT `market_index`)
- `ClientOrderIndex` (NOT `client_order_index`)
- `BaseAmount` (NOT `base_amount`)
- `Price` (NOT `price`)
- `IsAsk` (NOT `is_ask`)
- `Type` (NOT `type` or `order_type`)
- `TimeInForce` (NOT `time_in_force`)
- `ReduceOnly` (NOT `reduce_only`)
- `TriggerPrice` (NOT `trigger_price`)
- `OrderExpiry` (NOT `order_expiry` or `expired_at`)
- `ExpiredAt` (NOT `expired_at`)
- `Nonce` (NOT `nonce`)
- `Sig` (NOT `sig` or `signature`)

---

## 4. Python SDK REST Batch Example

### Answer to Question 4: Is there a Python SDK example for REST batch (not WebSocket)?

**YES** - The file `examples/send_batch_tx_http.py` exists in the official SDK.

Based on available documentation, the pattern is:

```python
import json
from lighter import SignerClient
from lighter.api.transaction_api import TransactionApi

# Initialize client
client = SignerClient(...)
api = TransactionApi(...)

# Sign multiple orders
tx_types = []
tx_infos = []

# Sign order 1
nonce = client.nonce_manager.next_nonce(api_key_index)
tx_type, tx_info, tx_hash, err = client.sign_create_order(
    market_index=0,
    client_order_index=1001,
    base_amount=1000,
    price=500000,
    is_ask=True,
    order_type=client.ORDER_TYPE_LIMIT,
    time_in_force=client.ORDER_TIME_IN_FORCE_GOOD_TILL_TIME,
    reduce_only=False,
    trigger_price=0,
    nonce=nonce,
    api_key_index=api_key_index
)
tx_types.append(tx_type)
tx_infos.append(json.loads(tx_info))  # Parse JSON string to object

# Sign order 2 (similar)...

# Send batch via REST
response = await api.send_tx_batch(
    tx_types=json.dumps(tx_types),      # "[14,14]"
    tx_infos=json.dumps(tx_infos)       # "[{...},{...}]"
)
```

---

## 5. The Likely Root Cause of Your Error

Based on your current implementation:

```json
{"AccountIndex":293,"ApiKeyIndex":5,"MarketIndex":1,"ClientOrderIndex":51347255250011,"BaseAmount":98,"Price":2312520,"IsAsk":0,"Type":0,"TimeInForce":2,"ReduceOnly":0,"TriggerPrice":0,"OrderExpiry":1767553927012,"ExpiredAt":1765135326012,"Nonce":179,"Sig":"base64..."}
```

**Potential Issues:**

### Issue 1: Missing or Incorrect `Sig` Field Format
The signature field `Sig` should be a hex string starting with `0x`, not base64.

Example from documentation:
```json
"Sig": "0x..."
```

### Issue 2: Possible Field Mismatches
Check if these fields match what the native signer produces:
- `ExpiredAt` vs `OrderExpiry` - the native library might use different field names
- `Type` vs `OrderType` - verify the exact field name

### Issue 3: Numeric vs Boolean Types
The native signer might produce booleans differently:
- `IsAsk`: Should be `0` or `1` (integer), or `true`/`false` (boolean)?
- `ReduceOnly`: Same question

---

## 6. Recommended Verification Steps

### Step 1: Debug the Native Signer Output
Add logging to see EXACTLY what the native signer produces:

```csharp
var (txInfo, error) = await _signer.CreateOrderAsync(request);
Console.WriteLine($"[DEBUG] Raw txInfo from native signer: {txInfo}");
```

### Step 2: Compare with Single Transaction
The single `sendTx` endpoint works (based on your existing code). Compare:
1. What `txInfo` looks like for a successful single transaction
2. What you're sending in the batch

### Step 3: Test with Minimal Batch
Try sending a batch with just 1 transaction to isolate the issue:

```csharp
var txTypes = new[] { TransactionTypes.CreateOrder };
var txInfos = new[] { singleTxInfo };
```

### Step 4: Verify JSON Escaping
When joining txInfos into a JSON array, ensure no double-escaping:

**Wrong**:
```
["{\"AccountIndex\":293,...}","{\"AccountIndex\":294,...}"]
```

**Correct**:
```
[{"AccountIndex":293,...},{"AccountIndex":294,...}]
```

---

## 7. Corrected C# Implementation

Based on the research, here's the likely correct implementation:

```csharp
internal async Task<RespSendTxBatch> SendTransactionBatchAsync(
    int[] txTypes,
    string[] txInfos,  // Each txInfo is a JSON string from native signer
    CancellationToken cancellationToken = default)
{
    // tx_types: JSON array of integers
    var txTypesJson = JsonSerializer.Serialize(txTypes);  // "[14,14,14]"

    // tx_infos: JSON array of objects
    // txInfos are already JSON strings, need to parse and re-serialize as array
    var txInfoObjects = txInfos.Select(json => JsonSerializer.Deserialize<JsonElement>(json));
    var txInfosJson = JsonSerializer.Serialize(txInfoObjects);  // "[{...},{...}]"

    using var formContent = new MultipartFormDataContent();
    formContent.Add(new StringContent(txTypesJson), "tx_types");
    formContent.Add(new StringContent(txInfosJson), "tx_infos");

    // ... rest of implementation
}
```

**CRITICAL**: The current implementation uses string concatenation:
```csharp
var txInfosJson = $"[{string.Join(",", txInfos)}]";
```

This assumes `txInfos` array contains JSON strings that can be directly concatenated. If the native signer returns properly formatted JSON, this should work. But if there's any encoding issue, it could produce malformed JSON.

---

## 8. Key Takeaways

| Aspect | Finding | Confidence |
|--------|---------|------------|
| `tx_types` format | JSON array as string `"[14,14,14]"` | HIGH |
| `tx_infos` format | JSON array of objects as string | HIGH |
| Property names | **PascalCase** (e.g., `AccountIndex`) | HIGH |
| Content-Type | `multipart/form-data` | CONFIRMED |
| Signature format | Hex string `0x...` (not base64) | MEDIUM |

---

## 9. Error Code Reference

| Code | Message | Likely Cause |
|------|---------|--------------|
| 21501 | invalid tx info | Malformed tx_info JSON, wrong property names, or invalid signature |
| 21104 | invalid nonce | Nonce already used or out of sequence |
| 21105 | invalid signature | Signature verification failed |

---

## 10. Debugging Recommendation

**Before changing code**, add detailed logging:

```csharp
Console.WriteLine($"=== SendTransactionBatchAsync Debug ===");
Console.WriteLine($"tx_types string: {txTypesJson}");
Console.WriteLine($"tx_infos count: {txInfos.Length}");
for (int i = 0; i < txInfos.Length; i++)
{
    Console.WriteLine($"tx_info[{i}]: {txInfos[i]}");
}
Console.WriteLine($"Final tx_infos JSON: {txInfosJson}");
Console.WriteLine($"=======================================");
```

Then compare with a successful single `sendTx` request to identify the difference.

---

## Sources

- [Lighter API Documentation](https://apidocs.lighter.xyz/reference/sendtxbatch)
- [Lighter WebSocket Reference](https://apidocs.lighter.xyz/docs/websocket-reference)
- [Lighter Python SDK](https://github.com/elliottech/lighter-python)
- [DeepWiki - Lighter Python API Reference](https://deepwiki.com/elliottech/lighter-python/4-usage-examples)
- [Lighter Docs - API](https://docs.lighter.xyz/perpetual-futures/api)
