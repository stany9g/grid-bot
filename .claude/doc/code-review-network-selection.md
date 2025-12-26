# Code Review: Testnet/Mainnet Network Selection Feature

## Review Summary

**Date:** 2025-12-26
**Reviewer:** csharp-code-reviewer
**Status:** Issues Found - Requires Fixes

---

## Issues Found

### [HIGH] Race Condition: Event Raised Outside Lock in NetworkSelectionService

**Location:** `NetworkSelectionService.SelectNetworkAsync` (lines 61-94)

**Problem:** The `NetworkChanged` event is fired **outside** the lock, after `_currentNetwork` has been modified. This creates a race condition:

1. Thread A enters lock, changes network from Testnet to Mainnet, exits lock
2. Thread A starts invoking `NetworkChanged` event
3. Thread B enters lock, reads `_currentNetwork` as Mainnet
4. Thread B tries to change to Testnet, but...
5. Event handlers from Thread A's change are still running, reading stale state

Additionally, between the lock release and event invocation, another caller could:
- Read a different `CurrentNetwork` than what the event reports
- Start another network switch before handlers process the first one

**Fix:**
```csharp
public Task SelectNetworkAsync(LighterNetworkType network, CancellationToken ct = default)
{
    bool networkChanged = false;

    lock (_lock)
    {
        if (_botControlService.IsRunning)
        {
            throw new InvalidOperationException(
                "Cannot switch network while bot is running. Stop the bot first.");
        }

        if (!_networksOptions.IsNetworkConfigured(network))
        {
            throw new ArgumentException(
                $"Network {network} is not configured. Please add valid credentials in appsettings.",
                nameof(network));
        }

        if (_currentNetwork == network)
        {
            _logger.LogDebug("Network {Network} is already selected", network);
            return Task.CompletedTask;
        }

        var previousNetwork = _currentNetwork;
        _currentNetwork = network;
        networkChanged = true;

        _logger.LogInformation(
            "Network switched from {PreviousNetwork} to {NewNetwork}",
            previousNetwork,
            network);
    }

    // Fire event outside lock to prevent deadlock, but only if we changed
    if (networkChanged)
    {
        NetworkChanged?.Invoke(this, network);
    }

    return Task.CompletedTask;
}
```

Note: Firing events outside the lock is actually correct pattern to prevent deadlocks (handlers might call back into the service). The key fix is using a flag to ensure event is only fired when change actually occurred.

---

### [HIGH] TOCTOU Race Condition: Bot Status Check

**Location:** `NetworkSelectionService.SelectNetworkAsync` (line 65)

**Problem:** Time-of-check to time-of-use (TOCTOU) vulnerability. The bot status is checked via `_botControlService.IsRunning`, but between this check and the actual network switch, the bot could be started by another thread.

This is especially problematic because:
1. The lock only protects `NetworkSelectionService` state
2. The `IGridBotControlService.IsRunning` property is external and can change at any time
3. The bot could start immediately after the check passes

**Severity:** HIGH because trading on wrong network with wrong credentials would cause loss of funds.

**Fix Options:**

Option A (Preferred): Implement a coordination lock between `IGridBotControlService` and `INetworkSelectionService`:
```csharp
// Both services should share a common coordination lock for state transitions
// Or use a state machine pattern where network cannot change while bot has "started" token
```

Option B: Make the bot control service aware of network selection and reject start if network switch is in progress:
```csharp
// In NetworkSelectionService
public bool IsSwitchingNetwork { get; private set; }

// In GridBotControlService.StartAsync
if (networkService.IsSwitchingNetwork)
{
    throw new InvalidOperationException("Cannot start bot while network switch is in progress");
}
```

---

### [MEDIUM] IEnumerable Enumerated Twice in RefreshNetworkStatusAsync

**Location:** `NetworkSelectionService.RefreshNetworkStatusAsync` (lines 119-122)

**Problem:** The `networks` list is enumerated twice with `.Count(n => n.IsConfigured)` and `.Count`. While `List<T>` doesn't re-enumerate, this pattern is fragile - if someone changes the type to IEnumerable, it will cause double enumeration.

**Fix:**
```csharp
var configuredCount = networks.Count(n => n.IsConfigured);
var totalCount = networks.Count;

_logger.LogDebug(
    "Refreshed network status: {ConfiguredCount} configured, {TotalCount} total",
    configuredCount,
    totalCount);
```

---

### [MEDIUM] Missing Null Check for NetworkChanged Event Handler

**Location:** `NetworkSelectionService.SelectNetworkAsync` (line 93)

**Problem:** While the `?.Invoke` pattern is used, there's no guarantee the event handler won't throw. If a handler throws, subsequent handlers won't be called, and the exception propagates to the caller without cleanup.

**Fix:** Consider wrapping event invocation:
```csharp
// Safe event invocation pattern
private void RaiseNetworkChanged(LighterNetworkType network)
{
    var handlers = NetworkChanged;
    if (handlers is null) return;

    foreach (var handler in handlers.GetInvocationList())
    {
        try
        {
            ((EventHandler<LighterNetworkType>)handler).Invoke(this, network);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event handler threw exception during NetworkChanged");
        }
    }
}
```

---

### [MEDIUM] Potential ObjectDisposedException in Blazor Components

**Location:** `NetworkSelector.razor` (lines 112-121, 240-244)

**Problem:** The event handlers `OnBotStatusChanged` and `OnNetworkChanged` call `InvokeAsync(StateHasChanged)` without checking if the component is disposed. If the service fires an event after the component is disposed but before the event is unsubscribed, this will throw `ObjectDisposedException`.

**Fix:** Add disposed check:
```csharp
@code {
    private bool _disposed;

    private void OnBotStatusChanged(object? sender, BotStatusChangedEventArgs e)
    {
        if (_disposed) return;
        InvokeAsync(StateHasChanged);
    }

    private void OnNetworkChanged(object? sender, LighterNetworkType e)
    {
        if (_disposed) return;
        InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        _disposed = true;
        BotControl.StatusChanged -= OnBotStatusChanged;
        NetworkSelection.NetworkChanged -= OnNetworkChanged;
    }
}
```

---

### [MEDIUM] Same Issue in BotControlPanel.razor

**Location:** `BotControlPanel.razor` (lines 200-214, 365-370)

**Problem:** Same ObjectDisposedException risk as NetworkSelector.razor. Three event handlers without disposed check.

**Fix:** Same pattern - add `_disposed` flag and check before `InvokeAsync(StateHasChanged)`.

---

### [MEDIUM] SettingsPanel.razor Missing IDisposable Pattern

**Location:** `SettingsPanel.razor` (line 339)

**Problem:** The component subscribes to `ConfigService.ConfigChanged` event but only implements `IDisposable` for unsubscription. However, same issue as above - the `OnConfigChanged` handler (line 355-360) calls `InvokeAsync(StateHasChanged)` without a disposed check.

**Fix:** Same pattern as above - add `_disposed` flag.

---

### [MEDIUM] LighterNetworksOptions.IsNetworkConfigured Has Incomplete Validation

**Location:** `LighterNetworksOptions.IsNetworkConfigured` (lines 60-69)

**Problem:** Only validates `PrivateKey` and `AccountIndex`. Missing validation for:
- `ApiUrl` (could be null/empty)
- `ChainId` (could be 0 or wrong value)
- `ApiKeyIndex` (could be invalid)

A user could have a partially configured network that passes validation but fails at runtime.

**Fix:**
```csharp
public bool IsNetworkConfigured(LighterNetworkType network)
{
    var options = GetNetwork(network);
    if (options is null)
    {
        return false;
    }

    return !string.IsNullOrWhiteSpace(options.PrivateKey)
        && !string.IsNullOrWhiteSpace(options.ApiUrl)
        && options.AccountIndex > 0
        && options.ChainId > 0;
}
```

---

## Approved (No Issues)

The following files have no HIGH or MEDIUM priority issues:

1. `LighterNetworkType.cs` - Clean enum definition
2. `NetworkInfo.cs` - Clean record definition
3. `NetworkSelectRequest.cs` - Clean DTO definition
4. `LighterAbstractionsExtensions.cs` (`AddLighterNetworks` method) - Clean registration pattern

---

## Summary

| Priority | Count | Status |
|----------|-------|--------|
| HIGH | 2 | Requires immediate fix |
| MEDIUM | 5 | Should fix before production |
| LOW | 0 | (not reviewed per request) |

**Critical Issues That MUST Be Fixed:**
1. TOCTOU race condition on bot status check - could cause trading on wrong network
2. Race condition on event firing (minor, but fix the pattern)

**Recommended Fixes Before Production:**
1. Add `_disposed` checks in all Blazor event handlers
2. Improve network configuration validation
3. Wrap event invocation to prevent handler exceptions from breaking the chain
