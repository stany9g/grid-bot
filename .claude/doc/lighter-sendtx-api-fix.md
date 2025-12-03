# Lighter sendTx REST API Format Fix

## Date
2025-12-03

## Issue Summary
The current C# implementation sends sendTx requests as JSON but the Lighter REST API expects **multipart/form-data**.

**Error received:**
```json
{"code":20001,"message":"invalid param: : field \"tx_type\" is not set"}
```

**Root Cause:** Content-Type mismatch. The API does not parse JSON bodies for sendTx - it expects form fields.

---

## Critical Finding: REST API Uses Form Data, NOT JSON

### Python SDK Evidence (from `lighter/api/transaction_api.py`)

```python
# The send_tx method constructs requests via multipart/form-data
def _send_tx_serialize(self, tx_type, tx_info, price_protection...):
    _form_params = []
    if tx_type is not None:
        _form_params.append(('tx_type', tx_type))
    if tx_info is not None:
        _form_params.append(('tx_info', tx_info))
    if price_protection is not None:
        _form_params.append(('price_protection', price_protection))
```

### Official Documentation (TransactionApi.md)

| Attribute | Value |
|-----------|-------|
| HTTP Method | POST |
| Endpoint | `/api/v1/sendTx` |
| **Content-Type** | **multipart/form-data** |
| Response Type | application/json |

---

## Request Parameters (Form Fields)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `tx_type` | integer | Yes | Transaction type identifier (e.g., 14 for CreateOrder) |
| `tx_info` | string | Yes | JSON string containing signed transaction data |
| `price_protection` | boolean | No | Enable price protection (defaults to True) |

### Important Notes

1. **`tx_info` is a JSON STRING, not a nested object** - The signer returns it as a serialized JSON string, and it should be sent as-is as a form field value
2. **Field names use snake_case**: `tx_type`, `tx_info`, `price_protection`
3. **No wrapper object needed** - Parameters are sent as direct form fields

---

## Current C# Implementation (BROKEN)

```csharp
// LighterCommandClient.cs - PostAsync method
// Currently sends JSON:
var json = JsonSerializer.Serialize(request, _jsonOptions);
using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
var response = await _writeHttpClient.PostAsync(endpoint, content, cancellationToken);
```

This sends:
```json
{"tx_type":14,"tx_info":{"AccountIndex":155,...},"price_protection":true}
```

But the API expects form data:
```
Content-Type: multipart/form-data

tx_type=14
tx_info={"AccountIndex":155,...}
price_protection=true
```

---

## Required Fix

### Option 1: Use MultipartFormDataContent (Recommended)

```csharp
internal async Task<RespSendTx> SendTransactionAsync(
    int txType,
    string txInfo,  // Already a JSON string from signer
    bool? priceProtection = null,
    CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(txInfo))
        throw new ArgumentException("Transaction info cannot be null or empty.", nameof(txInfo));

    using var formContent = new MultipartFormDataContent();
    formContent.Add(new StringContent(txType.ToString()), "tx_type");
    formContent.Add(new StringContent(txInfo), "tx_info");

    if (priceProtection.HasValue)
    {
        formContent.Add(new StringContent(priceProtection.Value.ToString().ToLowerInvariant()), "price_protection");
    }

    var response = await _writeHttpClient.PostAsync("sendTx", formContent, cancellationToken);
    await EnsureSuccessStatusCodeAsync(response);

    return await response.Content.ReadFromJsonAsync<RespSendTx>(_jsonOptions, cancellationToken)
        ?? throw new LighterApiException("Failed to deserialize response");
}
```

### Option 2: Use FormUrlEncodedContent

```csharp
internal async Task<RespSendTx> SendTransactionAsync(
    int txType,
    string txInfo,
    bool? priceProtection = null,
    CancellationToken cancellationToken = default)
{
    var formFields = new List<KeyValuePair<string, string>>
    {
        new("tx_type", txType.ToString()),
        new("tx_info", txInfo)  // txInfo is already a JSON string
    };

    if (priceProtection.HasValue)
    {
        formFields.Add(new("price_protection", priceProtection.Value.ToString().ToLowerInvariant()));
    }

    using var formContent = new FormUrlEncodedContent(formFields);
    var response = await _writeHttpClient.PostAsync("sendTx", formContent, cancellationToken);
    await EnsureSuccessStatusCodeAsync(response);

    return await response.Content.ReadFromJsonAsync<RespSendTx>(_jsonOptions, cancellationToken)
        ?? throw new LighterApiException("Failed to deserialize response");
}
```

---

## Key Change: Do NOT Parse txInfo

The current code incorrectly parses the txInfo string into an object:

```csharp
// WRONG - This converts the JSON string to a nested object
var txInfoObject = JsonSerializer.Deserialize<object>(txInfo);
var request = new SendTxRequest
{
    TxType = txType,
    TxInfo = txInfoObject!,  // Now an object, not a string
    PriceProtection = priceProtection
};
```

The signer already returns `txInfo` as a properly formatted JSON string. It should be sent as-is:

```csharp
// CORRECT - Use txInfo directly as a string form field
formContent.Add(new StringContent(txInfo), "tx_info");
```

---

## sendTxBatch Endpoint

The batch endpoint likely also uses form data. Based on the Python SDK pattern:

| Field | Type | Description |
|-------|------|-------------|
| `tx_types` | string | Comma-separated list of transaction types |
| `tx_infos` | string | Comma-separated JSON strings |

**Current implementation may also need fixing:**

```csharp
// Current (potentially broken):
var request = new
{
    TxTypes = string.Join(",", txTypes),
    TxInfos = string.Join(",", txInfos)
};
var response = await PostAsync<RespSendTxBatch>("sendTxBatch", request, cancellationToken);

// Fixed version:
using var formContent = new MultipartFormDataContent();
formContent.Add(new StringContent(string.Join(",", txTypes)), "tx_types");
formContent.Add(new StringContent(string.Join(",", txInfos)), "tx_infos");
var response = await _writeHttpClient.PostAsync("sendTxBatch", formContent, cancellationToken);
```

---

## Response Format

The API returns JSON responses:

```json
{
    "code": 200,
    "message": "success",
    "tx_hash": "0x...",
    "predicted_execution_time_ms": 1500
}
```

**Success code is 200**, not 0. Verify the `IsSuccess` check in `RespSendTx`:

```csharp
public bool IsSuccess => Code == 200;  // NOT Code == 0
```

---

## Transaction Types Reference

| Type | Value | Description |
|------|-------|-------------|
| CreateOrder | 14 | Create new order |
| ModifyOrder | 11 | Modify existing order |
| CancelOrder | 8 | Cancel specific order |
| CancelAllOrders | 9 | Cancel all orders in market |
| CreateGroupedOrders | 15 | Create OCO/OTO/OTOCO orders |
| UpdateLeverage | 17 | Update position leverage |

---

## Files to Modify

1. **`GridBot.Lighter/LighterCommandClient.cs`**
   - Replace `SendTransactionAsync` to use `MultipartFormDataContent`
   - Replace `SendTransactionBatchAsync` to use `MultipartFormDataContent`
   - Remove `SendTxRequest` class (no longer needed)
   - Remove the `JsonSerializer.Deserialize<object>(txInfo)` line

2. **`GridBot.Lighter/Models/Api/RespSendTx.cs`** (if needed)
   - Verify `IsSuccess` checks for `Code == 200`

---

## Testing Checklist

After implementing the fix:

1. [ ] Verify `tx_type` is sent as form field, not JSON property
2. [ ] Verify `tx_info` is sent as raw JSON string, not escaped/nested
3. [ ] Verify `price_protection` is lowercase boolean string ("true"/"false")
4. [ ] Test CreateOrder transaction
5. [ ] Test CancelOrder transaction
6. [ ] Test batch transactions
7. [ ] Verify response parsing still works (JSON response)

---

## Summary

| Aspect | Current (Broken) | Required (Fixed) |
|--------|------------------|------------------|
| Content-Type | application/json | multipart/form-data |
| tx_info format | Nested JSON object | Raw JSON string |
| Request structure | JSON body | Form fields |
| Wrapper | SendTxRequest class | Direct form fields |

The fix is straightforward: change from JSON body to form data, and keep `txInfo` as a string instead of deserializing it.

---

## Sources

- [Lighter API Reference](https://apidocs.lighter.xyz/reference/sendtx)
- [Lighter Python SDK - TransactionApi](https://github.com/elliottech/lighter-python)
- [Get Started For Programmers](https://apidocs.lighter.xyz/docs/get-started-for-programmers-1)
