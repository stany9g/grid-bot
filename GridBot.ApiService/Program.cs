using GridBot.Lighter;
using Scalar.AspNetCore;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add service defaults & Aspire client integrations.
        builder.AddServiceDefaults();

        // Add Lighter client from configuration
        builder.Services.AddLighterClient(builder.Configuration);

        // Add services to the container.
        builder.Services.AddProblemDetails();

        // Learn more about configuring OpenAPI at https://aka.ms/aspnet/opentapi
        builder.Services.AddOpenApi();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        app.UseExceptionHandler();

       
            app.MapOpenApi();
            app.MapScalarApiReference();


        var lighter = app.MapGroup("/api/lighter")
            .WithTags("Lighter Trading");

        // Order Management Endpoints
        lighter.MapPost("/orders", async (GridBot.Lighter.Models.CreateOrderRequest request, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                var result = await client.CreateOrderAsync(request, cancellationToken: ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CreateOrder")
        .WithSummary("Create a new order")
        .WithDescription("Creates and submits a limit, market, stop-loss, or take-profit order.");

        lighter.MapPost("/orders/grouped", async (GridBot.Lighter.Models.CreateGroupedOrdersRequest request, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                var result = await client.CreateGroupedOrdersAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CreateGroupedOrders")
        .WithSummary("Create grouped orders")
        .WithDescription("Creates and submits grouped orders (OCO, OTO, OTOCO).");

        lighter.MapDelete("/orders/{marketId}/{orderId}", async ([Microsoft.AspNetCore.Mvc.FromRoute] int marketId, [Microsoft.AspNetCore.Mvc.FromRoute] long orderId, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.CancelOrderAsync(marketId, orderId, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CancelOrder")
        .WithSummary("Cancel a specific order")
        .WithDescription("Cancels a specific order by market ID and order ID.");

        lighter.MapDelete("/orders/{marketId}/cancel-all", async ([Microsoft.AspNetCore.Mvc.FromRoute] int marketId, [Microsoft.AspNetCore.Mvc.FromQuery] long timeInForce, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.CancelAllOrdersAsync(marketId, timeInForce, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("CancelAllOrders")
        .WithSummary("Cancel all orders in a market")
        .WithDescription("Cancels all orders in a specific market.");

        lighter.MapPut("/orders", async (GridBot.Lighter.Models.ModifyOrderRequest request, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                var result = await client.ModifyOrderAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("ModifyOrder")
        .WithSummary("Modify an existing order")
        .WithDescription("Modifies an existing order's price, size, or other parameters.");

        // Account Management Endpoints
        lighter.MapGet("/account/{accountIndex}", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetAccountAsync(accountIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetAccount")
        .WithSummary("Get account information")
        .WithDescription("Gets account information including positions and balances.");

        lighter.MapGet("/account/{accountIndex}/metadata", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetAccountMetadataAsync(accountIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetAccountMetadata")
        .WithSummary("Get account metadata")
        .WithDescription("Gets account metadata including public key and status.");

        lighter.MapGet("/account/{accountIndex}/orders", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetActiveOrdersAsync(accountIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetActiveOrders")
        .WithSummary("Get active orders")
        .WithDescription("Gets all active orders for an account.");

        // Market Data Endpoints
        lighter.MapGet("/markets", async (GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetOrderBooksAsync(ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetMarkets")
        .WithSummary("Get all markets")
        .WithDescription("Gets order book metadata for all markets.");

        lighter.MapGet("/markets/{marketId}/orderbook", async ([Microsoft.AspNetCore.Mvc.FromRoute] int marketId, [Microsoft.AspNetCore.Mvc.FromQuery] int? depth, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetOrderBookDetailsAsync(marketId, depth, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetOrderBookDetails")
        .WithSummary("Get order book details")
        .WithDescription("Gets detailed order book data for a specific market.");

        // Transaction Endpoints
        lighter.MapGet("/transactions/{hashOrIndex}", async ([Microsoft.AspNetCore.Mvc.FromRoute] string hashOrIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetTransactionAsync(hashOrIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetTransaction")
        .WithSummary("Get transaction details")
        .WithDescription("Gets a transaction by its hash or sequence index.");

        // Leverage Management Endpoints
        lighter.MapPut("/leverage", async (GridBot.Lighter.Models.UpdateLeverageRequest request, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var validationError = request.Validate();
                if (validationError != null)
                    return Results.BadRequest(new { error = validationError });

                var result = await client.UpdateLeverageAsync(request, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("UpdateLeverage")
        .WithSummary("Update position leverage")
        .WithDescription("Updates the leverage for a position in a specific market.");

        // Nonce Management Endpoints
        lighter.MapGet("/nonce", async ([Microsoft.AspNetCore.Mvc.FromQuery] long accountIndex, [Microsoft.AspNetCore.Mvc.FromQuery] int apiKeyIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.GetNextNonceAsync(accountIndex, apiKeyIndex, ct);
                return Results.Ok(result);
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("GetNextNonce")
        .WithSummary("Get next nonce")
        .WithDescription("Gets the next nonce for an account from the server.");

        lighter.MapPost("/nonce/sync", async ([Microsoft.AspNetCore.Mvc.FromQuery] long accountIndex, [Microsoft.AspNetCore.Mvc.FromQuery] int apiKeyIndex, GridBot.Lighter.ILighterCommandClient client, CancellationToken ct) =>
        {
            try
            {
                var result = await client.SyncNonceAsync(accountIndex, apiKeyIndex, ct);
                return Results.Ok(new { nonce = result });
            }
            catch (GridBot.Lighter.LighterApiException ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: ex.StatusCode ?? 500,
                    title: "Lighter API Error");
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Internal Server Error");
            }
        })
        .WithName("SyncNonce")
        .WithSummary("Synchronize nonce")
        .WithDescription("Synchronizes the local signer nonce with the server.");

        app.MapDefaultEndpoints();

        app.Run();
    }
}