# Location Embed

This document describes the `location` embed supported by posts and chat messages.

## Summary

The `location` embed uses the same location input shape as Passport meets:

- `location_name`
- `location_address`
- `location_wkt`

This embed is stored in the content metadata `embeds` list with `type = "location"`.

## Supported APIs

Posts:

- `POST /api/posts`
- `PUT /api/posts/{id}`

Chat:

- `POST /api/chat/{roomId}/messages`
- `PATCH /api/chat/{roomId}/messages/{messageId}`

## Request Fields

All request payloads use snake_case JSON.

Available fields:

- `location_name`: optional string, max length `256`
- `location_address`: optional string, max length `1024`
- `location_wkt`: optional string

At least one of the above fields can be provided to create a location embed.

## WKT Format

`location_wkt` is parsed with `NetTopologySuite.IO.WKTReader` and normalized to `SRID = 4326`.

Example point:

```text
POINT (121.5654 25.0330)
```

If `location_wkt` is invalid, the API returns:

```text
Invalid location WKT.
```

## Stored Embed Shape

The embed is stored in `meta.embeds` or `metadata.embeds` as:

```json
{
  "type": "location",
  "name": "Taipei 101",
  "address": "Taipei, Taiwan",
  "wkt": "POINT (121.5654 25.0330)"
}
```

All properties except `type` are optional.

## Post Example

Create a post with a location embed:

```json
{
  "content": "Meet me here",
  "location_name": "Taipei 101",
  "location_address": "Taipei, Taiwan",
  "location_wkt": "POINT (121.5654 25.0330)"
}
```

## Chat Example

Send a chat message with a location embed:

```json
{
  "content": "Let's meet here",
  "location_name": "Taipei 101",
  "location_address": "Taipei, Taiwan",
  "location_wkt": "POINT (121.5654 25.0330)"
}
```

## Update Behavior

Location embed updates use remove-and-replace semantics.

If location fields are present in the update request:

- existing `location` embeds are removed
- the new `location` embed is added

If location fields are omitted from the update request:

- existing `location` embeds are removed

This matches the current explicit behavior selected for this feature.

## E2EE Chat Behavior

In encrypted chat rooms, plaintext location fields are not allowed.

If a client sends any of these fields in an E2EE room:

- `location_name`
- `location_address`
- `location_wkt`

the request is rejected with the same plaintext-forbidden rule already used for poll and fund embeds.

## Notes

- No additional post read-side hydration is required because embeds are already returned through metadata.
- `DysonNetwork.Sphere/Post/PostController.cs` does not create embeds; post write behavior lives in `PostActionController.cs`.
- See also `docs/MEET_EMBED.md` for lightweight meet references.
