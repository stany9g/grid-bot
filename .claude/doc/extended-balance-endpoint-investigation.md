# Extended DEX Balance Endpoint Investigation

## Date: 2026-01-02

## Problem Statement
The C# implementation is receiving a 404 error when calling the `user/balance` endpoint.

## Key Findings

### 1. Python SDK Implementation (Source of Truth)

**File:** `x10/perpetual/trading_client/account_module.py` (lines 39-45)

```python
async def get_balance(self) -> WrappedApiResponse[BalanceModel]:
    """
    https://api.docs.extended.exchange/#get-balance
    """
    url = self._get_url("/user/balance")
    return await send_get_request(await self.get_session(), url, BalanceModel, api_key=self._get_api_key())
```

**Key Observations:**
- Endpoint path: `/user/balance` (NOT `/user/balances`)
- HTTP Method: `GET`
- No query parameters required
- Requires `X-Api-Key` header for authentication
- Returns a **SINGLE object**, NOT a list

### 2. API Base URL Configuration

**Python SDK Configuration** (`x10/perpetual/configuration.py`):
```python
# Testnet
api_base_url="https://api.starknet.sepolia.extended.exchange/api/v1"

# Mainnet
api_base_url="https://api.starknet.extended.exchange/api/v1"
```

**C# Configuration** (`appsettings.json`):
```json
"Testnet": {
    "ApiUrl": "https://starknet.sepolia.extended.exchange/api/v1/"
}
"Mainnet": {
    "ApiUrl": "https://api.starknet.extended.exchange/api/v1/"
}
```

**CRITICAL ISSUE FOUND**: The testnet URL is different!
- Python SDK: `https://api.starknet.sepolia.extended.exchange/api/v1`
- C# Config: `https://starknet.sepolia.extended.exchange/api/v1/`

The C# testnet URL is missing the `api.` subdomain prefix!

### 3. Response Format Difference

**Python SDK Expected Response** (`x10/perpetual/balances.py`):
```python
class BalanceModel(X10BaseModel):
    collateral_name: str
    balance: Decimal
    equity: Decimal
    available_for_trade: Decimal
    available_for_withdrawal: Decimal
    unrealised_pnl: Decimal
    initial_margin: Decimal
    margin_ratio: Decimal
    updated_time: int
```

**C# Implementation Expected Response** (`BalanceResponse.cs`):
```csharp
public sealed record BalanceResponse
{
    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    [JsonPropertyName("total")]
    public required string Total { get; init; }

    [JsonPropertyName("available")]
    public required string Available { get; init; }

    [JsonPropertyName("locked")]
    public string? Locked { get; init; }

    [JsonPropertyName("inPositions")]
    public string? InPositions { get; init; }
}
```

**CRITICAL MODEL MISMATCH**: The C# model has completely different fields than the Python SDK model:

| Python SDK (Correct)          | C# Implementation (Wrong) |
|-------------------------------|---------------------------|
| `collateral_name` (camelCase: `collateralName`) | `asset` |
| `balance`                     | `total` |
| `available_for_trade` (camelCase: `availableForTrade`) | `available` |
| `equity`                      | (missing) |
| `available_for_withdrawal` (camelCase: `availableForWithdrawal`) | (missing) |
| `unrealised_pnl` (camelCase: `unrealisedPnl`) | (missing) |
| `initial_margin` (camelCase: `initialMargin`) | (missing) |
| `margin_ratio` (camelCase: `marginRatio`) | (missing) |
| `updated_time` (camelCase: `updatedTime`) | (missing) |
| (missing)                     | `locked` |
| (missing)                     | `inPositions` |

### 4. Return Type Issue

**Python SDK**: Returns a **single** `BalanceModel` object
```python
async def get_balance(self) -> WrappedApiResponse[BalanceModel]:
```

**C# Implementation**: Expects a **list** of `BalanceResponse` objects
```csharp
public async Task<IReadOnlyList<BalanceResponse>> GetBalancesAsync(CancellationToken ct = default)
{
    var response = await SendAsync<ApiResponse<IReadOnlyList<BalanceResponse>>>(
        HttpMethod.Get,
        "user/balance",
        RequestPriority.Medium,
        ct);
    return response?.Data ?? [];
}
```

The API returns a single object wrapped in `{"status": "OK", "data": {...}}`, NOT a list!

### 5. Required Headers

From `x10/utils/http.py` (lines 238-251):
```python
def __get_headers(*, api_key: Optional[str] = None, request_headers: Optional[Dict[str, str]] = None) -> Dict[str, str]:
    headers: dict[str, str] = {
        RequestHeader.ACCEPT: "application/json",
        RequestHeader.CONTENT_TYPE: "application/json",
        RequestHeader.USER_AGENT: USER_AGENT,
    }
    if api_key:
        headers[RequestHeader.API_KEY] = api_key
    return headers
```

Required headers:
- `Accept: application/json`
- `Content-Type: application/json`
- `User-Agent: <user agent string>`
- `X-Api-Key: <api key>` (for authenticated endpoints)

## Root Cause Analysis

The 404 error is most likely caused by the **incorrect Testnet API URL**:
- The C# configuration uses `https://starknet.sepolia.extended.exchange/api/v1/`
- It should be `https://api.starknet.sepolia.extended.exchange/api/v1`

Note: The mainnet URL is correct in both implementations.

## Required Fixes

### Fix 1: Update Testnet API URL in `appsettings.json`

**Current (Wrong)**:
```json
"Testnet": {
    "ApiUrl": "https://starknet.sepolia.extended.exchange/api/v1/",
```

**Correct**:
```json
"Testnet": {
    "ApiUrl": "https://api.starknet.sepolia.extended.exchange/api/v1",
```

Note: Also remove trailing slash for consistency with Python SDK.

### Fix 2: Update `BalanceResponse.cs` Model

Replace with correct fields matching the Python SDK:

```csharp
public sealed record BalanceResponse
{
    [JsonPropertyName("collateralName")]
    public required string CollateralName { get; init; }

    [JsonPropertyName("balance")]
    public required decimal Balance { get; init; }

    [JsonPropertyName("equity")]
    public required decimal Equity { get; init; }

    [JsonPropertyName("availableForTrade")]
    public required decimal AvailableForTrade { get; init; }

    [JsonPropertyName("availableForWithdrawal")]
    public required decimal AvailableForWithdrawal { get; init; }

    [JsonPropertyName("unrealisedPnl")]
    public required decimal UnrealisedPnl { get; init; }

    [JsonPropertyName("initialMargin")]
    public required decimal InitialMargin { get; init; }

    [JsonPropertyName("marginRatio")]
    public required decimal MarginRatio { get; init; }

    [JsonPropertyName("updatedTime")]
    public required long UpdatedTime { get; init; }
}
```

### Fix 3: Update `GetBalancesAsync` Return Type

Change from `IReadOnlyList<BalanceResponse>` to single `BalanceResponse`:

```csharp
public async Task<BalanceResponse?> GetBalanceAsync(CancellationToken ct = default)
{
    var response = await SendAsync<ApiResponse<BalanceResponse>>(
        HttpMethod.Get,
        "user/balance",
        RequestPriority.Medium,
        ct);
    return response?.Data;
}
```

### Fix 4: Consider Adding Missing Headers

The Python SDK always sends:
- `Accept: application/json`
- `Content-Type: application/json`

The C# implementation only adds these headers when there's a body. Consider adding them always.

## Summary

| Issue | Impact | Priority |
|-------|--------|----------|
| Wrong Testnet URL (missing `api.` subdomain) | 404 errors on testnet | **HIGH** |
| Wrong response model fields | Deserialization failure | **HIGH** |
| List vs single object return type | Deserialization failure | **HIGH** |
| Missing Accept/Content-Type headers | Potential API issues | Low |
| Trailing slash inconsistency | May cause issues | Low |

## References

- Python SDK Account Module: `.claude/repos/python_sdk/x10/perpetual/trading_client/account_module.py`
- Python SDK Balance Model: `.claude/repos/python_sdk/x10/perpetual/balances.py`
- Python SDK Configuration: `.claude/repos/python_sdk/x10/perpetual/configuration.py`
- Python SDK HTTP Utils: `.claude/repos/python_sdk/x10/utils/http.py`
- Extended API Documentation: https://api.docs.extended.exchange/#get-balance
