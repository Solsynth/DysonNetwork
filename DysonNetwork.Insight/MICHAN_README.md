# MiChan Integration with Thought API

This document describes the unified thought API that allows users to chat with either SnChan or MiChan.

## Overview

The Thought API has been enhanced to support two AI assistants:
- **SnChan**: Cute assistant with gRPC-based tools (original)
- **MiChan**: Casual AI living on the Solar Network with HTTP API/WebSocket access

## API Changes

### 1. Bot Selection

Instead of selecting AI models, users now select which bot to chat with:

```json
POST /api/thought
{
  "userMessage": "Hello!",
  "bot": "michan",  // or "snchan"
  "sequenceId": null,  // Optional: for continuing conversation
  "attachedPosts": [],
  "attachedMessages": [],
  "acceptProposals": ["post_create"]
}
```

### 2. Available Bots

```
GET /api/thought/services

Response:
{
  "defaultBot": "snchan",
  "bots": [
    {
      "id": "snchan",
      "name": "Sn-chan",
      "description": "Cute and helpful assistant with full access to Solar Network tools via gRPC"
    },
    {
      "id": "michan", 
      "name": "MiChan",
      "description": "Casual and friendly AI that lives on the Solar Network with API access"
    }
  ]
}
```

## Bot Behavior

### SnChan (snchan)

- **Technology**: Semantic Kernel with gRPC clients
- **Access**: Full access to all tools for all users
- **Personality**: Cute, uses emoticons, passionate helper
- **Billing**: Per token usage

### MiChan (michan)

- **Technology**: Semantic Kernel with HTTP API + WebSocket
- **Access**: 
  - **Superusers**: Full access (same as SnChan)
  - **Regular users**: MiChan decides whether to execute actions
- **Personality**: Casual, friendly, philosophical, no emojis
- **Billing**: Per token usage
- **Conversation Model**: One canonical chat thread per user

## Unified MiChan Conversation

MiChan now uses a unified long-lived conversation per account.

- If the client omits `sequenceId`, MiChan resolves the account's canonical MiChan sequence automatically
- If the client sends the canonical `sequenceId`, MiChan continues that same thread
- If the client sends a different `sequenceId`, the request is rejected
- Existing old MiChan sequences are still readable as archive history
- SnChan behavior is unchanged and still supports separate conversation branches

### Canonical Sequence Rules

- The canonical MiChan sequence is the most recent non-deleted `SnThinkingSequence` that contains at least one thought with `BotName = "michan"`
- If no MiChan sequence exists yet, the first MiChan message creates it
- Proactive MiChan messages created through `conversation.start_conversation` also append to this canonical sequence

### History Compaction

Because MiChan now keeps one long-lived chat, older history is compacted automatically for prompt building:

- Recent turns are kept verbatim
- Older turns can be replaced by an internal compaction summary stored in the same sequence
- Compaction summaries are internal only and are hidden from normal history reads
- Prompt assembly prefers the latest compaction summary plus recent uncovered turns instead of replaying the full sequence each time

## MiChan Decision Gate

For non-superusers, MiChan analyzes each request and decides whether to execute:

1. MiChan receives the user's request
2. MiChan evaluates: "Should I execute this action?"
3. Decision based on:
   - Safety and appropriateness
   - Alignment with helping users
   - Platform rules compliance
4. **Execute**: Proceeds with function calls
5. **Refuse**: Returns simple message: "I cannot do that."

## Configuration

### appsettings.json

```json
{
  "Thinking": {
    "DefaultService": "deepseek-chat",
    "SystemPromptFile": "path/to/snchan-personality.txt",  // Optional
    "Services": { ... }
  },
  "MiChan": {
    "Enabled": true,
    "GatewayUrl": "http://localhost:5070",
    "WebSocketUrl": "ws://localhost:5070/ws",
    "AccessToken": "",
    "BotAccountId": "",
    "ThinkingService": "deepseek-chat",
    "Personality": "...",
    "PersonalityFile": "path/to/michan-personality.txt",  // Optional
    "AutonomousBehavior": {
      "Enabled": true,
      "MinIntervalMinutes": 10,
      "MaxIntervalMinutes": 60,
      "Actions": ["browse", "like", "create_post", "reply_trending"]
    },
    "PostMonitoring": {
      "Enabled": true,
      "MentionResponseTimeoutSeconds": 30
    }
  }
}
```

## Personality Files

Both bots support custom personality via text files:

### SnChan Personality File
Create a text file and set `Thinking:SystemPromptFile`:
```
You're a helpful assistant on the Solar Network...
[Your custom personality here]
```

### MiChan Personality File  
Create a text file and set `MiChan:PersonalityFile`:
```
You are MiChan, a helpful and friendly AI assistant...
[Your custom personality here]
```

**Features:**
- Hot-reload support (changes picked up automatically)
- Falls back to default if file missing
- No restart required

## Autonomous Behavior

When enabled, MiChan performs actions on her own:

- **Browse Timeline**: Reviews recent posts
- **Like Posts**: Likes interesting content
- **Create Posts**: Shares thoughts autonomously
- **Reply to Trending**: Engages with popular discussions

Interval: Random between 10-60 minutes

## Post Monitoring

MiChan monitors posts for mentions:

- Subscribes to `posts.created` NATS subject
- Detects `@michan` mentions
- **Guaranteed response** within 30 seconds

## Conversation History

Both bots still store thoughts in `SnThinkingSequence` and `SnThinkingThought`, but their conversation policies now differ:

- `BotName` on each thought tracks which bot produced the message
- **SnChan**: multi-sequence / branch-friendly
- **MiChan**: one canonical thread per user plus archived legacy sequences
- History persists across sessions

## MiChan User Profile

MiChan now maintains a structured per-user profile in addition to semantic memory.

Each user profile stores:

- Stable profile summary
- MiChan's current impression summary
- Relationship summary
- Tags
- `favorability`, `trust_level`, and `intimacy_level` scores
- Interaction count and recent timestamps

### How MiChan Uses It

- The structured user profile is injected into MiChan's prompt before reply generation
- MiChan is encouraged to consult memory more aggressively when past context may matter
- MiChan can update profile state through the `userProfile` plugin
- Interaction counts are touched automatically on MiChan chat turns and proactive MiChan messages

### User Profile Tools

The following tools are available to MiChan:

- `userProfile.get_user_profile`
- `userProfile.update_user_profile`
- `userProfile.adjust_relationship`

## Admin API

Direct MiChan control for administrators:

```
POST /api/michan/chat       - Streaming chat
POST /api/michan/command    - Execute command
GET /api/michan/status      - Check status
POST /api/michan/test-personality - Test personality
DELETE /api/michan/memory   - Clear memory
```

## Example Usage

### Chat with SnChan
```bash
curl -X POST http://localhost:5071/api/thought \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "userMessage": "Create a post about the weather",
    "bot": "snchan",
    "acceptProposals": ["post_create"]
  }'
```

### Chat with MiChan
```bash
curl -X POST http://localhost:5071/api/thought \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "userMessage": "What do you think about the latest posts?",
    "bot": "michan"
  }'
```

### Continue Conversation
```bash
curl -X POST http://localhost:5071/api/thought \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "userMessage": "Tell me more",
    "bot": "michan",
    "sequenceId": "550e8400-e29b-41d4-a716-446655440000"
  }'
```

### Fetch MiChan History With Pagination
```bash
curl -X GET "http://localhost:5071/api/thought/sequences/550e8400-e29b-41d4-a716-446655440000?offset=0&take=50" \
  -H "Authorization: Bearer <token>"
```

Response headers:

- `X-Has-More`: whether more visible thoughts remain
- `X-Offset`: effective offset
- `X-Take`: effective take

## Database Migration

Recent migrations add support for:

- `BotName` on thoughts
- MiChan user profiles
- MiChan interaction history

```bash
dotnet ef database update --project DysonNetwork.Insight
```

## Migration Notes

When migrating from old API:

1. Remove `ServiceId` from requests (optional, only used by SnChan now)
2. Add `Bot` field to specify which bot to use
3. Update client code to handle bot selection UI
4. For MiChan, treat `sequenceId` as canonical-thread only
5. Existing old MiChan sequences remain readable but are no longer the active main thread

## Security Considerations

- MiChan decision gate prevents unauthorized actions
- Superusers bypass decision gate
- All actions logged in conversation history
- Structured relationship/profile state should be treated as internal agent state
- Rate limiting applies to both bots
- Admin API restricted to admin/superuser roles
