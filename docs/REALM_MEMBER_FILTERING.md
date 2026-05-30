# Realm Member Filtering

The `GET /api/realms/{slug}/members` endpoint now supports filtering by account name and label.

## New Query Parameters

| Parameter     | Type   | Required | Description                                      |
| ------------- | ------ | -------- | ------------------------------------------------ |
| `accountName` | string | No       | Fuzzy search on account name via Padlock search. |
| `labelId`     | guid   | No       | Filter members by their assigned label ID.       |

Existing parameters (`offset`, `take`, `withStatus`) are unchanged.

## Behavior

### Account Name Filter

When `accountName` is provided:

1. The server calls Padlock's `SearchAccount` gRPC endpoint with the given query.
2. Matching account IDs are collected.
3. The member query is narrowed to only those account IDs.
4. If no accounts match, an empty list is returned immediately.

This is a fuzzy search — partial matches, typos, and prefix matches are supported by the underlying search engine.

### Label Filter

When `labelId` is provided, only members with that exact label assignment are returned. Pass a valid label ID from the realm's label list (`GET /api/realms/{slug}/labels`).

### Combining Filters

Both filters can be used together. The result is the intersection: members whose account matches the name query **and** who have the specified label.

## Examples

Search members by name:

```
GET /api/realms/my-realm/members?accountName=john
```

Filter by label:

```
GET /api/realms/my-realm/members?labelId=7b602089-3c6c-45bf-91be-4c812e4fb5a1
```

Combine both:

```
GET /api/realms/my-realm/members?accountName=john&labelId=7b602089-3c6c-45bf-91be-4c812e4fb5a1
```

With pagination and status:

```
GET /api/realms/my-realm/members?accountName=jane&withStatus=true&offset=0&take=10
```

## Response

The response shape is unchanged — the standard `SnRealmMember` list with `X-Total` header. Filters only affect which members are included.
