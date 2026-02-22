# DysonNetwork.Zone FullyManaged Template Generator: Client Migration Guide

## Audience
This document is for dashboard/web clients and automation tools that manage publication sites in DysonNetwork.Zone.a
When using with Gateway, the `/api` should change to `/zone` in base url

## What changed
`FullyManaged` no longer renders from DB publication pages.

Old behavior (removed for `FullyManaged`):
- Read `publication_pages` records (`HtmlPage` / `Redirect` / `PostPage`) and render from DB config.

New behavior:
- Read files from site storage.
- Render `.liquid` templates dynamically.
- Serve non-`.liquid` files as static assets.

`SelfManaged` behavior is unchanged (static hosting).

## Required client changes

### 1. Use file APIs for FullyManaged content
If your client previously created/updated DB pages for `FullyManaged`, switch to file upload/edit/deploy APIs:
- `GET /api/sites/{siteId}/files`
- `POST /api/sites/{siteId}/files/upload`
- `POST /api/sites/{siteId}/files/deploy`
- `PUT /api/sites/{siteId}/files/edit/{**relativePath}`
- `DELETE /api/sites/{siteId}/files/delete/{**relativePath}`
- `DELETE /api/sites/{siteId}/files/purge`

Important:
- These file APIs are now available for both `FullyManaged` and `SelfManaged`.

### 2. Stop relying on DB page rendering in FullyManaged
For `FullyManaged` sites, page CRUD APIs may still exist but are no longer the render source:
- `POST /api/sites/{pubName}/{siteSlug}/pages`
- `PATCH /api/sites/pages/{id}`
- `DELETE /api/sites/pages/{id}`

If your frontend currently has a “page builder” for `FullyManaged`, migrate it to a “template/file manager”.

### 3. Keep sending `X-SiteName` on public render requests
Public runtime site selection still uses:
- HTTP header: `X-SiteName: {siteSlug}`

Without this header, the request falls through to normal app routing.

### 4. Adopt template file conventions
Convention resolution order:
- `/` -> `index.html.liquid` (or `templates/index.html.liquid`)
- `/foo` -> `foo.html.liquid` -> `foo/index.html.liquid`
- Optional `routes.json` (or `templates/routes.json`) can map route -> template explicitly
- 404 fallback: `404.html.liquid` (or `templates/404.html.liquid`)

Layout convention:
- `layout.html.liquid` (or `templates/layout.html.liquid`) wraps page output via `content_for_layout`.

### 5. Asset handling expectation
- Non-`.liquid` files are served directly as static assets.
- `.liquid` files are rendered as templates.

## Template runtime context (v1)
Templates can use:
- `site`
- `route`
- `page`
- `posts`
- `post`
- `publisher`
- `now`
- `asset_url`
- Compatibility helpers: `page_type`, `content_for_layout`, `theme`, `locale`, `config`, `base_url`

## Recommended migration checklist
1. For each `FullyManaged` site, export or recreate page content into template files.
2. Upload template package with `/files/deploy` (zip) or individual `/files/upload`.
3. Ensure at least:
   - `index.html.liquid`
   - `layout.html.liquid` (optional but recommended)
   - `404.html.liquid` (recommended)
4. If needed, add `routes.json` for dynamic routes like `/posts/{slug}`.
5. Update frontend UX labels from “Pages” to “Templates/Files” for `FullyManaged`.
6. Validate rendering with `X-SiteName` header and key routes.

## Compatibility notes
- This is a hard switch for `FullyManaged` rendering.
- Existing DB page data is not used at runtime for `FullyManaged`.
- `SelfManaged` static behavior remains unchanged.

## Example minimal file tree
```text
/
  index.html.liquid
  layout.html.liquid
  404.html.liquid
  routes.json
  css/style.css
  js/site.js
  templates/
    head.html.liquid
    article.html.liquid
```
