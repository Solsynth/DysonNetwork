# Shared API Error Responses

DysonNetwork HTTP APIs use `ApiError` for application errors. It is an RFC 7807-inspired JSON payload, defined in [`DysonNetwork.Shared/Networking/ApiError.cs`](../DysonNetwork.Shared/Networking/ApiError.cs).

Clients must use the HTTP status as the primary transport outcome and `code` as the machine-readable application outcome. Treat `message` and `detail` as display/debugging text, not as values to branch on.

## Payload

```json
{
  "code": "CHAT_SEARCH_QUERY_REQUIRED",
  "message": "One or more validation errors occurred.",
  "status": 400,
  "detail": null,
  "traceId": "0HNF4S44V8MTT:00000001",
  "errors": {
    "query": [
      "Search query cannot be empty."
    ]
  },
  "meta": null
}
```

Fields whose value is `null` are omitted from an actual response.

| Field | Type | Client use |
|---|---|---|
| `code` | string | Stable application-specific identifier. Use this for feature-specific handling. Codes are normally uppercase with underscores, although older endpoints can use service-specific naming. |
| `message` | string | Short human-readable explanation. Suitable for a generic UI message after localization/product review. |
| `status` | integer, optional | Server-reported HTTP status. Prefer the actual HTTP response status if the two disagree. |
| `detail` | string, optional | Extra diagnostic context, such as a resource identifier. Do not assume a fixed schema. |
| `traceId` | string, optional | Include this when reporting an issue to support or examining server logs. The JSON key is camel-case by design. |
| `errors` | object, optional | Field-level validation messages. Keys identify request fields; values are arrays of messages. Render these next to the corresponding inputs. |
| `meta` | object, optional | Endpoint-specific structured information. Only read documented keys for the endpoint/code that returned it. |

## Standard factories and status codes

`ApiError` provides these common shapes. Endpoints can also return a custom `code` with the same payload format.

| HTTP status | Default code | Typical client action |
|---:|---|---|
| 400 | `VALIDATION_ERROR` | Show `errors` by field; do not retry unchanged input. |
| 401 | `UNAUTHORIZED` | Refresh/reacquire credentials, then retry the original request once if appropriate. Otherwise sign in. |
| 403 | `FORBIDDEN` | Do not retry automatically. Hide/disable the unavailable action or show an entitlement/permission explanation. |
| 404 | `NOT_FOUND` | Remove or refresh stale local state. |
| 409 | `CONFLICT` | Refresh the affected resource and let the user reconcile/retry. Inspect documented `meta` when available. |
| 5xx | `SERVER_ERROR` | Treat as transient where safe: retry with bounded exponential backoff and retain `traceId`. |

Rate limiting or gateway errors may use other HTTP statuses such as `429`; handle those by status and the relevant HTTP headers even if the payload is not an `ApiError`.

## Client handling flow

1. If the response is successful, decode the endpoint's success schema.
2. If it is not successful, attempt to decode an `ApiError` JSON body.
3. Use HTTP status for authentication, authorization, retry, and navigation behavior.
4. Use `code` for documented product behavior. For example, `CHAT_SEARCH_QUERY_REQUIRED` focuses the search input.
5. If `errors` is present, attach every message to its matching form field.
6. For an unknown code, use a safe generic error UI and retain `traceId`; never crash or assume the error is retryable.

## TypeScript example

```ts
export type ApiError = {
  code: string;
  message: string;
  status?: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
  meta?: Record<string, unknown>;
};

export async function parseApiError(response: Response): Promise<ApiError | undefined> {
  const contentType = response.headers.get("content-type") ?? "";
  if (!contentType.includes("application/json")) return undefined;

  try {
    const value: unknown = await response.json();
    if (typeof value === "object" && value !== null && "code" in value && "message" in value) {
      return value as ApiError;
    }
  } catch {
    // A gateway or proxy can return malformed/non-API JSON.
  }
  return undefined;
}

const response = await fetch(url, options);
if (!response.ok) {
  const error = await parseApiError(response);

  if (response.status === 401) {
    await refreshCredentials();
  } else if (error?.errors) {
    showFieldErrors(error.errors);
  } else {
    showError(error?.message ?? "The request could not be completed.");
  }

  logFailure({ status: response.status, code: error?.code, traceId: error?.traceId });
}
```

## Chat cloud-search example

`GET /api/chat/messages/search` can return these documented errors:

| Status | Code | Client action |
|---:|---|---|
| 400 | `CHAT_SEARCH_QUERY_REQUIRED` | Keep the search UI open and require non-whitespace query text. |
| 400 | `CHAT_SEARCH_DATE_RANGE_INVALID` | Mark the date range invalid; `after` must be earlier than `before`. |
| 403 | `FORBIDDEN` | Cloud search requires perk level 1 or higher. Offer the relevant entitlement path if the product supports it. |

See [CHAT_MESSAGE_SEARCH](./CHAT_MESSAGE_SEARCH.md) for the endpoint contract.
