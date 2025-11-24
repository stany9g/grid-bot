# Lighter Configuration Guide

This guide shows how to configure the Lighter client using `appsettings.json` and dependency injection.

## Step 1: Add Configuration to appsettings.json

Create or update your `appsettings.json`:

```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "PrivateKey": "your-api-private-key-here",
    "ChainId": 304,
    "ApiKeyIndex": 0,
    "AccountIndex": 123456,
    "InitialNonce": 0
  }
}
```

### Configuration Options

| Property | Type | Description | Default |
|----------|------|-------------|---------|
| `ApiUrl` | string | API base URL | `https://mainnet.zklighter.elliot.ai` |
| `PrivateKey` | string | API private key for signing | *Required* |
| `ChainId` | int | Chain ID (304=mainnet, 300=testnet) | `304` |
| `ApiKeyIndex` | int | API key index | `0` |
| `AccountIndex` | long | Account index on Lighter | *Required* |
| `InitialNonce` | long | Starting nonce (0=sync from server) | `0` |

## Step 2: Secure Your Private Key

**NEVER commit your private key to source control!**

### Development: User Secrets

```bash
cd GridBot.ApiService
dotnet user-secrets init
dotnet user-secrets set "Lighter:PrivateKey" "your-private-key-here"
dotnet user-secrets set "Lighter:AccountIndex" "123456"
```

Your `appsettings.Development.json` can then omit the private key:

```json
{
  "Lighter": {
    "ApiUrl": "https://mainnet.zklighter.elliot.ai",
    "ChainId": 304,
    "ApiKeyIndex": 0,
    "InitialNonce": 0
  }
}
```

### Production: Environment Variables

Set environment variables on your server:

```bash
export Lighter__PrivateKey="your-private-key-here"
export Lighter__AccountIndex="123456"
```

Or in Docker:

```dockerfile
ENV Lighter__PrivateKey="your-private-key-here"
ENV Lighter__AccountIndex="123456"
```

### Azure: Key Vault

Use Azure Key Vault for production:

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri("https://your-vault.vault.azure.net/"),
    new DefaultAzureCredential());
```

Store secrets in Key Vault:
- `Lighter--PrivateKey`
- `Lighter--AccountIndex`

## Step 3: Register in Program.cs

```csharp
using GridBot.Lighter;

var builder = WebApplication.CreateBuilder(args);

// Add Lighter client from configuration
builder.Services.AddLighterClient(builder.Configuration);

var app = builder.Build();
app.Run();
```

## Step 4: Use via Dependency Injection

### In a Service

```csharp
public class TradingService
{
    private readonly LighterClient _lighter;
    private readonly ILogger<TradingService> _logger;

    public TradingService(LighterClient lighter, ILogger<TradingService> logger)
    {
        _lighter = lighter;
        _logger = logger;
    }

    public async Task PlaceOrderAsync()
    {
        try
        {
            var request = new CreateOrderRequest
            {
                MarketIndex = 0,
                ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                BaseAmount = 1_000_000,
                Price = 50_000,
                IsAsk = false,
                OrderType = OrderType.Limit,
                TimeInForce = TimeInForce.GoodTillTime,
                OrderExpiry = OrderConstants.Default28DayOrderExpiry
            };

            var response = await _lighter.CreateOrderAsync(request);
            _logger.LogInformation("Order placed: {TxHash}", response.TxHash);
        }
        catch (LighterApiException ex)
        {
            _logger.LogError(ex, "Order failed: {Message}", ex.Message);
            throw;
        }
    }
}
```

### In a Minimal API Endpoint

```csharp
app.MapPost("/api/orders", async (LighterClient lighter, CreateOrderDto dto) =>
{
    var request = new CreateOrderRequest
    {
        MarketIndex = dto.MarketId,
        ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        BaseAmount = dto.Amount,
        Price = dto.Price,
        IsAsk = !dto.IsBuy,
        OrderType = OrderType.Limit,
        TimeInForce = TimeInForce.GoodTillTime,
        OrderExpiry = OrderConstants.Default28DayOrderExpiry
    };

    try
    {
        var response = await lighter.CreateOrderAsync(request);
        return Results.Ok(new { txHash = response.TxHash });
    }
    catch (LighterApiException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});
```

### In a Blazor Component

```razor
@inject LighterClient Lighter

<button @onclick="PlaceOrder">Place Order</button>

@code {
    private async Task PlaceOrder()
    {
        var request = new CreateOrderRequest { /* ... */ };
        var response = await Lighter.CreateOrderAsync(request);
        Console.WriteLine($"Order placed: {response.TxHash}");
    }
}
```

## Alternative: Manual Configuration

If you prefer not to use `appsettings.json`:

```csharp
builder.Services.AddLighterClient(options =>
{
    options.ApiUrl = "https://mainnet.zklighter.elliot.ai";
    options.PrivateKey = builder.Configuration["LIGHTER_PRIVATE_KEY"]!;
    options.ChainId = 304;
    options.ApiKeyIndex = 0;
    options.AccountIndex = 123456;
    options.InitialNonce = 0;
});
```

## Testnet Configuration

For testnet, change the ChainId and URL:

```json
{
  "Lighter": {
    "ApiUrl": "https://testnet.zklighter.elliot.ai",
    "ChainId": 300,
    "PrivateKey": "your-testnet-private-key",
    "ApiKeyIndex": 0,
    "AccountIndex": 123456,
    "InitialNonce": 0
  }
}
```

## Multiple Accounts

To use multiple accounts, register multiple clients with keyed services:

```csharp
builder.Services.AddKeyedSingleton<LighterClient>("account1", (sp, key) =>
{
    var client = new LighterClient();
    var initTask = client.Signer.InitializeAsync(
        url: "https://mainnet.zklighter.elliot.ai",
        privateKey: builder.Configuration["Lighter:Account1:PrivateKey"]!,
        chainId: 304,
        apiKeyIndex: 0,
        accountIndex: 123456,
        initialNonce: 0
    );
    initTask.Wait();
    return client;
});

builder.Services.AddKeyedSingleton<LighterClient>("account2", (sp, key) =>
{
    var client = new LighterClient();
    var initTask = client.Signer.InitializeAsync(
        url: "https://mainnet.zklighter.elliot.ai",
        privateKey: builder.Configuration["Lighter:Account2:PrivateKey"]!,
        chainId: 304,
        apiKeyIndex: 0,
        accountIndex: 789012,
        initialNonce: 0
    );
    initTask.Wait();
    return client;
});
```

Then inject with:

```csharp
public class MultiAccountService
{
    private readonly LighterClient _account1;
    private readonly LighterClient _account2;

    public MultiAccountService(
        [FromKeyedServices("account1")] LighterClient account1,
        [FromKeyedServices("account2")] LighterClient account2)
    {
        _account1 = account1;
        _account2 = account2;
    }
}
```

## Troubleshooting

### "Missing 'Lighter' configuration section"
Ensure your `appsettings.json` contains the `Lighter` section.

### "Invalid Lighter configuration: PrivateKey is required"
Set your private key via User Secrets, environment variables, or Key Vault.

### "Invalid Lighter configuration: AccountIndex must be greater than 0"
Set a valid account index in your configuration.

### "Failed to initialize Lighter client"
Check that your private key is correct and your account index exists on the network.
