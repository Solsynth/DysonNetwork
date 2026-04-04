# Federation

## Supported federation protocols and standards

- [ActivityPub](https://www.w3.org/TR/activitypub/) (Server-to-Server)
- [WebFinger](https://webfinger.net/)
- [HTTP Signatures](https://datatracker.ietf.org/doc/html/draft-cavage-http-signatures)
- [NodeInfo](https://nodeinfo.diaspora.software/) 2.0 and 2.1

## Supported FEPs

- [FEP-67ff: FEDERATION.md](https://codeberg.org/fediverse/fep/src/branch/main/fep/67ff/fep-67ff.md)
- [FEP-044f: Consent-respecting quote posts](https://fediverse.codeberg.page/fep/fep/044f/) (Draft)
- [FEP-c0e0: Emoji reactions](https://fediverse.codeberg.page/fep/fep/c0e0/) (Draft)
- [FEP-1311: Media Attachments](https://fediverse.codeberg.page/fep/fep/1311/) (Draft)
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
| QuoteRequest | Supported | Request quote permission (FEP-044f) |
| Update (Actor) | Supported | Update profile information |
| Add | Supported | Featured collection (pinning) |
| Remove | Supported | Remove from featured collection |

### Incoming Activities

| Activity | Status | Notes |
|----------|--------|-------|
| Follow | Supported | Handle incoming follow requests |
| Accept | Supported | Handle follow acceptance |
| Reject | Supported | Handle follow rejection |
| QuoteRequest | Supported | Handle quote permission requests (FEP-044f) |
| Undo | Supported | Handle undo for Follow, Like, EmojiReact, Announce, QuoteAuthorization |
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
- Image - Media attachments (FEP-1311)
- Video - Video attachments
- Audio - Audio attachments
- Tombstone - Deleted content marker
- Person - Actor type
- Group - Community actor type
- QuoteAuthorization - Quote permission stamp (FEP-044f)

**Incoming:**
- Note and Article content types
- Tombstone for deletions
- Mention for @mentions
- Hashtag for #tags
- QuoteAuthorization for quote permissions

### Custom Extensions

- LitePub vocabulary for EmojiReact (`http://litepub.social/ns#EmojiReact`)
- GoToSocial interactionPolicy for quote permissions (`https://gotosocial.org/ns#interactionPolicy`)
- FEP-044f quote properties: `quote`, `quoteUrl`, `quoteUri`, `quoteAuthorization`

## Additional documentation

- ActivityPub endpoints available at `/activitypub/actors/{username}`
- QuoteAuthorization endpoint at `/quote-authorizations/{id}`
- NodeInfo discovery at `/.well-known/nodeinfo`
- NodeInfo documents at `/nodeinfo/2.0` and `/nodeinfo/2.1`