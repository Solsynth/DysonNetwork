# Domain Block API Documentation

## Overview

The Domain Block system provides URL validation and domain blocking capabilities. It uses a blacklist approach where all URLs are allowed by default, except for:

- HTTP URLs (configurable)
- Direct IP addresses (configurable)
- Private network addresses (configurable)
- Blocked ports (configurable)
- Custom block rules in the database

This system is useful for preventing SSRF attacks, restricting unsafe protocols, and managing trusted domains for OAuth redirects, embeds, and API calls.

## Base URL

```
/pass/domain-blocks
```

## Configuration

The system is configured via `appsettings.json`:

```json
{
  "DomainBlock": {
    "BlockHttpByDefault": true,
    "BlockIpAddressesByDefault": true,
    "BlockPrivateNetworksByDefault": true,
    "AdditionalBlockedPorts": [21, 22, 23, 25, 53, 110, 143, 993, 995, 3306, 5432, 6379, 27017]
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `BlockHttpByDefault` | bool | `true` | Block HTTP (non-HTTPS) URLs |
| `BlockIpAddressesByDefault` | bool | `true` | Block direct IP address URLs |
| `BlockPrivateNetworksByDefault` | bool | `true` | Block private network IPs (10.x, 172.16-31.x, 192.168.x, 127.x, fc00::/7) |
| `AdditionalBlockedPorts` | int[] | See above | Ports to block (default includes FTP, SSH, SMTP, DNS, database ports, etc.) |

---

## Wildcard Pattern Syntax

Domain patterns support wildcard matching:

| Pattern | Description | Example Matches |
|---------|-------------|-----------------|
| `*` | Match all domains | Everything |
| `example.com` | Exact match | `example.com` only |
| `*.example.com` | Match subdomains only | `api.example.com`, `www.example.com` |
| `**.example.com` | Match domain + all subdomains | `example.com`, `api.example.com` |
| `192.168.*` | Wildcard IP ranges | `192.168.1.1`, `192.168.100.50` |

---

## Endpoints

### List Block Rules

Get a paginated list of all domain block rules.

**Endpoint:** `GET /api/domain-blocks`

**Authorization:** Required

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `offset` | int | Pagination offset (default: 0) |
| `limit` | int | Pagination limit (default: 50) |

**Response:** `200 OK`

```json
[
  {
    "id": "uuid",
    "domain_pattern": "malicious.com",
    "protocol": null,
    "port_restriction": null,
    "reason": "Known phishing site",
    "priority": 10,
    "is_active": true,
    "created_by_account_id": "uuid",
    "created_at": "2026-05-05T10:30:00Z",
    "updated_at": "2026-05-05T10:30:00Z"
  }
]
```

**Headers:**

- `X-Total`: Total count of rules

---

### Get Block Rule

Get details of a specific block rule.

**Endpoint:** `GET /api/domain-blocks/{id}`

**Authorization:** Required

**Path Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `id` | uuid | Block rule ID |

**Response:** `200 OK` or `404 Not Found`

---

### Create Block Rule

Create a new domain block rule.

**Endpoint:** `POST /api/domain-blocks`

**Authorization:** Required

**Request Body:**

```json
{
  "domain_pattern": "string (required, max 512 chars)",
  "protocol": "string (optional, max 16 chars - e.g. 'http', 'https')",
  "port_restriction": "int (optional - specific port to block)",
  "reason": "string (optional, max 256 chars)",
  "priority": "int (default: 0 - higher = checked first)",
  "is_active": "bool (default: true)"
}
```

**Example Request:**

```json
{
  "domain_pattern": "*.malicious.net",
  "reason": "Known malware distribution network",
  "priority": 100
}
```

**Response:** `200 OK`

```json
{
  "id": "uuid",
  "domain_pattern": "*.malicious.net",
  "protocol": null,
  "port_restriction": null,
  "reason": "Known malware distribution network",
  "priority": 100,
  "is_active": true,
  "created_by_account_id": "uuid",
  "created_at": "2026-05-05T10:30:00Z",
  "updated_at": "2026-05-05T10:30:00Z"
}
```

---

### Update Block Rule

Update an existing block rule.

**Endpoint:** `PATCH /api/domain-blocks/{id}`

**Authorization:** Required

**Request Body:**

```json
{
  "domain_pattern": "string (optional)",
  "protocol": "string (optional)",
  "port_restriction": "int (optional)",
  "reason": "string (optional)",
  "priority": "int (optional)",
  "is_active": "bool (optional)"
}
```

**Response:** `200 OK` or `404 Not Found`

---

### Delete Block Rule

Delete a block rule.

**Endpoint:** `DELETE /api/domain-blocks/{id}`

**Authorization:** Required

**Response:** `204 No Content` or `404 Not Found`

---

### Validate URL

Validate a URL against all block rules and default restrictions. Returns detailed validation result.

**Endpoint:** `POST /api/domain-blocks/validate`

**Authorization:** Not required

**Request Body:**

```json
{
  "url": "string (required)"
}
```

**Example Request:**

```json
{
  "url": "http://malicious-site.com/path?query=1"
}
```

**Response:** `200 OK`

```json
{
  "is_allowed": false,
  "block_reason": "HTTP protocol is blocked by default (not secure)",
  "matched_rule": null,
  "matched_source": "default_http_block"
}
```

**Response Fields:**

| Field | Type | Description |
|-------|------|-------------|
| `is_allowed` | bool | Whether the URL is allowed |
| `block_reason` | string? | Human-readable reason if blocked |
| `matched_rule` | object? | The matching database rule (if any) |
| `matched_source` | string? | Source of the block decision |

**Possible `matched_source` values:**

| Value | Description |
|-------|-------------|
| `default_http_block` | Blocked by HTTP protocol restriction |
| `default_ip_block` | Blocked by IP address restriction |
| `default_private_network_block` | Blocked by private network restriction |
| `default_port_block` | Blocked by port restriction |
| `database_rule` | Blocked by a rule in the database |

---

### Quick Check URL

Quick check if a URL is allowed. Returns minimal information.

**Endpoint:** `GET /api/domain-blocks/check`

**Authorization:** Not required

**Query Parameters:**

| Parameter | Type | Description |
|-----------|------|-------------|
| `url` | string | URL to check (required) |

**Response:** `200 OK`

```json
{
  "is_allowed": false,
  "block_reason": "HTTP protocol is blocked by default (not secure)"
}
```

---

## gRPC Service

The Domain Block system provides a gRPC service for interservice communication.

**Service:** `DyDomainBlockService`

### ValidateUrl

Validate a URL and return detailed result.

```protobuf
rpc ValidateUrl(DyValidateUrlRequest) returns (DyDomainValidationResult);

message DyValidateUrlRequest {
  string url = 1;
}

message DyDomainValidationResult {
  bool is_allowed = 1;
  string block_reason = 2;
  string matched_source = 3;
}
```

### IsDomainBlocked

Check if a specific domain/host is blocked.

```protobuf
rpc IsDomainBlocked(DyDomainBlockCheckRequest) returns (DyDomainBlockCheckResult);

message DyDomainBlockCheckRequest {
  string host = 1;
  string protocol = 2;
  int32 port = 3;
}

message DyDomainBlockCheckResult {
  bool is_blocked = 1;
  string reason = 2;
}
```

---

## Usage Examples

### Client-side URL Validation

```javascript
// Check if a redirect URL is safe
async function isUrlSafe(url) {
  const response = await fetch(`/api/domain-blocks/check?url=${encodeURIComponent(url)}`);
  const result = await response.json();
  return result.is_allowed;
}

// Usage
const redirectUrl = 'http://example.com';
if (await isUrlSafe(redirectUrl)) {
  window.location.href = redirectUrl;
} else {
  alert('This URL is not allowed');
}
```

### Server-side Validation (via gRPC)

```csharp
// In another service
public class MyService
{
    private readonly DyDomainBlockService.DyDomainBlockServiceClient _domainBlock;
    
    public MyService(LazyGrpcClientFactory<DyDomainBlockService.DyDomainBlockServiceClient> factory)
    {
        _domainBlock = factory.GetClient();
    }
    
    public async Task<bool> IsUrlAllowedAsync(string url)
    {
        var result = await _domainBlock.ValidateUrlAsync(new DyValidateUrlRequest { Url = url });
        return result.IsAllowed;
    }
}
```

---

## Security Considerations

1. **Default Deny for Unsafe Patterns**: HTTP, IP addresses, and private networks are blocked by default to prevent SSRF attacks.

2. **Priority System**: Higher priority rules are checked first. Use this to create exceptions or more specific rules.

3. **Protocol-Specific Rules**: You can block specific protocols for certain domains:
   ```json
   {
     "domain_pattern": "example.com",
     "protocol": "http",
     "reason": "HTTPS only for this domain"
   }
   ```

4. **Port Restrictions**: Block specific ports per domain:
   ```json
   {
     "domain_pattern": "internal.company.com",
     "port_restriction": 8080,
     "reason": "Block internal admin panel"
   }
   ```

5. **IPv6 Support**: The system correctly handles IPv6 addresses including link-local (`fe80::/10`) and unique local addresses (`fc00::/7`).

---

## Response Format

All responses use snake_case naming convention for properties.

**Example:**

```json
{
  "domain_pattern": "*.example.com",
  "is_active": true,
  "created_at": "2026-05-05T10:30:00Z"
}
```

---

## Error Responses

| Status Code | Description |
|-------------|-------------|
| `400 Bad Request` | Invalid request body or parameters |
| `401 Unauthorized` | Authentication required |
| `404 Not Found` | Resource not found |
| `500 Internal Server Error` | Server error |
