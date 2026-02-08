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

Both bots share the same conversation sequences:

- Conversations stored in `SnThinkingSequence` and `SnThinkingThought`
- `BotName` field tracks which bot was used
- Users can switch between bots in the same conversation
- History persists across sessions

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

## Database Migration

The migration adds a `BotName` column to track which bot was used:

```bash
dotnet ef database update --project DysonNetwork.Insight
```

## Migration Notes

When migrating from old API:

1. Remove `ServiceId` from requests (optional, only used by SnChan now)
2. Add `Bot` field to specify which bot to use
3. Update client code to handle bot selection UI
4. Existing sequences remain compatible

## Security Considerations

- MiChan decision gate prevents unauthorized actions
- Superusers bypass decision gate
- All actions logged in conversation history
- Rate limiting applies to both bots
- Admin API restricted to admin/superuser roles
