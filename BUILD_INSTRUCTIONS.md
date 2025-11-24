# Build Instructions for GridBot with Lighter Integration

## Prerequisites

- **.NET 10.0 SDK** - Your project targets .NET 10.0
- Download from: https://dotnet.microsoft.com/download/dotnet/10.0

## Quick Build

From the solution root:

```bash
# Restore all dependencies
dotnet restore

# Build the entire solution
dotnet build

# Or build specific projects
dotnet build GridBot.Lighter/GridBot.Lighter.csproj
dotnet build GridBot.ApiService/GridBot.ApiService.csproj
```

## What Was Added

### GridBot.Lighter Project

**New NuGet Packages** (added to GridBot.Lighter.csproj):
- `Microsoft.Extensions.Configuration.Abstractions` v9.0.0
- `Microsoft.Extensions.DependencyInjection.Abstractions` v9.0.0
- `Microsoft.Extensions.Options` v9.0.0

These packages enable configuration and dependency injection support.

### New Files Created

1. **API Client Layer**
   - `Api/LighterApiClient.cs` - REST API client
   - `Models/Api/*.cs` - 10 response models
   - `Models/TransactionTypes.cs` - Transaction constants

2. **Configuration Support**
   - `LighterOptions.cs` - Configuration model
   - `LighterServiceCollectionExtensions.cs` - DI extensions
   - `CONFIGURATION.md` - Setup guide

3. **Integration Layer**
   - `LighterClient.cs` - Unified client (signer + API)

4. **Examples**
   - `Examples/LighterConfigurationExample.cs` - DI usage examples
   - Updated `Examples/SignerUsageExample.cs` - End-to-end workflows

5. **Documentation**
   - Updated `README.md` - Complete API documentation
   - `CONFIGURATION.md` - Configuration guide

## Configuration Setup

Your `appsettings.Development.json` is already configured with testnet:

```json
{
  "Lighter": {
    "ApiUrl": "https://testnet.zklighter.elliot.ai",
    "ChainId": 300,
    "ApiKeyIndex": 5,
    "AccountIndex": 363,
    "InitialNonce": 0
  }
}
```

**Private key is set** (visible in your config) - make sure to use User Secrets in production!

## Enable Lighter in ApiService

Uncomment this line in `GridBot.ApiService/Program.cs`:

```csharp
// Currently commented out (line 8):
// builder.Services.AddLighterClient(builder.Configuration);

// Uncomment to enable:
builder.Services.AddLighterClient(builder.Configuration);
```

## Verify Build

After building, verify the native libraries are copied:

```bash
# Check that native signing library exists
ls GridBot.Lighter/bin/Debug/net10.0/Native/

# Expected output:
# signer-amd64.dll (Windows)
# signer-amd64.so (Linux)
```

## Run the Application

```bash
# Run the entire Aspire application
dotnet run --project GridBot.AppHost

# Or run ApiService standalone
dotnet run --project GridBot.ApiService
```

## Troubleshooting

### "The current .NET SDK does not support targeting .NET 10.0"
- Install .NET 10.0 SDK from https://dotnet.microsoft.com/download/dotnet/10.0
- Verify: `dotnet --version` should show 10.0.x

### "Missing 'Lighter' configuration section"
- Ensure `appsettings.json` or `appsettings.Development.json` contains the `Lighter` section
- Your Development config is already set up!

### "Native library not found"
- Verify `Native/signer-amd64.dll` or `.so` exists in `GridBot.Lighter/Native/`
- Check that files are marked to copy to output directory in `.csproj`

### Package restore issues
```bash
# Clear package cache
dotnet nuget locals all --clear

# Restore again
dotnet restore
```

## Project Structure After Changes

```
GridBot.Lighter/
├── Api/
│   └── LighterApiClient.cs          ✨ NEW
├── Models/
│   ├── Api/                          ✨ NEW (10 files)
│   ├── TransactionTypes.cs          ✨ NEW
│   ├── Enums.cs
│   └── OrderRequest.cs
├── Native/
│   ├── signer-amd64.dll
│   └── signer-amd64.so
├── Examples/
│   └── LighterConfigurationExample.cs ✨ NEW
├── LighterClient.cs                  ✨ NEW
├── LighterOptions.cs                 ✨ NEW
├── LighterServiceCollectionExtensions.cs ✨ NEW
├── SignerClient.cs
├── README.md                         📝 UPDATED
├── CONFIGURATION.md                  ✨ NEW
└── GridBot.Lighter.csproj           📝 UPDATED
```

## Next Steps

1. **Build the solution** with `dotnet build`
2. **Uncomment** the AddLighterClient line in Program.cs
3. **Run** the application with `dotnet run --project GridBot.AppHost`
4. **Test** the integration using the examples in `SignerUsageExample.cs`

Your testnet configuration is ready to go! 🚀
