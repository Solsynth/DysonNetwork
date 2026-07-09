# Publisher Query Params

Several Sphere endpoints accept a publisher name filter via the `pub` query parameter.

Use `pub` when the endpoint needs to resolve a publisher by name, for example:

```http
GET /api/posts?pub=mypublisher
POST /api/posts/tags?pub=mypublisher
POST /api/stickers?pub=mypublisher
POST /api/livestreams?pub=mypublisher
POST /api/surveys?pub=mypublisher
```

Notes:

- The query key is `pub`, not `pubName`.
- Passing an unknown publisher name returns a not found or bad request response depending on the endpoint.
- Endpoints that scope by publisher path segment, such as `/api/publishers/{publisherName}/collections`, use the route parameter instead of `pub`.
