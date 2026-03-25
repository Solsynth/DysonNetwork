# Insight Service Porting Guide: .NET -> Python

Complete checklist of everything that needs to be implemented in the Python rewrite of `DysonNetwork.Insight`.

## Service Overview

The Insight service is the **AI agent brain** of Solar Network. It provides:
- AI chat with SSE streaming (Sn-chan + Mi-chan)
- MiChan autonomous behavior engine (browse posts, reply, react, create content)
- Semantic memory with pgvector
- Web scraping / link previews
- RSS/Atom feed management + article scraping
- Token billing for AI usage
- Scheduled task execution

## Technology Stack (Python Recommendation)

| Component | .NET | Python Recommendation |
|-----------|------|-----------------------|
| Web framework | ASP.NET Core | FastAPI (native SSE, async, OpenAPI) |
| ORM | EF Core + Npgsql | SQLAlchemy 2.0 + asyncpg |
| Vector DB | pgvector via EF | pgvector via SQLAlchemy + pgvector-python |
| AI/LLM | Microsoft Semantic Kernel | LangChain or direct OpenAI SDK |
| Tokenizer | TiktokenSharp | tiktoken |
| HTML parsing | AngleSharp + HtmlAgilityPack | beautifulsoup4 + lxml |
| RSS parsing | System.ServiceModel.Syndication | feedparser |
| HTTP client | HttpClient | httpx (async) |
| Caching | Redis (ICacheService) | redis.asyncio |
| Auth | Custom JWT (DysonToken) | PyJWT + RSA |
| gRPC | Grpc.AspNetCore | grpcio + grpcio-tools |
| Scheduler | Quartz.NET | APScheduler or asyncio tasks |
| WebSocket | ClientWebSocket | websockets |
| SSE | Manual SseStreamWriter | FastAPI StreamingResponse |
| Task queue | BackgroundService | asyncio or Celery |

---

## 1. DATABASE MODELS

All tables use PostgreSQL. Timestamps use `Instant` (UTC). Soft deletes via `deleted_at`.

### 1.1 ModelBase (inherit for all models)

```python
class ModelBase:
    created_at: datetime
    updated_at: datetime
    deleted_at: datetime | None  # soft delete
```

### 1.2 thinking_sequences

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK, auto-generate |
| topic | String(4096) | nullable |
| total_token | BigInt | accumulated token usage |
| paid_token | BigInt | tokens already billed |
| free_tokens | BigInt | free tokens granted |
| is_public | Boolean | default false |
| account_id | UUID | owner |
| agent_initiated | Boolean | AI-initiated vs user-initiated |
| user_last_read_at | Instant | nullable |
| last_message_at | Instant | |
| created_at | Instant | |
| updated_at | Instant | |
| deleted_at | Instant | soft delete |

### 1.3 thinking_thoughts

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| parts | JSONB | `List[SnapThinkingMessagePart]` (see 1.3.1) |
| role | Enum | `assistant` / `user` |
| token_count | BigInt | |
| model_name | String(4096) | nullable |
| bot_name | String(50) | "snchan" or "michan" |
| sequence_id | UUID | FK -> thinking_sequences |
| created_at | Instant | |
| updated_at | Instant | |
| deleted_at | Instant | soft delete |

#### 1.3.1 MessagePart JSONB structure

```json
{
  "type": "text" | "function_call" | "function_result",
  "text": "string?",
  "metadata": {},
  "files": [/* SnCloudFileReferenceObject */],
  "functionCall": {
    "id": "string",
    "pluginName": "string?",
    "name": "string",
    "arguments": "string (JSON)"
  },
  "functionResult": {
    "callId": "string",
    "pluginName": "string?",
    "functionName": "string",
    "result": "any",
    "isError": false
  }
}
```

### 1.4 memory_records (MiChan)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| is_hot | Boolean | hot memory = always in context |
| is_active | Boolean | soft delete flag |
| type | String | "fact" / "user" / "topic" / "context" / "interaction" |
| content | Text | memory content |
| embedding | Vector(1536) | pgvector |
| confidence | Float | 0-1, how factual |
| account_id | UUID | nullable, null = global memory |
| last_accessed_at | Instant | nullable |
| created_at | Instant | |
| updated_at | Instant | |

### 1.5 user_profiles (MiChan)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| account_id | UUID | unique index |
| profile_summary | Text | nullable |
| impression_summary | Text | nullable |
| relationship_summary | Text | nullable |
| tags | JSONB | `["tag1", "tag2"]` |
| favorability | Integer | -100 to 100 |
| trust_level | Integer | -100 to 100 |
| intimacy_level | Integer | -100 to 100 |
| interaction_count | Integer | default 0 |
| last_interaction_at | Instant | nullable |
| last_profile_update_at | Instant | nullable |
| created_at | Instant | |
| updated_at | Instant | |

### 1.6 interactive_history (MiChan)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| resource_id | UUID | post/user ID |
| resource_type | String | "post" / "user" |
| behaviour | String | "reply" / "react" / "repost" / "conversation" / "seen" |
| is_active | Boolean | default true |
| expires_at | Instant | nullable |
| created_at | Instant | |
| updated_at | Instant | |

### 1.7 scheduled_tasks (MiChan)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| scheduled_at | Instant | when to run |
| recurrence_interval | Duration | nullable, for recurring |
| recurrence_end_at | Instant | nullable |
| prompt | Text | task prompt |
| context | Text | nullable |
| status | String(1024) | "pending" / "running" / "completed" / "failed" / "cancelled" |
| completed_at | Instant | nullable |
| error_message | Text | nullable |
| execution_count | Integer | default 0 |
| account_id | UUID | owner |
| is_active | Boolean | default true |
| last_executed_at | Instant | nullable |
| created_at | Instant | |
| updated_at | Instant | |

### 1.8 mood_states (MiChan)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| energy_level | Float | 0.0-1.0, default 0.7 |
| positivity_level | Float | 0.0-1.0, default 0.7 |
| sociability_level | Float | 0.0-1.0, default 0.6 |
| curiosity_level | Float | 0.0-1.0, default 0.8 |
| current_mood_description | Text | "curious and friendly" |
| recent_emotional_events | String | comma-separated events |
| last_mood_update | Instant | |
| interactions_since_update | Integer | default 0 |
| created_at | Instant | |
| updated_at | Instant | |

### 1.9 unpaid_accounts (Billing)

| Column | Type | Notes |
|--------|------|-------|
| account_id | UUID | PK |
| marked_at | DateTime | |

### 1.10 feed_articles (Web)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| title | String(4096) | |
| url | String(8192) | |
| author | String(4096) | nullable |
| meta | JSONB | nullable |
| preview | JSONB | LinkEmbed (nullable) |
| content | Text | nullable |
| published_at | DateTime | nullable |
| feed_id | UUID | FK -> feeds |
| created_at | Instant | |
| updated_at | Instant | |

### 1.11 feeds (Web)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| url | String(8192) | |
| title | String(4096) | |
| description | String(8192) | nullable |
| verified_at | Instant | nullable |
| verification_key | String(8192) | nullable, JSON-ignored |
| preview | JSONB | LinkEmbed (nullable) |
| config | JSONB | `{"scrap_page": false}` |
| publisher_id | UUID | |
| created_at | Instant | |
| updated_at | Instant | |

### 1.12 feed_subscriptions (Web)

| Column | Type | Notes |
|--------|------|-------|
| id | UUID | PK |
| feed_id | UUID | FK -> feeds |
| account_id | UUID | |
| created_at | Instant | |
| updated_at | Instant | |

### 1.13 PostgreSQL Extensions Required

```sql
CREATE EXTENSION IF NOT EXISTS vector;  -- pgvector for embeddings
```

---

## 2. REST API ENDPOINTS

### 2.1 Billing (`/api/billing`)

#### GET `/api/billing/status`
- **Auth**: Required
- **Response**: `{ "status": "unpaid" | "ok" }`

#### POST `/api/billing/retry`
- **Auth**: Required
- **Response**: 200 `{ "message": "..." }` or 400 `{ "message": "..." }`

---

### 2.2 Thought / Chat (`/api/thought`)

#### GET `/api/thought/services`
- **Auth**: None
- **Response**:
```json
{
  "defaultBot": "snchan",
  "bots": [
    { "id": "snchan", "name": "Sn-chan", "description": "..." },
    { "id": "michan", "name": "Mi-chan", "description": "..." }
  ]
}
```

#### POST `/api/thought`
- **Auth**: Required (CurrentUser)
- **Request Body**:
```json
{
  "userMessage": "string?",
  "bot": "snchan" | "michan",  // default "snchan"
  "sequenceId": "uuid?",
  "attachedPosts": ["post_id"],
  "attachedFiles": ["file_id"],
  "attachedMessages": [{"key": "value"}],
  "acceptProposals": ["proposal_id"]
}
```
- **Response**: **SSE stream** (`text/event-stream`)
- **SSE Events**:
  - `data: {"type": "text", "data": "chunk"}` - text chunks
  - `data: {"type": "function_call", "data": {"id": "...", "pluginName": "...", "name": "...", "arguments": "..."}}` - tool call
  - `data: {"type": "function_result", "data": {"callId": "...", "functionName": "...", "result": "...", "isError": false}}` - tool result
  - `topic: {"topic": "generated topic"}` - topic metadata
  - `thought: {"thoughtId": "uuid", "sequenceId": "uuid"}` - saved thought ID

#### GET `/api/thought/sequences`
- **Auth**: Required (CurrentUser)
- **Query**: `offset` (int), `take` (int)
- **Response**: `List[SnapThinkingSequence]` + `X-Total` header

#### GET `/api/thought/michan/sequence`
- **Auth**: Required (CurrentUser)
- **Response**: `SnThinkingSequence` (canonical MiChan sequence)

#### PATCH `/api/thought/sequences/{sequenceId}/sharing`
- **Auth**: Required (CurrentUser, owner only)
- **Request Body**: `{ "isPublic": true }`
- **Response**: 204 NoContent

#### GET `/api/thought/sequences/{sequenceId}`
- **Auth**: Optional (public sequences accessible without auth)
- **Query**: `offset` (int), `take` (int)
- **Response**: `List[SnapThinkingThought]` + `X-Has-More`, `X-Offset`, `X-Take` headers

#### DELETE `/api/thought/sequences/{sequenceId}`
- **Auth**: Required (CurrentUser, owner only)
- **Response**: 200 OK

#### POST `/api/thought/sequences/{sequenceId}/read`
- **Auth**: Required (CurrentUser, owner only)
- **Response**: 204 NoContent

#### POST `/api/thought/sequences/{sequenceId}/memorize`
- **Auth**: Required (CurrentUser + `michan.admin` permission)
- **Response**: `{ "success": true, "summary": "...", "sequenceId": "uuid" }`

---

### 2.3 Web Scraping (`/api/scrap`)

#### GET `/api/scrap/link`
- **Auth**: None
- **Query**: `url` (string, required)
- **Response**: `LinkEmbed`
```json
{
  "type": "link",
  "url": "https://example.com",
  "title": "Example",
  "description": "...",
  "imageUrl": "https://...",
  "faviconUrl": "https://...",
  "siteName": "Example",
  "contentType": "website",
  "author": "...",
  "publishedDate": "2024-01-01T00:00:00Z"
}
```

#### DELETE `/api/scrap/link/cache`
- **Auth**: Required + `cache.scrap` permission
- **Query**: `url` (string)
- **Response**: `{ "message": "..." }`

#### DELETE `/api/scrap/cache/all`
- **Auth**: Required + `cache.scrap` permission
- **Response**: `{ "message": "..." }`

---

### 2.4 Web Feed Management (`/api/publishers/{pubName}/feeds`)

#### GET `/api/publishers/{pubName}/feeds`
- **Auth**: None
- **Response**: `List[SnWebFeed]`

#### GET `/api/publishers/{pubName}/feeds/{feedId}`
- **Auth**: None
- **Response**: `SnWebFeed`

#### POST `/api/publishers/{pubName}/feeds`
- **Auth**: Required (Editor role via publisher service)
- **Request Body**:
```json
{
  "url": "https://example.com/feed.xml",  // required
  "title": "Example Feed",                // required
  "description": "...",
  "config": { "scrapPage": false }
}
```
- **Response**: `SnWebFeed`

#### PATCH `/api/publishers/{pubName}/feeds/{feedId}`
- **Auth**: Required (Editor role)
- **Request Body**: same as create, all optional
- **Response**: `SnWebFeed`

#### DELETE `/api/publishers/{pubName}/feeds/{feedId}`
- **Auth**: Required (Editor role)
- **Response**: 204 NoContent

#### POST `/api/publishers/{pubName}/feeds/{feedId}/scrap`
- **Auth**: Required (Editor role)
- **Response**: 200 OK

#### POST `/api/publishers/{pubName}/feeds/{feedId}/verify/init`
- **Auth**: Required (Editor role)
- **Response**:
```json
{
  "verificationUrl": "https://domain/.well-known/solar-network-feed.txt",
  "code": "dn_20240101_...",
  "instructions": "..."
}
```

#### POST `/api/publishers/{pubName}/feeds/{feedId}/verify`
- **Auth**: Required (Editor role)
- **Response**:
```json
{
  "success": true,
  "verifiedAt": "2024-01-01T00:00:00Z",
  "message": "..."
}
```

---

### 2.5 Web Feed Subscriptions (`/api/feeds`)

#### POST `/api/feeds/{feedId}/subscribe`
- **Auth**: Required
- **Response**: 201 Created + `SnWebFeedSubscription`

#### DELETE `/api/feeds/{feedId}/subscribe`
- **Auth**: Required
- **Response**: 204 NoContent

#### GET `/api/feeds/{feedId}/subscription`
- **Auth**: Required
- **Response**: `SnWebFeedSubscription`

#### GET `/api/feeds/subscribed`
- **Auth**: Required
- **Query**: `offset`, `take`
- **Response**: `List[SnWebFeed]`

#### GET `/api/feeds`
- **Auth**: Required
- **Query**: `offset`, `take`
- **Response**: `List[SnWebFeed]`

#### GET `/api/feeds/{feedId}`
- **Auth**: Optional (anonymous allowed)
- **Response**: `SnWebFeed`

#### GET `/api/feeds/{feedId}/articles`
- **Auth**: Optional (anonymous)
- **Query**: `offset`, `take`
- **Response**: `List[SnWebArticle]`

#### GET `/api/feeds/explore`
- **Auth**: Required
- **Query**: `offset`, `take`, `query?`
- **Response**: `List[SnWebFeed]`

---

### 2.6 Web Articles (`/api/feeds/articles`)

#### GET `/api/feeds/articles`
- **Auth**: None
- **Query**: `limit`, `offset`, `feedId?`, `publisherId?`
- **Response**: `List[SnWebArticle]` + `X-Total` header

#### GET `/api/feeds/articles/{articleId}`
- **Auth**: None
- **Response**: `SnWebArticle`

#### GET `/api/feeds/articles/random`
- **Auth**: None
- **Query**: `limit`
- **Response**: `List[SnWebArticle]`

---

### 2.7 Scheduled Tasks (`/api/tasks`)

#### GET `/api/tasks`
- **Auth**: Required
- **Query**: `offset`, `take`, `isActive?`, `status?`
- **Response**: `List[ListTasksResponse]`
```json
[{
  "id": "uuid",
  "scheduledAt": "2024-01-01T00:00:00Z",
  "recurrenceInterval": "PT1H",
  "recurrenceEndAt": "...",
  "prompt": "...",
  "context": "...",
  "status": "pending",
  "completedAt": "...",
  "errorMessage": "...",
  "executionCount": 0,
  "isActive": true,
  "lastExecutedAt": "...",
  "createdAt": "..."
}]
```

#### POST `/api/tasks`
- **Auth**: Required
- **Request Body**:
```json
{
  "prompt": "Do something",     // required
  "scheduledAt": "2024-...",    // required, ISO-8601
  "recurrenceInterval": 3600,   // seconds, optional
  "recurrenceEndAt": "...",     // optional
  "context": "..."              // optional
}
```
- **Response**: 201 Created + `MiChanScheduledTask`

#### GET `/api/tasks/{taskId}`
- **Auth**: Required (owner or superuser)
- **Response**: `ListTasksResponse`

#### PATCH `/api/tasks/{taskId}`
- **Auth**: Required (owner)
- **Request Body**:
```json
{
  "prompt": "...",
  "scheduledAt": "...",
  "recurrenceInterval": 3600,
  "recurrenceEndAt": "...",
  "context": "...",
  "isActive": true
}
```
- **Response**: `MiChanScheduledTask`

#### POST `/api/tasks/{taskId}/cancel`
- **Auth**: Required (owner)
- **Response**: `{ "success": true, "message": "..." }`

#### DELETE `/api/tasks/{taskId}`
- **Auth**: Required (owner)
- **Response**: `{ "success": true, "message": "..." }`

#### POST `/api/tasks/{taskId}/run`
- **Auth**: Required + `michan.admin` permission
- **Response**: `{ "success": true, "message": "...", "taskId": "uuid", "status": "...", "executionCount": 1 }`

---

### 2.8 MiChan Admin (`/api/michan`)

#### GET `/api/michan/personality`
- **Auth**: Required + `michan.admin` permission
- **Response**: `{ "personality": "..." }`

#### POST `/api/michan/trigger`
- **Auth**: Required + `michan.admin` permission
- **Response**: `{ "success": true, "message": "...", "timestamp": "2024-..." }`

---

## 3. gRPC SERVICES (To Implement)

These are gRPC services the Insight **provides** (other Dyson services call these).

### 3.1 DyWebReaderService

| RPC Method | Request Fields | Response Fields |
|------------|---------------|-----------------|
| `ScrapeArticle` | `url: string` | `link: DyLinkEmbed?, content: string?` |
| `GetLinkPreview` | `url: string, bypass_cache: bool` | `link: DyLinkEmbed` |
| `InvalidateLinkPreviewCache` | `url: string` | `success: bool` |

### 3.2 DyWebFeedService

| RPC Method | Request Fields | Response Fields |
|------------|---------------|-----------------|
| `GetWebFeed` | `id: string` OR `url: string` (oneof) | `feed: DyWebFeed?` |
| `ListWebFeeds` | `publisher_id: string` | `feeds: List[DyWebFeed]` |

### 3.3 DyWebArticleService

| RPC Method | Request Fields | Response Fields |
|------------|---------------|-----------------|
| `GetWebArticle` | `id: string` | `article: DyWebArticle?` |
| `GetWebArticleBatch` | `ids: List[string]` | `articles: List[DyWebArticle]` |
| `ListWebArticles` | `feed_id: string?, limit: int, offset: int` | `articles: List[DyWebArticle], total: int` |
| `GetRecentArticles` | `limit: int` | `articles: List[DyWebArticle]` |

#### Proto Message Shapes (DyLinkEmbed)

```protobuf
message DyLinkEmbed {
  string url = 1;
  string title = 2;
  string description = 3;
  string image_url = 4;
  string favicon_url = 5;
  string site_name = 6;
  string content_type = 7;
  string author = 8;
  google.protobuf.Timestamp published_date = 9;
}
```

---

## 4. gRPC CLIENTS (To Consume)

These are gRPC services Insight **calls** (hosted in other Dyson services).

| Service | Methods Used | Purpose |
|---------|-------------|---------|
| `DyPaymentService` | `CreateTransactionWithAccountAsync` | Token billing |
| `DyProfileService` | `ListBlockedAsync` | Get blocked user list |
| `DyAccountService` | `GetAccountAsync`, `LookupAccountBatchAsync` | Account lookup |
| `DyPostService` | post CRUD | Sn-chan post plugin |
| `DyPublisherService` | publisher lookup, membership check | Feed management |

---

## 5. AUTHENTICATION

### Auth Scheme

Custom JWT-based auth. Validate `Authorization: Bearer <token>` header.

| Aspect | Detail |
|--------|--------|
| Algorithm | RS256 |
| Public Key | Config `AuthToken:PublicKeyPath` |
| Fallback | gRPC call to Passport service for legacy tokens |
| Token Formats Accepted | `Bearer`, `Bot`, `AtField` (legacy), `AkField` (legacy), `?tk=` query param, `AuthToken` cookie |

### Permissions Used

| Permission | Used By |
|------------|---------|
| `michan.admin` | ThoughtController.MemorizeSequence, MiChanAdminController, ScheduledTaskController.RunTaskImmediately |
| `cache.scrap` | WebReaderController.InvalidateCache, InvalidateAllCache |

### Controller-Level Auth

| Controller | Auth Requirement |
|------------|-----------------|
| ThoughtController | Most endpoints require `CurrentUser` |
| BillingController | `CurrentUser` |
| WebReaderController | Cache invalidation requires auth + permission |
| WebFeedController | Class-level `[Authorize]` |
| WebFeedPublicController | Mix of anonymous + authorized |
| ScheduledTaskController | `michan.admin` permission |
| MiChanAdminController | `michan.admin` permission |

---

## 6. BACKGROUND JOBS

| Job | Interval | Logic |
|-----|----------|-------|
| **TokenBilling** | Every 5 minutes | Settles token bills: finds sequences where `paid_token < total_token`, calculates cost (`ceil((total - paid - free) / 10)`), calls `DyPaymentService.CreateTransactionWithAccountAsync`. On failure, marks account in `unpaid_accounts`. |
| **WebFeedScraper** | Daily | Scrapes all RSS/Atom feeds, creates new articles. For `config.scrap_page` feeds, scrapes full page content. |
| **WebFeedVerification** | Daily at 4am | Re-verifies domain ownership of all verified feeds via `.well-known/solar-network-feed.txt`. |
| **ScheduledTask** | Every 1 minute | Finds pending tasks where `scheduled_at <= now`. Executes each with AI kernel (personality + mood + memories). Handles recurrence by setting next run time. |
| **MiChanAutonomous** | Every 5 min (heartbeat) | Checks if enough time passed (random interval 10-60 min). Fetches posts, decides actions (REPLY/REACT/PIN/STORE/IGNORE). 25% chance for additional action (create post, repost, start conversation). |
| **MiChanSequenceUnification** | Once at startup | Merges historic MiChan sequences into canonical thread. Uses distributed lock. |

---

## 7. BUSINESS LOGIC

### 7.1 Thought/SSE Streaming

Core flow for AI chat:
1. Resolve sequence (MiChan enforces canonical sequence)
2. Save user thought to DB
3. Build chat history with:
   - System prompt (personality, mood, user profile, hot memories)
   - Previous thoughts (with compaction if >8000 tokens)
   - Attached posts/files
4. Stream response via SSE:
   - Text chunks as `data: {"type": "text", "data": "..."}`
   - Function calls as `data: {"type": "function_call", ...}`
   - Function results as `data: {"type": "function_result", ...}`
   - Max 8 tool call rounds
5. Save assistant thought
6. Update token counts

### 7.2 MiChan Compaction

When conversation exceeds 8000 tokens:
1. Generate summary of old thoughts via AI (max 12 bullets)
2. Store as special compaction thought with metadata
3. On next load, only load thoughts after covered thought + summary
4. Keeps at least 8 most recent thoughts

### 7.3 MiChan Autonomous Decision Flow

```
Fetch posts from /sphere/posts (paginated, up to 300)
    -> Skip: own posts, blocked users, already-interacted, seen
    -> Detect @michan mentions
    -> For each post:
        -> Search relevant memories
        -> Build decision prompt (personality + mood + memories + context)
        -> AI decides: REPLY / REACT:emoji:attitude / PIN / STORE / IGNORE
        -> Execute actions
        -> Always store at least 1-3 memories per post
```

### 7.4 Token Billing

- Cost formula: `ceil((total_tokens - paid_tokens - free_tokens) / 10)` per 100 tokens
- Currency: `WalletCurrency.SourcePoint`
- Unpaid accounts get marked for retry
- Free tokens granted for first bot thought in agent-initiated conversations

### 7.5 Memory System

- **Storage**: Generate embedding (1536-dim), store with type/confidence/accountId
- **Search**: Cosine distance via pgvector, includes global memories (accountId=null)
- **Hot memory**: Always in context without tool call
- **Types**: `fact`, `user`, `topic`, `context`, `interaction`
- **Confidence**: 0-1, how factual the memory is
- **Auto-cleanup**: `LastAccessedAt` updated on retrieval

### 7.6 Web Scraping

- Link preview: OpenGraph + Twitter Card + meta tag extraction
- Article content: `<article>` tag extraction via HtmlAgilityPack
- User agent: `facebookexternalhit/1.1` (bypasses some bot blocks)
- Timeout: 3 seconds, max size: 10MB
- Cache: SHA256 of normalized URL, 24h TTL

### 7.7 Feed Management

- RSS/Atom parsing via `SyndicationFeed`
- Domain ownership verification via `.well-known` file
- Articles deduplicated by URL
- Optional full-page scraping per feed config

---

## 8. EXTERNAL API CALLS (Gateway)

All via HTTP with `Authorization: AtField <token>`:

### Sphere Service

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/sphere/posts` | List/feed posts |
| GET | `/sphere/posts?shuffle=true` | Random posts |
| GET | `/sphere/posts/{id}` | Get single post |
| GET | `/sphere/posts/{id}/replies` | Get replies |
| GET | `/sphere/posts/search?q=` | Search posts |
| POST | `/sphere/posts` | Create post / reply / repost |
| POST | `/sphere/posts/{id}/reactions` | React to post |
| POST | `/sphere/posts/{id}/pin` | Pin post |
| DELETE | `/sphere/posts/{id}/pin` | Unpin post |
| GET | `/sphere/publishers/{name}` | Get publisher |

### Passport Service

| Method | Endpoint | Purpose |
|--------|----------|---------|
| GET | `/passport/accounts/{id}` | Get account |
| GET | `/passport/accounts/me` | Bot profile |
| GET | `/passport/accounts/search?q=` | Search accounts |
| POST | `/passport/accounts/{id}/follow` | Follow user |
| POST | `/passport/accounts/{id}/unfollow` | Unfollow user |
| GET | `/passport/accounts/{id}/followers` | Get followers |
| GET | `/passport/accounts/{id}/following` | Get following |

---

## 9. CACHING

| Key Pattern | TTL | Purpose |
|-------------|-----|---------|
| `thoughts:{sequence_id}` | 10 min | Cached thinking thoughts |
| `scrap:preview:{sha256(url)}` | 24h | Link preview embeds |
| `auth:profile:{account_id}` | 5 min | Account profile hydration |
| `auth:last_seen_touch:{account_id}` | 1 min | Last-seen touch throttle |
| `michan-sequence-unification` | 10 min | Distributed lock (startup) |

---

## 10. AI/LLM CONFIGURATION

### Providers Supported

- **Ollama** (local, `api.openai.com/v1` compatible)
- **DeepSeek** (deepseek-chat)
- **OpenRouter** (qwen/qwen3, vision models)
- **Aliyun DashScope** (text-embedding-v3)
- **BigModel** (zhipuai)

### Embedding Configuration

All embeddings use 1536 dimensions via `IEmbeddingGenerator<string, Embedding<float>>`.

### Kernel Configuration

| Use Case | Temperature | Auto-Invoke |
|----------|-------------|-------------|
| Sn-chan chat | 0.6 | No (manual) |
| Mi-chan chat | 0.6 | No (manual) |
| Vision analysis | 0.7 | No (manual) |
| Scheduled tasks | 0.7 | Yes (auto) |
| Topic generation | 0.3 | No |
| Compaction summary | 0.3 | No |
| Mood reflection | 0.7 | No |
| Memory memorization | 0.3 | No |

### MiChan Plugins (Tool Calls)

| Plugin | Functions |
|--------|-----------|
| `post` | `get_post`, `create_post`, `react_to_post`, `pin_post`, `unpin_post`, `reply_to_post`, `repost_post`, `search_posts`, `list_posts`, `shuffle_posts`, `list_publisher_posts`, `get_publisher` |
| `account` | `get_account_info`, `search_accounts`, `follow_account`, `unfollow_account`, `get_followers`, `get_following`, `get_bot_profile` |
| `memory` | `search_memory`, `get_memories_by_filter`, `get_memories_by_type`, `store_memory`, `update_memory`, `delete_memory` |
| `userProfile` | `get_user_profile`, `update_user_profile`, `adjust_relationship` |
| `scheduledTasks` | `get_current_time`, `list_scheduled_tasks`, `create_scheduled_task`, `get_scheduled_task`, `update_scheduled_task`, `cancel_scheduled_task`, `delete_scheduled_task` |
| `conversation` | `start_conversation` |
| `mood` | `update_mood`, `get_current_mood`, `record_emotional_event`, `reflect_and_update_mood` |

### Sn-chan Plugins (gRPC, not HTTP)

| Plugin | Functions |
|--------|-----------|
| `SnAccountKernelPlugin` | `get_account`, `get_account_by_name` |
| `SnPostKernelPlugin` | `get_post`, `search_posts`, `list_posts`, `list_posts_within_time`, `list_publisher_posts`, `get_publisher`, `get_publisher_by_id` |

---

## 11. WEBSOCKET CONNECTION

MiChan connects to the gateway WebSocket for real-time events:
- URL: `config.webSocketUrl` (e.g., `ws://localhost:5070/ws`)
- Auth: `Authorization: Bearer <token>` header
- Protocol: Binary `WebSocketPacket` (16KB buffer)
- Events: `OnPacketReceived`, `OnConnected`, `OnDisconnected`

---

## 12. RESPONSE TYPE REQUIREMENTS FOR CLIENT COMPATIBILITY

### HTTP Status Codes

| Code | Usage |
|------|-------|
| 200 | Success with body |
| 201 | Created (feed, task) |
| 204 | No Content (delete, read mark, share update) |
| 400 | Bad Request |
| 401 | Unauthorized |
| 403 | Forbidden (insufficient permissions) |
| 404 | Not Found |
| 409 | Conflict |

### Custom Headers

| Header | Used By |
|--------|---------|
| `X-Total` | Sequence list, article list |
| `X-Has-More` | Thought pagination |
| `X-Offset` | Thought pagination |
| `X-Take` | Thought pagination |

### SSE Format (Critical for Client)

```
Content-Type: text/event-stream
Cache-Control: no-cache
Connection: keep-alive

data: {"type":"text","data":"Hello"}

data: {"type":"function_call","data":{"id":"call_1","pluginName":"post","name":"get_post","arguments":"{\"postId\":\"...\"}"}}

data: {"type":"function_result","data":{"callId":"call_1","functionName":"get_post","result":"{\"id\":\"...\",\"content\":\"...\"}","isError":false}}

topic: {"topic":"AI Chat Discussion"}

thought: {"thoughtId":"uuid-here","sequenceId":"uuid-here"}
```

### Plugin Return Format (Critical for AI Understanding)

All plugin methods must return **JSON strings**, not model types:

```python
# Correct - returns JSON string
async def get_post(self, post_id: str) -> str:
    post = await self.api_client.get(f"/sphere/posts/{post_id}")
    return json.dumps(post, default=str)

# Wrong - returns complex type (agent gets metadata wrapper)
async def get_post(self, post_id: str) -> SnPost:
    return await self.api_client.get(f"/sphere/posts/{post_id}")
```

Standard response envelope for plugins:
```json
{"success": true, "data": {...}}
{"success": false, "error": "error message"}
```

---

## 13. CONFIGURATION

```yaml
# appsettings.yml equivalent
michan:
  enabled: false
  gateway_url: "http://localhost:5070"
  websocket_url: "ws://localhost:5070/ws"
  access_token: ""
  bot_account_id: ""
  bot_publisher_id: ""
  thinking_service: "deepseek-chat"
  personality: ""
  personality_file: null
  
  auto_respond:
    to_chat_messages: true
    to_mentions: true
    to_direct_messages: true
  
  autonomous_behavior:
    enabled: true
    dry_run: false
    fixed_interval_minutes: 10
    min_interval_minutes: 10
    max_interval_minutes: 60
    actions: ["browse", "react", "create_post", "pin", "repost", "start_conversation"]
    personality_mood: "curious, friendly, occasionally philosophical"
    min_repost_age_days: 3
    dynamic_mood:
      enabled: true
      update_interval_minutes: 30
      min_update_interval_minutes: 15
      min_interactions_for_update: 5
      base_personality: "curious, friendly, occasionally philosophical"
    max_conversations_per_day: 3
    min_hours_since_last_contact: 24
    conversation_probability: 10
    reply_probability: 30
    repost_probability: 15
    create_post_probability: 20
  
  post_monitoring:
    enabled: true
    mention_response_timeout_seconds: 30
  
  memory:
    max_context_length: 100
    persist_to_database: true
    enable_semantic_search: true
    min_similarity_threshold: 0.7
    semantic_search_limit: 5
  
  vision:
    vision_thinking_service: "vision-openrouter"
    enable_vision_analysis: true

thinking:
  default_service: "deepseek-chat"
  services:
    deepseek-chat:
      provider: "deepseek"
      model: "deepseek-chat"
      endpoint: "https://api.deepseek.com/v1"
      api_key: ""
      billing_multiplier: 1.0
      perk_level: 0
  embeddings:
    provider: "openai"
    model: "text-embedding-3-small"
    endpoint: "https://api.openai.com/v1"
    api_key: ""
    dimensions: 1536

auth:
  token:
    public_key_path: ""

bing:
  api_key: ""

google:
  api_key: ""
  cx: ""

redis:
  url: "redis://localhost:6379"

database:
  url: "postgresql+asyncpg://user:pass@localhost:5432/insight"
```

---

## 14. IMPLEMENTATION ORDER RECOMMENDATION

1. **Foundation**: Database models + migrations, config, auth
2. **Web scraping**: `/api/scrap/link` + link preview cache (simplest)
3. **Web feeds**: Feed CRUD + article scraping + gRPC services
4. **Billing**: `/api/billing` endpoints + billing job
5. **Thinking core**: ThoughtService, sequences, thoughts, compaction
6. **SSE streaming**: `/api/thought` POST endpoint (hardest part)
7. **Memory system**: pgvector integration, MemoryService
8. **Sn-chan plugins**: Account + Post plugins via gRPC
9. **MiChan plugins**: All 7 plugin namespaces
10. **MiChan autonomous**: Post checking, decision engine, actions
11. **MiChan services**: MoodService, UserProfileService, InteractiveHistoryService
12. **Scheduled tasks**: CRUD + Quartz-equivalent job
13. **WebSocket**: Real-time connection to gateway
14. **Background jobs**: All scheduled jobs

---

## 15. KNOWN PITFALLS

1. **Token counting**: Tiktoken is not available natively in Python. Use `tiktoken` package (pip). Model-specific tokenizers needed.
2. **pgvector**: Requires `pgvector` PostgreSQL extension. Use `pgvector-python` for async SQLAlchemy.
3. **SSE streaming**: FastAPI supports this natively via `StreamingResponse`. Must match the exact event format the client expects.
4. **Soft deletes**: EF Core global query filters translate to SQLAlchemy `@event.listens_for` or `query_cls` pattern.
5. **NodaTime**: Python equivalent is `datetime` with `timezone.utc`. Duration parsing needs custom logic.
6. **Semantic Kernel**: No direct Python equivalent. Use LangChain or direct OpenAI SDK with function calling.
7. **gRPC proto files**: Copy `.proto` files from gateway. Generate Python stubs with `grpc_tools.protoc`.
8. **Vision analysis**: Multimodal model support varies by provider. OpenRouter provides good vision models.
9. **Compaction logic**: The MiChan history compaction is complex. Port carefully - it decides when to summarize old conversations to save tokens.
10. **Distributed locking**: Use `redis.lock()` or `redlock` for the startup sequence unification.
