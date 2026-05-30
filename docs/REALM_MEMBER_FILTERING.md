# Member Filtering

The member list endpoints for realms and chat rooms support filtering by account name.

## Realm Members

`GET /api/realms/{slug}/members`

| Parameter     | Type   | Required | Description                                      |
| ------------- | ------ | -------- | ------------------------------------------------ |
| `accountName` | string | No       | Fuzzy search on account name via Padlock search. |
| `labelId`     | guid   | No       | Filter members by their assigned label ID.       |

Existing parameters (`offset`, `take`, `withStatus`) are unchanged.

### Label Filter

When `labelId` is provided, only members with that exact label assignment are returned. Pass a valid label ID from the realm's label list (`GET /api/realms/{slug}/labels`).

### Combining Filters

Both filters can be used together. The result is the intersection: members whose account matches the name query **and** who have the specified label.

### Examples

```
GET /api/realms/my-realm/members?accountName=john
GET /api/realms/my-realm/members?labelId=7b602089-3c6c-45bf-91be-4c812e4fb5a1
GET /api/realms/my-realm/members?accountName=john&labelId=7b602089-3c6c-45bf-91be-4c812e4fb5a1
GET /api/realms/my-realm/members?accountName=jane&withStatus=true&offset=0&take=10
```

## Chat Room Members

`GET /api/chat/{roomId}/members`

| Parameter     | Type   | Required | Description                                      |
| ------------- | ------ | -------- | ------------------------------------------------ |
| `accountName` | string | No       | Fuzzy search on account name via Padlock search. |

Existing parameters (`offset`, `take`, `withStatus`) are unchanged.

### Examples

```
GET /api/chat/{roomId}/members?accountName=john
GET /api/chat/{roomId}/members?accountName=jane&withStatus=true&offset=0&take=10
```

## Behavior

When `accountName` is provided on either endpoint:

1. The server calls Padlock's `SearchAccount` gRPC endpoint with the given query.
2. Matching account IDs are collected.
3. The member query is narrowed to only those account IDs.
4. If no accounts match, an empty list is returned immediately.

This is a fuzzy search — partial matches, typos, and prefix matches are supported by the underlying search engine.

## Response

The response shape is unchanged for both endpoints — standard member list with `X-Total` header. Filters only affect which members are included.
