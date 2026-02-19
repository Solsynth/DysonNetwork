# Live Stream Feature Documentation

## Overview

The Live Stream feature enables users to create and broadcast live video streams using LiveKit as the real-time infrastructure. Users can:

- Create live streams with metadata
- Stream via RTMP (using OBS or similar software)
- Allow viewers to join via WebRTC (Flutter/Web clients)
- Enable HLS playback for broader platform compatibility
- Push streams to external platforms (YouTube, Bilibili, etc.) via Egress
- Track viewer counts and stream statistics
- When using with the Gateway, the base url is `/sphere/livestreams` instead of `/api/livestreams`

## Architecture

### Components

1. **LiveKit Infrastructure**
    - **Room**: WebRTC room for each live stream session
    - **Ingress**: RTMP input endpoint for streamers (OBS → LiveKit)
    - **Egress (RTMP)**: RTMP output to external platforms
    - **Egress (HLS)**: HLS segment generation for playback

2. **Sphere API** (DysonNetwork.Sphere)
    - RESTful API for stream management
    - Token generation for WebRTC authentication
    - Database persistence via Entity Framework

3. **Models**
    - `SnLiveStream`: Database model for live stream metadata
    - `LiveStreamStatus`: Pending, Active, Ended, Error
    - `LiveStreamType`: Regular, Interactive
    - `LiveStreamVisibility`: Public, Unlisted, Private

## Configuration

Add to `appsettings.json`:

```json
{
    "LiveStream": {
        "Endpoint": "wss://your-livekit-server.com",
        "ApiKey": "your-api-key",
        "ApiSecret": "your-api-secret"
    }
}
```

Or use the existing `RealtimeChat` config as fallback.

## API Endpoints

**Note:** All endpoints that return live stream data return the `SnLiveStream` database object directly (not DTOs), including related `Publisher` objects. Only specific endpoints like token generation return custom anonymous objects.

**Security:** Sensitive fields (`ingress_id`, `ingress_stream_key`, `egress_id`) are automatically removed from public responses. These fields are only visible to users who have Editor role on the stream's publisher. The `Start Streaming` endpoint returns the RTMP credentials only to authorized users.

### Stream Management

#### Create Live Stream

Creates a new live stream for the user's publisher.

```http
POST /api/livestreams
Authorization: Bearer {token}
Content-Type: application/json

{
  "title": "My Awesome Stream",
  "description": "Streaming some cool stuff",
  "slug": "awesome-stream-2026",
  "thumbnail_id": "file-uuid-here",
  "type": "Regular",
  "visibility": "Public"
}

Response 200 OK:
{
  "id": "uuid",
  "room_name": "livestream_abc123",
  "title": "My Awesome Stream",
  "description": "Streaming some cool stuff",
  "slug": "awesome-stream-2026",
  "type": "Regular",
  "visibility": "Public",
  "status": "Pending",
  "thumbnail": {
    "id": "file-uuid-here",
    "name": "thumbnail.jpg",
    "url": "https://...",
    "mime_type": "image/jpeg",
    "size": 12345
  },
  "publisher_id": "uuid",
  "publisher": {
    "id": "uuid",
    "name": "channel",
    "nick": "Channel Name",
    "picture": {...}
  },
  "created_at": "2026-02-19T15:00:00Z",
  "updated_at": "2026-02-19T15:00:00Z"
}
```

**Authorization:** Requires authentication and membership in at least one publisher.

**Thumbnail:** The `thumbnail_id` should be a file ID from the file service. The thumbnail is stored as a `SnCloudFileReferenceObject` in the jsonb column.

#### Start Streaming (Get RTMP Info)

Starts the live stream and returns RTMP connection details. Requires Editor role on the publisher.

```http
POST /api/livestreams/{id}/start
Authorization: Bearer {token}

Response 200 OK:
{
  "rtmp_url": "rtmp://your-livekit-server.com/live",
  "stream_key": "live_xxx",
  "room_name": "livestream_abc123"
}
```

**Authorization:** Requires authentication and Editor role on the stream's publisher.

**OBS Configuration:**

- Service: Custom
- Server: `{rtmp_url}`
- Stream Key: `{stream_key}`

#### End Live Stream

Ends the live stream and cleans up resources. Requires Editor role on the publisher.

```http
POST /api/livestreams/{id}/end
Authorization: Bearer {token}

Response 200 OK
```

**Authorization:** Requires authentication and Editor role on the stream's publisher.

### Viewer Endpoints

#### List Active Streams

Returns a list of active public live streams with full database objects.

```http
GET /api/livestreams?limit=20&offset=0

Response 200 OK:
[
  {
    "id": "uuid",
    "title": "My Awesome Stream",
    "description": "Streaming some cool stuff",
    "room_name": "livestream_abc123",
    "type": "Regular",
    "visibility": "Public",
    "status": "Active",
    "viewer_count": 150,
    "peak_viewer_count": 200,
    "publisher_id": "uuid",
    "publisher": {
      "id": "uuid",
      "name": "channel",
      "nick": "Channel Name",
      "picture": {...}
    },
    "started_at": "2026-02-19T15:30:00Z",
    "created_at": "2026-02-19T15:00:00Z",
    "updated_at": "2026-02-19T15:00:00Z"
  }
]
```

**Authorization:** Public, no authentication required.

#### Get Streams by Publisher

Returns all live streams (including ended) for a specific publisher.

```http
GET /api/livestreams/publisher/{publisherId}?limit=20&offset=0

Response 200 OK:
[
  {
    "id": "uuid",
    "title": "Past Stream",
    "description": "This is a past stream",
    "room_name": "livestream_def456",
    "status": "Ended",
    "viewer_count": 1000,
    "peak_viewer_count": 1500,
    "publisher_id": "uuid",
    "publisher": {...},
    "started_at": "2026-02-18T14:00:00Z",
    "ended_at": "2026-02-18T16:00:00Z",
    "created_at": "2026-02-18T14:00:00Z"
  }
]
```

**Authorization:** Public, no authentication required.

#### Get Stream Details

Returns the full live stream database object including publisher details.

```http
GET /api/livestreams/{id}

Response 200 OK:
{
  "id": "uuid",
  "title": "My Awesome Stream",
  "description": "Streaming some cool stuff",
  "slug": "awesome-stream-2026",
  "room_name": "livestream_abc123",
  "type": "Regular",
  "visibility": "Public",
  "status": "Active",
  "ingress_id": "ingress_xxx",
  "ingress_stream_key": "live_xxx",
  "egress_id": "egress_xxx",
  "viewer_count": 150,
  "peak_viewer_count": 200,
  "publisher_id": "uuid",
  "publisher": {
    "id": "uuid",
    "name": "channel",
    "nick": "Channel Name",
    "picture": {...}
  },
  "thumbnail": {...},
  "metadata": {...},
  "started_at": "2026-02-19T15:30:00Z",
  "ended_at": null,
  "created_at": "2026-02-19T15:00:00Z",
  "updated_at": "2026-02-19T15:00:00Z"
}
```

**Authorization:** Public, no authentication required.

#### Join Stream (Get Token)

Generates a LiveKit token for joining the stream. Automatically detects if the user should be a streamer (if they have Editor role on the publisher).

```http
GET /api/livestreams/{id}/token?identity=viewer_123
Authorization: Bearer {token} (optional for public streams)

Response 200 OK:
{
  "token": "jwt-token-here",
  "room_name": "livestream_abc123",
  "url": "wss://your-livekit-server.com"
}
```

**Authorization:** Public, authentication optional. If authenticated and user has Editor role on the publisher, they will receive streamer permissions (can publish audio/video). Otherwise, they receive viewer permissions (can only subscribe).

Use this token with the LiveKit Flutter SDK:

```dart
final room = Room();
await room.connect(response.url, response.token);
```

### Egress (External Streaming)

#### Start Egress (Push to External Platforms)

Starts pushing the live stream to external RTMP endpoints. Can also record to file. Requires Editor role on the publisher.

```http
POST /api/livestreams/{id}/egress
Authorization: Bearer {token}
Content-Type: application/json

{
  "rtmp_urls": [
    "rtmp://live-push.bilivideo.com/live-bvc/...",
    "rtmp://a.rtmp.youtube.com/live2/..."
  ],
  "file_path": "recordings/stream-2026-02-19.mp4"
}

Response 200 OK:
{
  "egress_id": "EG_xxx"
}
```

**Authorization:** Requires authentication and Editor role on the stream's publisher.

#### Stop Egress

Stops the egress (external streaming). Requires Editor role on the publisher.

```http
POST /api/livestreams/{id}/egress/stop
Authorization: Bearer {token}

Response 200 OK
```

**Authorization:** Requires authentication and Editor role on the stream's publisher.

### HLS Egress (Playback Support)

HLS (HTTP Live Streaming) egress allows viewers to watch the stream using standard HLS players (e.g., HTML5 video player, VLC, iOS native player) instead of WebRTC. This is useful for:

- Better compatibility across platforms
- Fallback when WebRTC is not supported
- Recording for later playback
- Reducing server load for large audiences

#### Start HLS Egress

Starts generating HLS segments and playlist from the live stream. Requires Editor role on the publisher.

```http
POST /api/livestreams/{id}/hls
Authorization: Bearer {token}
Content-Type: application/json

{
  "playlist_name": "playlist.m3u8",
  "segment_duration": 6,
  "segment_count": 0,
  "layout": "default",
  "hls_base_url": "https://your-cdn.com/hls"
}

Response 200 OK:
{
  "egress_id": "EG_xxx",
  "playlist_url": "https://your-cdn.com/hls/{stream-id}/playlist.m3u8",
  "playlist_name": "playlist.m3u8"
}
```

**Request Body Fields:**

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `playlist_name` | string | "playlist.m3u8" | Name of the HLS playlist file |
| `segment_duration` | uint | 6 | Duration of each segment in seconds |
| `segment_count` | int | 0 | Number of segments to keep (0 = unlimited) |
| `layout` | string | "default" | Video composition layout |
| `hls_base_url` | string | Auto | Base URL for HLS playback (defaults to `https://{host}/hls`) |

**Authorization:** Requires authentication and Editor role on the stream's publisher.

**Notes:**
- Only one HLS egress can be active per stream. Call stop first if you want to restart.
- The `playlist_url` is stored in the live stream and returned in GET responses (visible to all viewers).
- Segments are stored in `{filename_prefix}` location configured in LiveKit.

#### Stop HLS Egress

Stops the HLS egress generation.

```http
POST /api/livestreams/{id}/hls/stop
Authorization: Bearer {token}

Response 200 OK
```

**Authorization:** Requires authentication and Editor role on the stream's publisher.

#### HLS Playback

Once HLS egress is started, the playlist URL is available in the stream response:

```http
GET /api/livestreams/{id}

Response 200 OK:
{
  "id": "uuid",
  "title": "My Awesome Stream",
  "status": "Active",
  "hls_egress_id": "EG_xxx",
  "hls_playlist_url": "https://your-cdn.com/hls/{stream-id}/playlist.m3u8",
  "hls_started_at": "2026-02-19T15:30:00Z",
  ...
}
```

**HTML5 Video Player Example:**

```html
<video controls autoplay>
  <source src="https://your-cdn.com/hls/{stream-id}/playlist.m3u8" type="application/x-mpegURL">
</video>
```

**HLS.js Example (for browsers without native HLS support):**

```javascript
import Hls from 'hls.js';

const video = document.getElementById('video');
const hlsUrl = 'https://your-cdn.com/hls/{stream-id}/playlist.m3u8';

if (Hls.isSupported()) {
  const hls = new Hls();
  hls.loadSource(hlsUrl);
  hls.attachMedia(video);
  hls.on(Hls.Events.MANIFEST_PARSED, () => {
    video.play();
  });
} else if (video.canPlayType('application/vnd.apple.mpegurl')) {
  video.src = hlsUrl;
  video.addEventListener('loadedmetadata', () => {
    video.play();
  });
}
```

#### Update Thumbnail

Updates or removes the live stream thumbnail. Requires Editor role on the publisher.

```http
PATCH /api/livestreams/{id}/thumbnail
Authorization: Bearer {token}
Content-Type: application/json

{
  "thumbnail_id": "new-file-uuid-here"
}

Response 200 OK:
{
  "id": "uuid",
  "title": "My Awesome Stream",
  "thumbnail": {
    "id": "new-file-uuid-here",
    "name": "new-thumbnail.jpg",
    "url": "https://...",
    "mime_type": "image/jpeg",
    "size": 23456
  },
  "updated_at": "2026-02-19T15:30:00Z"
}
```

To remove the thumbnail, send `null`:

```http
PATCH /api/livestreams/{id}/thumbnail
Authorization: Bearer {token}
Content-Type: application/json

{
  "thumbnail_id": null
}

Response 200 OK
```

**Authorization:** Requires authentication and Editor role on the stream's publisher.

#### Get Room Details

Returns real-time room statistics including participant count.

```http
GET /api/livestreams/{id}/details

Response 200 OK:
{
  "participant_count": 150,
  "viewer_count": 150,
  "peak_viewer_count": 200
}
```

**Authorization:** Public, no authentication required.

### Posts Integration

Posts can have live streams attached as embeds, similar to polls and funds.

#### Create Post with Live Stream

```http
POST /api/posts
Authorization: Bearer {token}
Content-Type: application/json

{
  "title": "Join my live stream!",
  "content": "I'm streaming right now, come join!",
  "live_stream_id": "uuid-of-livestream"
}

Response 200 OK:
{
  "id": "post-uuid",
  "title": "Join my live stream!",
  "metadata": {
    "embeds": [
      {
        "type": "livestream",
        "id": "uuid-of-livestream"
      }
    ]
  }
}
```

#### Update Post Live Stream

```http
PATCH /api/posts/{post-id}
Authorization: Bearer {token}
Content-Type: application/json

{
  "live_stream_id": "uuid-of-another-livestream"
}

Response 200 OK
```

To remove a live stream from a post, send `null` for `live_stream_id`:

```http
PATCH /api/posts/{post-id}
Authorization: Bearer {token}
Content-Type: application/json

{
  "live_stream_id": null
}
```

**Requirements:**
- The live stream must exist
- The live stream must belong to the same publisher as the post
- Only the publisher owner/editor can attach their live streams

### Subscriptions

#### Get Subscribed Publishers' Live Streams

Returns full live stream objects from publishers the current user is subscribed to.

```http
GET /api/publishers/subscriptions/live
Authorization: Bearer {token}

Response 200 OK:
[
  {
    "id": "uuid",
    "title": "Stream from subscribed publisher",
    "description": "Live now!",
    "room_name": "livestream_xyz789",
    "type": "Regular",
    "visibility": "Public",
    "status": "Active",
    "viewer_count": 500,
    "peak_viewer_count": 600,
    "publisher_id": "uuid",
    "publisher": {
      "id": "uuid",
      "name": "channel",
      "nick": "Channel Name",
      "picture": {...}
    },
    "started_at": "2026-02-19T15:30:00Z",
    "created_at": "2026-02-19T15:00:00Z",
    "updated_at": "2026-02-19T15:00:00Z"
  }
]
```

**Authorization:** Requires authentication.

## Usage Flow

### For Streamers

1. **Create Stream** → `POST /api/livestreams`
2. **Start Streaming** → `POST /api/livestreams/{id}/start` (get RTMP URL)
3. **Configure OBS** → Set RTMP URL and stream key
4. **Go Live** → Start streaming in OBS
5. **Optional: Start HLS Egress** → Enable HLS playback for viewers
6. **Optional: Start RTMP Egress** → Push to YouTube/Bilibili
7. **End Stream** → `POST /api/livestreams/{id}/end`

**HLS Playback Flow:**
1. **Start HLS Egress** → `POST /api/livestreams/{id}/hls`
2. **Get Playlist URL** → Returned in response or `GET /api/livestreams/{id}`
3. **Distribute URL** → Share with viewers for HLS playback
4. **Stop HLS** → `POST /api/livestreams/{id}/hls/stop` (auto-stops on stream end)

### For Viewers

1. **Browse Streams** → `GET /api/livestreams`
2. **Get Token** → `GET /api/livestreams/{id}/token`
3. **Connect** → Use LiveKit Flutter/Web SDK with token
4. **Subscribe** → Listen for tracks and render video

## Database Schema

Table: `live_streams`

| Column               | Type          | Description                                   |
| -------------------- | ------------- | --------------------------------------------- |
| `id`                 | uuid          | Primary key                                   |
| `title`              | varchar(256)  | Stream title                                  |
| `description`        | varchar(4096) | Stream description                            |
| `slug`               | varchar(128)  | URL-friendly identifier                       |
| `type`               | integer       | Regular (0) or Interactive (1)                |
| `visibility`         | integer       | Public (0), Unlisted (1), Private (2)         |
| `status`             | integer       | Pending (0), Active (1), Ended (2), Error (3) |
| `room_name`          | varchar(256)  | LiveKit room name                             |
| `ingress_id`         | varchar(256)  | LiveKit ingress ID                            |
| `ingress_stream_key` | varchar(256)  | RTMP stream key                               |
| `egress_id`          | varchar(256)  | LiveKit egress ID (RTMP)                      |
| `hls_egress_id`      | varchar(256)  | LiveKit HLS egress ID                         |
| `hls_playlist_url`   | text          | Public HLS playlist URL                       |
| `hls_started_at`     | timestamptz   | When HLS egress started                       |
| `started_at`         | timestamptz   | When stream started                           |
| `ended_at`           | timestamptz   | When stream ended                             |
| `viewer_count`       | integer       | Current viewer count                          |
| `peak_viewer_count`  | integer       | Peak viewer count                             |
| `thumbnail`          | jsonb         | Thumbnail image reference                     |
| `metadata`           | jsonb         | Additional metadata                           |
| `publisher_id`       | uuid          | Foreign key to publishers                     |

## Security Considerations

1. **Authentication**: Uses Sphere's standard authentication pattern via `HttpContext.Items["CurrentUser"]`
2. **Authorization**: Uses role-based permissions:
   - **Create**: Requires membership in any publisher
   - **Manage** (start, stop, egress): Requires `Editor` role in the stream's publisher
3. **Token Expiry**: Generated tokens expire after 4 hours by default
4. **Visibility**:
   - `Public`: Anyone can view and join
   - `Unlisted`: Only accessible with direct link
   - `Private`: Only authorized viewers
5. **Streamer Detection**: Automatically grants streamer permissions if user has Editor role on the publisher

## Troubleshooting

### Stream Not Appearing in List

- Check that `Status` is `Active` (not `Pending`)
- Verify OBS is connected and streaming
- Check LiveKit server logs for ingress issues

### Token Expired

- Re-fetch token via `GET /api/livestreams/{id}/token`
- Tokens are valid for 4 hours

### Egress Failures

- Ensure egress service is running
- Verify RTMP URLs are valid
- Check Redis connectivity (required for egress)

### HLS Playback Issues

- **Playlist not accessible**: Ensure the `hls_base_url` is publicly accessible or configure a CDN
- **Segments 404**: Verify LiveKit egress has write access to the storage location
- **Buffering/Latency**: Increase `segment_duration` for more stable playback (trade-off: higher latency)
- **Old segments not deleted**: Set `segment_count` to limit storage usage (e.g., 20 for ~2 minutes of history)

### HLS Egress Already Active

If you get "HLS egress is already active for this stream":
1. Stop the existing HLS egress: `POST /api/livestreams/{id}/hls/stop`
2. Wait a few seconds for cleanup
3. Start again: `POST /api/livestreams/{id}/hls`

## Related Files

- `DysonNetwork.Shared/Models/LiveStream.cs` - Data model
- `DysonNetwork.Sphere/Live/LiveKitLivestreamService.cs` - LiveKit integration
- `DysonNetwork.Sphere/Live/LiveStreamService.cs` - Business logic
- `DysonNetwork.Sphere/Live/LiveStreamController.cs` - API controller
