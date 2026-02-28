# Chat Voice Messages API

This document describes how clients should upload and play voice messages in chat.

When using with the gateway, `/api/chat` is exposed as `/messager/chat`.

## Overview

Voice messages are sent as normal chat messages with:

- `type = "voice"`
- voice metadata in `message.meta`

The binary audio content is stored by the Messager service in S3-compatible object storage (not the shared file upload service).

## Endpoints

### 1. Send Voice Message

**Endpoint:** `POST /api/chat/{roomId:guid}/messages/voice`  
**Auth:** required (`Bearer`)  
**Content-Type:** `multipart/form-data`

#### Form fields

| Field | Type | Required | Description |
|---|---|---|---|
| `file` | file | Yes | Audio file (`audio/*`) |
| `nonce` | string | No | Client message nonce |
| `durationMs` | int | No | Voice duration in milliseconds |
| `repliedMessageId` | guid | No | Reply target message ID |
| `forwardedMessageId` | guid | No | Forward target message ID |

#### Example (cURL)

```bash
curl -X POST "https://api.example.com/api/chat/{roomId}/messages/voice" \
  -H "Authorization: Bearer <token>" \
  -F "file=@./voice-message.webm;type=audio/webm" \
  -F "durationMs=4200" \
  -F "nonce=3c2f8ad6-2a9b-4ee1-8f21-fbb8b2945e36"
```

#### Example (Web / fetch)

```ts
async function sendVoiceMessage(roomId: string, blob: Blob, token: string, durationMs?: number) {
  const form = new FormData();
  form.append("file", blob, "voice.webm");
  if (durationMs != null) form.append("durationMs", String(durationMs));
  form.append("nonce", crypto.randomUUID());

  const res = await fetch(`/api/chat/${roomId}/messages/voice`, {
    method: "POST",
    headers: { Authorization: `Bearer ${token}` },
    body: form
  });

  if (!res.ok) throw new Error(`Send failed: ${res.status}`);
  return await res.json(); // SnChatMessage
}
```

#### Response

Returns created `SnChatMessage` with:

- `type: "voice"`
- `meta.voice_clip_id` (guid)
- `meta.voice_url` (public absolute URL when `VoiceMessages:S3:PublicBaseUrl` is configured; otherwise relative API stream path)
- `meta.mime_type` (audio mime)
- `meta.size` (bytes)
- optional `meta.duration_ms`
- optional `meta.file_name`

---

### 2. Get / Stream Voice File

**Endpoint:** `GET /api/chat/{roomId:guid}/voice/{voiceId:guid}`  
**Auth:** required for private rooms, optional for public rooms

Returns audio stream (`Content-Type` = clip mime type).

#### Example (playback URL)

Use `meta.voice_url` from message:

```text
/api/chat/{roomId}/voice/{voiceId}
```

For private rooms, include auth header in the request used by your media player/downloader.

If server config sets `VoiceMessages:S3:PublicBaseUrl`, `voice_url` can be a direct CDN/custom-domain URL instead of `/api/chat/...`.

## Message Example

```json
{
  "id": "message-guid",
  "type": "voice",
  "chat_room_id": "room-guid",
  "sender_id": "member-guid",
  "meta": {
    "voice_clip_id": "voice-guid",
    "voice_url": "/api/chat/room-guid/voice/voice-guid",
    "mime_type": "audio/webm",
    "size": 82341,
    "duration_ms": 4200,
    "file_name": "voice.webm"
  }
}
```

## Error Cases

- `400` invalid input (empty file, too large, non-audio file, invalid reply/forward target)
- `401` unauthenticated
- `403` not a chat member / timed out / private room access denied
- `404` room or voice clip not found

## Client Notes

- Record/export in a browser-friendly format like `audio/webm` when possible.
- Keep local optimistic state with `nonce` while upload is in flight.
- Use `duration_ms` from metadata to render timeline duration.
- Reuse normal chat sync/list APIs; voice messages are regular chat messages.
