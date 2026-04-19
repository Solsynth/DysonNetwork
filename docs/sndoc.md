# SnDoc - Solar Network Documentation System

SnDoc is a semantic documentation system for Solar Network that allows storing, searching, and retrieving documentation with AI-powered semantic search capabilities.

## Overview

SnDoc provides:
- **Document Storage**: Store documentation pages with slug-based URLs
- **Automatic Chunking**: Large documents are automatically split into chunks for better search granularity
- **Semantic Search**: Find relevant docs using natural language queries
- **Agent Integration**: MiChan and SnChan agents can search and read docs via plugins
- **REST API**: Full CRUD API for CLI and external integrations

## Architecture

```
┌─────────────┐     ┌──────────────┐     ┌─────────────┐
│   CLI/API   │────▶│ SnDocService │────▶│  Database   │
└─────────────┘     └──────────────┘     └─────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │EmbeddingSvce │
                    └──────────────┘
                           │
                           ▼
                    ┌──────────────┐
                    │  AI Models   │
                    └──────────────┘
```

### Database Schema

**sn_doc_pages** - Document metadata
- `id` (UUID, PK)
- `slug` (unique, URL-friendly identifier)
- `title`, `description`, `content`
- `chunk_count`, `content_length`
- `created_at`, `updated_at`, `deleted_at` (soft delete)

**sn_doc_chunks** - Searchable document chunks
- `id` (UUID, PK)
- `page_id` (FK → sn_doc_pages)
- `chunk_index`, `content`, `embedding` (1536-dim vector)
- `start_offset`, `end_offset` (position in original content)
- `is_first_chunk` (for quick lookups)

## REST API

### Authentication

- **Write operations** (`POST`, `DELETE`): Requires `docs.write` permission via `Authorization: Bearer <token>` header
- **Read operations** (`GET`, `POST /search`): Public access

### Endpoint Summary

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| **POST** | `/api/docs/pages` | `docs.write` | Create or update page (upsert) |
| **GET** | `/api/docs/pages` | Public | List all pages (summary) |
| **GET** | `/api/docs/pages/slug/{**slug}` | Public | Get page by slug (supports slashes like `api/v2/auth`) |
| **GET** | `/api/docs/pages/{id:guid}` | Public | Get page by ID |
| **GET** | `/api/docs/pages/{id:guid}/content` | Public | Read content with pagination |
| **DELETE** | `/api/docs/pages/slug/{**slug}` | `docs.write` | Delete by slug (supports slashes) |
| **DELETE** | `/api/docs/pages/{id:guid}` | `docs.write` | Delete by ID |
| **POST** | `/api/docs/search` | Public | Semantic search |

> **Note**: The `{**slug}` syntax is a catch-all parameter that allows slugs to contain forward slashes (e.g., `api/v2/authentication`, `guides/getting-started/setup`).

### Endpoints

#### Create or Update Document
```http
POST /api/docs/pages
Authorization: Bearer <token>
Content-Type: application/json

{
  "slug": "getting-started",
  "title": "Getting Started",
  "description": "A quick start guide for new users",
  "content": "# Getting Started\n\nWelcome to Solar Network..."
}
```

**Response**: Full page object with generated embeddings and chunks

#### List All Documents
```http
GET /api/docs/pages?limit=20&offset=0
```

**Response**: Array of page summaries (no full content)

#### Get Document by Slug
```http
GET /api/docs/pages/slug/getting-started
```

Slug can contain slashes for hierarchical organization:
```http
GET /api/docs/pages/slug/api/v2/authentication
```

#### Get Document by ID
```http
GET /api/docs/pages/550e8400-e29b-41d4-a716-446655440000
```

#### Read Document Content (Paginated)
```http
GET /api/docs/pages/{id}/content?offset=0&take=4000
```

- `offset`: Character offset to start reading (default: 0)
- `take`: Number of characters to return (default: all, max: 8000)
- `has_more`: Indicates if more content is available

#### Search Documents
```http
POST /api/docs/search
Content-Type: application/json

{
  "query": "how to create a post",
  "limit": 10,
  "min_similarity": 0.6
}
```

**Parameters**:
- `query`: Natural language search query
- `limit`: Max results (default: 10)
- `min_similarity`: Similarity threshold 0.0-1.0 (default: 0.6)

**Response**:
```json
{
  "query": "how to create a post",
  "results": [
    {
      "page_id": "...",
      "slug": "posting-guide",
      "title": "Posting Guide",
      "similarity": 0.89,
      "best_chunk_index": 0,
      "relevant_chunk_indices": [0, 2]
    }
  ]
}
```

#### Delete Document by Slug
```http
DELETE /api/docs/pages/slug/getting-started
Authorization: Bearer <token>
```

Supports slashes:
```http
DELETE /api/docs/pages/slug/api/v2/authentication
```

#### Delete Document by ID
```http
DELETE /api/docs/pages/550e8400-e29b-41d4-a716-446655440000
Authorization: Bearer <token>
```

## CLI Usage

### Upload a Document

```bash
# Create or update a doc page
curl -X POST https://api.solarnetwork.io/api/docs/pages \
  -H "Authorization: Bearer $ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d @- << 'EOF'
{
  "slug": "api/v2/reference",
  "title": "API v2 Reference",
  "description": "Complete API documentation",
  "content": "# API v2 Reference\n\n## Authentication\nAll API requests require..."
}
EOF
```

### Search Documentation

```bash
# Find relevant docs
curl -X POST https://api.solarnetwork.io/api/docs/search \
  -H "Content-Type: application/json" \
  -d '{"query": "authentication token", "limit": 5}'
```

### Read Full Document

```bash
# Get doc by slug
curl https://api.solarnetwork.io/api/docs/pages/slug/api/v2/reference

# Or read with pagination (for large docs)
curl "https://api.solarnetwork.io/api/docs/pages/{page-id}/content?offset=0&take=4000"
```

## Agent Plugin (SnDocPlugin)

MiChan and SnChan agents have access to the `SnDoc` plugin with these functions:

### `search_docs`

Search documentation using semantic search.

```
search_docs(
  query: "how to set up two-factor authentication",
  limit: 5,
  min_similarity: 0.6
)
```

**Returns**: Formatted list of matching pages with relevance scores and preview snippets.

### `read_doc`

Read a specific document by ID with optional pagination.

```
read_doc(
  page_id: "550e8400-e29b-41d4-a716-446655440000",
  offset: 0,
  take: 4000
)
```

**Returns**: Full document content with pagination info.

### `list_docs`

List all available documentation pages.

```
list_docs(limit: 20)
```

**Returns**: Summary of all pages (title, slug, description, size).

### `get_doc_by_slug`

Get a document by its slug identifier.

```
get_doc_by_slug(
  slug: "api/v2/reference",
  preview_length: 500
)
```

**Returns**: Page details with content preview.

## Chunking Strategy

Documents are automatically chunked for optimal semantic search:

- **Chunk Size**: ~1000 characters per chunk
- **Overlap**: 200 characters between chunks (for context continuity)
- **Break Points**: Prefers paragraph breaks (\n\n) or sentence endings (.!?)
- **Embeddings**: Each chunk gets a 1536-dimensional vector embedding

The embedding text format:
```
Title: {page_title}
Description: {page_description}
Content:
{chunk_content}
```

## Slug Best Practices

SnDoc supports hierarchical slugs with slashes:

**Good examples**:
- `getting-started`
- `api/authentication`
- `api/v2/posts/create`
- `guides/mobile/ios/setup`

**Structure**:
- Use lowercase letters, numbers, and hyphens
- Use slashes for hierarchy: `category/subcategory/page`
- Keep it descriptive but concise
- Avoid special characters except `-` and `/`

## Implementation Details

### Services

- **SnDocService** (`/SnDoc/SnDocService.cs`): Core business logic, chunking, embeddings
- **SnDocController** (`/SnDoc/SnDocController.cs`): REST API endpoints
- **SnDocPlugin** (`/SnDoc/SnDocPlugin.cs`): Semantic Kernel plugin for agents

### Models

- **SnDocPage** (`/SnDoc/SnDocPage.cs`): Document metadata and full content
- **SnDocChunk** (`/SnDoc/SnDocChunk.cs`): Searchable chunk with embedding

### Database Migration

```bash
cd DysonNetwork.Insight
dotnet ef database update
```

Migration: `20260419052447_AddSnDocTables`

## Configuration

No additional configuration required. Uses existing:
- `EmbeddingService` for vector generation
- `AppDatabase` with pgvector extension
- Authentication via existing permission middleware

## Best Practices

### Writing Good Documentation

1. **Use descriptive slugs**: `api/v2/authentication` not `doc-1`
2. **Include a description**: Helps with search relevance
3. **Structure content**: Use headers for better chunk boundaries
4. **Keep sections focused**: Each chunk should cover one topic

### Search Tips

- Be specific: "create encrypted DM" vs "messaging"
- Use natural language: "how do I reset my password?"
- Check similarity scores: Results below 0.6 may be irrelevant

### For CLI Tools

1. Use `slug` for human-friendly references (supports slashes)
2. Use `page_id` returned from search for subsequent reads
3. Implement pagination for documents > 4000 chars
4. Cache search results briefly to reduce API calls

## Examples

### Complete CLI Workflow

```bash
# 1. Upload hierarchical documentation
SLUG="api/v2/encryption-guide"
curl -X POST /api/docs/pages \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"slug\": \"$SLUG\",
    \"title\": \"End-to-End Encryption Guide\",
    \"description\": \"How Solar Network encrypts your messages\",
    \"content\": \"# End-to-End Encryption...\"
  }"

# 2. Search for relevant docs
RESULT=$(curl -s -X POST /api/docs/search \
  -d '{"query": "how is my data encrypted"}')
PAGE_ID=$(echo $RESULT | jq -r '.results[0].page_id')

# 3. Read the full document
curl "/api/docs/pages/$PAGE_ID/content"

# 4. Update the doc
curl -X POST /api/docs/pages \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"slug\": \"$SLUG\",
    \"title\": \"Updated Encryption Guide\",
    \"content\": \"# Updated Content...\"
  }"

# 5. Delete by slug
curl -X DELETE "/api/docs/pages/slug/$SLUG" \
  -H "Authorization: Bearer $TOKEN"
```

### Agent Interaction Example

```
User: How do I enable 2FA?

Agent: [Calls search_docs]
Found 2 relevant documentation pages:

1. Page ID: xxx
   Slug: security/2fa-setup
   Title: Two-Factor Authentication Setup
   Relevance: 92%

2. Page ID: yyy
   Slug: guides/account-setup
   Title: Account Setup Guide
   Relevance: 78%

[Calls read_doc for page xxx]

To enable two-factor authentication:
1. Go to Settings > Security
2. Click "Enable 2FA"
3. Scan the QR code with your authenticator app
...
```

## Troubleshooting

### "Permission denied" on upload
- Verify your account has the `docs.write` permission
- Check the Authorization header format: `Bearer <token>`

### Search returns no results
- Try broader search terms
- Lower `min_similarity` threshold (e.g., 0.5)
- Verify documents have been indexed (check chunk count)

### Large documents truncated
- Use the `/content` endpoint with pagination
- Default `take` limit is 8000 characters
- Check `has_more` flag for continuation

### Slug with slashes not working
- Ensure URL encoding: `/` becomes `%2F` in query strings
- When using curl, quote the URL: `curl "/api/docs/pages/slug/api/v2/auth"`

## Future Enhancements

- [ ] Version history for documents
- [ ] Markdown rendering endpoint
- [ ] Category/tags support
- [ ] Related document suggestions
- [ ] Search result highlighting
