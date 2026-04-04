# Federation

## Supported federation protocols and standards

- [ActivityPub](https://www.w3.org/TR/activitypub/) (Server-to-Server)
- [WebFinger](https://webfinger.net/)
- [HTTP Signatures](https://datatracker.ietf.org/doc/html/draft-cavage-http-signatures)
- [NodeInfo](https://nodeinfo.diaspora.software/) 2.0 and 2.1

## Supported FEPs

- [FEP-67ff: FEDERATION.md](https://codeberg.org/fediverse/fep/src/branch/main/fep/67ff/fep-67ff.md)
- [FEP-c0e0: Emoji reactions](https://fediverse.codeberg.page/fep/fep/c0e0/) (Draft)
- [FEP-1b12: Group federation](https://fediverse.codeberg.page/fep/fep/1b12/) (Communities/Forums)

## ActivityPub

### Actors

- `Person` - User actors with inbox/outbox
- `Group` - Community actors for federated forums (FEP-1b12)
- Supports actor discovery via WebFinger
- Actor endpoints: `/activitypub/actors/{name}`, `/activitypub/actors/{name}/inbox`, `/activitypub/actors/{name}/outbox`, `/activitypub/actors/{name}/followers`, `/activitypub/actors/{name}/following`

### Outgoing Activities

| Activity | Status | Notes |
|----------|--------|-------|
| Follow | Supported | Send follow requests to remote actors |
| Accept | Supported | Accept incoming follow requests |
| Reject | Supported | Reject incoming follow requests |
| Undo (Follow) | Supported | Unfollow remote actors |
| Create | Supported | Create Note/Article posts |
| Update | Supported | Update posts |
| Delete | Supported | Delete posts (as Tombstone) |
| Like | Supported | Send likes with full object |
| EmojiReact | Supported | Send emoji reactions (FEP-c0e0) |
| Undo (Like/EmojiReact) | Supported | Unlike/unreact posts |
| Announce | Supported | Boost/share posts |
| Undo (Announce) | Supported | Un-boost posts |
| Update (Actor) | Supported | Update profile information |
| Add | Supported | Featured collection (pinning) |
| Remove | Supported | Remove from featured collection |

### Incoming Activities

| Activity | Status | Notes |
|----------|--------|-------|
| Follow | Supported | Handle incoming follow requests |
| Accept | Supported | Handle follow acceptance |
| Reject | Supported | Handle follow rejection |
| Undo | Supported | Handle undo for Follow, Like, EmojiReact, Announce |
| Create | Supported | Receive Note and Article posts |
| Like | Supported | Handle likes and Like with content |
| EmojiReact | Supported | Handle emoji reactions |
| Announce | Supported | Handle boosts |
| Delete | Supported | Handle post deletion |
| Update | Supported | Handle post updates |
| Add | Supported | Handle featured collection additions |
| Remove | Supported | Handle featured collection removals |

### Object Types

**Outgoing:**
- Note - Standard microblog posts
- Article - Long-form articles
- Document - Media attachments (images, videos, audio)
- Image - Avatar and header images
- Tombstone - Deleted content marker
- Person - Actor type

**Incoming:**
- Note and Article content types
- Tombstone for deletions
- Mention for @mentions
- Hashtag for #tags

### Custom Extensions

- LitePub vocabulary for EmojiReact (`http://litepub.social/ns#EmojiReact`)

## Additional documentation

- ActivityPub endpoints available at `/activitypub/actors/{username}`
- NodeInfo discovery at `/.well-known/nodeinfo`
- NodeInfo documents at `/nodeinfo/2.0` and `/nodeinfo/2.1`