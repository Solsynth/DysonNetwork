# DysonNetwork.Zone Template & Theming Guide

This guide explains how to build template-based sites for `FullyManaged` mode in DysonNetwork.Zone.

## 1. Overview

In `FullyManaged`, Zone renders `.liquid` files from your site file storage at request time.

- `.liquid` files: rendered by DotLiquid
- non-`.liquid` files: served as static files (css/js/images/fonts/etc.)

## 2. Upload and manage files

Use file APIs under:

- `GET /api/sites/{siteId}/files`
- `POST /api/sites/{siteId}/files/upload`
- `POST /api/sites/{siteId}/files/folder`
- `PUT /api/sites/{siteId}/files/edit/{**relativePath}`
- `DELETE /api/sites/{siteId}/files/delete/{**relativePath}`
- `POST /api/sites/{siteId}/files/deploy` (zip deploy)

Create folder example:

```http
POST /api/sites/{siteId}/files/folder
Content-Type: application/json

{
  "path": "templates/partials"
}
```

## 3. Route resolution rules

Zone resolves routes in this order:

1. Convention lookup

- `/` -> `index.html.liquid`
- `/foo` -> `foo.html.liquid`
- `/foo` -> `foo/index.html.liquid`
- same checks also under `templates/`

1. Optional manifest lookup

- `routes.json` or `templates/routes.json`

1. Fallback 404 template

- `404.html.liquid` or `templates/404.html.liquid`

If no template/static file matches, request falls through to app default routing.

## 4. Layout and partials

### Layout

If current template is not `layout.html.liquid`, Zone will look for:

- `layout.html.liquid`, then
- `templates/layout.html.liquid`

If found, rendered page content is injected into `content_for_layout`.

Example layout usage:

```liquid
<!doctype html>
<html>
  <body>
    {{ content_for_layout }}
  </body>
</html>
```

### Partials

Your theme can use Shopify-style `render`:

```liquid
{% render 'head' %}
{% render 'article', post: post %}
```

Zone registers `render` to DotLiquid include behavior and resolves candidates like:

- `templates/head.html.liquid`
- `templates/head.liquid`
- `head.html.liquid`
- `head.liquid`

## 5. `routes.json` format

Place at root or `templates/routes.json`.

```json
{
  "routes": [
    {
      "path": "/",
      "template": "templates/index.html.liquid",
      "page_type": "home",
      "data": {
        "mode": "posts_list",
        "order_by": "published_at",
        "order_desc": true,
        "page_size": 10,
        "types": ["article"]
      }
    },
    {
      "path": "/posts/{slug}",
      "template": "templates/post.html.liquid",
      "page_type": "post",
      "data": {
        "mode": "post_detail",
        "slug_param": "slug"
      }
    },
    {
      "path": "/github",
      "redirect_to": "https://github.com/your-org/your-repo",
      "redirect_status": 302
    }
  ]
}
```

Supported route fields:

- `path` (supports `{param}` segment placeholders)
- `template`
- `redirect_to` (optional redirect target URL/path)
- `redirect_status` (optional; supports 301, 302, 307, 308; default 302)
- `page_type` (optional)
- `data.mode`: `posts_list` | `post_detail` | `none`
- `data.order_by`, `data.order_desc`, `data.page_size`, `data.types`, `data.publisher_ids`, `data.categories`, `data.tags`, `data.query`, `data.include_replies`, `data.include_forwards`, `data.slug_param`
- `query_defaults` is accepted in schema but currently not applied at runtime.

## 6. Template variables available

Top-level variables injected by Zone:

- `site`
- `publisher`
- `route`
- `page`
- `posts`
- `post`
- `page_type`
- `asset_url`
- `base_url`
- `config`
- `theme`
- `locale`
- `now`
- `open_graph_tags`
- `feed_tag`
- `favicon_tag`
- `content_for_layout` (only when layout wrapping is used)

### `site`

- `id`, `slug`, `name`, `description`, `mode`, `publisher_id`, `config`

### `route`

- `path`
- `query` (dictionary)
- `params` (dictionary from `{param}`)
- `index`, `page`

### `page` (list pages)

- `title`, `description`, `posts`
- `current`, `total`, `total_size`
- `prev_link`, `next_link`, `pagination_html`

### `post` / `page.posts` items

- `id`, `title`, `description`, `slug`
- `layout`, `content`, `excerpt`
- `path`, `url`
- `photos` (image URLs)
- `attachments` (objects with `id`, `name`, `url`, `mime_type`, `size`, `width`, `height`, `is_image`)
- `word_count`, `published_at`
- `categories[]`, `tags[]`

## 7. Minimal starter structure

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

## 8. Example templates

### `index.html.liquid`

```liquid
<h1>{{ site.name }}</h1>

{% for post in page.posts %}
  {% render 'article', post: post %}
{% endfor %}
```

### `templates/article.html.liquid`

```liquid
<article>
  <h2><a href="{{ post.path }}">{{ post.title }}</a></h2>
  <p>{{ post.excerpt }}</p>
</article>
```

## 9. Theming tips

- Keep all theme partials under `templates/` for predictable lookup.
- Put CSS/JS/fonts/images in static folders (`css/`, `js/`, `images/`) and reference with root-relative URLs.
- Use `site.config` for site-level style toggles/content decisions.
- Prefer route manifest for post detail pages (`/posts/{slug}`) instead of hardcoding path parsing in templates.

## 10. Rendering tags, categories, and attachments

### Categories

```liquid
{% if post.categories and post.categories.size > 0 %}
  <ul class="post-categories">
    {% for category in post.categories %}
      <li><a href="{{ category.path }}">{{ category.name }}</a></li>
    {% endfor %}
  </ul>
{% endif %}
```

### Tags

```liquid
{% if post.tags and post.tags.size > 0 %}
  <ul class="post-tags">
    {% for tag in post.tags %}
      <li><a href="{{ tag.path }}">#{{ tag.name }}</a></li>
    {% endfor %}
  </ul>
{% endif %}
```

### Attachments (generic)

```liquid
{% if post.attachments and post.attachments.size > 0 %}
  <ul class="post-attachments">
    {% for file in post.attachments %}
      <li>
        <a href="{{ file.url }}" target="_blank" rel="noopener">
          {{ file.name | default: file.id }}
        </a>
        {% if file.mime_type %} ({{ file.mime_type }}){% endif %}
      </li>
    {% endfor %}
  </ul>
{% endif %}
```

### Attachments (images only)

```liquid
{% if post.attachments and post.attachments.size > 0 %}
  <div class="post-gallery">
    {% for file in post.attachments %}
      {% if file.is_image %}
        <img src="{{ file.url }}" alt="{{ file.name }}" loading="lazy" />
      {% endif %}
    {% endfor %}
  </div>
{% endif %}
```

### Existing shortcut: `post.photos`

If you only need image URLs, `post.photos` remains available:

```liquid
{% for image in post.photos %}
  <img src="{{ image }}" alt="" loading="lazy" />
{% endfor %}
```

### Debug nested values (`photos`, `attachments`, `tags`, `categories`)

DotLiquid may print CLR type names when you output whole objects directly.
Use the built-in filters below for readable output:

```liquid
{{ post | json }}
{{ post.attachments | json }}
{{ post.tags | json }}
{{ post.categories | inspect }}
```

## 11. Current limitations

- `query_defaults` in `routes.json` is not applied yet.
- `asset_url` is currently an empty string by default; use root-relative paths for assets.
- `open_graph_tags`, `feed_tag`, and `favicon_tag` are placeholders (empty by default).

## 12. RSS configuration per site

RSS is configured via `site.config.rss` (in site create/update API payload).

Example:

```json
{
  "rss": {
    "enabled": true,
    "path": "/feed.xml",
    "source_route_path": "/posts",
    "title": "My Site Feed",
    "description": "Latest updates",
    "order_by": "published_at",
    "order_desc": true,
    "item_limit": 30,
    "types": ["article", "moment"],
    "publisher_ids": [
      "11111111-1111-1111-1111-111111111111",
      "22222222-2222-2222-2222-222222222222"
    ],
    "include_replies": false,
    "include_forwards": true,
    "categories": ["tech"],
    "tags": ["dotnet"],
    "query": "release",
    "content_mode": "excerpt",
    "post_url_pattern": "/posts/{slug}"
  }
}
```

Fields:

- `enabled`: turn RSS on/off for this site
- `path`: request path to serve RSS (for example `/feed.xml`)
- `source_route_path`: optional route path (from `routes.json`) to reuse regular posts-page filters
- `title`, `description`: feed metadata overrides
- `order_by`, `order_desc`, `item_limit`: post selection and ordering
- `types`: `article` and/or `moment`
- `publisher_ids`: custom publisher scope for feed (if empty, uses site publisher only)
- `include_replies`: include/exclude reply posts
- `include_forwards`: include/exclude forwarded posts
- `categories`, `tags`, `query`: additional post filters
- `content_mode`: `excerpt` | `html` | `none`
- `post_url_pattern`: supports `{slug}` and `{id}`

Notes:

- RSS serving applies to `FullyManaged` sites (resolved in site middleware).
- Request must still target the site context (for example with `X-SiteName` in gateway/internal routing flow).
- When `source_route_path` is set, RSS can inherit route `data` filters (such as `types`, `categories`, `tags`, `query`, `publisher_ids`); explicit RSS fields still take precedence.

## 13. Troubleshooting

### `Unknown tag 'render'`

- Ensure Zone is updated to a build that registers `render` alias.
- Restart/redeploy Zone service/container after upgrade.

### Template not found

- Verify route conventions and actual filename.
- Check whether file is uploaded under site root or `templates/`.

### No posts rendered

- Confirm route `data.mode` and `types` in `routes.json`.
- Verify publisher has available posts.
