# Timeline Ranking

This document describes how the timeline ranking currently works in `DysonNetwork.Sphere`.

## Summary

Timeline ranking is built in layers:

1. base post score
2. personalized interest bonus for signed-in users
3. publisher social-credit bonus for individual publishers
4. a diversification pass to avoid one publisher filling the whole feed

Anonymous users only receive the base score, publisher bonus, and diversification.

## Base Score

The base score is calculated from:

- `reaction_score`
- `thread_replies_count`
- `awarded_score`
- freshness decay over time
- `article` type preference

Current implementation details:

- `reaction_score` uses weighted reaction attitudes
  - positive: `+2`
  - neutral: `+1`
  - negative: `-2`
- article posts receive an additional base boost

The time decay reduces older posts even when they have strong engagement.

## Personalized Score

For signed-in users, the timeline adds an interest bonus from `post_interest_profiles`.

Interest signals currently include:

- reactions
- authored replies
- post views, rate-limited per account/post/day

Interest is tracked against:

- tags
- categories
- publishers

The personalized bonus is decayed over time so old interests matter less than recent ones.

Explicit tag/category subscriptions also add a bonus.

## Publisher Bonus

For individual publishers, the timeline reads the account profile via batched gRPC account fetch and uses `socialCreditsLevel` as a small ranking bonus.

Organization publishers do not receive this bonus because they do not have an account-level social-credit profile.

## Diversity Pass

After candidate posts are scored, the final selection step applies a repeat-publisher penalty.

This is a soft penalty, not a hard filter:

- the first high-ranking post from a publisher is unaffected
- later posts from the same publisher lose score during selection
- this helps prevent the timeline from being dominated by a single publisher

## Debug Rank In Response

For debugging, timeline post payloads now include:

- `debugRank`

This field is:

- only meaningful in timeline responses
- the final score after personalization, publisher bonus, and diversification penalty
- not persisted in the database

Example timeline event payload shape:

```json
{
  "id": "event-id",
  "type": "posts.new",
  "data": {
    "id": "post-id",
    "type": 1,
    "repliesCount": 3,
    "threadRepliesCount": 7,
    "debugRank": 6.8421
  }
}
```

## Notes

- `debugRank` is intended for debugging and tuning, not long-term client product logic
- the current weights are implementation defaults and may be tuned later
- ranking is intentionally explainable and deterministic in this version; it does not rely on embeddings or LLM inference
