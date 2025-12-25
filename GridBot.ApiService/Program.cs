using GridBot.ApiService.Components;
using GridBot.ApiService.Extensions;
using GridBot.ApiService.Services.MarketData;
using GridBot.Lighter;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using MudBlazor.Services;
using Scalar.AspNetCore;

public partial class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        if (builder.Environment.IsDevelopment())
        {
            StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
        }

        builder.AddServiceDefaults();
        builder.AddRedisDistributedCache("cache");
        builder.Services.AddLighterClient(builder.Configuration);
        builder.Services.AddTradingBot(builder.Configuration);
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();
        builder.Services.AddMudServices();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();

        app.UseExceptionHandler();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapOpenApi();
        app.MapScalarApiReference();

        var lighter = app.MapGroup("/api/lighter").WithTags("Lighter Trading");

        lighter.MapGet("/markets", async (GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            var result = await client.GetOrderBooksAsync(ct);
            return Results.Ok(result);
        }).WithName("GetMarkets");

        lighter.MapGet("/account/{accountIndex}", async ([Microsoft.AspNetCore.Mvc.FromRoute] long accountIndex, GridBot.Lighter.ILighterQueryClient client, CancellationToken ct) =>
        {
            var result = await client.GetAccountAsync(accountIndex, ct);
            return Results.Ok(result);
        }).WithName("GetAccount");

        var trading = app.MapGroup("/api/trading").WithTags("Trading Dashboard");

        trading.MapGet("/status", (GridBot.Core.Services.Engine.ISimpleTradingEngine engine, Microsoft.Extensions.Options.IOptions<GridBot.Core.Configuration.SimpleGridConfig> config) =>
        {
            var state = engine.State;
            var cfg = config.Value;
            return Results.Ok(new { TradingState = state.State.ToString(), IsRunning = engine.IsRunning, Market = cfg.Market });
        }).WithName("GetTradingStatus");

        trading.MapPost("/control/pause", async (GridBot.Core.Services.Engine.ISimpleTradingEngine engine, CancellationToken ct) =>
        {
            await engine.PauseAsync("Manual pause", ct);
            return Results.Ok(new { Success = true });
        }).WithName("PauseTrading");

        trading.MapPost("/control/resume", async (GridBot.Core.Services.Engine.ISimpleTradingEngine engine, CancellationToken ct) =>
        {
            await engine.ResumeAsync(ct);
            return Results.Ok(new { Success = true });
        }).WithName("ResumeTrading");

        app.MapDefaultEndpoints();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        await app.RunAsync();
    }
}