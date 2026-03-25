# File Upload API Changes

## CompleteUpload Endpoint Changes

### Endpoint
`POST /api/files/upload/complete/{taskId}`

### Response Changes

**Before:**
- HTTP Status: `200 OK`
- Body: `SnCloudFile` object

**After:**
- HTTP Status: `202 Accepted`
- Body:
```json
{
  "message": "Upload is being processed",
  "taskId": "<taskId>",
  "status": "processing"
}
```

### Client Update Required

The client must:
1. Handle `202 Accepted` as a successful response (not an error)
2. The file is NOT immediately available after this call - it goes through async processing
3. Poll progress using `GET /api/files/upload/progress/{taskId}` to track status

### Progress Tracking

| Progress | Phase |
|----------|-------|
| 0-50% | Merging chunks |
| 50-55% | Local processing |
| 55-70% | S3 optimization |
| 70-100% | S3 upload |

When progress reaches 100% and status is `Completed`, the file is ready.

### Example Client Code (JavaScript/Fetch)

```javascript
// Before (no longer valid)
const result = await fetch(`/api/files/upload/complete/${taskId}`, { method: 'POST' });
const cloudFile = await result.json(); // SnCloudFile

// After
const result = await fetch(`/api/files/upload/complete/${taskId}`, { method: 'POST' });
if (result.status === 202) {
  const { taskId, status } = await result.json();
  // Poll for completion
  await waitForUploadComplete(taskId);
}

async function waitForUploadComplete(taskId) {
  while (true) {
    const progress = await fetch(`/api/files/upload/progress/${taskId}`);
    const data = await progress.json();
    
    if (data.status === 'Completed') {
      console.log('Upload complete!');
      break;
    }
    if (data.status === 'Failed') {
      throw new Error('Upload failed');
    }
    
    await new Promise(r => setTimeout(r, 1000)); // Poll every second
  }
}
```
