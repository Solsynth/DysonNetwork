# File Indexing System Documentation

## Overview

The File Indexing System provides a hierarchical file organization layer on top of the existing file storage system in DysonNetwork Drive. It allows users to organize their files in folders and paths while maintaining the underlying file storage capabilities.

When using with the gateway, replace the `/api` with the `/drive` in the path.
And all the arguments will be transformed into snake case via the gateway.

## Architecture

### Core Components

1. **SnCloudFileIndex Model** - Represents the file-to-path mapping
2. **SnCloudFolder Model** - Represents hierarchical folder structure
3. **FileIndexService** - Business logic for file index operations
4. **FolderService** - Business logic for folder operations
5. **FileIndexController** - REST API endpoints for file and folder management
6. **FileUploadController Integration** - Automatic index creation during upload

### Database Schema

```sql
-- File Indexes table
CREATE TABLE "FileIndexes" (
    "Id" uuid NOT NULL DEFAULT gen_random_uuid(),
    "Path" character varying(8192) NOT NULL,
    "FileId" uuid NOT NULL,
    "AccountId" uuid NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL DEFAULT (now() at time zone 'utc'),
    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT (now() at time zone 'utc'),
    CONSTRAINT "PK_FileIndexes" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_FileIndexes_Files_FileId" FOREIGN KEY ("FileId") REFERENCES "Files" ("Id") ON DELETE CASCADE,
    INDEX "IX_FileIndexes_Path_AccountId" ("Path", "AccountId")
);
```

## API Endpoints

### Browse Files
**GET** `/api/index/browse?path=/documents/`

Browse files in a specific path.

**Query Parameters:**
- `path` (optional, default: "/") - The path to browse

**Response:**
```json
{
  "path": "/documents/",
  "files": [
    {
      "id": "guid",
      "path": "/documents/",
      "fileId": "guid",
      "accountId": "guid",
      "createdAt": "2024-01-01T00:00:00Z",
      "updatedAt": "2024-01-01T00:00:00Z",
      "file": {
        "id": "string",
        "name": "document.pdf",
        "size": 1024,
        "mimeType": "application/pdf",
        "hash": "sha256-hash",
        "uploadedAt": "2024-01-01T00:00:00Z",
        "expiredAt": null,
        "hasCompression": false,
        "hasThumbnail": true,
        "isEncrypted": false,
        "description": null
      }
    }
  ],
  "totalCount": 1
}
```

### Get All Files
**GET** `/api/index/all`

Get all files for the current user across all paths.

**Response:**
```json
{
  "files": [
    // Same structure as browse endpoint
  ],
  "totalCount": 10
}
```

### Move File
**POST** `/api/index/move/{indexId}`

Move a file to a new path.

**Path Parameters:**
- `indexId` - The file index ID

**Request Body:**
```json
{
  "newPath": "/archived/"
}
```

**Response:**
```json
{
  "fileId": "guid",
  "indexId": "guid",
  "oldPath": "/documents/",
  "newPath": "/archived/",
  "message": "File moved successfully"
}
```

### Remove File Index
**DELETE** `/api/index/remove/{indexId}?deleteFile=false`

Remove a file index. Optionally delete the actual file data.

**Path Parameters:**
- `indexId` - The file index ID

**Query Parameters:**
- `deleteFile` (optional, default: false) - Whether to also delete the file data

**Response:**
```json
{
  "message": "File index removed successfully",
  "fileId": "guid",
  "fileName": "document.pdf",
  "path": "/documents/",
  "fileDataDeleted": false
}
```

### Clear Path
**DELETE** `/api/index/clear-path?path=/temp/&deleteFiles=false`

Remove all file indexes in a specific path.

**Query Parameters:**
- `path` (optional, default: "/") - The path to clear
- `deleteFiles` (optional, default: false) - Whether to also delete orphaned files

**Response:**
```json
{
  "message": "Cleared 5 file indexes from path",
  "path": "/temp/",
  "removedCount": 5,
  "filesDeleted": false
}
```

### Create File Index
**POST** `/api/index/create`

Create a new file index for an existing file.

**Request Body:**
```json
{
  "fileId": "guid",
  "path": "/documents/"
}
```

**Response:**
```json
{
  "indexId": "guid",
  "fileId": "guid",
  "path": "/documents/",
  "message": "File index created successfully"
}
```

### Search Files
**GET** `/api/index/search?query=report&path=/documents/`

Search for files by name or metadata.

**Query Parameters:**
- `query` (required) - The search query
- `path` (optional) - Limit search to specific path

**Response:**
```json
{
  "query": "report",
  "path": "/documents/",
  "results": [
    // Same structure as browse endpoint
  ],
  "totalCount": 3
}
```

### Folder Management

The system provides comprehensive folder management capabilities alongside file indexing.

#### Create Folder
**POST** `/api/index/folders`

Create a new folder.

**Request Body:**
```json
{
  "name": "Documents",
  "parentFolderId": null  // null for root folder
}
```

**Response:**
```json
{
  "id": "guid",
  "name": "Documents",
  "parentFolderId": null,
  "accountId": "guid",
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

#### Get Folder by ID
**GET** `/api/index/folders/{folderId}`

Get a folder with its contents.

**Path Parameters:**
- `folderId` - The folder ID

**Response:**
```json
{
  "id": "guid",
  "name": "Documents",
  "parentFolderId": null,
  "accountId": "guid",
  "childFolders": [
    {
      "id": "guid",
      "name": "Reports",
      "parentFolderId": "guid",
      "accountId": "guid"
    }
  ],
  "files": [
    // File index objects
  ],
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

#### Get All Folders
**GET** `/api/index/folders`

Get all folders for the current user.

**Response:**
```json
[
  {
    "id": "guid",
    "name": "Documents",
    "parentFolderId": null,
    "accountId": "guid",
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-01T00:00:00Z"
  }
]
```

#### Update Folder
**PUT** `/api/index/folders/{folderId}`

Update a folder's name.

**Path Parameters:**
- `folderId` - The folder ID

**Request Body:**
```json
{
  "name": "Updated Documents"
}
```

#### Delete Folder
**DELETE** `/api/index/folders/{folderId}`

Delete a folder and all its contents.

**Path Parameters:**
- `folderId` - The folder ID

#### Move File to Folder
**POST** `/api/index/files/{fileIndexId}/move-to-folder`

Move a file to a different folder.

**Path Parameters:**
- `fileIndexId` - The file index ID

**Request Body:**
```json
{
  "newFolderId": "guid"
}
```

#### Get Files in Folder
**GET** `/api/index/folders/{folderId}/files`

Get all files in a specific folder.

**Path Parameters:**
- `folderId` - The folder ID

**Response:**
```json
[
  // File index objects
]
```

## Path Normalization

The system automatically normalizes paths to ensure consistency:

- **Trailing Slash**: All paths end with `/`
- **Root Path**: User home folder is represented as `/`
- **Query Safety**: Paths are validated to avoid SQL injection
- **Examples**:
  - `/documents/` ✅ (correct)
  - `/documents` → `/documents/` ✅ (normalized)
  - `/documents/reports/` ✅ (correct)
  - `/documents/reports` → `/documents/reports/` ✅ (normalized)

## File Upload Integration

When uploading files with the `FileUploadController`, you can specify a path to automatically create file indexes:

**Create Upload Task Request:**
```json
{
  "fileName": "document.pdf",
  "fileSize": 1024,
  "contentType": "application/pdf",
  "hash": "sha256-hash",
  "path": "/documents/"  // New field for file indexing
}
```

The system will automatically create a file index when the upload completes successfully.

## Service Methods

### FileIndexService

```csharp
public class FileIndexService
{
    // Create a new file index at path
    Task<SnCloudFileIndex> CreateAsync(string path, string fileId, Guid accountId);

    // Create a new file index in folder
    Task<SnCloudFileIndex> CreateInFolderAsync(Guid folderId, string fileId, Guid accountId);

    // Get files by path
    Task<List<SnCloudFileIndex>> GetByPathAsync(Guid accountId, string path);

    // Get files by folder
    Task<List<SnCloudFileIndex>> GetByFolderAsync(Guid accountId, Guid folderId);

    // Get all files for account
    Task<List<SnCloudFileIndex>> GetByAccountIdAsync(Guid accountId);

    // Get indexes for specific file
    Task<List<SnCloudFileIndex>> GetByFileIdAsync(string fileId);

    // Move file to new path
    Task<SnCloudFileIndex?> UpdateAsync(Guid indexId, string newPath);

    // Move file to different folder
    Task<SnCloudFileIndex?> MoveAsync(Guid fileIndexId, Guid newFolderId, Guid accountId);

    // Remove file index
    Task<bool> RemoveAsync(Guid indexId);

    // Remove all indexes in path
    Task<int> RemoveByPathAsync(Guid accountId, string path);

    // Remove all indexes in folder
    Task<int> RemoveByFolderAsync(Guid accountId, Guid folderId);

    // Normalize path format
    public static string NormalizePath(string path);
}
```

### FolderService

```csharp
public class FolderService
{
    // Create a new folder
    Task<SnCloudFolder> CreateAsync(string name, Guid accountId, Guid? parentFolderId = null);

    // Get folder by ID with contents
    Task<SnCloudFolder?> GetByIdAsync(Guid folderId, Guid accountId);

    // Get all folders for account
    Task<List<SnCloudFolder>> GetByAccountIdAsync(Guid accountId);

    // Get child folders
    Task<List<SnCloudFolder>> GetChildFoldersAsync(Guid parentFolderId, Guid accountId);

    // Update folder name
    Task<SnCloudFolder?> UpdateAsync(Guid folderId, string name, Guid accountId);

    // Move folder to new parent
    Task<SnCloudFolder?> MoveAsync(Guid folderId, Guid? newParentFolderId, Guid accountId);

    // Delete folder and contents
    Task<bool> DeleteAsync(Guid folderId, Guid accountId);

    // Search folders by name
    Task<List<SnCloudFolder>> SearchAsync(Guid accountId, string searchTerm);
}
```

## Error Handling

The API returns appropriate HTTP status codes and error messages:

- **400 Bad Request**: Invalid input parameters
- **401 Unauthorized**: User not authenticated
- **403 Forbidden**: User lacks permission
- **404 Not Found**: Resource not found
- **500 Internal Server Error**: Server-side error

**Error Response Format:**
```json
{
  "code": "BROWSE_FAILED",
  "message": "Failed to browse files",
  "status": 500
}
```

## Security Considerations

1. **Ownership Verification**: All operations verify that the user owns the file indexes
2. **Path Validation**: Paths are normalized and validated
3. **Cascade Deletion**: File indexes are automatically removed when files are deleted
4. **Safe File Deletion**: Files are only deleted when no other indexes reference them

## Usage Examples

### Upload File to Specific Path
```bash
# Create upload task with path
curl -X POST /api/files/upload/create \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "fileName": "report.pdf",
    "fileSize": 2048,
    "contentType": "application/pdf",
    "path": "/documents/reports/"
  }'
```

### Browse Files
```bash
curl -X GET "/api/index/browse?path=/documents/reports/" \
  -H "Authorization: Bearer {token}"
```

### Move File
```bash
curl -X POST "/api/index/move/{indexId}" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"newPath": "/archived/"}'
```

### Search Files
```bash
curl -X GET "/api/index/search?query=invoice&path=/documents/" \
  -H "Authorization: Bearer {token}"
```

## Best Practices

1. **Use Trailing Slashes**: Always include trailing slashes in paths
2. **Organize Hierarchically**: Use meaningful folder structures
3. **Search Efficiently**: Use the search endpoint instead of client-side filtering
4. **Clean Up**: Use the clear-path endpoint for temporary directories
5. **Monitor Usage**: Check total file counts for quota management

## Integration Notes

- The file indexing system works alongside the existing file storage
- Files can exist in multiple paths (hard links)
- File deletion is optional and only removes data when safe
- The system maintains referential integrity between files and indexes
