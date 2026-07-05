# Private Developer Routes

This document describes the new private route layout for developer-owned resources.

## Summary

The old nested routes like:

- `/api/developers/{pubName}/projects/{projectId}/apps`
- `/api/developers/{pubName}/projects/{projectId}/bots`
- `/api/developers/{pubName}/projects/{projectId}/miniapps`

have been replaced with shorter private routes.

This is a **breaking change**.

## Route design

- Main resource identity lives in the path.
- Ownership context is passed by query string.
- Use `?dev=` for developer slug.
- Use `?proj=` for project id when the resource belongs to a project.

## New routes

### Projects

Base:

- `GET /api/private/projects?dev={developer_slug}`
- `POST /api/private/projects?dev={developer_slug}`
- `GET /api/private/projects/{id}?dev={developer_slug}`
- `PUT /api/private/projects/{id}?dev={developer_slug}`
- `DELETE /api/private/projects/{id}?dev={developer_slug}`

### Custom apps

Base:

- `GET /api/private/apps?dev={developer_slug}&proj={project_id}`
- `POST /api/private/apps?dev={developer_slug}&proj={project_id}`
- `GET /api/private/apps/{appId}?dev={developer_slug}&proj={project_id}`
- `PATCH /api/private/apps/{appId}?dev={developer_slug}&proj={project_id}`
- `DELETE /api/private/apps/{appId}?dev={developer_slug}&proj={project_id}`

Secrets:

- `GET /api/private/apps/{appId}/secrets?dev={developer_slug}&proj={project_id}`
- `POST /api/private/apps/{appId}/secrets?dev={developer_slug}&proj={project_id}`
- `GET /api/private/apps/{appId}/secrets/{secretId}?dev={developer_slug}&proj={project_id}`
- `DELETE /api/private/apps/{appId}/secrets/{secretId}?dev={developer_slug}&proj={project_id}`
- `POST /api/private/apps/{appId}/secrets/{secretId}/rotate?dev={developer_slug}&proj={project_id}`

Products:

- `GET /api/private/apps/{appId}/products?dev={developer_slug}&proj={project_id}`
- `POST /api/private/apps/{appId}/products?dev={developer_slug}&proj={project_id}`
- `GET /api/private/apps/{appId}/products/{productId}?dev={developer_slug}&proj={project_id}`
- `PATCH /api/private/apps/{appId}/products/{productId}?dev={developer_slug}&proj={project_id}`
- `DELETE /api/private/apps/{appId}/products/{productId}?dev={developer_slug}&proj={project_id}`

Product payloads also support `fulfillment` and `state`.
See `docs/APP_PRODUCTS.md`.

### Bots

Base:

- `GET /api/private/bots?dev={developer_slug}&proj={project_id}`
- `POST /api/private/bots?dev={developer_slug}&proj={project_id}`
- `GET /api/private/bots/{botId}?dev={developer_slug}&proj={project_id}`
- `PATCH /api/private/bots/{botId}?dev={developer_slug}&proj={project_id}`
- `DELETE /api/private/bots/{botId}?dev={developer_slug}&proj={project_id}`

Keys:

- `GET /api/private/bots/{botId}/keys?dev={developer_slug}&proj={project_id}`
- `POST /api/private/bots/{botId}/keys?dev={developer_slug}&proj={project_id}`
- `GET /api/private/bots/{botId}/keys/{keyId}?dev={developer_slug}&proj={project_id}`
- `POST /api/private/bots/{botId}/keys/{keyId}/rotate?dev={developer_slug}&proj={project_id}`
- `DELETE /api/private/bots/{botId}/keys/{keyId}?dev={developer_slug}&proj={project_id}`

Chat:

- `GET /api/private/bots/{botId}/chat?dev={developer_slug}&proj={project_id}`
- `PUT /api/private/bots/{botId}/chat?dev={developer_slug}&proj={project_id}`
- `POST /api/private/bots/{botId}/chat/manifest?dev={developer_slug}&proj={project_id}`

### Mini apps

Base:

- `GET /api/private/miniapps?dev={developer_slug}&proj={project_id}`
- `POST /api/private/miniapps?dev={developer_slug}&proj={project_id}`
- `GET /api/private/miniapps/{miniAppId}?dev={developer_slug}&proj={project_id}`
- `PATCH /api/private/miniapps/{miniAppId}?dev={developer_slug}&proj={project_id}`
- `DELETE /api/private/miniapps/{miniAppId}?dev={developer_slug}&proj={project_id}`

## Notifications endpoint

Custom app notifications live in Padlock:

- `POST /api/private/apps/{appId}/notifications`

Authentication:

- `X-Api-Key: {custom_app_api_key}`

Behavior:

- only targets accounts that authorized the app with scope `notifications.send`
- accepts single target, many targets, or broadcast to all authorized users

Request body supports:

- `account_id`
- `account_ids`
- `broadcast_to_all`
- `topic`
- `title`
- `subtitle`
- `body`
- `action_uri`
- `push_type`
- `is_silent`
- `is_savable`
- `meta`

## Migration note

Frontend callers must stop using the old nested developer/project route tree and switch to the new `/api/private/...` endpoints immediately.
