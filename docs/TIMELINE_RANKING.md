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

## Signed-In Ranking Formula

For a signed-in user, the current final rank is:

\[
R_{\text{final}}(p,u)=R_{\text{base}}(p)+R_{\text{personal}}(p,u)+R_{\text{publisher}}(p)-R_{\text{diversity}}(p)
\]

Base rank:

\[
R_{\text{base}}(p)=\frac{S_{\text{perf}}(p)+5}{\left(1+H(p)\right)^{1.2}}
\]

\[
S_{\text{perf}}(p)=1.4\,S_{\text{reaction}}(p)+0.8\,C_{\text{thread}}(p)+0.1\,A(p)+B_{\text{article}}(p)
\]

\[
B_{\text{article}}(p)=
\begin{cases}
1.5, & \text{if } p \text{ is an article} \\
0, & \text{otherwise}
\end{cases}
\]

\[
S_{\text{reaction}}(p)=2N_{+}(p)+1N_{0}(p)-2N_{-}(p)
\]

\[
H(p)=\frac{\text{age in minutes of }p}{60}
\]

Personalization bonus:

\[
R_{\text{personal}}(p,u)=
0.8\sum_{t\in T(p)} I_u^{\text{tag}}(t)
+0.75\sum_{c\in C(p)} I_u^{\text{cat}}(c)
+\min\left(2,\;0.35\,I_u^{\text{pub}}(P(p))\right)
+1.25\,M_{\text{tag-sub}}(p,u)
+1.5\,M_{\text{cat-sub}}(p,u)
\]

Interest decay:

\[
I_u^{k}(x)=I_{u,\text{stored}}^{k}(x)\cdot e^{-d(x)/30}
\]

Publisher bonus:

\[
R_{\text{publisher}}(p)=
\begin{cases}
\min(3,\;0.05\,L_{\text{social}}(P(p))), & \text{if publisher has an account profile} \\
0, & \text{for organization publishers or no linked account}
\end{cases}
\]

Diversity penalty:

\[
R_{\text{diversity}}(p)=1.35\cdot N_{\text{same-publisher-before}}(p)
\]

Where:

- \(T(p)\): tags on post \(p\)
- \(C(p)\): categories on post \(p\)
- \(P(p)\): publisher of post \(p\)
- \(A(p)\): awarded score of post \(p\)
- \(C_{\text{thread}}(p)\): thread replies count of post \(p\)
- \(d(x)\): days since the user's last interaction for that interest entry
- \(M_{\text{tag-sub}}(p,u)\): count of matched tag subscriptions
- \(M_{\text{cat-sub}}(p,u)\): count of matched category subscriptions
- \(N_{\text{same-publisher-before}}(p)\): number of already-selected posts from the same publisher earlier in the diversification pass
