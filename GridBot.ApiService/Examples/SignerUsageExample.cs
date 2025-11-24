//using GridBot.Lighter;
//using GridBot.Lighter.Api;
//using GridBot.Lighter.Models;
//using GridBot.Lighter.Models.Api;

//namespace GridBot.ApiService.Examples;

///// <summary>
///// Example usage of the Lighter client library for cryptocurrency trading operations.
///// Includes both low-level signing examples and high-level end-to-end workflows.
///// </summary>
//public static class SignerUsageExample
//{
//    /// <summary>
//    /// Example: Generate a new API key pair.
//    /// </summary>
//    public static async Task<(string? privateKey, string? publicKey, string? error)> GenerateNewApiKeyExample()
//    {
//        // Generate a new key pair (optionally with a seed for deterministic generation)
//        var (privateKey, publicKey, error) = await SignerClient.GenerateApiKeyAsync(seed: null);

//        if (error != null)
//        {
//            Console.WriteLine($"Error generating key: {error}");
//            return (null, null, error);
//        }

//        Console.WriteLine($"Generated Key Pair:");
//        Console.WriteLine($"Private Key: {privateKey}");
//        Console.WriteLine($"Public Key: {publicKey}");

//        return (privateKey, publicKey, null);
//    }

//    /// <summary>
//    /// Example: Initialize the signer client and create a limit order.
//    /// </summary>
//    public static async Task<string?> CreateLimitOrderExample()
//    {
//        using var signer = new SignerClient();

//        // Initialize the client
//        var initError = await signer.InitializeAsync(
//            url: "https://api.lighter.xyz",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet,
//            apiKeyIndex: 0,
//            accountIndex: 0,
//            initialNonce: 0
//        );

//        if (initError != null)
//        {
//            Console.WriteLine($"Initialization error: {initError}");
//            return initError;
//        }

//        // Create a limit order request
//        var orderRequest = new CreateOrderRequest
//        {
//            MarketIndex = 0, // BTC-USDC
//            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), // Unique order ID
//            BaseAmount = 1_000_000, // 0.01 BTC (scaled by 100M)
//            Price = 50_000, // $50,000 (scaled appropriately)
//            IsAsk = false, // Buy order
//            OrderType = OrderType.Limit,
//            TimeInForce = TimeInForce.GoodTillTime,
//            ReduceOnly = false,
//            TriggerPrice = OrderConstants.NilTriggerPrice,
//            OrderExpiry = OrderConstants.Default28DayOrderExpiry
//        };

//        // Sign the order
//        var (txInfo, error) = await signer.CreateOrderAsync(orderRequest);

//        if (error != null)
//        {
//            Console.WriteLine($"Order signing error: {error}");
//            return error;
//        }

//        Console.WriteLine($"Order signed successfully!");
//        Console.WriteLine($"Transaction Info: {txInfo}");

//        return null;
//    }

//    /// <summary>
//    /// Example: Create a One-Cancels-Other (OCO) order pair.
//    /// </summary>
//    public static async Task<string?> CreateOcoOrderExample()
//    {
//        using var signer = new SignerClient();

//        // Initialize (same as above)
//        var initError = await signer.InitializeAsync(
//            url: "https://api.lighter.xyz",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet
//        );

//        if (initError != null) return initError;

//        // Create OCO order: One take-profit, one stop-loss
//        var ocoRequest = new CreateGroupedOrdersRequest
//        {
//            GroupingType = GroupingType.OneCancelOther,
//            Orders = new List<CreateOrderRequest>
//            {
//                // Take-profit order
//                new CreateOrderRequest
//                {
//                    MarketIndex = 0,
//                    ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
//                    BaseAmount = 1_000_000,
//                    Price = 55_000, // Sell at $55k
//                    IsAsk = true,
//                    OrderType = OrderType.TakeProfitLimit,
//                    TimeInForce = TimeInForce.GoodTillTime
//                },
//                // Stop-loss order
//                new CreateOrderRequest
//                {
//                    MarketIndex = 0,
//                    ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1,
//                    BaseAmount = 1_000_000,
//                    Price = 45_000, // Sell at $45k
//                    IsAsk = true,
//                    OrderType = OrderType.StopLossLimit,
//                    TimeInForce = TimeInForce.GoodTillTime,
//                    TriggerPrice = 45_000
//                }
//            }
//        };

//        var (txInfo, error) = await signer.CreateGroupedOrdersAsync(ocoRequest);

//        if (error != null)
//        {
//            Console.WriteLine($"OCO order error: {error}");
//            return error;
//        }

//        Console.WriteLine($"OCO order signed successfully!");
//        Console.WriteLine($"Transaction Info: {txInfo}");

//        return null;
//    }

//    /// <summary>
//    /// Example: Cancel a specific order.
//    /// </summary>
//    public static async Task<string?> CancelOrderExample(long orderId)
//    {
//        using var signer = new SignerClient();

//        var initError = await signer.InitializeAsync(
//            url: "https://api.lighter.xyz",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet
//        );

//        if (initError != null) return initError;

//        // Cancel the order
//        var (txInfo, error) = await signer.CancelOrderAsync(
//            marketIndex: 0,
//            orderId: orderId
//        );

//        if (error != null)
//        {
//            Console.WriteLine($"Cancel order error: {error}");
//            return error;
//        }

//        Console.WriteLine($"Order cancelled successfully!");
//        Console.WriteLine($"Transaction Info: {txInfo}");

//        return null;
//    }

//    /// <summary>
//    /// Example: Update position leverage.
//    /// </summary>
//    public static async Task<string?> UpdateLeverageExample()
//    {
//        using var signer = new SignerClient();

//        var initError = await signer.InitializeAsync(
//            url: "https://api.lighter.xyz",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet
//        );

//        if (initError != null) return initError;

//        // Update leverage to 10x isolated
//        var leverageRequest = new UpdateLeverageRequest
//        {
//            MarketIndex = 0,
//            MarginMode = MarginMode.Isolated,
//            Leverage = 10
//        };

//        var (txInfo, error) = await signer.UpdateLeverageAsync(leverageRequest);

//        if (error != null)
//        {
//            Console.WriteLine($"Update leverage error: {error}");
//            return error;
//        }

//        Console.WriteLine($"Leverage updated successfully!");
//        Console.WriteLine($"Transaction Info: {txInfo}");

//        return null;
//    }

//    #region End-to-End API Integration Examples

//    /// <summary>
//    /// Example: Complete end-to-end order creation using LighterClient.
//    /// This combines signing and API submission in a single operation.
//    /// </summary>
//    public static async Task EndToEndCreateOrderExample()
//    {
//        using var client = new LighterClient();

//        // Initialize the signer component
//        var initError = await client.Signer.InitializeAsync(
//            url: "https://mainnet.zklighter.elliot.ai",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet,
//            apiKeyIndex: 0,
//            accountIndex: 123456,
//            initialNonce: 0
//        );

//        if (initError != null)
//        {
//            Console.WriteLine($"Initialization error: {initError}");
//            return;
//        }

//        // Create order request
//        var orderRequest = new CreateOrderRequest
//        {
//            MarketIndex = 0,
//            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
//            BaseAmount = 1_000_000,
//            Price = 50_000,
//            IsAsk = false,
//            OrderType = OrderType.Limit,
//            TimeInForce = TimeInForce.GoodTillTime,
//            OrderExpiry = OrderConstants.Default28DayOrderExpiry
//        };

//        try
//        {
//            // Sign and submit in one call
//            var response = await client.CreateOrderAsync(orderRequest);

//            Console.WriteLine($"✅ Order submitted successfully!");
//            Console.WriteLine($"Transaction Hash: {response.TxHash}");
//            Console.WriteLine($"Predicted Execution: {response.PredictedExecutionTimeMs}ms");
//        }
//        catch (LighterApiException ex)
//        {
//            Console.WriteLine($"❌ Order failed: {ex.Message}");
//            Console.WriteLine($"Status Code: {ex.StatusCode}");
//            Console.WriteLine($"Error Code: {ex.ErrorCode}");
//        }
//    }

//    /// <summary>
//    /// Example: Query account information and active orders.
//    /// </summary>
//    public static async Task QueryAccountDataExample()
//    {
//        using var client = new LighterClient();

//        long accountIndex = 123456;

//        try
//        {
//            // Get account information
//            var account = await client.GetAccountAsync(accountIndex);
//            Console.WriteLine($"Account {accountIndex}:");
//            Console.WriteLine($"  Status: {account.Status}");
//            Console.WriteLine($"  Collateral: {account.Collateral}");
//            Console.WriteLine($"  Available Balance: {account.AvailableBalance}");
//            Console.WriteLine($"  Total Orders: {account.TotalOrderCount}");
//            Console.WriteLine($"  Pending Orders: {account.PendingOrderCount}");

//            if (account.Positions?.Count > 0)
//            {
//                Console.WriteLine($"\nPositions:");
//                foreach (var position in account.Positions)
//                {
//                    Console.WriteLine($"  Market {position.MarketId}:");
//                    Console.WriteLine($"    Size: {position.Size}");
//                    Console.WriteLine($"    Entry Price: {position.EntryPrice}");
//                    Console.WriteLine($"    Unrealized PnL: {position.UnrealizedPnl}");
//                }
//            }

//            // Get active orders
//            var activeOrders = await client.GetActiveOrdersAsync(accountIndex);
//            Console.WriteLine($"\nActive Orders: {activeOrders.Count}");

//            foreach (var order in activeOrders.Take(5))
//            {
//                Console.WriteLine($"  Order {order.OrderId}:");
//                Console.WriteLine($"    Type: {order.Type}, Side: {order.Side}");
//                Console.WriteLine($"    Price: {order.Price}");
//                Console.WriteLine($"    Remaining: {order.RemainingBaseAmount}");
//                Console.WriteLine($"    Status: {order.Status}");
//            }
//        }
//        catch (LighterApiException ex)
//        {
//            Console.WriteLine($"❌ Query failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Example: Get market data and order book information.
//    /// </summary>
//    public static async Task QueryMarketDataExample()
//    {
//        using var api = new LighterApiClient();

//        try
//        {
//            // Get all available markets
//            var orderBooks = await api.GetOrderBooksAsync();
//            Console.WriteLine($"Available Markets: {orderBooks.Count}");

//            foreach (var book in orderBooks.Take(5))
//            {
//                Console.WriteLine($"\n{book.Symbol} (Market ID: {book.MarketId})");
//                Console.WriteLine($"  Status: {book.Status}");
//                Console.WriteLine($"  Maker Fee: {book.MakerFee}");
//                Console.WriteLine($"  Taker Fee: {book.TakerFee}");
//                Console.WriteLine($"  Min Size: {book.MinBaseAmount}");
//                Console.WriteLine($"  Min Quote: {book.MinQuoteAmount}");
//                Console.WriteLine($"  Max Leverage: {book.MaxLeverage ?? "N/A"}");
//            }

//            // Get detailed order book for BTC-USDC
//            Console.WriteLine($"\n--- Order Book Depth (BTC-USDC) ---");
//            var bookDetails = await api.GetOrderBookDetailsAsync(marketId: 0, depth: 5);

//            Console.WriteLine($"Best Bids:");
//            foreach (var bid in bookDetails.Bids.Take(5))
//            {
//                Console.WriteLine($"  {bid.Price} x {bid.Size}");
//            }

//            Console.WriteLine($"\nBest Asks:");
//            foreach (var ask in bookDetails.Asks.Take(5))
//            {
//                Console.WriteLine($"  {ask.Price} x {ask.Size}");
//            }

//            if (bookDetails.Bids.Any() && bookDetails.Asks.Any())
//            {
//                var spread = decimal.Parse(bookDetails.Asks[0].Price) - decimal.Parse(bookDetails.Bids[0].Price);
//                Console.WriteLine($"\nSpread: {spread}");
//            }
//        }
//        catch (LighterApiException ex)
//        {
//            Console.WriteLine($"❌ Query failed: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Example: Complete trading workflow with error handling and nonce recovery.
//    /// </summary>
//    public static async Task CompleteWorkflowExample()
//    {
//        using var client = new LighterClient();

//        long accountIndex = 123456;
//        int apiKeyIndex = 0;

//        // Initialize
//        var initError = await client.Signer.InitializeAsync(
//            url: "https://mainnet.zklighter.elliot.ai",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet,
//            apiKeyIndex: apiKeyIndex,
//            accountIndex: accountIndex,
//            initialNonce: 0
//        );

//        if (initError != null)
//        {
//            Console.WriteLine($"Initialization failed: {initError}");
//            return;
//        }

//        try
//        {
//            // Step 1: Sync nonce with server
//            Console.WriteLine("Syncing nonce with server...");
//            var nonce = await client.SyncNonceAsync(accountIndex, apiKeyIndex);
//            Console.WriteLine($"Nonce synced: {nonce}");

//            // Step 2: Get account info
//            Console.WriteLine("\nFetching account information...");
//            var account = await client.GetAccountAsync(accountIndex);
//            Console.WriteLine($"Available Balance: {account.AvailableBalance}");

//            // Step 3: Get market data
//            Console.WriteLine("\nFetching market data...");
//            var orderBooks = await client.GetOrderBooksAsync();
//            var btcMarket = orderBooks.FirstOrDefault(b => b.Symbol == "BTC-USDC");
//            if (btcMarket == null)
//            {
//                Console.WriteLine("BTC-USDC market not found");
//                return;
//            }

//            // Step 4: Create and submit order
//            Console.WriteLine("\nCreating limit order...");
//            var orderRequest = new CreateOrderRequest
//            {
//                MarketIndex = btcMarket.MarketId,
//                ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
//                BaseAmount = 1_000_000,
//                Price = 50_000,
//                IsAsk = false,
//                OrderType = OrderType.Limit,
//                TimeInForce = TimeInForce.GoodTillTime,
//                OrderExpiry = OrderConstants.Default28DayOrderExpiry
//            };

//            var response = await client.CreateOrderAsync(orderRequest);
//            Console.WriteLine($"✅ Order created! Hash: {response.TxHash}");

//            // Step 5: Wait a bit and check active orders
//            Console.WriteLine("\nWaiting 2 seconds...");
//            await Task.Delay(2000);

//            var activeOrders = await client.GetActiveOrdersAsync(accountIndex);
//            Console.WriteLine($"Active orders: {activeOrders.Count}");

//            // Step 6: Cancel the order if it exists
//            var ourOrder = activeOrders.FirstOrDefault(o =>
//                o.ClientOrderIndex == orderRequest.ClientOrderIndex);

//            if (ourOrder != null)
//            {
//                Console.WriteLine($"\nCancelling order {ourOrder.OrderId}...");
//                var cancelResponse = await client.CancelOrderAsync(
//                    btcMarket.MarketId,
//                    long.Parse(ourOrder.OrderId)
//                );
//                Console.WriteLine($"✅ Order cancelled! Hash: {cancelResponse.TxHash}");
//            }

//            Console.WriteLine("\n✅ Complete workflow finished successfully!");
//        }
//        catch (LighterApiException ex) when (ex.Message.Contains("nonce"))
//        {
//            Console.WriteLine($"❌ Nonce error: {ex.Message}");
//            Console.WriteLine("Attempting nonce recovery...");

//            try
//            {
//                await client.SyncNonceAsync(accountIndex, apiKeyIndex);
//                Console.WriteLine("Nonce recovered. Please retry the operation.");
//            }
//            catch (Exception recoverEx)
//            {
//                Console.WriteLine($"❌ Nonce recovery failed: {recoverEx.Message}");
//            }
//        }
//        catch (LighterApiException ex)
//        {
//            Console.WriteLine($"❌ API Error: {ex.Message}");
//            Console.WriteLine($"Status Code: {ex.StatusCode}");
//            Console.WriteLine($"Error Code: {ex.ErrorCode}");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Unexpected error: {ex.Message}");
//        }
//    }

//    /// <summary>
//    /// Example: Using separate components (SignerClient + LighterApiClient).
//    /// For advanced users who need more control.
//    /// </summary>
//    public static async Task SeparateComponentsExample()
//    {
//        using var signer = new SignerClient();
//        using var api = new LighterApiClient();

//        // Initialize signer
//        var initError = await signer.InitializeAsync(
//            url: "https://mainnet.zklighter.elliot.ai",
//            privateKey: "your-api-private-key-here",
//            chainId: ChainId.Mainnet,
//            apiKeyIndex: 0,
//            accountIndex: 123456,
//            initialNonce: 0
//        );

//        if (initError != null)
//        {
//            Console.WriteLine($"Init error: {initError}");
//            return;
//        }

//        // Create order request
//        var orderRequest = new CreateOrderRequest
//        {
//            MarketIndex = 0,
//            ClientOrderIndex = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
//            BaseAmount = 1_000_000,
//            Price = 50_000,
//            IsAsk = false,
//            OrderType = OrderType.Limit,
//            TimeInForce = TimeInForce.GoodTillTime,
//            OrderExpiry = OrderConstants.Default28DayOrderExpiry
//        };

//        try
//        {
//            // Step 1: Sign locally
//            Console.WriteLine("Signing transaction locally...");
//            var (txInfo, error) = await signer.CreateOrderAsync(orderRequest);

//            if (error != null)
//            {
//                Console.WriteLine($"Signing error: {error}");
//                return;
//            }

//            Console.WriteLine($"Transaction signed: {txInfo?[..50]}...");

//            // Step 2: Submit to API
//            Console.WriteLine("Submitting to API...");
//            var response = await api.SendTransactionAsync(
//                TransactionTypes.CreateOrder,
//                txInfo!,
//                priceProtection: true
//            );

//            Console.WriteLine($"✅ Transaction submitted!");
//            Console.WriteLine($"Hash: {response.TxHash}");
//            Console.WriteLine($"Predicted Execution: {response.PredictedExecutionTimeMs}ms");
//        }
//        catch (LighterApiException ex)
//        {
//            Console.WriteLine($"❌ API Error: {ex.Message}");
//        }
//    }

//    #endregion
//}
