# Extended DEX Order Creation Analysis

## Date: 2026-01-02

## Summary

Analysis of the Python SDK to understand order creation requirements for Extended DEX.

---

## 1. Market Symbol/Name for BTC-USDC Perpetual

**Answer: `BTC-USD`**

From `markets.py` (line 91-92):
```python
class MarketModel(X10BaseModel):
    name: str  # This is the market symbol, e.g., "BTC-USD"
```

The market name format is `{BASE_ASSET}-USD` (NOT `BTC-USDC`). The collateral is always USD (internally USDC with 6 decimals).

From `configuration.py` (lines 36-37, 50-51):
```python
collateral_decimals=6,
collateral_asset_id="0x1",
```

**Important**: You must fetch available markets from `/info/markets` endpoint to get the exact market names and their configuration.

---

## 2. Minimum Order Size for BTC

**Answer: Retrieved from market data via `trading_config.min_order_size`**

From `markets.py` (lines 35-46):
```python
class TradingConfigModel(X10BaseModel):
    min_order_size: Decimal        # Minimum BTC quantity
    min_order_size_change: Decimal # Minimum step size
    min_price_change: Decimal      # Price tick size
    max_market_order_value: Decimal
    max_limit_order_value: Decimal
    max_position_value: Decimal
    max_leverage: Decimal
    # ...
```

**To get BTC minimum order size:**
1. Call `GET /info/markets`
2. Find market with `name: "BTC-USD"`
3. Read `trading_config.min_order_size`

Typical values (example):
- `min_order_size`: 0.001 BTC (subject to change)
- `min_order_size_change`: 0.001 BTC
- `min_price_change`: 0.1 USD

---

## 3. Required Fields for Creating Orders

### 3.1 Limit Order Required Fields

From `orders.py` (lines 134-157) - `NewOrderModel`:

```python
class NewOrderModel(X10BaseModel):
    id: str                           # External order ID (can be order_hash as string)
    market: str                       # "BTC-USD"
    type: OrderType                   # "LIMIT"
    side: OrderSide                   # "BUY" or "SELL"
    qty: Decimal                      # Amount in base asset (e.g., 0.001 BTC)
    price: Decimal                    # Limit price in USD
    reduce_only: bool = False
    post_only: bool = False
    time_in_force: TimeInForce        # "GTT" (Good-Till-Time) or "IOC"
    expiry_epoch_millis: int          # Expiration timestamp in milliseconds
    fee: Decimal                      # Fee rate (e.g., 0.0006 for 0.06%)
    nonce: Decimal                    # Unique nonce for signing
    self_trade_protection_level: SelfTradeProtectionLevel  # "ACCOUNT", "CLIENT", or "DISABLED"
    settlement: StarkSettlementModel  # REQUIRED - Contains signature
```

### 3.2 Market Order

**Note**: Market orders are still type `LIMIT` in Extended DEX!

From `order_object.py` (line 206):
```python
order = NewOrderModel(
    # ...
    type=OrderType.LIMIT,  # Always LIMIT, even for IOC "market" orders
    # ...
)
```

**To simulate a market order:**
- Use `time_in_force: "IOC"` (Immediate-Or-Cancel)
- Set price aggressively (high for BUY, low for SELL)
- Use the best bid/ask from order book as price

**FOK (Fill-or-Kill) is NOT supported for creating orders** - see `order_object.py` line 139:
```python
if time_in_force not in TimeInForce or time_in_force == TimeInForce.FOK:
    raise ValueError(f"Unexpected time in force value: {time_in_force}")
```

### 3.3 Settlement Model (Signature Data)

From `orders.py` (lines 106-109):
```python
class StarkSettlementModel(X10BaseModel):
    signature: SettlementSignatureModel  # { r: hex, s: hex }
    stark_key: HexValue                   # Public key as hex
    collateral_position: Decimal          # Vault ID
```

From `model.py` (lines 64-66):
```python
class SettlementSignatureModel(X10BaseModel):
    r: HexValue  # Signature r component as hex string
    s: HexValue  # Signature s component as hex string
```

---

## 4. Order Signing - Hash Calculation

### 4.1 The Signing Flow

From `order_object_settlement.py`:

1. **Convert amounts to Stark format** (lines 96-106):
   ```python
   synthetic_amount_human = HumanReadableAmount(synthetic_amount, ctx.market.synthetic_asset)
   collateral_amount_human = HumanReadableAmount(synthetic_amount * price, ctx.market.collateral_asset)
   fee_amount_human = HumanReadableAmount(total_fee * collateral_amount_human.value, ctx.market.collateral_asset)

   stark_collateral_amount = collateral_amount_human.to_stark_amount(rounding_context)
   stark_synthetic_amount = synthetic_amount_human.to_stark_amount(rounding_context)
   stark_fee_amount = fee_amount_human.to_stark_amount(ROUNDING_FEE_CONTEXT)
   ```

2. **Negate one side based on order direction** (lines 108-111):
   ```python
   if is_buying_synthetic:
       stark_collateral_amount = stark_collateral_amount.negate()  # Negative when BUYING
   else:
       stark_synthetic_amount = stark_synthetic_amount.negate()    # Negative when SELLING
   ```

3. **Calculate order hash** (lines 119-128):
   ```python
   order_hash = hash_order(
       amount_synthetic=stark_synthetic_amount,
       amount_collateral=stark_collateral_amount,
       max_fee=stark_fee_amount,
       nonce=ctx.nonce,
       position_id=ctx.collateral_position_id,
       expiration_timestamp=ctx.expire_time,
       public_key=ctx.public_key,
       starknet_domain=ctx.starknet_domain,
   )
   ```

4. **Sign the hash** (line 130):
   ```python
   (order_signature_r, order_signature_s) = ctx.signer(order_hash)
   ```

### 4.2 Hash Parameters (get_order_msg_hash)

From `order_object_settlement.py` (lines 55-83):

```python
def hash_order(...) -> int:
    return get_order_msg_hash(
        position_id=position_id,                              # Vault ID (int)
        base_asset_id=int(synthetic_asset.settlement_external_id, 16),  # e.g., BTC asset ID
        base_amount=amount_synthetic.value,                   # Signed int (negative for SELL)
        quote_asset_id=int(collateral_asset.settlement_external_id, 16), # USDC asset ID
        quote_amount=amount_collateral.value,                 # Signed int (negative for BUY)
        fee_amount=max_fee.value,                            # Always positive
        fee_asset_id=int(collateral_asset.settlement_external_id, 16),  # Same as quote
        expiration=calc_settlement_expiration(expiration_timestamp),    # Seconds since epoch
        salt=nonce,                                          # Random nonce
        user_public_key=public_key,                          # Stark public key
        domain_name=starknet_domain.name,                    # "Perpetuals"
        domain_version=starknet_domain.version,              # "v0"
        domain_chain_id=starknet_domain.chain_id,            # "SN_SEPOLIA" or "SN_MAIN"
        domain_revision=starknet_domain.revision,            # "1"
    )
```

### 4.3 Starknet Domain Configuration

From `configuration.py` (lines 38, 52):

**Testnet:**
```python
starknet_domain=StarknetDomain(
    name="Perpetuals",
    version="v0",
    chain_id="SN_SEPOLIA",
    revision="1"
)
```

**Mainnet:**
```python
starknet_domain=StarknetDomain(
    name="Perpetuals",
    version="v0",
    chain_id="SN_MAIN",
    revision="1"
)
```

### 4.4 Settlement Expiration Calculation

From `order_object_settlement.py` (lines 48-52):
```python
def __calc_settlement_expiration(expiration_timestamp: datetime):
    expire_time_with_buffer = expiration_timestamp + timedelta(days=14)
    expire_time_as_seconds = math.ceil(expire_time_with_buffer.timestamp())
    return expire_time_as_seconds
```

**Important**: The settlement expiration is 14 days AFTER the order expiration!

---

## 5. Amount Conversion (StarkAmount)

From `assets.py` (lines 22-27):
```python
def convert_human_readable_to_stark_quantity(self, internal: Decimal, rounding_context: Context) -> int:
    return int(
        rounding_context.multiply(internal, Decimal(self.settlement_resolution)).to_integral(
            context=rounding_context
        )
    )
```

**Formula**: `stark_amount = human_amount * settlement_resolution`

The `settlement_resolution` comes from market data in `l2_config`:
- `l2_config.synthetic_resolution` for base asset (BTC)
- `l2_config.collateral_resolution` for quote asset (USD)

---

## 6. Signing Function

From `accounts.py` (lines 55-56):
```python
def sign(self, msg_hash: int) -> Tuple[int, int]:
    return sign(private_key=self.__private_key, msg_hash=msg_hash)
```

Uses `fast_stark_crypto.sign()` which implements ECDSA on the Stark curve.

---

## 7. API Endpoint

From `order_management_module.py` (lines 30-38):
```python
async def place_order(self, order: NewOrderModel):
    url = self._get_url("/user/order")
    response = await send_post_request(
        await self.get_session(),
        url,
        PlacedOrderModel,
        json=order.to_api_request_json(exclude_none=True),
        api_key=self._get_api_key(),
    )
    return response
```

**Endpoint**: `POST /user/order`

**Response** (wrapped):
```json
{
  "status": "OK",
  "data": {
    "id": 123456,
    "externalId": "0x..."
  }
}
```

---

## 8. Complete Order Creation Example

### Input:
- Market: BTC-USD
- Side: BUY
- Quantity: 0.001 BTC
- Price: $95,000.00
- Balance: $10

### Calculation Steps:

1. **Check if order value fits balance**:
   - Order value = 0.001 * 95000 = $95 (EXCEEDS $10 balance!)
   - With 10x leverage: Need $9.50 margin (still close to $10)

2. **Convert to Stark amounts**:
   - Synthetic: `0.001 * synthetic_resolution` (e.g., 0.001 * 10^10 = 10,000,000)
   - Collateral: `95 * collateral_resolution` (e.g., 95 * 10^6 = 95,000,000) - NEGATED for BUY
   - Fee: `95 * 0.0006 * collateral_resolution` = 57,000

3. **Hash parameters**:
   - position_id: User's vault ID (from account info)
   - base_asset_id: BTC's settlement_external_id (from market l2_config)
   - base_amount: 10,000,000 (positive for BUY)
   - quote_asset_id: USD's settlement_external_id
   - quote_amount: -95,000,000 (NEGATIVE for BUY)
   - fee_amount: 57,000
   - expiration: order_expiry + 14 days (in seconds)
   - salt: random nonce

4. **Sign the hash** with Stark private key

5. **Build request**:
```json
{
  "id": "order_hash_as_string",
  "market": "BTC-USD",
  "type": "LIMIT",
  "side": "BUY",
  "qty": 0.001,
  "price": 95000,
  "postOnly": false,
  "reduceOnly": false,
  "timeInForce": "GTT",
  "expiryEpochMillis": 1704153600000,
  "fee": 0.0006,
  "nonce": 1234567890,
  "selfTradeProtectionLevel": "ACCOUNT",
  "settlement": {
    "signature": { "r": "0x...", "s": "0x..." },
    "starkKey": "0x...",
    "collateralPosition": 123456
  },
  "debuggingAmounts": {
    "syntheticAmount": 10000000,
    "collateralAmount": -95000000,
    "feeAmount": 57000
  }
}
```

---

## 9. Key Gotchas

1. **FOK not supported** - Only GTT and IOC for new orders
2. **Market orders are LIMIT+IOC** - No separate MARKET type for creation
3. **Collateral is negated for BUY** - The buyer gives collateral
4. **Synthetic is negated for SELL** - The seller gives synthetic
5. **Settlement expiration != Order expiration** - Add 14 days buffer
6. **Fee is taker fee rate** - Not the absolute amount in the request
7. **Amounts in debugging are Stark format** - Multiplied by resolution

---

## Python SDK Reference Files

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
