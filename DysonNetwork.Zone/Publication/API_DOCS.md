# Publication Site Management API

This API provides file management capabilities for self-managed publication sites on the Dyson Network platform. It allows authenticated users with editor permissions to manage static files for their sites.

When using with the gateway, the `/api` should be replaced with the `/zone`

## Overview

The Publication API provides comprehensive management capabilities for publication sites and their content on the Dyson Network platform. It includes:

### Site Management
- Site CRUD operations (Create, Read, Update, Delete)
- Publisher-based site organization
- Support for FullyManaged and SelfManaged site modes

### Page Management
- Page CRUD operations within sites
- Support for HTML pages and redirects
- Flexible configuration using JSON config objects

### File Management (SelfManaged Sites Only)
- File and directory listing
- File upload with size validation
- File content reading and editing
- File downloading with appropriate MIME types
- File deletion
- Total site size tracking and limits

### Base URL
```
/api/sites/{siteId}/files
```

### Authentication
All endpoints require authentication via JWT bearer token or similar authentication mechanism configured in the application.

### Authorization
- User must be authenticated
- Site must exist and be in `SelfManaged` mode
- User must have `Editor` role in the site's publisher

### File Limits
- **Individual file size limit**: 1 MB (1,048,576 bytes)
- **Total site size limit**: 25 MB (26,214,400 bytes)
- Files are stored in the site's dedicated directory structure

## Endpoints

## Site Management Endpoints

The following endpoints handle publication site CRUD operations.

### Get Site (Public)

Get a publication site by publisher name and slug.

**Endpoint**: `GET /api/sites/{pubName}/{slug}`

**Response**: `200 OK`

```json
{
  "id": "123e4567-e89b-12d3-a456-426614174000",
  "slug": "my-site",
  "name": "My Site",
  "description": "A description of my site",
  "mode": "FullyManaged",
  "pages": [
    {
      "id": "456e7890-e89b-12d3-a456-426614174000",
      "type": "HtmlPage",
      "path": "/",
      "config": {
        "content": "<h1>Home</h1>",
        "title": "Home Page"
      },
      "site_id": "123e4567-e89b-12d3-a456-426614174000"
    }
  ],
  "publisher_id": "456e7890-e89b-12d3-a456-426614174000",
  "account_id": "789e0123-e89b-12d3-a456-426614174000"
}
```

### List Sites for Publisher

List all sites for a specific publisher.

**Endpoint**: `GET /api/sites/{pubName}`

**Authorization**: Required (Viewer role or higher in publisher)

**Response**: `200 OK`

```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "slug": "site1",
    "name": "Site One",
    "description": "First site",
    "mode": "SelfManaged",
    "publisher_id": "456e7890-e89b-12d3-a456-426614174000",
    "account_id": "789e0123-e89b-12d3-a456-426614174000"
  }
]
```

### List Owned Sites

List all sites for publishers where the authenticated user is a member.

**Endpoint**: `GET /api/sites/me`

**Authorization**: Required

**Response**: `200 OK` - Array of sites as shown above.

### Create Site

Create a new publication site.

**Endpoint**: `POST /api/sites/{pubName}`

**Authorization**: Required (Editor role or higher in publisher)

**Request Body**:

```json
{
  "mode": "SelfManaged",
  "slug": "my-new-site",
  "name": "My New Site",
  "description": "Description of my new site"
}
```

**Response**: `200 OK` - Returns created site object.

**Validation**:
- User must have appropriate permissions in the publisher
- Slug must be unique within the publisher
- Name is required, max 4096 characters
- Description max 8192 characters

### Update Site

Update an existing publication site.

**Endpoint**: `PATCH /api/sites/{pubName}/{id}`

**Authorization**: Required (Editor role or higher in publisher)

**Request Body**: Same as Create Site, all fields optional.

**Response**: `200 OK` - Returns updated site object.

### Delete Site

Delete a publication site and all its associated pages.

**Endpoint**: `DELETE /api/sites/{pubName}/{id}`

**Authorization**: Required (Editor role or higher in publisher)

**Response**: `204 No Content`

## Page Management Endpoints

The following endpoints handle publication page CRUD operations.

### Render Page (Public)

Render a publication page for public access.

**Endpoint**: `GET /api/sites/site/{slug}/page`

**Query Parameters**:
- `path`: Page path (defaults to "/")

**Response**: `200 OK` - Returns page object.

### List Pages for Site

List all pages belonging to a specific site.

**Endpoint**: `GET /api/sites/{pubName}/{siteSlug}/pages`

**Authorization**: Required (Viewer role or higher in publisher)

**Response**: `200 OK`

```json
[
  {
    "id": "123e4567-e89b-12d3-a456-426614174000",
    "type": "HtmlPage",
    "path": "/",
    "config": {
      "content": "<h1>Welcome</h1>",
      "title": "Home Page"
    },
    "site_id": "456e7890-e89b-12d3-a456-426614174000"
  }
]
```

### Get Page

Get a specific page by ID.

**Endpoint**: `GET /api/sites/pages/{id}`

**Response**: `200 OK` - Returns page object as shown above.

### Create Page

Create a new publication page.

**Endpoint**: `POST /api/sites/{pubName}/{siteSlug}/pages`

**Authorization**: Required (Editor role or higher in publisher)

**Request Body**:

```json
{
  "type": "HtmlPage",
  "path": "/about",
  "config": {
    "content": "<h1>About Us</h1><p>Content here</p>",
    "title": "About Page"
  }
}
```

**Response**: `200 OK` - Returns created page object.

**Validation**:
- Path must be unique within the site
- Config is a flexible JSON object

### Update Page

Update an existing publication page.

**Endpoint**: `PATCH /api/sites/pages/{id}`

**Authorization**: Required (Editor role or higher in publisher)

**Request Body**: Same as Create Page, all fields optional except id.

**Response**: `200 OK` - Returns updated page object.

### Delete Page

Delete a publication page.

**Endpoint**: `DELETE /api/sites/pages/{id}`

**Authorization**: Required (Editor role or higher in publisher)

**Response**: `204 No Content`

## File Management Endpoints

### List Files

Get a list of files and directories in a site directory.

**Endpoint**: `GET /api/sites/{siteId}/files`

**Query Parameters**:
- `path` (optional): Relative path to the directory. Defaults to root directory.

**Response**: `200 OK`

```json
[
  {
    "is_directory": true,
    "relative_path": "folder1",
    "size": 0,
    "modified": "2024-01-15T10:30:00Z"
  },
  {
    "is_directory": false,
    "relative_path": "index.html",
    "size": 1024,
    "modified": "2024-01-15T09:15:00Z"
  }
]
```

### Upload File

Upload a new file to the site.

**Endpoint**: `POST /api/sites/{siteId}/files/upload`

**Content-Type**: `multipart/form-data`

**Form Data**:
- `filePath`: Relative path where the file should be stored (including filename)
- `file`: The file to upload

**Response**: `200 OK`

**Validation**:
- File must be provided and not empty
- File size must not exceed 1 MB
- Total site size must not exceed 25 MB after upload

### Create Folder

Create a directory in the site file tree.

**Endpoint**: `POST /api/sites/{siteId}/files/folder`

**Request Body**:

```json
{
  "path": "templates/partials"
}
```

**Response**: `200 OK`

**Validation**:
- `path` is required
- Path traversal is blocked

### Get File Content

Retrieve the text content of a file.

**Endpoint**: `GET /api/sites/{siteId}/files/content/{relativePath}`

**Response**: `200 OK`

```json
{
  "content": "<!DOCTYPE html>\n<html>\n<body>Hello World</body>\n</html>"
}
```

**Supported file types**: Primarily text-based files. Returns raw text content.

### Download File

Download a file with proper MIME type headers.

**Endpoint**: `GET /api/sites/{siteId}/files/download/{relativePath}`

**Response**: `200 OK` with file content

**MIME Types**:
- `.txt` → `text/plain`
- `.html`, `.htm` → `text/html`
- `.css` → `text/css`
- `.js` → `application/javascript`
- `.json` → `application/json`
- Others → `application/octet-stream`

File is returned as attachment with the original filename.

### Update File Content

Update the content of an existing text file.

**Endpoint**: `PUT /api/sites/{siteId}/files/edit/{relativePath}`

**Request Body**:

```json
{
  "new_content": "<!DOCTYPE html>\n<html>\n<body>Updated content</body>\n</html>"
}
```

**Response**: `200 OK`

**Validation**:
- New content size must not exceed 1 MB
- Total site size must not exceed 25 MB after update

### Delete File

Delete a file or empty directory.

**Endpoint**: `DELETE /api/sites/{siteId}/files/delete/{relativePath}`

**Response**: `200 OK`

**Note**: Deletes both files and directories. Directories must be empty to be deleted.

## Error Responses

### 401 Unauthorized
User is not authenticated.
```json
{
  "statusCode": 401,
  "message": "Unauthorized"
}
```

### 403 Forbidden
User does not have required permissions.
```json
{
  "statusCode": 403,
  "message": "Forbidden"
}
```

### 404 Not Found
- Site not found or not in SelfManaged mode
- File or directory not found
```json
{
  "statusCode": 404,
  "message": "Site not found or not self-managed"
}
```

### 400 Bad Request
Various validation errors including:
- File size limits exceeded
- Total site size limits exceeded
- Invalid file path
- Missing required parameters
```json
{
  "statusCode": 400,
  "message": "File size exceeds 1MB limit"
}
```

## Usage Examples

### Upload a file using curl

```bash
curl -X POST "https://api.dyson.network/api/sites/123e4567-e89b-12d3-a456-426614174000/files/upload" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -F "filePath=index.html" \
  -F "file=@./index.html"
```

### Update file content using curl

```bash
curl -X PUT "https://api.dyson.network/api/sites/123e4567-e89b-12d3-a456-426614174000/files/edit/index.html" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"new_content": "<!DOCTYPE html><html><body>Hello World</body></html>"}'
```

### List files in a subdirectory

```bash
curl -X GET "https://api.dyson.network/api/sites/123e4567-e89b-12d3-a456-426614174000/files?path=assets/css" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### Download a file

```bash
curl -X GET "https://api.dyson.network/api/sites/123e4567-e89b-12d3-a456-426614174000/files/download/index.html" \
  -H "Authorization: Bearer YOUR_JWT_TOKEN" \
  -o downloaded_index.html
```

## Data Models

### PublicationSite
Represents a publication site.

```typescript
interface PublicationSite {
  id: string; // GUID
  slug: string; // Unique within publisher, max 4096 chars
  name: string; // Display name, max 4096 chars
  description?: string; // Optional description, max 8192 chars
  mode: "FullyManaged" | "SelfManaged";
  pages: PublicationPage[];
  publisher_id: string; // GUID
  account_id: string; // GUID
}
```

### PublicationPage
Represents a page within a publication site.

```typescript
interface PublicationPage {
  id: string; // GUID
  type: "HtmlPage" | "Redirect";
  path: string; // Page path within site, max 8192 chars
  config: { [key: string]: any }; // Flexible JSON configuration
  site_id: string; // GUID of parent site
}
```

### PublicationSiteRequest
Used for creating/updating sites.

```typescript
interface PublicationSiteRequest {
  mode: "FullyManaged" | "SelfManaged";
  slug: string;
  name: string;
  description?: string;
}
```

### PublicationPageRequest
Used for creating/updating pages.

```typescript
interface PublicationPageRequest {
  type: "HtmlPage" | "Redirect";
  path?: string;
  config?: { [key: string]: any };
}
```

### FileEntry
Represents a file or directory entry.

```typescript
interface FileEntry {
  is_directory: boolean;
  relative_path: string;
  size: number; // Size in bytes (0 for directories)
  modified: string; // ISO 8601 timestamp
}
```

### UpdateFileRequest
Used for updating file content.

```typescript
interface UpdateFileRequest {
  new_content: string; // The new content for the file
}
```

## Security Notes

- All file operations are restricted to the authenticated user's authorized sites
- Path traversal attacks are prevented through path validation
- Size limits prevent abuse of storage resources
- Files are served from a dedicated web root directory to prevent access to sensitive system files
