# Extended DEX Order Hash Algorithm Investigation

## Date: 2026-01-02

## Executive Summary

**The C# implementation is using the WRONG hash algorithm.**

The current C# `StarkSigner.cs` uses the standard StarkEx perpetual order hash (5-element Pedersen hash chain), but the Extended DEX uses a **custom SNIP-12 typed domain hash** via the `fast_stark_crypto.get_order_msg_hash()` Rust library function.

## Key Finding: Domain Parameters

The Extended DEX order hash includes **SNIP-12 typed domain parameters** that are NOT present in the standard StarkEx documentation:

| Parameter | Value (Testnet) | Value (Mainnet) |
|-----------|-----------------|-----------------|
| `domain_name` | `"Perpetuals"` | `"Perpetuals"` |
| `domain_version` | `"v0"` | `"v0"` |
| `domain_chain_id` | `"SN_SEPOLIA"` | `"SN_MAIN"` |
| `domain_revision` | `"1"` | `"1"` |

Source: `x10/perpetual/configuration.py` lines 38 and 52

## Python SDK Function Signature

From `fast_stark_crypto.get_order_msg_hash()`:

```python
get_order_msg_hash(
    position_id=position_id,              # int - Account l2Vault
    base_asset_id=int(hex_id, 16),        # int - syntheticId as integer
    base_amount=synthetic_amount.value,   # int - SIGNED Stark amount
    quote_asset_id=int(hex_id, 16),       # int - collateralId as integer
    quote_amount=collateral_amount.value, # int - SIGNED Stark amount
    fee_amount=fee.value,                 # int - ALWAYS positive
    fee_asset_id=int(hex_id, 16),         # int - same as quote_asset_id
    expiration=seconds,                   # int - order_expiry + 14 days (SECONDS)
    salt=nonce,                           # int - random 32-bit nonce
    user_public_key=public_key,           # int - Stark public key
    domain_name="Perpetuals",             # str - SNIP-12 domain
    domain_version="v0",                  # str - SNIP-12 version
    domain_chain_id="SN_SEPOLIA",         # str - SNIP-12 chain ID
    domain_revision="1",                  # str - SNIP-12 revision
)
```

Source: `x10/perpetual/order_object_settlement.py` lines 68-83

## Algorithm Difference

### Current C# Implementation (WRONG)

```
Hash = Pedersen(asset_id_sell, asset_id_buy, asset_id_fee, packed_message0, packed_message1)
```

Where:
- `packed_message0` = `(amount_sell << 160) | (amount_buy << 96) | (fee << 32) | nonce`
- `packed_message1` = `(type << 241) | (pos << 177) | (pos << 113) | (pos << 49) | (exp << 17)`

This is the standard StarkEx perpetual hash from the official documentation.

### Required Implementation (Python SDK)

The Python SDK uses `fast_stark_crypto.get_order_msg_hash()` which implements a **SNIP-12 typed domain hash**. The exact algorithm is:

1. **Domain Separator Hash** using SNIP-12 revision 1 (Poseidon hash):
   ```
   domain_hash = Poseidon(
       type_hash("StarknetDomain"),
       encode("Perpetuals"),
       encode("v0"),
       encode("SN_SEPOLIA" or "SN_MAIN"),
       encode("1")
   )
   ```

2. **Order Type Hash** for the order structure

3. **Order Data Hash** including all order parameters

4. **Final Hash** combining domain, account, and order:
   ```
   final_hash = Poseidon(
       "StarkNet Message" prefix,
       domain_hash,
       user_public_key,
       order_data_hash
   )
   ```

## Sign Convention

From `order_object_settlement.py` lines 108-111:

| Order Side | base_amount (synthetic) | quote_amount (collateral) |
|------------|-------------------------|---------------------------|
| **BUY**    | POSITIVE (receive BTC)  | NEGATIVE (pay USD)        |
| **SELL**   | NEGATIVE (pay BTC)      | POSITIVE (receive USD)    |

Fee amount is **ALWAYS positive**.

## Test Case from Python SDK

From `tests/perpetual/test_order_object.py`:

**BUY 0.001 BTC @ $43,445.1168:**

```json
{
  "debuggingAmounts": {
    "collateralAmount": "-43445117",
    "feeAmount": "21723",
    "syntheticAmount": "1000"
  }
}
```

Note:
- `syntheticAmount = 1000` (positive for BUY, resolution = 1,000,000 -> 0.001 * 1000000 = 1000)
- `collateralAmount = -43445117` (negative for BUY, rounded UP)
- `feeAmount = 21723` (always positive, rounded UP)

**SELL 0.001 BTC @ $43,445.1168:**

```json
{
  "debuggingAmounts": {
    "collateralAmount": "43445116",
    "feeAmount": "21723",
    "syntheticAmount": "-1000"
  }
}
```

Note:
- `syntheticAmount = -1000` (negative for SELL)
- `collateralAmount = 43445116` (positive for SELL, rounded DOWN)

## Rounding Rules

From `x10/perpetual/amounts.py`:

| Context | Python Rounding | When Used |
|---------|-----------------|-----------|
| `ROUNDING_BUY_CONTEXT` | `ROUND_UP` | BUY orders (pay more) |
| `ROUNDING_SELL_CONTEXT` | `ROUND_DOWN` | SELL orders (receive less) |
| `ROUNDING_FEE_CONTEXT` | `ROUND_UP` | Fee calculations (always) |

## Expiration Calculation

From `order_object_settlement.py` lines 48-52:

```python
def __calc_settlement_expiration(expiration_timestamp: datetime):
    expire_time_with_buffer = expiration_timestamp + timedelta(days=14)
    expire_time_as_seconds = math.ceil(expire_time_with_buffer.timestamp())
    return expire_time_as_seconds
```

**Critical:**
1. Add **14 days** to order expiry
2. Convert to **Unix SECONDS** (not milliseconds!)
3. Use `ceil()` (round up)

## Asset Resolution

From `tests/fixtures/markets.py` (BTC-USD):

```json
"l2Config": {
    "type": "STARKX",
    "collateralId": "0x31857064564ed0ff978e687456963cba09c2c6985d8f9300a1de4962fafa054",
    "syntheticId": "0x4254432d3600000000000000000000",
    "syntheticResolution": 1000000,
    "collateralResolution": 1000000
}
```

## Why Current C# Fails

The current C# implementation at `GridBot.Extended\StarkSigner.cs`:

1. **Missing domain parameters** - Does NOT include `domain_name`, `domain_version`, `domain_chain_id`, `domain_revision`
2. **Wrong hash algorithm** - Uses standard StarkEx 5-element Pedersen, not SNIP-12 typed domain hash
3. **Missing user_public_key** - The Python SDK includes `user_public_key` in the hash computation

## Required Fix

To fix the signature:

### Option A: Port the Rust Algorithm

Port the `get_order_msg_hash` function from `fast_stark_crypto` (Rust) to C#. This requires understanding the exact SNIP-12 implementation.

### Option B: Use Native Library

Wrap the `fast_stark_crypto` Rust library as a native DLL and P/Invoke it from C#, similar to how the Python SDK uses it.

### Option C: Contact Extended Exchange

Request the exact algorithm specification from the Extended Exchange team, or access to their Rust library as a C-compatible DLL.

## Python SDK File References

| Purpose | File Path |
|---------|-----------|
| Hash computation | `x10/perpetual/order_object_settlement.py` |
| Domain configuration | `x10/perpetual/configuration.py` |
| Amount rounding | `x10/perpetual/amounts.py` |
| Asset resolution | `x10/perpetual/assets.py` |
| Market model | `x10/perpetual/markets.py` |
| Account/signing | `x10/perpetual/accounts.py` |
| Nonce generation | `x10/utils/nonce.py` |
| Test cases | `tests/perpetual/test_order_object.py` |
| Test fixtures | `tests/fixtures/markets.py`, `tests/fixtures/accounts.py` |

## External References

- [fast-stark-crypto PyPI](https://pypi.org/project/fast-stark-crypto/)
- [x10xchange/stark-crypto-wrapper-py GitHub](https://github.com/x10xchange/stark-crypto-wrapper-py)
- [SNIP-12 Specification](https://github.com/starknet-io/SNIPs/blob/main/SNIPS/snip-12.md)
- [StarkEx Documentation](https://docs.starkware.co/starkex/perpetual/signature_construction_perpetual.html)

## Conclusion

The Extended DEX does NOT use the standard StarkEx perpetual order hash. It uses a **SNIP-12 typed domain hash** that includes:

1. Domain separator (`StarknetDomain` struct with name, version, chain_id, revision)
2. User public key as part of the hash
3. Different parameter ordering and packing

The C# implementation must be completely rewritten to use the correct algorithm, or the Rust library must be wrapped and called via P/Invoke.

## Priority

**CRITICAL** - Orders will fail with "Invalid StarkEx signature" (error 1101) until the hash algorithm is corrected.
