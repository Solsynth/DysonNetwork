# Bot Chat Config Migration Guide

## Database Migration

### New Table: `bot_chat_configs`

The migration `20260607154050_AddBotChatConfig` adds the `bot_chat_configs` table to the Develop database.

```sql
CREATE TABLE bot_chat_configs (
    id UUID NOT NULL PRIMARY KEY,
    commands JSONB NOT NULL DEFAULT '[]',
    webhooks JSONB NOT NULL DEFAULT '[]',
    auto_approve_dm BOOLEAN NOT NULL DEFAULT TRUE,
    support_chat BOOLEAN NOT NULL DEFAULT TRUE,
    subscribed_events JSONB NOT NULL DEFAULT '["messages.new"]',
    created_at TIMESTAMP WITH TIME ZONE NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL,
    deleted_at TIMESTAMP WITH TIME ZONE
);
```

### Running the Migration

```bash
cd DysonNetwork.Develop
dotnet ef database update
```

---

## Service Dependencies

### New Services to Register

**In `DysonNetwork.Shared/Registry/ServiceInjectionHelper.cs`:**

The `RemoteBotChatConfigService` is automatically registered when calling `AddDevelopService()`.

**In `DysonNetwork.Messager/Startup/ServiceCollectionExtensions.cs`:**

The following event listeners are registered:
- `BotChatMessageEvent` - For webhook delivery
- `BotChatConfigUpdatedEvent` - For cache invalidation

### Required Configuration

**appsettings.json (Messager):**
```json
{
  "Services": {
    "Develop": {
      "BaseUrl": "https://localhost:5001"
    }
  }
}
```

---

## API Changes Summary

### New Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/developers/{pub}/projects/{pid}/bots/{bid}/chat` | Get bot chat config |
| `PUT` | `/api/developers/{pub}/projects/{pid}/bots/{bid}/chat` | Update bot chat config |
| `POST` | `/api/developers/{pub}/projects/{pid}/bots/{bid}/chat/manifest` | Update manifest |
| `GET` | `/api/bots/public/{bid}/chat` | Get bot chat config (public) |
| `GET` | `/api/bots/public/{bid}/commands` | Get bot commands (public) |
| `GET` | `/api/chat/{roomId}/bots/commands` | Get all bot commands in room |

### Modified Endpoints

| Method | Endpoint | Change |
|--------|----------|--------|
| `POST` | `/api/chat/direct` | Added bot auto-approve logic |
| `POST` | `/api/chat/{roomId}/messages` | Added `?identity=` parameter |

---

## Breaking Changes

None. All changes are additive and backward compatible.

---

## Rollback

To rollback the migration:

```bash
cd DysonNetwork.Develop
dotnet ef database update <previous-migration-name>
dotnet ef migrations remove
```
