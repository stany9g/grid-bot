# Session 3: Implement Webhook Alert Notifications

## Date
2025-12-13

## Request Summary
User requested webhook alert notifications to be sent to their Home Assistant instance at:
`http://192.168.1.175:8123/api/webhook/-Y-QjbVoWNw1UzxMqW2OhAOpP`

Format: POST with JSON payload `{ "title": "...", "message": "..." }`

## Implementation

### New Files Created

1. **`GridBot.ApiService/Services/Notifications/IWebhookNotifier.cs`**
   - Interface for webhook notification service
   - Single method: `SendNotificationAsync(title, message, ct)`

2. **`GridBot.ApiService/Services/Notifications/WebhookNotifier.cs`**
   - Implementation using typed `HttpClient`
   - Posts JSON with `title` and `message` fields
   - Configurable timeout
   - Graceful error handling (network errors, timeouts)
   - Fire-and-forget pattern (doesn't block logging)

### Modified Files

1. **`GridBot.ApiService/Configuration/TradingBotOptions.cs`**
   - Added `using GridBot.ApiService.Models.Trading;`
   - Added `WebhookOptions Webhook` property
   - Added new `WebhookOptions` class with:
     - `Enabled` (default: false)
     - `Url` (webhook endpoint)
     - `MinimumSeverity` (default: Critical)
     - `TimeoutSeconds` (default: 10)

2. **`GridBot.ApiService/Services/Risk/RiskEventLogger.cs`**
   - Added dependency on `IWebhookNotifier`
   - Added dependency on `IOptions<TradingBotOptions>`
   - Modified `LogEventAsync` to send webhook for events at or above `MinimumSeverity`
   - Webhook message format:
     - Title: `[Severity] RuleId`
     - Message: `Description\n\nAction: ActionTaken\nTrigger: X\nThreshold: Y`

3. **`GridBot.ApiService/Extensions/RiskServiceExtensions.cs`**
   - Added `using GridBot.ApiService.Services.Notifications;`
   - Added `services.AddHttpClient<IWebhookNotifier, WebhookNotifier>();`

4. **`GridBot.ApiService/appsettings.Production.json`**
   - Added Webhook configuration section:
     ```json
     "Webhook": {
       "Enabled": true,
       "Url": "http://192.168.1.175:8123/api/webhook/-Y-QjbVoWNw1UzxMqW2OhAOpP",
       "MinimumSeverity": "High",
       "TimeoutSeconds": 10
     }
     ```

## Configuration

| Setting | Description | Default |
|---------|-------------|---------|
| `Webhook:Enabled` | Enable/disable notifications | `false` |
| `Webhook:Url` | Webhook endpoint URL | empty |
| `Webhook:MinimumSeverity` | Minimum severity to trigger webhook | `Critical` |
| `Webhook:TimeoutSeconds` | HTTP request timeout | `10` |

### Severity Levels (from highest to lowest)
- `Critical` - Immediate halt required
- `High` - Requires response within 5 minutes
- `Medium` - Requires response within 1 hour
- `Low` - Daily digest

Production is configured for `High` severity, meaning both `Critical` and `High` events will trigger webhooks.

## Build Status
Build succeeded with 0 warnings, 0 errors.

## Testing
To test, you can:
1. Trigger a risk event (e.g., flash crash detection)
2. Or manually call the webhook via curl:
```bash
curl -X POST \
  -H "Content-Type: application/json" \
  -d '{"title": "Test Alert", "message": "Testing webhook from GridBot"}' \
  "http://192.168.1.175:8123/api/webhook/-Y-QjbVoWNw1UzxMqW2OhAOpP"
```
