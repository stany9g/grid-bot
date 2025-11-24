using GridBot.Lighter;
using GridBot.Lighter.Models;

namespace GridBot.ApiService.Examples;

/// <summary>
/// Example usage of the Lighter SignerClient for signing cryptocurrency trading operations.
/// </summary>
public static class SignerUsageExample
{
    /// <summary>
    /// Example: Generate a new API key pair.
    /// </summary>
    public static async Task<(string? privateKey, string? publicKey, string? error)> GenerateNewApiKeyExample()
    {
        // Generate a new key pair (optionally with a seed for deterministic generation)
        var (privateKey, publicKey, error) = await SignerClient.GenerateApiKeyAsync(seed: null);

        if (error != null)
        {
            Console.WriteLine($"Error generating key: {error}");
            return (null, null, error);
        }

        Console.WriteLine($"Generated Key Pair:");
        Console.WriteLine($"Private Key: {privateKey}");
        Console.WriteLine($"Public Key: {publicKey}");

        return (privateKey, publicKey, null);
    }

    /// <summary>
    /// Example: Initialize the signer client and create a limit order.
    /// </summary>
    public static async Task<string?> CreateLimitOrderExample()
    {
        using var signer = new SignerClient();

        // Initialize the client
        var initError = await signer.InitializeAsync(
            url: "https://api.lighter.xyz",
            privateKey: "your-api-private-key-here",
            chainId: ChainId.Mainnet,
            apiKeyIndex: 0,
            accountIndex: 0,
            initialNonce: 0
        );

        if (initError != null)
        {
            Console.WriteLine($"Initialization error: {initError}");
            return initError;
        }

        // Create a limit order request
        var orderRequest = new CreateOrderRequest
        {
            MarketIndex = 0, // BTC-USDC
            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), // Unique order ID
            BaseAmount = 1_000_000, // 0.01 BTC (scaled by 100M)
            Price = 50_000, // $50,000 (scaled appropriately)
            IsAsk = false, // Buy order
            OrderType = OrderType.Limit,
            TimeInForce = TimeInForce.GoodTillTime,
            ReduceOnly = false,
            TriggerPrice = OrderConstants.NilTriggerPrice,
            OrderExpiry = OrderConstants.Default28DayOrderExpiry
        };

        // Sign the order
        var (txInfo, error) = await signer.CreateOrderAsync(orderRequest);

        if (error != null)
        {
            Console.WriteLine($"Order signing error: {error}");
            return error;
        }

        Console.WriteLine($"Order signed successfully!");
        Console.WriteLine($"Transaction Info: {txInfo}");

        return null;
    }

    /// <summary>
    /// Example: Create a One-Cancels-Other (OCO) order pair.
    /// </summary>
    public static async Task<string?> CreateOcoOrderExample()
    {
        using var signer = new SignerClient();

        // Initialize (same as above)
        var initError = await signer.InitializeAsync(
            url: "https://api.lighter.xyz",
            privateKey: "your-api-private-key-here",
            chainId: ChainId.Mainnet
        );

        if (initError != null) return initError;

        // Create OCO order: One take-profit, one stop-loss
        var ocoRequest = new CreateGroupedOrdersRequest
        {
            GroupingType = GroupingType.OneCancelOther,
            Orders = new List<CreateOrderRequest>
            {
                // Take-profit order
                new CreateOrderRequest
                {
                    MarketIndex = 0,
                    ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    BaseAmount = 1_000_000,
                    Price = 55_000, // Sell at $55k
                    IsAsk = true,
                    OrderType = OrderType.TakeProfitLimit,
                    TimeInForce = TimeInForce.GoodTillTime
                },
                // Stop-loss order
                new CreateOrderRequest
                {
                    MarketIndex = 0,
                    ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1,
                    BaseAmount = 1_000_000,
                    Price = 45_000, // Sell at $45k
                    IsAsk = true,
                    OrderType = OrderType.StopLossLimit,
                    TimeInForce = TimeInForce.GoodTillTime,
                    TriggerPrice = 45_000
                }
            }
        };

        var (txInfo, error) = await signer.CreateGroupedOrdersAsync(ocoRequest);

        if (error != null)
        {
            Console.WriteLine($"OCO order error: {error}");
            return error;
        }

        Console.WriteLine($"OCO order signed successfully!");
        Console.WriteLine($"Transaction Info: {txInfo}");

        return null;
    }

    /// <summary>
    /// Example: Cancel a specific order.
    /// </summary>
    public static async Task<string?> CancelOrderExample(long orderId)
    {
        using var signer = new SignerClient();

        var initError = await signer.InitializeAsync(
            url: "https://api.lighter.xyz",
            privateKey: "your-api-private-key-here",
            chainId: ChainId.Mainnet
        );

        if (initError != null) return initError;

        // Cancel the order
        var (txInfo, error) = await signer.CancelOrderAsync(
            marketIndex: 0,
            orderId: orderId
        );

        if (error != null)
        {
            Console.WriteLine($"Cancel order error: {error}");
            return error;
        }

        Console.WriteLine($"Order cancelled successfully!");
        Console.WriteLine($"Transaction Info: {txInfo}");

        return null;
    }

    /// <summary>
    /// Example: Update position leverage.
    /// </summary>
    public static async Task<string?> UpdateLeverageExample()
    {
        using var signer = new SignerClient();

        var initError = await signer.InitializeAsync(
            url: "https://api.lighter.xyz",
            privateKey: "your-api-private-key-here",
            chainId: ChainId.Mainnet
        );

        if (initError != null) return initError;

        // Update leverage to 10x isolated
        var leverageRequest = new UpdateLeverageRequest
        {
            MarketIndex = 0,
            MarginMode = MarginMode.Isolated,
            Leverage = 10
        };

        var (txInfo, error) = await signer.UpdateLeverageAsync(leverageRequest);

        if (error != null)
        {
            Console.WriteLine($"Update leverage error: {error}");
            return error;
        }

        Console.WriteLine($"Leverage updated successfully!");
        Console.WriteLine($"Transaction Info: {txInfo}");

        return null;
    }
}
