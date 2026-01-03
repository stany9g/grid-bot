# Extended DEX Balance Endpoint 404 Error - Mainnet Investigation

## Date: 2026-01-02

## Problem Statement
GET request to mainnet balance endpoint is returning 404:
```
GET https://api.starknet.extended.exchange/api/v1/user/balance
Headers:
  X-Api-Key: 07d6d52da9805d3e744f3b6750ca2d9c
  User-Agent: GridBot/1.0
```

## Investigation Summary

I analyzed the Python SDK at `C:\Users\stany\.claude\repos\python_sdk` to understand the exact URL construction and headers.

---

## Finding #1: WRONG API URL - Trailing Slash Issue

### C# Configuration (appsettings.json line 73)
```json
"ApiUrl": "https://api.starknet.extended.exchange/api/v1/"
```

### Python SDK Configuration (configuration.py line 43)
```python
api_base_url="https://api.starknet.extended.exchange/api/v1",  # NO trailing slash
```

### How Python SDK Constructs URLs

**File:** `x10/perpetual/trading_client/base_module.py` (line 30-31)
```python
def _get_url(self, path: str, *, query: Optional[Dict] = None, **path_params) -> str:
    return get_url(f"{self.__endpoint_config.api_base_url}{path}", query=query, **path_params)
```

**File:** `x10/utils/http.py` (line 117)
```python
template = template.rstrip("/")  # Removes trailing slashes AFTER concatenation
```

**File:** `x10/perpetual/trading_client/account_module.py` (line 44)
```python
url = self._get_url("/user/balance")  # Path starts with /
```

### Resulting URL Comparison

| Source | URL Produced |
|--------|--------------|
| Python SDK | `https://api.starknet.extended.exchange/api/v1/user/balance` |
| C# Current | `https://api.starknet.extended.exchange/api/v1//user/balance` (DOUBLE SLASH!) |

### The Bug
When C# concatenates `BaseAddress` with the relative path:
- BaseAddress: `https://api.starknet.extended.exchange/api/v1/` (trailing slash)
- Path: `user/balance` (no leading slash - as in C# code line 125)
- Result: `https://api.starknet.extended.exchange/api/v1/user/balance` (correct)

BUT wait - checking the C# code at line 252:
```csharp
using var request = new HttpRequestMessage(method, path);  // path = "user/balance"
```

This should work correctly with HttpClient because:
- `_httpClient.BaseAddress = new Uri("https://api.starknet.extended.exchange/api/v1/")`
- Request path = `"user/balance"` (no leading slash)

Actually, the issue might be different. Let me re-check...

---

## Finding #2: Missing Required Headers

### Python SDK Headers (http.py lines 238-250)
```python
def __get_headers(*, api_key: Optional[str] = None, request_headers: Optional[Dict[str, str]] = None) -> Dict[str, str]:
    headers: dict[str, str] = {
        RequestHeader.ACCEPT: "application/json",           # MISSING IN C#
        RequestHeader.CONTENT_TYPE: "application/json",     # MISSING IN C#
        RequestHeader.USER_AGENT: USER_AGENT,
    }

    if api_key:
        headers[RequestHeader.API_KEY] = api_key

    if request_headers:
        headers.update(request_headers)

    return headers
```

### C# Headers (ExtendedHttpClient.cs lines 41-42)
```csharp
_httpClient.DefaultRequestHeaders.Add(ExtendedConstants.ApiKeyHeader, _options.ApiKey);
_httpClient.DefaultRequestHeaders.Add("User-Agent", _options.UserAgent);
// MISSING: Accept: application/json
// MISSING: Content-Type: application/json (for POST/PATCH)
```

### Missing Headers
| Header | Python SDK | C# Implementation |
|--------|-----------|-------------------|
| `Accept` | `application/json` | **MISSING** |
| `Content-Type` | `application/json` | **MISSING** |
| `X-Api-Key` | Present | Present |
| `User-Agent` | Present | Present |

---

## Finding #3: Python SDK User-Agent Format

### Python SDK (config.py lines 9-10)
```python
SDK_VERSION = importlib.metadata.version("x10-python-trading-starknet")
USER_AGENT = f"X10PythonTradingClient/{SDK_VERSION}"
```

This produces: `X10PythonTradingClient/0.0.17`

### C# Configuration (appsettings.json line 83)
```json
"UserAgent": "GridBot/1.0"
```

The User-Agent format may not be significant for 404 errors, but note the difference.

---

## Root Cause Analysis

After examining the code carefully, the 404 is likely caused by **one of two issues**:

### Theory 1: HttpClient BaseAddress Behavior

HttpClient has specific rules for combining `BaseAddress` with request URIs:

1. If the request URI is **absolute**, `BaseAddress` is ignored
2. If the request URI is **relative** and starts with `/`, it replaces the path component of `BaseAddress`
3. If the request URI is **relative** and does NOT start with `/`, it's appended to `BaseAddress`

**C# Code:**
```csharp
_httpClient.BaseAddress = new Uri("https://api.starknet.extended.exchange/api/v1/");  // Trailing slash
// ...
using var request = new HttpRequestMessage(method, "user/balance");  // No leading slash - CORRECT
```

This SHOULD produce: `https://api.starknet.extended.exchange/api/v1/user/balance`

### Theory 2: Missing Accept Header

The API may require `Accept: application/json` header and return 404 for requests without it (or return HTML/redirect that then 404s).

---

## Recommended Fixes

### Fix 1: Add Missing Headers
```csharp
// In ExtendedHttpClient constructor
_httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

// For requests with body, add Content-Type
request.Content = JsonContent.Create(body, options: ExtendedJsonOptions.Default);
request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
```

Or use the more standard approach:
```csharp
_httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
```

### Fix 2: Remove Trailing Slash from Base URL
```json
"ApiUrl": "https://api.starknet.extended.exchange/api/v1"  // No trailing slash
```

### Fix 3: Debug the Actual URL Being Sent

Add logging to see the exact URL:
```csharp
_logger.LogDebug("Full URL: {Url}", new Uri(_httpClient.BaseAddress, path).ToString());
```

---

## How Python SDK Builds Request (Detailed)

### Step-by-step for `/user/balance`:

1. **EndpointConfig** has `api_base_url="https://api.starknet.extended.exchange/api/v1"` (no trailing slash)

2. **`_get_url("/user/balance")`** in `base_module.py`:
   ```python
   return get_url(f"{self.__endpoint_config.api_base_url}{path}", ...)
   # Result: "https://api.starknet.extended.exchange/api/v1/user/balance"
   ```

3. **`get_url()`** in `http.py`:
   ```python
   template = template.rstrip("/")  # Removes trailing slash if present
   # Result: "https://api.starknet.extended.exchange/api/v1/user/balance"
   ```

4. **`send_get_request()`** sends with headers:
   ```python
   headers = {
       "Accept": "application/json",
       "Content-Type": "application/json",
       "User-Agent": "X10PythonTradingClient/0.0.17",
       "X-Api-Key": "<api_key>"
   }
   ```

---

## Verification Steps

To verify the exact issue, test these scenarios:

1. **Test with curl including Accept header:**
   ```bash
   curl -v "https://api.starknet.extended.exchange/api/v1/user/balance" \
     -H "X-Api-Key: 07d6d52da9805d3e744f3b6750ca2d9c" \
     -H "User-Agent: GridBot/1.0" \
     -H "Accept: application/json"
   ```

2. **Test without Accept header:**
   ```bash
   curl -v "https://api.starknet.extended.exchange/api/v1/user/balance" \
     -H "X-Api-Key: 07d6d52da9805d3e744f3b6750ca2d9c" \
     -H "User-Agent: GridBot/1.0"
   ```

3. **Check if the API key is valid for mainnet** - A 404 might actually be a masked 401/403 if the API key doesn't have account access.

---

## Summary

| Issue | Priority | Status |
|-------|----------|--------|
| Missing `Accept: application/json` header | HIGH | Needs fix |
| Trailing slash in ApiUrl config | MEDIUM | Verify behavior |
| API key validity for mainnet | HIGH | Verify externally |

The most likely cause of the 404 is the **missing `Accept: application/json` header**, as this is present in all Python SDK requests but absent in the C# implementation.

---

## Files Referenced

**Python SDK:**
- `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\configuration.py` - Endpoint configs
- `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\trading_client\base_module.py` - URL construction
- `C:\Users\stany\.claude\repos\python_sdk\x10\perpetual\trading_client\account_module.py` - get_balance()
- `C:\Users\stany\.claude\repos\python_sdk\x10\utils\http.py` - HTTP utilities, headers
- `C:\Users\stany\.claude\repos\python_sdk\x10\config.py` - User-Agent format

**C# Implementation:**
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.ApiService\appsettings.json` - Configuration
- `C:\Users\stany\source\repos\plan\GridBot\GridBot.Extended\ExtendedHttpClient.cs` - HTTP client
