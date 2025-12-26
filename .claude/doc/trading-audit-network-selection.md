# Trading Bot Audit: Testnet/Mainnet Network Selection Feature

**Auditor:** Trading Systems Auditor (Crypto Trading Safety Specialist)
**Date:** 2025-12-26
**Session:** context_session_3
**Feature:** Runtime Testnet/Mainnet Network Selection

---

## Executive Summary

This audit examines the testnet/mainnet network selection feature from a crypto trading safety perspective. The feature allows users to switch between Lighter DEX testnet and mainnet at runtime, with separate credentials for each network.

**CRITICAL FINDING: The network selection is currently DISPLAY-ONLY.** The `NetworkSelectionService` tracks which network is "selected" but there is NO mechanism to actually switch the underlying exchange client, WebSocket connections, or signing credentials. The bot will ALWAYS trade on whatever network was configured at startup via `AddLighterExchange()`, regardless of what the UI shows.

---

## Findings

### FINDING 1: Network Selection Has No Effect on Actual Trading

**Risk Level:** HIGH (BLOCKING)
**Category:** Safety
**Location:** `GridBot.ApiService/Program.cs:34` and entire NetworkSelectionService architecture
**Financial Impact:** User could believe they are trading on testnet while actually trading with real funds on mainnet (or vice versa)

**Problem:**
The `NetworkSelectionService` maintains a `_currentNetwork` variable and fires events, but this has ZERO connection to the actual exchange client used for trading. Looking at `Program.cs`:

```csharp
// Line 34: Only registers ONE exchange with ONE configuration
builder.Services.AddLighterExchange(builder.Configuration, "lighter-main");

// Line 37: Registers network OPTIONS, but doesn't use them for trading
builder.Services.AddLighterNetworks(builder.Configuration);
```

The `AddLighterExchange()` method reads from the legacy `"Lighter"` section in appsettings.json (not the new `"LighterNetworks"` section). The `SimpleTradingEngine`, `LighterWebSocketClient`, and all trading operations use `IOptions<LighterOptions>` which is bound ONCE at startup.

**Evidence:**
```csharp
// LighterWebSocketClient.cs:91-98 - Uses fixed LighterOptions
public LighterWebSocketClient(
    SignerClient signerClient,
    IOptions<WebSocketOptions> options,
    IOptions<LighterOptions> lighterOptions,  // <-- Single config, never changes
    ILogger<LighterWebSocketClient> logger)
{
    _lighterOptions = lighterOptions?.Value ?? throw new ArgumentNullException(nameof(lighterOptions));
    // Uses _lighterOptions.ApiUrl to connect - NEVER UPDATED
}
```

The `SimpleTradingEngine` gets its clients via DI:
```csharp
// SimpleTradingEngine.cs:26-42 - Injected once, never re-resolved
public SimpleTradingEngine(
    IGridConfigurationService configService,
    IGridManager gridManager,
    IBasicRiskMonitor riskMonitor,
    IMarketDataClient marketData,      // <-- Singleton, bound to startup config
    IAccountClient accountClient,       // <-- Singleton, bound to startup config
    ILogger<SimpleTradingEngine> logger)
```

**Fix:**
The network selection feature is incomplete. To actually switch networks, you must:

1. Create network-specific keyed services for each network:
```csharp
services.AddKeyedSingleton<IExchangeClient>("lighter-testnet", (sp, key) =>
    CreateExchangeClient(sp, LighterNetworkType.Testnet));
services.AddKeyedSingleton<IExchangeClient>("lighter-mainnet", (sp, key) =>
    CreateExchangeClient(sp, LighterNetworkType.Mainnet));
```

2. Modify `NetworkSelectionService` to:
   - Dispose old WebSocket connections
   - Switch the active keyed service
   - Reinitialize connections to new network

3. Modify trading engine to resolve exchange client dynamically or be recreated on network switch

**Verdict:** FAIL (BLOCKING)

---

### FINDING 2: TOCTOU Race Condition on Bot Status Check

**Risk Level:** HIGH
**Category:** Race Condition
**Location:** `NetworkSelectionService.cs:67-104`
**Financial Impact:** If race occurs, could trade on wrong network after switch, causing fund loss

**Problem:**
The bot status is checked outside the critical section where the network switch occurs. Between checking `_botControlService.IsRunning` and actually switching the network, the bot could start.

**Evidence:**
```csharp
// NetworkSelectionService.cs:67-104
public Task SelectNetworkAsync(LighterNetworkType network, CancellationToken ct = default)
{
    bool networkChanged = false;

    try
    {
        _isSwitchingNetwork = true;  // Flag set

        lock (_lock)
        {
            if (_botControlService.IsRunning)  // Check here
            {
                throw new InvalidOperationException(...);
            }
            // ... but bot could START between check and network switch

            _currentNetwork = network;  // Switch happens here
            networkChanged = true;
        }
        // ...
    }
    finally
    {
        _isSwitchingNetwork = false;  // Flag cleared
    }
}
```

The `GridBotControlService.StartAsync()` does check `_networkSelectionService.IsSwitchingNetwork` but this is still vulnerable:

```csharp
// GridBotControlService.cs:106-112
public async Task StartAsync(CancellationToken ct = default)
{
    // Check if network switch is in progress before attempting to start
    if (_networkSelectionService.IsSwitchingNetwork)  // volatile flag check
    {
        throw new InvalidOperationException("Cannot start bot while network switch is in progress");
    }
    // ... but IsSwitchingNetwork could become false RIGHT AFTER this check

    lock (_lock)
    {
        // Bot continues to start...
    }
}
```

**Fix:**
Implement atomic coordination between services. Options:

1. **Shared Lock Pattern:**
```csharp
// Both services use the same lock object
private static readonly object SharedNetworkBotLock = new();

// In NetworkSelectionService.SelectNetworkAsync:
lock (SharedNetworkBotLock)
{
    if (_botControlService.IsRunningInternal)
        throw new InvalidOperationException(...);
    _currentNetwork = network;
}

// In GridBotControlService.StartAsync:
lock (SharedNetworkBotLock)
{
    if (_networkSelectionService.IsSwitchingNetworkInternal)
        throw new InvalidOperationException(...);
    _status = BotStatus.Starting;
}
```

2. **State Machine Pattern:**
Use a single state machine that manages both network and bot state atomically.

**Verdict:** FAIL

---

### FINDING 3: No WebSocket Cleanup on Network Switch

**Risk Level:** HIGH
**Category:** State Corruption
**Location:** `NetworkSelectionService.cs` (missing), `LighterWebSocketClient.cs`
**Financial Impact:** Stale data from old network could cause incorrect trading decisions

**Problem:**
When the network "switches" (even though it currently doesn't work), there is no mechanism to:
1. Disconnect WebSocket from old network
2. Clear subscription state
3. Clear pending orders state
4. Clear account/balance cache
5. Connect to new network WebSocket
6. Re-authenticate with new credentials
7. Resubscribe to channels

The `LighterWebSocketClient` maintains state that would become stale:
- `_currentAuthToken` and `_authTokenExpiry`
- `_subscriptions` dictionary
- `_pendingRequests` dictionary
- Channel data (order book, account, orders)

**Evidence:**
```csharp
// NetworkSelectionService.cs - NO cleanup happens
if (_currentNetwork == network)
{
    _logger.LogDebug("Network {Network} is already selected", network);
    return Task.CompletedTask;
}

var previousNetwork = _currentNetwork;
_currentNetwork = network;  // <-- Just changes enum, nothing else
networkChanged = true;
```

**Fix:**
Network switch must include proper cleanup:
```csharp
public async Task SelectNetworkAsync(LighterNetworkType network, CancellationToken ct)
{
    // ... validation ...

    // 1. Disconnect old WebSocket
    var oldWsClient = GetWebSocketClientForNetwork(_currentNetwork);
    await oldWsClient.DisconnectAsync(ct);

    // 2. Clear all cached state
    var oldRealtimeState = GetRealtimeStateForNetwork(_currentNetwork);
    oldRealtimeState.ClearAll();

    // 3. Switch network
    _currentNetwork = network;

    // 4. Connect new WebSocket
    var newWsClient = GetWebSocketClientForNetwork(network);
    await newWsClient.ConnectAsync(ct);

    // 5. Re-authenticate and subscribe
    await newWsClient.SubscribeAccountAsync(ct);
    await newWsClient.SubscribeOrdersAsync(ct);
    // etc.
}
```

**Verdict:** FAIL

---

### FINDING 4: Nonce Sequences Not Isolated Per Network

**Risk Level:** HIGH
**Category:** Safety
**Location:** `LighterOptions.cs:43`, `SignerClient` (implicit)
**Financial Impact:** Nonce collision could cause transaction rejection or replay attacks

**Problem:**
Each Lighter network maintains its own nonce sequence. The current implementation uses a single `SignerClient` instance which tracks a single nonce. If networks could actually be switched:
- Testnet nonce != Mainnet nonce
- Using testnet nonce on mainnet would cause "invalid nonce" errors
- Worse: if nonces happen to overlap, transactions could be accepted unexpectedly

**Evidence:**
From appsettings.json structure:
```json
{
  "LighterNetworks": {
    "Testnet": {
      "InitialNonce": 0,  // Testnet starts at 0
      "AccountIndex": 363
    },
    "Mainnet": {
      "InitialNonce": 0,  // Mainnet also starts at 0 but has different actual nonce on chain
      "AccountIndex": 123
    }
  }
}
```

The `SignerClient` is registered as singleton and initialized once with one nonce value.

**Fix:**
Each network needs its own `SignerClient` instance with independent nonce tracking:
```csharp
services.AddKeyedSingleton<SignerClient>("lighter-testnet", (sp, key) =>
    new SignerClient(testnetOptions.PrivateKey, testnetOptions.ChainId));
services.AddKeyedSingleton<SignerClient>("lighter-mainnet", (sp, key) =>
    new SignerClient(mainnetOptions.PrivateKey, mainnetOptions.ChainId));
```

**Verdict:** FAIL

---

### FINDING 5: Chain ID Mismatch Detection is Advisory Only

**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `LighterNetworksOptions.cs:60-72`, `NetworkSelectionService.cs:196-223`
**Financial Impact:** Misconfigured chain ID could cause signature failures or unexpected behavior

**Problem:**
The code detects chain ID mismatches (e.g., testnet config with mainnet chain ID) but only reports it as a status message. It does not BLOCK switching to a misconfigured network.

**Evidence:**
```csharp
// LighterNetworksOptions.cs - Only checks PrivateKey, ApiUrl, AccountIndex, ChainId > 0
public bool IsNetworkConfigured(LighterNetworkType network)
{
    var options = GetNetwork(network);
    if (options is null)
        return false;

    return !string.IsNullOrWhiteSpace(options.PrivateKey)
        && !string.IsNullOrWhiteSpace(options.ApiUrl)
        && options.AccountIndex > 0
        && options.ChainId > 0;  // <-- Only checks > 0, not correct value
}

// NetworkSelectionService.cs - Chain ID mismatch is just a status message
private string GetStatusMessage(LighterNetworkType network, bool isConfigured)
{
    // ...
    if (chainId != expectedChainId)
    {
        return $"Chain ID mismatch: expected {expectedChainId}, got {chainId}";  // Advisory only
    }
    return "Ready";
}
```

**Fix:**
Add validation that blocks switching to misconfigured networks:
```csharp
public bool IsNetworkConfigured(LighterNetworkType network)
{
    var options = GetNetwork(network);
    if (options is null) return false;

    var expectedChainId = network switch
    {
        LighterNetworkType.Testnet => 300,
        LighterNetworkType.Mainnet => 304,
        _ => 0
    };

    return !string.IsNullOrWhiteSpace(options.PrivateKey)
        && !string.IsNullOrWhiteSpace(options.ApiUrl)
        && options.AccountIndex > 0
        && options.ChainId == expectedChainId;  // Strict validation
}
```

**Verdict:** CONDITIONAL PASS (if network selection worked, this would be HIGH)

---

### FINDING 6: UI Shows Mainnet Warning but No Confirmation Dialog

**Risk Level:** MEDIUM
**Category:** Safety
**Location:** `NetworkSelector.razor:145-147`, `BotControlPanel.razor:176-185`
**Financial Impact:** User could accidentally switch to mainnet without realizing consequences

**Problem:**
Switching to mainnet only shows a Snackbar warning AFTER the switch completes. There is no confirmation dialog BEFORE switching that requires explicit user acknowledgment.

**Evidence:**
```csharp
// NetworkSelector.razor:145-147
if (networkType.Value == LighterNetworkType.Mainnet)
{
    Snackbar.Add($"Switched to {displayName} - CAUTION: Real funds!", Severity.Warning);
}
```

This is post-facto notification, not a preventive measure.

**Fix:**
Add confirmation dialog before switching to mainnet:
```csharp
private async Task OnNetworkSelected(LighterNetworkType? networkType)
{
    if (networkType == LighterNetworkType.Mainnet)
    {
        var confirmed = await DialogService.ShowMessageBox(
            "Switch to Mainnet?",
            "WARNING: You are about to switch to MAINNET. " +
            "This will use REAL FUNDS. Are you sure?",
            yesText: "Yes, switch to Mainnet",
            cancelText: "Cancel");

        if (confirmed != true) return;
    }

    await NetworkSelection.SelectNetworkAsync(networkType.Value);
}
```

**Verdict:** CONDITIONAL PASS

---

### FINDING 7: Startup Defaults to Testnet Even if Only Mainnet Configured

**Risk Level:** LOW
**Category:** Safety
**Location:** `NetworkSelectionService.cs:153-186`, `LighterNetworksOptions.cs:17`
**Financial Impact:** Confusion about which network is active; potential for trading on wrong network

**Problem:**
The `DefaultNetwork` defaults to "Testnet". If testnet is not configured but mainnet is, the initialization logic will:
1. Try testnet first (fails)
2. Fall back to first configured network (mainnet)
3. Log a warning, but continue

This silent fallback could confuse users.

**Evidence:**
```csharp
// LighterNetworksOptions.cs:17
public string DefaultNetwork { get; set; } = "Testnet";

// NetworkSelectionService.cs:163-176
if (!_networksOptions.IsNetworkConfigured(defaultNetwork))
{
    // Try to find any configured network as fallback
    foreach (var networkType in Enum.GetValues<LighterNetworkType>())
    {
        if (_networksOptions.IsNetworkConfigured(networkType))
        {
            _currentNetwork = networkType;
            _logger.LogWarning(
                "Default network {DefaultNetwork} is not configured, using {FallbackNetwork} instead",
                defaultNetwork,
                networkType);
            break;
        }
    }
}
```

**Fix:**
Either:
1. Fail loudly if default network is not configured
2. Or require explicit configuration of which network to use
3. Or always show a network selection dialog on startup if default unavailable

**Verdict:** PASS (acceptable trade-off for usability)

---

### FINDING 8: Private Key Exposure in appsettings.json

**Risk Level:** HIGH
**Category:** Safety / Credential Leakage
**Location:** `GridBot.ApiService/appsettings.json:21`
**Financial Impact:** Total loss of funds if private key is compromised

**Problem:**
The testnet private key is stored in plain text in `appsettings.json` which is typically committed to source control.

**Evidence:**
```json
// appsettings.json:21
"PrivateKey": "0ce5443bc5b07220f3a5786b488cf305deead6057d1e99fbec6cfa1656be72ddd3e73a77bf617d15",
```

**Fix:**
1. Remove private key from appsettings.json
2. Use User Secrets for development:
   ```bash
   dotnet user-secrets set "LighterNetworks:Testnet:PrivateKey" "your-key"
   dotnet user-secrets set "LighterNetworks:Mainnet:PrivateKey" "your-key"
   ```
3. Use Azure Key Vault or similar for production
4. Add `appsettings.json` with private keys to `.gitignore`

**Note:** The mainnet section shows `"PrivateKey": ""` which is correct - it should never be in source.

**Verdict:** FAIL (testnet key exposure is acceptable for development but should not be in committed code)

---

## Answers to Specific Questions

### 1. Are there any scenarios where the bot could trade on the wrong network?

**YES - CRITICAL.** The bot will ALWAYS trade on the network configured at startup via the `"Lighter"` section, NOT the `"LighterNetworks"` section. The `NetworkSelectionService` is display-only and has no effect on actual trading.

Scenarios:
- User selects "Mainnet" in UI, but bot trades on testnet (if startup config was testnet)
- User believes they're on testnet (UI shows testnet), but startup config points to mainnet

### 2. Is there sufficient protection against accidental mainnet trading?

**NO.**
- No confirmation dialog before switching to mainnet
- The network selection doesn't actually work (Finding 1)
- The startup configuration determines actual network, not UI selection
- Mainnet warning banner exists but is cosmetic since selection is non-functional

### 3. What happens to WebSocket connections when network switches?

**NOTHING.** WebSocket connections are not touched during network "switch" because:
- The switch only changes an enum value in `NetworkSelectionService`
- `LighterWebSocketClient` is a singleton bound to startup config
- No disconnect/reconnect logic exists for network switching
- All subscriptions, auth tokens, and cached data remain from original network

### 4. Are there any race conditions that could cause fund loss?

**YES.**
- TOCTOU race between bot status check and network switch (Finding 2)
- If network selection worked, starting bot during switch could use wrong credentials
- The `_isSwitchingNetwork` flag check is not atomic with status change

---

## Summary

```
===========================================
AUDIT SUMMARY
===========================================
Total Findings: 8
+-- HIGH Risk: 5 (BLOCKING)
+-- MEDIUM Risk: 2
+-- LOW Risk: 1

Overall Verdict: FAIL

Deployment Recommendation: DO NOT DEPLOY
===========================================
```

**Critical Issues:**
1. **Network selection is non-functional** - This is the showstopper. The feature appears to work in the UI but has no effect on actual trading operations.
2. **Race conditions** exist that could cause trading on wrong network if the feature were completed.
3. **No WebSocket/state cleanup** - Network switch would leave stale data.
4. **Nonce isolation missing** - Each network needs independent nonce tracking.

**Before This Feature Can Ship:**
1. Complete the network switching implementation with keyed services per network
2. Implement proper WebSocket disconnect/reconnect on network switch
3. Add atomic coordination between network switch and bot start
4. Add mainnet confirmation dialog
5. Move testnet private key to user secrets
6. Add strict chain ID validation

**Immediate Mitigation:**
- Hide or disable the network selector UI until the feature is properly implemented
- Add a banner warning that network selection is not yet functional
- Ensure the startup `"Lighter"` section points to testnet for safety

---

## Files Reviewed

| File | Status |
|------|--------|
| `GridBot.ApiService/Services/Network/NetworkSelectionService.cs` | FAIL |
| `GridBot.ApiService/Services/Network/INetworkSelectionService.cs` | N/A (interface) |
| `GridBot.ApiService/Services/Bot/GridBotControlService.cs` | FAIL (race) |
| `GridBot.Lighter/LighterNetworksOptions.cs` | CONDITIONAL PASS |
| `GridBot.ApiService/appsettings.json` | FAIL (private key) |
| `GridBot.ApiService/Components/Dashboard/NetworkSelector.razor` | CONDITIONAL PASS |
| `GridBot.ApiService/Components/Dashboard/BotControlPanel.razor` | PASS |
| `GridBot.Lighter/LighterWebSocketClient.cs` | N/A (no changes needed) |
| `GridBot.Core/Services/Engine/SimpleTradingEngine.cs` | N/A (no changes needed) |
| `GridBot.Lighter/Extensions/LighterAbstractionsExtensions.cs` | FAIL (incomplete) |
| `GridBot.ApiService/Program.cs` | FAIL (only one network registered) |
