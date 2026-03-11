# Timeline Discovery

This document describes the personalized discovery layer used by the timeline.

## Event Types

The timeline now has two discovery event families:

- `discovery`: legacy generic discovery event used for older clients and anonymous traffic
- `discovery.v2`: personalized discovery event for signed-in users

`discovery.v2` is intentionally a new type so older clients do not mis-handle the new payload shape.

## Personalized Discovery Sources

The personalized discovery system currently suggests:

- publishers
- individual account profiles
- realms

It uses the same post-derived interest data as the timeline ranking system:

- tag interests
- category interests
- publisher interests

Realm suggestions are inferred from public posts in that realm because direct chat activity is not available in `Sphere`.

Account suggestions are derived from individual publishers whose recent posts align with the user's interest profile and who are not already the current user or an existing friend.

## Event Shape

Example `discovery.v2` payload:

```json
{
  "id": "event-id",
  "type": "discovery.v2",
  "resourceIdentifier": "discovery:publisher",
  "data": {
    "kind": "publisher",
    "title": "Suggested publisher",
    "items": [
      {
        "kind": 0,
        "referenceId": "publisher-id",
        "label": "Example Publisher",
        "score": 3.28,
        "reasons": ["technology", "ai", "analysis"],
        "data": {
          "id": "publisher-id",
          "name": "example-publisher"
        }
      }
    ]
  }
}
```

`kind` values:

- `publisher`
- `account`
- `realm`

## Discovery Profile API

### `GET /api/timeline/discovery/profile`

Returns the server-generated discovery profile for the signed-in user.

Response shape:

```json
{
  "generatedAt": "2026-03-11T15:20:00Z",
  "interests": [
    {
      "kind": "tag",
      "referenceId": "tag-id",
      "label": "technology",
      "score": 4.2,
      "interactionCount": 9,
      "lastInteractedAt": "2026-03-10T09:20:00Z",
      "lastSignalType": "reaction_positive"
    }
  ],
  "suggestedPublishers": [],
  "suggestedAccounts": [],
  "suggestedRealms": [],
  "suppressed": []
}
```

`interests` exposes the current top inferred interests. `suggested*` lists show the current best discovery candidates after filtering subscriptions, existing friendships, joined realms, and manual opt-outs.

## Manual Uninterested Actions

### `POST /api/timeline/discovery/uninterested`

Marks a discovery target as uninterested.

Request:

```json
{
  "kind": "publisher",
  "referenceId": "00000000-0000-0000-0000-000000000000",
  "reason": "too much crypto"
}
```

Supported `kind` values:

- `publisher`
- `account`
- `realm`

### `DELETE /api/timeline/discovery/uninterested?kind=publisher&referenceId=...`

Removes the uninterested preference and allows the target to be considered again.

## Filtering Rules

Discovery suggestions are filtered to avoid obvious bad recommendations:

- publisher suggestions exclude owned publishers and already-subscribed publishers
- account suggestions exclude the current user and existing friends
- realm suggestions exclude already-joined realms
- all personalized suggestion types exclude manually suppressed targets

## Scoring Notes

Discovery scoring is intentionally simpler than full timeline ranking. It uses:

- matches against tag/category interests
- recent post engagement
- mild article preference
- freshness decay

This keeps discovery explainable and cheap to compute while still being personalized.
