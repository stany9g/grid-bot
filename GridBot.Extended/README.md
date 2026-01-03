# GridBot.Extended

Extended DEX (X10) client library for .NET. Provides REST API and WebSocket clients for trading on the Extended perpetual DEX built on Starknet.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [Initialization](#initialization)
- [API Examples](#api-examples)
  - [Creating a Limit Order](#creating-a-limit-order)
  - [Creating a Market Order](#creating-a-market-order)
  - [Canceling Orders](#canceling-orders)
  - [Getting Open Positions](#getting-open-positions)
  - [Getting Order History](#getting-order-history)
  - [Getting Account Balance](#getting-account-balance)
- [WebSocket Streaming](#websocket-streaming)
- [Architecture](#architecture)

---

## Prerequisites

### Native Signing Library

The project requires a native Stark signing library for order cryptographic signatures:

- **Windows**: `stark-signer-windows-amd64.dll`
- **Linux (x64)**: `stark-signer-linux-amd64.so`
- **Linux (ARM64)**: `stark-signer-linux-arm64.so`

These files are located in `GridBot.Extended/Native/` and are automatically copied to the output directory during build.

### Required NuGet Packages

The project depends on:
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Logging`
- `Microsoft.Extensions.Http`

---

## Configuration

### appsettings.json Structure

```json
{
  "ExtendedNetworks": {
    "DefaultNetwork": "Testnet",
    "Testnet": {
      "ApiUrl": "https://api.starknet.sepolia.extended.exchange/api/v1/",
      "ApiKey": "your-api-key",
      "StarkPrivateKey": "0x...",
      "StarkPublicKey": "0x...",
      "AccountAddress": "0x...",
      "VaultNumber": 506215,
      "ClientId": 8681,
      "IsTestnet": true,
      "InitialNonce": 0,
      "DryRun": false,
      "UserAgent": "GridBot/1.0",
      "IsMarketMaker": false,
      "WebSocket": {
        "WebSocketUrl": "wss://api.starknet.sepolia.extended.exchange",
        "ReconnectDelayMs": 1000,
        "MaxReconnectDelayMs": 60000,
        "HeartbeatIntervalSeconds": 30
      }
    },
    "Mainnet": {
      "ApiUrl": "https://api.starknet.extended.exchange/api/v1/",
      "ApiKey": "your-mainnet-api-key",
      "StarkPrivateKey": "0x...",
      "StarkPublicKey": "0x...",
      "AccountAddress": "0x...",
      "IsTestnet": false
    }
  }
}
```

### Configuration Fields

| Field | Description | Required |
|-------|-------------|----------|
| `ApiUrl` | REST API base URL | Yes |
| `ApiKey` | API key for authentication | Yes |
| `StarkPrivateKey` | Stark private key for signing (hex with 0x prefix) | Yes |
| `StarkPublicKey` | Stark public key (hex with 0x prefix) | Yes |
| `AccountAddress` | Account address on Extended | Yes |
| `VaultNumber` | Vault number for deposits/withdrawals | No |
| `ClientId` | Client ID | No |
| `IsTestnet` | Whether this is testnet (affects order expiry) | No |
| `DryRun` | If true, logs operations without executing | No |
| `IsMarketMaker` | Enables higher rate limits (60,000/5min vs 1,000/min) | No |

> **Security Note**: Store `StarkPrivateKey` securely using User Secrets or Azure Key Vault. Never commit private keys to source control.

---

## Initialization

### Option 1: Multi-Network Support (Recommended)

Register the factory for runtime network switching:

```csharp
// In Program.cs
builder.Services.AddExtendedNetworks(builder.Configuration);
```

Initialize and use the client:

```csharp
// Get the factory
var factory = serviceProvider.GetRequiredService<IExtendedNetworkExchangeFactory>();

// Initialize for a specific network
var exchangeClient = await factory.CreateForNetworkAsync(ExtendedNetworkType.Testnet, ct);

// Access the HTTP client for direct API calls
var httpClient = factory.GetHttpClient();
var starkSigner = factory.GetStarkSigner();
var options = factory.GetCurrentOptions();
```

### Option 2: Single Network Setup

For a simpler single-network setup:

```csharp
// In Program.cs
builder.Services.AddExtendedExchange(builder.Configuration, "extended-main");

// Usage
var httpClient = serviceProvider.GetRequiredService<IExtendedHttpClient>();
```

---

## API Examples

### Creating a Limit Order

Complete example of creating a limit order with proper signing:

```csharp
public async Task<CreateOrderResponse> CreateLimitOrderAsync(
    IExtendedNetworkExchangeFactory factory,
    string market,
    decimal quantity,
    decimal price,
    bool isBuy,
    CancellationToken ct)
{
    var httpClient = factory.GetHttpClient()!;
    var starkSigner = factory.GetStarkSigner()!;
    var options = factory.GetCurrentOptions()!;

    // 1. Get account info for vault ID (collateralPosition)
    var accountInfo = await httpClient.GetAccountInfoAsync(ct);
    var positionId = long.Parse(accountInfo.L2Vault);

    // 2. Get market info for L2Config (required for signing)
    var markets = await httpClient.GetMarketsAsync(ct);
    var marketInfo = markets.First(m => m.Name == market);
    var l2Config = marketInfo.L2Config!;

    // 3. Generate nonce and calculate expiry
    var nonce = StarkAmountCalculator.GenerateNonce();
    var expiryMs = DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds();
    var clientOrderId = Guid.NewGuid().ToString("N");

    // 4. Calculate settlement expiration (order expiry + 14 days)
    var settlementExpiration = StarkAmountCalculator.CalcSettlementExpiration(expiryMs);

    // 5. Calculate Stark amounts using market resolutions
    var (baseAmount, quoteAmount, feeAmount) = StarkAmountCalculator.CalculateStarkAmounts(
        quantity,
        price,
        ExtendedConstants.DefaultFeeRate,  // 0.00025 (0.025%)
        isBuy,
        l2Config.SyntheticResolution,
        l2Config.CollateralResolution);

    // 6. Build order parameters for signing
    var orderParams = new StarkExOrderParams
    {
        PositionId = positionId,
        BaseAmount = baseAmount,
        QuoteAmount = quoteAmount,
        FeeAmount = feeAmount,
        Nonce = nonce,
        ExpirationSeconds = settlementExpiration
    };

    // 7. Sign the order
    var isTestnet = factory.GetCurrentNetwork() == ExtendedNetworkType.Testnet;
    var (r, s) = starkSigner.SignOrder(orderParams, l2Config, isTestnet);

    // 8. Build the request
    var request = new CreateOrderRequest
    {
        Id = clientOrderId,
        Market = market,
        Type = "LIMIT",
        Side = isBuy ? "BUY" : "SELL",
        Qty = quantity.ToString(CultureInfo.InvariantCulture),
        Price = price.ToString(CultureInfo.InvariantCulture),
        Fee = ExtendedConstants.DefaultFeeRate.ToString(CultureInfo.InvariantCulture),
        ExpiryEpochMillis = expiryMs,
        TimeInForce = "GTT",  // Good Till Time
        ReduceOnly = false,
        PostOnly = false,
        Nonce = nonce.ToString(),
        SelfTradeProtectionLevel = "ACCOUNT",
        Settlement = new SettlementObject
        {
            StarkKey = starkSigner.StarkPublicKey!,
            CollateralPosition = accountInfo.L2Vault,
            Signature = new SignatureObject { R = r, S = s }
        }
    };

    // 9. Submit the order
    return await httpClient.CreateOrderAsync(request, ct);
}
```

**Important Notes**:
- HTTP 200 does NOT mean the order is active. Wait for WebSocket confirmation.
- Always use `CultureInfo.InvariantCulture` for decimal-to-string conversion.
- Nonce is a random 32-bit value per order (not sequential).

### Creating a Market Order

Extended DEX uses LIMIT + IOC (Immediate-Or-Cancel) to simulate market orders:

```csharp
public async Task<CreateOrderResponse> CreateMarketOrderAsync(
    IExtendedNetworkExchangeFactory factory,
    string market,
    decimal quantity,
    bool isBuy,
    CancellationToken ct)
{
    var httpClient = factory.GetHttpClient()!;
    var starkSigner = factory.GetStarkSigner()!;

    // Get current market price from order book
    var orderBook = await httpClient.GetOrderBookAsync(market, 5, ct);
    var bestPrice = isBuy
        ? decimal.Parse(orderBook.Asks!.First().Price!, CultureInfo.InvariantCulture)
        : decimal.Parse(orderBook.Bids!.First().Price!, CultureInfo.InvariantCulture);

    // Add slippage (1% for market orders)
    var marketPrice = isBuy ? bestPrice * 1.01m : bestPrice * 0.99m;

    // ... (same signing process as limit order)

    var request = new CreateOrderRequest
    {
        // ... same fields as limit order
        TimeInForce = "IOC",  // Immediate-Or-Cancel for market orders
        ExpiryEpochMillis = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeMilliseconds(),
    };

    return await httpClient.CreateOrderAsync(request, ct);
}
```

### Canceling Orders

#### Cancel Single Order

```csharp
var httpClient = factory.GetHttpClient()!;

// Cancel by order ID
bool cancelled = await httpClient.CancelOrderAsync("12345", ct);
if (cancelled)
{
    Console.WriteLine("Order cancelled successfully");
}
```

#### Mass Cancel Orders

```csharp
var httpClient = factory.GetHttpClient()!;

// Cancel all orders for a specific market
var response = await httpClient.MassCancelOrdersAsync(
    new MassCancelRequest { Market = "BTC-USD" },
    ct);

Console.WriteLine($"Cancelled {response.CancelledCount} orders");
```

### Getting Open Positions

```csharp
var httpClient = factory.GetHttpClient()!;

// Get all open positions
var positions = await httpClient.GetPositionsAsync(ct);

foreach (var position in positions)
{
    Console.WriteLine($"Market: {position.Market}");
    Console.WriteLine($"Side: {position.Side}");
    Console.WriteLine($"Size: {position.Size}");
    Console.WriteLine($"Entry Price: {position.EntryPrice}");
    Console.WriteLine($"Unrealized PnL: {position.UnrealizedPnl}");
    Console.WriteLine($"Leverage: {position.Leverage}");
    Console.WriteLine("---");
}
```

### Getting Order History

```csharp
var httpClient = factory.GetHttpClient()!;

// Get all open orders
var allOrders = await httpClient.GetOrdersAsync(null, ct);

// Get orders for specific market
var btcOrders = await httpClient.GetOrdersAsync("BTC-USD", ct);

foreach (var order in btcOrders)
{
    Console.WriteLine($"Order ID: {order.Id}");
    Console.WriteLine($"Client ID: {order.ClientOrderId}");
    Console.WriteLine($"Side: {order.Side}");
    Console.WriteLine($"Price: {order.Price}");
    Console.WriteLine($"Quantity: {order.Qty}");
    Console.WriteLine($"Filled: {order.FilledQty}");
    Console.WriteLine($"Status: {order.Status}");
    Console.WriteLine("---");
}
```

### Getting Account Balance

```csharp
var httpClient = factory.GetHttpClient()!;

// Get account info
var account = await httpClient.GetAccountInfoAsync(ct);
Console.WriteLine($"Account ID: {account.AccountId}");
Console.WriteLine($"L2 Vault: {account.L2Vault}");

// Get balance
var balance = await httpClient.GetBalanceAsync(ct);
if (balance != null)
{
    Console.WriteLine($"Collateral: {balance.CollateralName}");
    Console.WriteLine($"Balance: {balance.Balance}");
    Console.WriteLine($"Equity: {balance.Equity}");
    Console.WriteLine($"Available: {balance.AvailableForTrade}");
    Console.WriteLine($"Unrealized PnL: {balance.UnrealisedPnl}");
    Console.WriteLine($"Margin Ratio: {balance.MarginRatio:P2}");
}
```

### Getting Market Data

```csharp
var httpClient = factory.GetHttpClient()!;

// Get all markets
var markets = await httpClient.GetMarketsAsync(ct);

// Get order book
var orderBook = await httpClient.GetOrderBookAsync("BTC-USD", depth: 20, ct);

// Get market stats
var stats = await httpClient.GetMarketStatsAsync("BTC-USD", ct);

// Get candles (OHLCV)
var candles = await httpClient.GetCandlesAsync("BTC-USD", "mark", "1h", 100, ct);

// Get funding rate
var funding = await httpClient.GetFundingRateAsync("BTC-USD", ct);
```

---

## WebSocket Streaming

### Subscribing to Account Updates

```csharp
var wsClient = serviceProvider.GetRequiredService<IExtendedWebSocketClient>();

// Connect
await wsClient.ConnectAsync(ct);

// Subscribe to account updates (orders, trades, balances, positions)
await wsClient.SubscribeAccountAsync(ct);

// Monitor order updates
await foreach (var orderUpdate in wsClient.OrderUpdates.ReadAllAsync(ct))
{
    Console.WriteLine($"Order {orderUpdate.OrderId}: {orderUpdate.Status}");
}

// Monitor trade executions
await foreach (var trade in wsClient.TradeUpdates.ReadAllAsync(ct))
{
    Console.WriteLine($"Trade: {trade.Quantity} @ {trade.Price}");
}

// Monitor balance changes
await foreach (var balance in wsClient.BalanceUpdates.ReadAllAsync(ct))
{
    Console.WriteLine($"Balance update: {balance.Total}");
}

// Monitor position updates
await foreach (var position in wsClient.PositionUpdates.ReadAllAsync(ct))
{
    Console.WriteLine($"Position: {position.Market} {position.Side} {position.Size}");
}

// Monitor connection state
await foreach (var state in wsClient.ConnectionStateChanges.ReadAllAsync(ct))
{
    Console.WriteLine($"Connection: {state.State}");
}

// Disconnect when done
await wsClient.DisconnectAsync(ct);
```

---

## Architecture

### Project Structure

```
GridBot.Extended/
├── ExtendedHttpClient.cs              # REST API client
├── IExtendedHttpClient.cs             # REST API interface
├── ExtendedWebSocketClient.cs         # WebSocket client
├── IExtendedWebSocketClient.cs        # WebSocket interface
├── ExtendedOptions.cs                 # Single network config
├── ExtendedNetworksOptions.cs         # Multi-network config
├── ExtendedConstants.cs               # Rate limits, thresholds
├── StarkSigner.cs                     # Order signing (Poseidon/SNIP-12)
├── NonceManager.cs                    # Transaction nonce management
├── RateLimiter.cs                     # Priority-based rate limiting
├── Extensions/
│   └── ExtendedServiceExtensions.cs   # DI registration
├── Factory/
│   ├── ExtendedNetworkExchangeFactory.cs   # Network client factory
│   └── IExtendedNetworkExchangeFactory.cs
├── Adapters/                          # IExchangeClient implementations
│   ├── ExtendedExchangeClient.cs
│   ├── ExtendedOrderAdapter.cs
│   ├── ExtendedAccountAdapter.cs
│   └── ...
├── Models/
│   ├── Api/                           # REST API models
│   │   ├── CreateOrderRequest.cs
│   │   ├── OrderResponse.cs
│   │   ├── PositionResponse.cs
│   │   └── ...
│   └── WebSocket/                     # WebSocket event models
└── Native/
    ├── StarkNativeMethods.cs          # P/Invoke bindings
    ├── stark-signer-windows-amd64.dll
    └── stark-signer-linux-*.so
```

### Key Components

| Component | Purpose |
|-----------|---------|
| `IExtendedHttpClient` | REST API for orders, positions, account info |
| `IExtendedWebSocketClient` | Real-time streaming for updates |
| `IExtendedNetworkExchangeFactory` | Multi-network client management |
| `StarkSigner` | Cryptographic order signing (Poseidon hash) |
| `RateLimiter` | Priority-based throttling (1,000/min standard) |

### Rate Limits

| Type | Limit |
|------|-------|
| Standard | 1,000 requests/minute |
| Market Maker | 60,000 requests/5 minutes |

The rate limiter uses soft limits with priority-based throttling:
- 70%: Low priority requests throttled
- 80%: Medium priority requests throttled
- 95%: High priority requests throttled
- Critical requests always proceed

---

## Error Handling

```csharp
try
{
    var response = await httpClient.CreateOrderAsync(request, ct);
}
catch (ExtendedApiException ex)
{
    Console.WriteLine($"API Error: {ex.StatusCode}");
    Console.WriteLine($"Error Code: {ex.ErrorCode}");
    Console.WriteLine($"Message: {ex.ServerMessage}");
}
```

Common HTTP status codes:
- `404` on balance: Treated as zero balance (not thrown)
- `404` on cancel: Treated as already cancelled (not thrown)
- `429`: Rate limit exceeded (triggers backoff)

---

## Best Practices

1. **Always initialize the signer** before creating orders: `starkSigner.Initialize()`
2. **Use invariant culture** for decimal conversions to avoid locale issues
3. **Wait for WebSocket confirmation** before considering an order active
4. **Handle rate limits** - the client has built-in backoff, but monitor utilization
5. **Store private keys securely** using User Secrets or Key Vault
6. **Use testnet first** for development and testing
