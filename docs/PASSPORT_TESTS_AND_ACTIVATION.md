# Passport Tests and Account Activation

Passport provides reusable tests for account onboarding and other permission-gated flows. Test definitions, questions, choices, attempts, and answers are stored only in the Passport database.

## Activation configuration

Configure the account-entry requirements in Passport `appsettings.json`:

```json
"AccountActivation": {
  "TestsEnabled": true,
  "RequireVerifiedContact": true,
  "RequiredTestKeys": ["platform-entry"]
}
```

- `RequireVerifiedContact` requires at least one verified Padlock contact.
- Set `TestsEnabled` to `false` to disable all test requirements. Applying a contact-verification spell then activates the account automatically.
- `RequiredTestKeys` requires a passed attempt for every listed published test.
- An empty `RequiredTestKeys` disables the test requirement.

New accounts receive a `ContactVerification` spell and a separate welcome email. Applying the spell verifies the contact only; it does not activate the account. After the requirements are satisfied, Passport publishes the account-activation event and Padlock sets `activated_at` and adds the account to the activated-users group.

## Permissions and groups

Padlock owns permission groups and seeds these groups at startup:

- `default`: assigned to every registered account, including unactivated accounts; contains `tests.take` and the explicit basic-account and chat permissions.
- `verified`: assigned when an account is activated and contains the explicit social, publishing, relationship, realm, location, and live-stream permissions.
- `moderator`: administrator-assigned; contains the explicit ticket-handling, post-locking, and moderation permissions.
- `developer`: administrator-assigned; contains the explicit developer, custom-app, bot-account, product, project, and mini-app permissions.

Each scaffolded group is synchronized from its complete, explicit permission-key list in `PermissionSeedService`; no permission is assigned by a key prefix. Startup also corrects stale nodes in those scaffolded groups and backfills `default` for registered accounts and `verified` for already activated accounts.

The test APIs use these permission keys:

- `tests.take`: view and take published tests, view attempts, and check activation status.
- `tests.manage`: create, update, publish, archive, and inspect tests.
- `tests.review`: inspect attempts and review subjective answers.

`tests.manage` and `tests.review` are not automatically added to the `default` group. Assign them to an administrator-managed permission group through Padlock’s permission-group APIs.

Padlock permission-group detail requests are paginated independently for nodes and members. `GET /api/admin/permissions/groups/{groupId}` accepts `nodesTake`, `nodesOffset`, `membersTake`, and `membersOffset` (50 by default; 200 maximum) and returns `node_total` and `member_total` with each page.

## Registration invitation spells

Authenticated users can create a Wallet order with `POST /api/affiliations/purchase`. The order uses the `points` currency and, after payment, creates a single-use registration invitation spell. Configuration is under `AffiliationPurchase`:

```json
"AffiliationPurchase": {
  "Enabled": true,
  "PricePoints": 100,
  "MaxPurchases": 2,
  "PurchasePeriodDays": 30
}
```

The purchase cap is enforced when an order is created. A paid spell can be used once by an unactivated account during registration. It bypasses configured test requirements but does not bypass the configured verified-contact requirement.

Administrators with `affiliations.manage` can instead create a spell with `POST /api/affiliations`. Provide `spell` for a custom code, `max_usages` for a finite shared-use limit, or omit `max_usages` for unlimited usage. Set `skip_tests` to `false` when the code should be tracked as an affiliation but must not bypass onboarding tests.

## Admin API

All admin endpoints are under `/passport/api/admin/tests` in production.

- `GET /api/admin/tests` lists test definitions, including correct choice data.
- `POST /api/admin/tests` creates a test and its questions.
- `PUT /api/admin/tests/{key}` replaces a test’s editable definition and questions. Existing attempts retain their stored snapshot.
- `POST /api/admin/tests/{key}/publish?published=true` publishes or unpublishes a test.
- `POST /api/admin/tests/{key}/listing?listed=true` lists or unlists a published test in the public catalog.
- `POST /api/admin/tests/{key}/archive?archived=true` archives or restores a test.
- `GET /api/admin/tests/{key}/attempts?status=pending_review` lists attempts for review.
- `POST /api/admin/tests/answers/{answerId}/review` scores a manually reviewed answer.

Create a choice test and grant a group on passing:

```json
{
  "key": "platform-entry",
  "title": "Platform Entry",
  "description": "Basic platform knowledge.",
  "is_published": true,
  "is_listed": true,
  "passing_score": 80,
  "max_attempts": 3,
  "attempt_period_days": 365,
  "time_limit_seconds": 600,
  "granted_permission_group_key": "community-member",
  "config": {},
  "questions": [
    {
      "sort_order": 0,
      "content": "Which action follows the community guidelines?",
      "type": "single_choice",
      "grading_mode": "auto",
      "difficulty": 1,
      "points": 1,
      "config": {},
      "choices": [
        { "sort_order": 0, "content": "Respect other members", "is_correct": true, "config": {} },
        { "sort_order": 1, "content": "Harass other members", "is_correct": false, "config": {} }
      ]
    }
  ]
}
```

`granted_permission_group_key` is optional. When the attempt passes, Passport publishes an account event; Padlock adds the account to that group and invalidates its permission cache. The named group must already exist in Padlock.

## Participant API

All participant endpoints are under `/passport/api/tests` in production and require `tests.take`.

- `GET /api/tests` is public and returns published, listed tests without correct-answer data.
- `GET /api/tests/activation` returns contact and required-test status.
- `POST /api/tests/activation/recheck` reevaluates requirements and activates an eligible account.
- `GET /api/tests/{key}` returns a published test without correct-answer data.
- `POST /api/tests/{key}/attempts` starts an attempt or resumes the active attempt.
- `POST /api/tests/attempts/{attemptId}/submit` submits answers.
- `GET /api/tests/attempts/{attemptId}` returns the caller’s attempt status and submitted values.

For account onboarding screens, `GET /api/accounts/me/activation/progress` returns the same requirement state with `is_activated`, `required_requirement_count`, and `completed_requirement_count`.

Submit answers with choice IDs or text:

```json
{
  "answers": [
    {
      "question_id": "question-guid",
      "choice_ids": ["choice-guid"]
    },
    {
      "question_id": "free-text-question-guid",
      "text": "My answer"
    }
  ]
}
```

## Grading and attempts

- `single_choice` and `multiple_choice` questions can use `auto` grading. The selected choice set must exactly match the correct choice set.
- `free_text` and other subjective questions use `manual` grading. A submission with any manual question becomes `pending_review` until every manual answer is reviewed.
- The final score is awarded points divided by all possible points, compared with the test’s `passing_score`.
- A time-limit expiry marks the active attempt `expired`. Submitted and expired attempts consume the configured `max_attempts` within the rolling `attempt_period_days` window (365 days by default); an unset limit permits unlimited attempts.
- Each attempt stores an immutable test snapshot, including grading rules and any permission-group grant. Editing a test changes only future attempts.
