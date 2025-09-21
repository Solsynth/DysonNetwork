# Multi-part File Upload API

This document outlines the process for uploading large files in chunks using the multi-part upload API.

## 1. Create an Upload Task

To begin a file upload, you first need to create an upload task. This is done by sending a `POST` request to the `/api/files/upload/create` endpoint.

**Endpoint:** `POST /api/files/upload/create`

**Request Body:**

```json
{
  "hash": "string (file hash, e.g., MD5 or SHA256)",
  "file_name": "string",
  "file_size": "long (in bytes)",
  "content_type": "string (e.g., 'image/jpeg')",
  "pool_id": "string (GUID)",
  "bundle_id": "string (GUID, optional)",
  "encrypt_password": "string (optional)",
  "expired_at": "string (ISO 8601 format, optional)",
  "chunk_size": "long (in bytes, optional, defaults to 5MB)"
}
```

**Response:**

If a file with the same hash already exists, the server will return a `200 OK` with the following body:

```json
{
  "file_exists": true,
  "file": { ... (CloudFile object in snake_case) ... }
}
```

If the file does not exist, the server will return a `200 OK` with a task ID and chunk information:

```json
{
  "file_exists": false,
  "task_id": "string",
  "chunk_size": "long",
  "chunks_count": "int"
}
```

You will need the `task_id`, `chunk_size`, and `chunks_count` for the next steps.

## 2. Upload File Chunks

Once you have a `task_id`, you can start uploading the file in chunks. Each chunk is sent as a `POST` request with `multipart/form-data`.

**Endpoint:** `POST /api/files/upload/chunk/{taskId}/{chunkIndex}`

-   `taskId`: The ID of the upload task from the previous step.
-   `chunkIndex`: The 0-based index of the chunk you are uploading.

**Request Body:**

The body of the request should be `multipart/form-data` with a single form field named `chunk` containing the binary data for that chunk.

The size of each chunk should be equal to the `chunk_size` returned in the "Create Upload Task" step, except for the last chunk, which may be smaller.

**Response:**

A successful chunk upload will return a `200 OK` with an empty body.

You should upload all chunks from `0` to `chunks_count - 1`.

## 3. Complete the Upload

After all chunks have been successfully uploaded, you must send a final request to complete the upload process. This will merge all the chunks into a single file and process it.

**Endpoint:** `POST /api/files/upload/complete/{taskId}`

-   `taskId`: The ID of the upload task.

**Request Body:**

The request body should be empty.

**Response:**

A successful request will return a `200 OK` with the `CloudFile` object for the newly uploaded file.

```json
{
  ... (CloudFile object) ...
}
```

If any chunks are missing or an error occurs during the merge process, the server will return a `400 Bad Request` with an error message.
