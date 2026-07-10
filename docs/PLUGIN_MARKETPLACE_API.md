# Plugin Marketplace API

The Develop service exposes developer-owned plugin management endpoints and a
public production marketplace. Plugins are stored as metadata plus a full
manifest JSON document. The installable plugin asset is a ZIP package stored in
S3.

## Plugin package

A package must contain exactly one `manifest.json` and should contain the entry
file declared by the manifest, normally `main.js`:

```text
my-plugin.zip
└── my-plugin/
    ├── manifest.json
    └── main.js
```

The backend validates the ZIP structure, rejects unsafe archive paths, and
requires `manifest.json` to contain `id` and `name`. The uploaded package is
limited to 5 MiB.

The package checksum is a lowercase SHA-256 digest of the exact uploaded ZIP
bytes. Clients should verify this digest after downloading before installing.

## Manifest

The manifest follows the Island plugin contract:

```json
{
  "id": "com.example.my_plugin",
  "name": "My Plugin",
  "version": "1.0.0",
  "author": "Example",
  "description": "A short description.",
  "entry": "main.js",
  "permissions": ["commandsRegister", "notify"],
  "background": false,
  "icon": "extension",
  "homepage": "https://example.com/my-plugin"
}
```

The full manifest is stored in the `manifest` JSONB column. Marketplace
metadata needed for filtering and sorting is also denormalized into columns:
`plugin_id`, `name`, `version`, `author`, `description`, `homepage`, and package
metadata. `plugin_id` is globally unique.

Plugin `icon` and `background` file references use
`SnCloudFileReferenceObject` snapshots in JSONB columns. They are supplied as
cloud file IDs in private create/update requests.

## Private developer API

All private endpoints require authentication, the developer/project query
context, publisher membership, and the permission listed below.

### List plugins

```http
GET /api/private/miniapps?dev={developer_slug}&proj={project_id}
```

Permission: `mini.apps.view`

### Create a plugin

```http
POST /api/private/miniapps?dev={developer_slug}&proj={project_id}
Content-Type: application/json
```

Permission: `mini.apps.create`

```json
{
  "slug": "my-plugin",
  "stage": 0,
  "manifest": { "id": "com.example.my_plugin", "name": "My Plugin" },
  "icon_id": "cloud-file-id",
  "background_id": "cloud-file-id"
}
```

### Update a plugin

```http
PATCH /api/private/miniapps/{mini_app_id}?dev={developer_slug}&proj={project_id}
```

Permission: `mini.apps.update`

`icon_id` and `background_id` resolve through the file service and are stored
as denormalized cloud-file references. Send an empty ID to clear a reference.

### Upload a plugin package

```http
POST /api/private/miniapps/{mini_app_id}/package?dev={developer_slug}&proj={project_id}
Content-Type: multipart/form-data
```

Form field: `File`

Permission: `mini.apps.package.upload`

Example response:

```json
{
  "key": "plugins/00000000-0000-0000-0000-000000000000/abc123.zip",
  "url": "https://cdn.example.com/plugins/.../abc123.zip",
  "file_name": "my-plugin.zip",
  "content_type": "application/zip",
  "size": 48321,
  "sha256": "7f83b1657ff1fc53b92dc18148a1d65dfa135cce4f6f7f3b0f6f5f6f6f6f6f6f"
}
```

The checksum, package key, URL, and size are also persisted on the plugin
record. `PluginStorage:S3:PublicBaseUrl` must be configured for a public URL;
the storage key and checksum are still returned if it is omitted.

### Get or delete a plugin

```http
GET /api/private/miniapps/{mini_app_id}?dev={developer_slug}&proj={project_id}
DELETE /api/private/miniapps/{mini_app_id}?dev={developer_slug}&proj={project_id}
```

Permissions: `mini.apps.view` and `mini.apps.delete` respectively.

## Public marketplace

Only plugins with `stage = Production` are visible:

```http
GET /api/miniapps?take=20&offset=0&search=calendar
GET /api/miniapps/{slug}
```

Discovery returns `X-Total` and searches the relational `slug`, `plugin_id`,
`name`, and `description` columns. Publisher metadata is hydrated from the
publisher service.
