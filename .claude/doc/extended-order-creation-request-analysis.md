# Extended DEX Order Creation Request Analysis

## Date: 2026-01-02

## Problem Statement
Order creation failing on Extended DEX mainnet. Need to compare C# `CreateOrderRequest` model with Python SDK to identify issues.

## Summary of Issues Found

### CRITICAL ISSUES

#### 1. Missing `collateralPosition` Field in Settlement Object (CRITICAL)
**C# Model (WRONG):**
```csharp
public sealed record SettlementObject
{
    [JsonPropertyName("starkKey")]
    public required string StarkKey { get; init; }

    [JsonPropertyName("r")]
    public required string R { get; init; }

    [JsonPropertyName("s")]
    public required string S { get; init; }

    [JsonPropertyName("nonce")]
    public required long Nonce { get; init; }  // WRONG: nonce is NOT in settlement

    [JsonPropertyName("collateral")]
    public string? Collateral { get; init; }  // WRONG: should be collateralPosition

    [JsonPropertyName("positionType")]
    public string? PositionType { get; init; }  // NOT NEEDED
}
```

**Python SDK Model (CORRECT) from `orders.py` line 106-109:**
```python
class StarkSettlementModel(X10BaseModel):
    signature: SettlementSignatureModel  # Contains { "r": hex, "s": hex }
    stark_key: HexValue                   # Hex string
    collateral_position: Decimal          # Vault ID (numeric)
```

**Settlement Signature Model from `model.py` line 64-66:**
```python
class SettlementSignatureModel(X10BaseModel):
    r: HexValue  # Hex string
    s: HexValue  # Hex string
```

**CRITICAL DIFFERENCE:** The signature `r` and `s` are nested inside a `signature` object, NOT at the top level of settlement!

#### 2. Wrong Signature Structure (CRITICAL)
**C# sends:**
```json
{
  "settlement": {
    "starkKey": "0x...",
    "r": "0x...",
    "s": "0x...",
    "nonce": 12345
  }
}
```

**Python SDK sends:**
```json
{
  "settlement": {
    "signature": {
      "r": "0x...",
      "s": "0x..."
    },
    "starkKey": "0x...",
    "collateralPosition": 301301
  }
}
```

#### 3. `nonce` is NOT inside Settlement (CRITICAL)
**C# puts nonce INSIDE settlement** - WRONG
**Python SDK puts nonce at TOP LEVEL of order** - CORRECT

From `NewOrderModel` (orders.py line 134-157), `nonce` is a direct property of the order, NOT inside settlement.

#### 4. Missing Required Fields at Order Level
The C# model is missing these **required** fields from `NewOrderModel`:

| Field | Python Type | Required | C# Status |
|-------|-------------|----------|-----------|
| `selfTradeProtectionLevel` | `SelfTradeProtectionLevel` | YES | **MISSING** |
| `nonce` | `Decimal` | YES | **MISSING (wrongly in settlement)** |
| `postOnly` | `bool` | YES (defaults to false) | **MISSING** |

### MODERATE ISSUES

#### 5. Field Casing Verification
Python SDK uses Pydantic with `to_camel` serialization alias, so all fields are camelCase in API:

| Python Field | JSON Field | C# Field | Status |
|--------------|-----------|----------|--------|
| `id` | `id` | `id` | OK |
| `market` | `market` | `market` | OK |
| `type` | `type` | `type` | OK |
| `side` | `side` | `side` | OK |
| `qty` | `qty` | `qty` | OK |
| `price` | `price` | `price` | OK |
| `reduce_only` | `reduceOnly` | `reduceOnly` | OK |
| `post_only` | `postOnly` | MISSING | **NEEDS FIX** |
| `time_in_force` | `timeInForce` | `timeInForce` | OK |
| `expiry_epoch_millis` | `expiryEpochMillis` | `expiryEpochMillis` | OK |
| `fee` | `fee` | `fee` | OK |
| `nonce` | `nonce` | MISSING (in wrong place) | **NEEDS FIX** |
| `self_trade_protection_level` | `selfTradeProtectionLevel` | MISSING | **NEEDS FIX** |
| `settlement` | `settlement` | `settlement` | Structure wrong |

#### 6. Type Values Case Sensitivity
**C# uses lowercase types:**
```csharp
Type = "limit"
```

**Python SDK uses uppercase enums:**
```python
class OrderType(StrEnum):
    LIMIT = "LIMIT"
    CONDITIONAL = "CONDITIONAL"
    MARKET = "MARKET"
    TPSL = "TPSL"
```

The API expects `"LIMIT"`, not `"limit"`.

#### 7. Optional vs Required Fields
Python SDK `NewOrderModel` has these default values:
- `reduce_only: bool = False`
- `post_only: bool = False`
- `cancel_id: Optional[str] = None`
- `settlement: Optional[StarkSettlementModel] = None` (but required for authenticated trading!)
- `trigger: Optional[...] = None`
- `tp_sl_type: Optional[...] = None`
- `take_profit: Optional[...] = None`
- `stop_loss: Optional[...] = None`
- `debugging_amounts: Optional[...] = None`
- `builderFee: Optional[Decimal] = None`
- `builderId: Optional[int] = None`

## Correct CreateOrderRequest Structure

### Python SDK Final JSON (from `to_api_request_json`):
```json
{
  "id": "123456789012345678",
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
      "r": "0x1234567890abcdef...",
      "s": "0x1234567890abcdef..."
    },
    "starkKey": "0x1234567890abcdef...",
    "collateralPosition": "301301"
  }
}
```

### Corrected C# Model:

```csharp
public sealed record CreateOrderRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("market")]
    public required string Market { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }  // "LIMIT", "CONDITIONAL", "MARKET", "TPSL"

    [JsonPropertyName("side")]
    public required string Side { get; init; }  // "BUY" or "SELL"

    [JsonPropertyName("qty")]
    public required string Qty { get; init; }

    [JsonPropertyName("price")]
    public required string Price { get; init; }

    [JsonPropertyName("reduceOnly")]
    public bool ReduceOnly { get; init; } = false;

    [JsonPropertyName("postOnly")]
    public bool PostOnly { get; init; } = false;

    [JsonPropertyName("timeInForce")]
    public required string TimeInForce { get; init; }  // "GTT", "IOC" (FOK not allowed for new orders)

    [JsonPropertyName("expiryEpochMillis")]
    public required long ExpiryEpochMillis { get; init; }

    [JsonPropertyName("fee")]
    public required string Fee { get; init; }

    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }  // MOVED FROM settlement to here!

    [JsonPropertyName("selfTradeProtectionLevel")]
    public required string SelfTradeProtectionLevel { get; init; }  // "ACCOUNT", "CLIENT", "DISABLED"

    [JsonPropertyName("settlement")]
    public required SettlementObject Settlement { get; init; }

    // Optional fields
    [JsonPropertyName("cancelId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CancelId { get; init; }

    [JsonPropertyName("builderFee")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BuilderFee { get; init; }

    [JsonPropertyName("builderId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BuilderId { get; init; }
}

public sealed record SettlementObject
{
    [JsonPropertyName("signature")]
    public required SignatureObject Signature { get; init; }  // NESTED signature object!

    [JsonPropertyName("starkKey")]
    public required string StarkKey { get; init; }

    [JsonPropertyName("collateralPosition")]
    public required string CollateralPosition { get; init; }  // Vault ID as string
}

public sealed record SignatureObject
{
    [JsonPropertyName("r")]
    public required string R { get; init; }  // Hex string "0x..."

    [JsonPropertyName("s")]
    public required string S { get; init; }  // Hex string "0x..."
}
```

## Key Differences Summary

| Issue | C# Current | Python SDK (Correct) | Severity |
|-------|-----------|---------------------|----------|
| Signature structure | `r`, `s` at settlement root | Nested in `signature` object | CRITICAL |
| `nonce` location | Inside settlement | At order root level | CRITICAL |
| `collateralPosition` | Missing (has `collateral` and `positionType`) | Required field (vault ID) | CRITICAL |
| `selfTradeProtectionLevel` | Missing | Required field | HIGH |
| `postOnly` | Missing | Required (defaults false) | MEDIUM |
| Type values | lowercase `"limit"` | UPPERCASE `"LIMIT"` | HIGH |

## Python SDK Reference Files

| File | Path |
|------|------|
| NewOrderModel | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\orders.py` |
| StarkSettlementModel | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\orders.py` (line 106-109) |
| SettlementSignatureModel | `C:\Users\stany\.claude\repos\python_sdk\x10\utils\model.py` (line 64-66) |
| Order creation | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\order_object.py` |
| Settlement calculation | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\order_object_settlement.py` |
| API call | `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\trading_client\order_management_module.py` |
| JSON serialization | `C:\Users\stany\.claude\repos\python_sdk\x10\utils\model.py` (to_api_request_json) |

## Recommended Actions

1. **IMMEDIATELY**: Fix `SettlementObject` to nest signature in a `SignatureObject`
2. **IMMEDIATELY**: Move `nonce` from settlement to order root level
3. **IMMEDIATELY**: Add `collateralPosition` (vault ID) to settlement
4. **IMMEDIATELY**: Add `selfTradeProtectionLevel` field (default to "ACCOUNT")
5. **HIGH**: Add `postOnly` field (default to false)
6. **HIGH**: Change type values to UPPERCASE ("LIMIT" not "limit")
7. **CLEANUP**: Remove `positionType` and `collateral` from settlement (not used)
