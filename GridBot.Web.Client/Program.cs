using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;
using GridBot.Web.Client;
using GridBot.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Configure HttpClient for API calls
// In Blazor WASM, BaseAddress is the server origin
builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register TradingApiClient
builder.Services.AddScoped<TradingApiClient>();

// Register DashboardStateService
// In WASM, Scoped services are effectively singletons per browser tab
builder.Services.AddScoped<IDashboardStateService, DashboardStateService>();

await builder.Build().RunAsync();
