# Extended DEX Order Hash Algorithm - Complete Specification

**Date:** 2026-01-02
**Status:** CRITICAL - This is the authoritative specification for C# implementation
**Related Session:** `.claude/sessions/context_session_x.md`

## Executive Summary

The Extended DEX uses a **SNIP-12 typed structured data hash** (similar to EIP-712 on Ethereum) implemented via **Poseidon hashing**, NOT the standard StarkEx perpetual order hash that uses Pedersen hashing with bit-packed messages.

The C# implementation must be completely rewritten to use the correct algorithm.

## Algorithm Overview

The final message hash is computed as:

```
message_hash = Poseidon(
    "StarkNet Message",   // cairo short string
    domain_hash,          // hash of StarknetDomain struct
    user_public_key,      // Felt
    order_hash            // hash of Order struct
)
```

## Step-by-Step Algorithm

### Step 1: Compute Order Type Selector

The Order type selector is computed from the type string:

```
Order_SELECTOR = starknet_keccak(
    '"Order"("position_id":"felt","base_asset_id":"AssetId",' +
    '"base_amount":"i64","quote_asset_id":"AssetId",' +
    '"quote_amount":"i64","fee_asset_id":"AssetId",' +
    '"fee_amount":"u64","expiration":"Timestamp","salt":"felt")' +
    '"PositionId"("value":"u32")"AssetId"("value":"felt")' +
    '"Timestamp"("seconds":"u64")'
)
```

Note: `starknet_keccak` is Keccak-256 with the result masked to 250 bits.

### Step 2: Compute Order Hash

```rust
order_hash = Poseidon(
    Order_SELECTOR,
    position_id,           // u32 converted to Felt
    base_asset_id,         // Felt (the hex value)
    base_amount,           // i64 converted to Felt (SIGNED!)
    quote_asset_id,        // Felt (the hex value)
    quote_amount,          // i64 converted to Felt (SIGNED!)
    fee_asset_id,          // Felt (the hex value)
    fee_amount,            // u64 converted to Felt
    expiration_seconds,    // u64 converted to Felt
    salt                   // Felt (the nonce)
)
```

### Step 3: Compute Domain Type Selector

```
Domain_SELECTOR = starknet_keccak(
    '"StarknetDomain"("name":"shortstring","version":"shortstring",' +
    '"chainId":"shortstring","revision":"shortstring")'
)
```

### Step 4: Compute Domain Hash

```rust
domain_hash = Poseidon(
    Domain_SELECTOR,
    cairo_short_string("Perpetuals"),   // "Perpetuals" as felt
    cairo_short_string("v0"),           // "v0" as felt
    cairo_short_string("SN_SEPOLIA"),   // or "SN_MAIN" for mainnet
    1                                    // revision as Felt
)
```

### Step 5: Compute Final Message Hash

```rust
message_hash = Poseidon(
    cairo_short_string("StarkNet Message"),
    domain_hash,
    user_public_key,
    order_hash
)
```

## Parameter Details

### Input Parameters to `get_order_msg_hash()`

| Parameter | Python Type | Rust Type | Description |
|-----------|-------------|-----------|-------------|
| `position_id` | int | u32 | Account's L2 vault ID (from `l2Vault`) |
| `base_asset_id` | int | Felt | Synthetic asset ID (from `l2Config.syntheticId`, converted from hex) |
| `base_amount` | int | i64 | **SIGNED** Stark amount (+BUY, -SELL) |
| `quote_asset_id` | int | Felt | Collateral asset ID (from `l2Config.collateralId`, converted from hex) |
| `quote_amount` | int | i64 | **SIGNED** Stark amount (-BUY, +SELL) |
| `fee_asset_id` | int | Felt | Same as `quote_asset_id` |
| `fee_amount` | int | u64 | Max fee (always positive) |
| `expiration` | int | u64 | Order expiry + 14 days, in SECONDS |
| `salt` | int | Felt | Nonce (random 32-bit integer) |
| `user_public_key` | int | Felt | Stark public key |
| `domain_name` | str | String | `"Perpetuals"` |
| `domain_version` | str | String | `"v0"` |
| `domain_chain_id` | str | String | `"SN_SEPOLIA"` or `"SN_MAIN"` |
| `domain_revision` | str | u32 | `"1"` (parsed as integer) |

### Sign Convention (CRITICAL)

| Order Side | base_amount (synthetic) | quote_amount (collateral) | fee_amount |
|------------|-------------------------|---------------------------|------------|
| **BUY**    | POSITIVE (receive)      | NEGATIVE (pay)            | POSITIVE   |
| **SELL**   | NEGATIVE (pay)          | POSITIVE (receive)        | POSITIVE   |

### Amount Conversion

From Python SDK `assets.py`:

```python
def convert_human_readable_to_stark_quantity(self, internal: Decimal, rounding_context: Context) -> int:
    return int(
        rounding_context.multiply(internal, Decimal(self.settlement_resolution)).to_integral(
            context=rounding_context
        )
    )
```

Formula:
```
stark_amount = int(human_amount * settlement_resolution)
```

Where `settlement_resolution` comes from market `l2Config`:
- `syntheticResolution` for base asset (typically 1,000,000 for BTC)
- `collateralResolution` for quote/fee asset (typically 1,000,000 for USD)

### Rounding Rules

| Amount Type | Order Side | Rounding |
|-------------|------------|----------|
| synthetic | BUY | ROUND_UP |
| synthetic | SELL | ROUND_DOWN |
| collateral | BUY | ROUND_UP |
| collateral | SELL | ROUND_DOWN |
| fee | ALL | ROUND_UP |

### Expiration Calculation

From Python SDK `order_object_settlement.py`:

```python
def __calc_settlement_expiration(expiration_timestamp: datetime):
    expire_time_with_buffer = expiration_timestamp + timedelta(days=14)
    expire_time_as_seconds = math.ceil(expire_time_with_buffer.timestamp())
    return expire_time_as_seconds
```

Steps:
1. Take order expiration timestamp
2. Add 14 days buffer
3. Convert to Unix timestamp in SECONDS (not milliseconds!)
4. Round UP (ceiling)

## Test Vector

From `rust-crypto-lib-base/src/lib.rs`:

### Input
```
position_id      = 100
base_asset_id    = 0x2
base_amount      = 100
quote_asset_id   = 0x1
quote_amount     = -156
fee_asset_id     = 0x1
fee_amount       = 74
expiration       = 100
salt             = 123
user_public_key  = 0x5d05989e9302dcebc74e241001e3e3ac3f4402ccf2f8e6f74b034b07ad6a904
domain_name      = "Perpetuals"
domain_version   = "v0"
domain_chain_id  = "SN_SEPOLIA"
domain_revision  = 1
```

### Expected Output
```
message_hash = 0x4de4c009e0d0c5a70a7da0e2039fb2b99f376d53496f89d9f437e736add6b48
```

## Python SDK Test Case

From `tests/perpetual/test_order_object.py`:

### BUY Order
- Quantity: 0.001 BTC
- Price: $43,445.1168
- Fee Rate: 0.0005 (0.05%)
- Nonce: 1473459052
- Vault: 10002

### Debugging Amounts (after conversion)
```json
{
  "syntheticAmount": "1000",       // 0.001 * 1,000,000 = 1000 (positive for BUY)
  "collateralAmount": "-43445117", // -(0.001 * 43445.1168 * 1,000,000) rounded up
  "feeAmount": "21723"             // 0.001 * 43445.1168 * 0.0005 * 1,000,000 rounded up
}
```

### Expected Signature (for BUY with TESTNET_CONFIG)
```json
{
  "r": "0xa55625c7d5f1b85bed22556fc805224b8363074979cf918091d9ddb1403e13",
  "s": "0x504caf634d859e643569743642ccf244434322859b2421d76f853af43ae7a46"
}
```

### Test Account
```python
private_key = "0x7a7ff6fd3cab02ccdcd4a572563f5976f8976899b03a39773795a3c486d4986"
public_key  = "0x61c5e7e8339b7d56f197f54ea91b776776690e3232313de0f2ecbd0ef76f466"
vault       = 10002
```

## C# Implementation Requirements

### Required Components

1. **Poseidon Hash Function**
   - Must use Starknet's Poseidon hash (3 inputs per round)
   - Different from standard Poseidon-128

2. **Cairo Short String Encoding**
   - Converts ASCII strings to felt values
   - Each character is placed in a byte of the felt

3. **Starknet Keccak (for selectors)**
   - Keccak-256 masked to 250 bits
   - Used to compute type selectors

4. **Signed Integer to Felt Conversion**
   - Negative i64 values must be converted correctly to felt
   - Use two's complement in the field

### Potential Libraries

1. **Nethereum.Starknet** - May have Poseidon implementation
2. **StarkSharp** - Community Starknet library for C#
3. **Custom implementation** - Port from Rust

### Current C# Issues

The current `StarkSigner.cs` implementation is COMPLETELY WRONG because it uses:

| Aspect | Current C# | Correct |
|--------|------------|---------|
| Hash function | Pedersen | Poseidon |
| Message structure | StarkEx bit-packed | SNIP-12 typed data |
| Domain | Not used | Required (Perpetuals, v0, chain, 1) |
| User public key | Not in hash | Included in hash |
| Type selectors | Not used | Required for SNIP-12 |

## Source Files Reference

### Python SDK
| File | Purpose |
|------|---------|
| `x10/perpetual/order_object_settlement.py` | `hash_order()` function, sign convention |
| `x10/perpetual/amounts.py` | Rounding contexts, StarkAmount |
| `x10/perpetual/assets.py` | `settlement_resolution` conversion |
| `x10/perpetual/configuration.py` | Domain values (Perpetuals, v0, chain) |
| `tests/perpetual/test_order_object.py` | Test vectors |
| `tests/fixtures/accounts.py` | Test private/public keys |
| `tests/fixtures/markets.py` | Test l2Config values |

### Rust Library (fast-stark-crypto)
| File | Purpose |
|------|---------|
| `stark-crypto-wrapper-py/python/fast_stark_crypto/lib.py` | Python wrapper |
| `stark-crypto-wrapper-py/src/lib.rs` | Rust binding to crypto lib |
| `rust-crypto-lib-base/src/starknet_messages.rs` | Order struct, hash implementation |
| `rust-crypto-lib-base/src/lib.rs` | Test vectors |

### GitHub Repositories
- Python SDK: https://github.com/x10xchange/python_sdk
- Crypto Wrapper: https://github.com/x10xchange/stark-crypto-wrapper-py (branch: `add_limit_orders`)
- Rust Base: https://github.com/x10xchange/rust-crypto-lib-base

## Next Steps

1. Research C# Poseidon hash implementations
2. Implement `starknet_keccak` for selector computation
3. Implement `cairo_short_string_to_felt` conversion
4. Implement the SNIP-12 message hash algorithm
5. Validate against test vectors
6. Update `StarkSigner.cs` with correct implementation

## Priority

**CRITICAL** - The current implementation will NEVER produce valid signatures. The entire hashing algorithm must be replaced.
