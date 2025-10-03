# Wallet Funds API Documentation

## Overview

The Wallet Funds API provides red packet functionality for the DysonNetwork platform, allowing users to create and distribute funds among multiple recipients with expiration and claiming mechanisms.

## Authentication

All endpoints require Bearer token authentication:

```
Authorization: Bearer {jwt_token}
```

## Data Types

### Enums

#### FundSplitType
```typescript
enum FundSplitType {
  Even = 0,    // Equal distribution
  Random = 1   // Lucky draw distribution
}
```

#### FundStatus
```typescript
enum FundStatus {
  Created = 0,           // Fund created, waiting for claims
  PartiallyReceived = 1, // Some recipients claimed
  FullyReceived = 2,     // All recipients claimed
  Expired = 3,           // Fund expired, unclaimed amounts refunded
  Refunded = 4           // Legacy status
}
```

### Request/Response Models

#### CreateFundRequest
```typescript
interface CreateFundRequest {
  recipientAccountIds: string[];  // UUIDs of recipients
  currency: string;               // e.g., "points", "golds"
  totalAmount: number;            // Total amount to distribute
  splitType: FundSplitType;       // Even or Random
  message?: string;               // Optional message
  expirationHours?: number;       // Optional: hours until expiration (default: 24)
  pinCode: string;                // Required: 6-digit PIN code for security
}
```

#### SnWalletFund
```typescript
interface SnWalletFund {
  id: string;                     // UUID
  currency: string;
  totalAmount: number;
  splitType: FundSplitType;
  status: FundStatus;
  message?: string;
  creatorAccountId: string;       // UUID
  creatorAccount: SnAccount;      // Creator account details (includes profile)
  recipients: SnWalletFundRecipient[];
  expiredAt: string;              // ISO 8601 timestamp
  createdAt: string;              // ISO 8601 timestamp
  updatedAt: string;              // ISO 8601 timestamp
}
```

#### SnWalletFundRecipient
```typescript
interface SnWalletFundRecipient {
  id: string;                     // UUID
  fundId: string;                 // UUID
  recipientAccountId: string;     // UUID
  recipientAccount: SnAccount;    // Recipient account details (includes profile)
  amount: number;                 // Allocated amount
  isReceived: boolean;
  receivedAt?: string;            // ISO 8601 timestamp (if claimed)
  createdAt: string;              // ISO 8601 timestamp
  updatedAt: string;              // ISO 8601 timestamp
}
```

#### SnWalletTransaction
```typescript
interface SnWalletTransaction {
  id: string;                     // UUID
  payerWalletId?: string;         // UUID (null for system transfers)
  payeeWalletId?: string;         // UUID (null for system transfers)
  currency: string;
  amount: number;
  remarks?: string;
  type: TransactionType;
  createdAt: string;              // ISO 8601 timestamp
  updatedAt: string;              // ISO 8601 timestamp
}
```

#### Error Response
```typescript
interface ErrorResponse {
  type: string;                   // Error type
  title: string;                  // Error title
  status: number;                 // HTTP status code
  detail: string;                 // Error details
  instance?: string;              // Request instance
}
```

## API Endpoints

### 1. Create Fund

Creates a new fund (red packet) for distribution among recipients.

**Endpoint:** `POST /api/wallets/funds`

**Request Body:** `CreateFundRequest`

**Response:** `SnWalletFund` (201 Created)

**Example Request:**
```bash
curl -X POST "/api/wallets/funds" \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{
    "recipientAccountIds": [
      "550e8400-e29b-41d4-a716-446655440000",
      "550e8400-e29b-41d4-a716-446655440001",
      "550e8400-e29b-41d4-a716-446655440002"
    ],
    "currency": "points",
    "totalAmount": 100.00,
    "splitType": "Even",
    "message": "Happy New Year! 🎉",
    "expirationHours": 48,
    "pinCode": "123456"
  }'
```

**Example Response:**
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440003",
  "currency": "points",
  "totalAmount": 100.00,
  "splitType": 0,
  "status": 0,
  "message": "Happy New Year! 🎉",
  "creatorAccountId": "550e8400-e29b-41d4-a716-446655440004",
  "creatorAccount": {
    "id": "550e8400-e29b-41d4-a716-446655440004",
    "username": "creator_user"
  },
  "recipients": [
    {
      "id": "550e8400-e29b-41d4-a716-446655440005",
      "fundId": "550e8400-e29b-41d4-a716-446655440003",
      "recipientAccountId": "550e8400-e29b-41d4-a716-446655440000",
      "amount": 33.34,
      "isReceived": false,
      "createdAt": "2025-10-03T22:00:00Z",
      "updatedAt": "2025-10-03T22:00:00Z"
    },
    {
      "id": "550e8400-e29b-41d4-a716-446655440006",
      "fundId": "550e8400-e29b-41d4-a716-446655440003",
      "recipientAccountId": "550e8400-e29b-41d4-a716-446655440001",
      "amount": 33.33,
      "isReceived": false,
      "createdAt": "2025-10-03T22:00:00Z",
      "updatedAt": "2025-10-03T22:00:00Z"
    },
    {
      "id": "550e8400-e29b-41d4-a716-446655440007",
      "fundId": "550e8400-e29b-41d4-a716-446655440003",
      "recipientAccountId": "550e8400-e29b-41d4-a716-446655440002",
      "amount": 33.33,
      "isReceived": false,
      "createdAt": "2025-10-03T22:00:00Z",
      "updatedAt": "2025-10-03T22:00:00Z"
    }
  ],
  "expiredAt": "2025-10-05T22:00:00Z",
  "createdAt": "2025-10-03T22:00:00Z",
  "updatedAt": "2025-10-03T22:00:00Z"
}
```

**Error Responses:**
- `400 Bad Request`: Invalid parameters, insufficient funds, invalid recipients
- `401 Unauthorized`: Missing or invalid authentication
- `403 Forbidden`: Invalid PIN code
- `422 Unprocessable Entity`: Business logic violations

---

### 2. Get Funds

Retrieves funds that the authenticated user is involved in (as creator or recipient).

**Endpoint:** `GET /api/wallets/funds`

**Query Parameters:**
- `offset` (number, optional): Pagination offset (default: 0)
- `take` (number, optional): Number of items to return (default: 20, max: 100)
- `status` (FundStatus, optional): Filter by fund status

**Response:** `SnWalletFund[]` (200 OK)

**Headers:**
- `X-Total`: Total number of funds matching the criteria

**Example Request:**
```bash
curl -X GET "/api/wallets/funds?offset=0&take=10&status=0" \
  -H "Authorization: Bearer {token}"
```

**Example Response:**
```json
[
  {
    "id": "550e8400-e29b-41d4-a716-446655440003",
    "currency": "points",
    "totalAmount": 100.00,
    "splitType": 0,
    "status": 0,
    "message": "Happy New Year! 🎉",
    "creatorAccountId": "550e8400-e29b-41d4-a716-446655440004",
    "creatorAccount": {
      "id": "550e8400-e29b-41d4-a716-446655440004",
      "username": "creator_user"
    },
    "recipients": [
      {
        "id": "550e8400-e29b-41d4-a716-446655440005",
        "fundId": "550e8400-e29b-41d4-a716-446655440003",
        "recipientAccountId": "550e8400-e29b-41d4-a716-446655440000",
        "amount": 33.34,
        "isReceived": false
      }
    ],
    "expiredAt": "2025-10-05T22:00:00Z",
    "createdAt": "2025-10-03T22:00:00Z",
    "updatedAt": "2025-10-03T22:00:00Z"
  }
]
```

**Error Responses:**
- `401 Unauthorized`: Missing or invalid authentication

---

### 3. Get Fund

Retrieves details of a specific fund.

**Endpoint:** `GET /api/wallets/funds/{id}`

**Path Parameters:**
- `id` (string): Fund UUID

**Response:** `SnWalletFund` (200 OK)

**Example Request:**
```bash
curl -X GET "/api/wallets/funds/550e8400-e29b-41d4-a716-446655440003" \
  -H "Authorization: Bearer {token}"
```

**Example Response:** (Same as create fund response)

**Error Responses:**
- `401 Unauthorized`: Missing or invalid authentication
- `403 Forbidden`: User doesn't have permission to view this fund
- `404 Not Found`: Fund not found

---

### 4. Receive Fund

Claims the authenticated user's portion of a fund.

**Endpoint:** `POST /api/wallets/funds/{id}/receive`

**Path Parameters:**
- `id` (string): Fund UUID

**Response:** `SnWalletTransaction` (200 OK)

**Example Request:**
```bash
curl -X POST "/api/wallets/funds/550e8400-e29b-41d4-a716-446655440003/receive" \
  -H "Authorization: Bearer {token}"
```

**Example Response:**
```json
{
  "id": "550e8400-e29b-41d4-a716-446655440008",
  "payerWalletId": null,
  "payeeWalletId": "550e8400-e29b-41d4-a716-446655440009",
  "currency": "points",
  "amount": 33.34,
  "remarks": "Received fund portion from 550e8400-e29b-41d4-a716-446655440004",
  "type": 1,
  "createdAt": "2025-10-03T22:05:00Z",
  "updatedAt": "2025-10-03T22:05:00Z"
}
```

**Error Responses:**
- `400 Bad Request`: Fund expired, already claimed, not a recipient
- `401 Unauthorized`: Missing or invalid authentication
- `404 Not Found`: Fund not found

---

### 5. Get Wallet Overview

Retrieves a summarized overview of wallet transactions grouped by type for graphing/charting purposes.

**Endpoint:** `GET /api/wallets/overview`

**Query Parameters:**
- `startDate` (string, optional): Start date in ISO 8601 format (e.g., "2025-01-01T00:00:00Z")
- `endDate` (string, optional): End date in ISO 8601 format (e.g., "2025-12-31T23:59:59Z")

**Response:** `WalletOverview` (200 OK)

**Example Request:**
```bash
curl -X GET "/api/wallets/overview?startDate=2025-01-01T00:00:00Z&endDate=2025-12-31T23:59:59Z" \
  -H "Authorization: Bearer {token}"
```

**Example Response:**
```json
{
  "accountId": "550e8400-e29b-41d4-a716-446655440000",
  "startDate": "2025-01-01T00:00:00.0000000Z",
  "endDate": "2025-12-31T23:59:59.0000000Z",
  "summary": {
    "System": {
      "type": "System",
      "currencies": {
        "points": {
          "currency": "points",
          "income": 150.00,
          "spending": 0.00,
          "net": 150.00
        }
      }
    },
    "Transfer": {
      "type": "Transfer",
      "currencies": {
        "points": {
          "currency": "points",
          "income": 25.00,
          "spending": 75.00,
          "net": -50.00
        },
        "golds": {
          "currency": "golds",
          "income": 0.00,
          "spending": 10.00,
          "net": -10.00
        }
      }
    },
    "Order": {
      "type": "Order",
      "currencies": {
        "points": {
          "currency": "points",
          "income": 0.00,
          "spending": 200.00,
          "net": -200.00
        }
      }
    }
  },
  "totalIncome": 175.00,
  "totalSpending": 285.00,
  "netTotal": -110.00
}
```

**Response Fields:**
- `accountId`: User's account UUID
- `startDate`/`endDate`: Date range applied (ISO 8601 format)
- `summary`: Object keyed by transaction type
  - `type`: Transaction type name
  - `currencies`: Object keyed by currency code
    - `currency`: Currency name
    - `income`: Total money received
    - `spending`: Total money spent
    - `net`: Income minus spending
- `totalIncome`: Sum of all income across all types/currencies
- `totalSpending`: Sum of all spending across all types/currencies
- `netTotal`: Overall net (totalIncome - totalSpending)

**Error Responses:**
- `401 Unauthorized`: Missing or invalid authentication

## Error Codes

### Common Error Types

#### Validation Errors
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "At least one recipient is required",
  "instance": "/api/wallets/funds"
}
```

#### Insufficient Funds
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Insufficient funds",
  "instance": "/api/wallets/funds"
}
```

#### Fund Not Available
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Fund is no longer available",
  "instance": "/api/wallets/funds/550e8400-e29b-41d4-a716-446655440003/receive"
}
```

#### Already Claimed
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "You have already received this fund",
  "instance": "/api/wallets/funds/550e8400-e29b-41d4-a716-446655440003/receive"
}
```

## Rate Limiting

- **Create Fund**: 10 requests per minute per user
- **Get Funds**: 60 requests per minute per user
- **Get Fund**: 60 requests per minute per user
- **Receive Fund**: 30 requests per minute per user

## Webhooks/Notifications

The system integrates with the platform's notification system:

- **Fund Created**: Creator receives confirmation
- **Fund Claimed**: Creator receives notification when someone claims
- **Fund Expired**: Creator receives refund notification

## SDK Examples

### JavaScript/TypeScript

```typescript
// Create a fund
const createFund = async (fundData: CreateFundRequest): Promise<SnWalletFund> => {
  const response = await fetch('/api/wallets/funds', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(fundData)
  });

  if (!response.ok) {
    throw new Error(`HTTP error! status: ${response.status}`);
  }

  return response.json();
};

// Get user's funds
const getFunds = async (params?: {
  offset?: number;
  take?: number;
  status?: FundStatus;
}): Promise<SnWalletFund[]> => {
  const queryParams = new URLSearchParams();
  if (params?.offset) queryParams.set('offset', params.offset.toString());
  if (params?.take) queryParams.set('take', params.take.toString());
  if (params?.status !== undefined) queryParams.set('status', params.status.toString());

  const response = await fetch(`/api/wallets/funds?${queryParams}`, {
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });

  if (!response.ok) {
    throw new Error(`HTTP error! status: ${response.status}`);
  }

  return response.json();
};

// Claim a fund
const receiveFund = async (fundId: string): Promise<SnWalletTransaction> => {
  const response = await fetch(`/api/wallets/funds/${fundId}/receive`, {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });

  if (!response.ok) {
    throw new Error(`HTTP error! status: ${response.status}`);
  }

  return response.json();
};
```

### Python

```python
import requests
from typing import List, Optional
from enum import Enum

class FundSplitType(Enum):
    EVEN = 0
    RANDOM = 1

class FundStatus(Enum):
    CREATED = 0
    PARTIALLY_RECEIVED = 1
    FULLY_RECEIVED = 2
    EXPIRED = 3
    REFUNDED = 4

def create_fund(token: str, fund_data: dict) -> dict:
    """Create a new fund"""
    response = requests.post(
        '/api/wallets/funds',
        json=fund_data,
        headers={
            'Authorization': f'Bearer {token}',
            'Content-Type': 'application/json'
        }
    )
    response.raise_for_status()
    return response.json()

def get_funds(
    token: str,
    offset: int = 0,
    take: int = 20,
    status: Optional[FundStatus] = None
) -> List[dict]:
    """Get user's funds"""
    params = {'offset': offset, 'take': take}
    if status is not None:
        params['status'] = status.value

    response = requests.get(
        '/api/wallets/funds',
        params=params,
        headers={'Authorization': f'Bearer {token}'}
    )
    response.raise_for_status()
    return response.json()

def receive_fund(token: str, fund_id: str) -> dict:
    """Claim a fund portion"""
    response = requests.post(
        f'/api/wallets/funds/{fund_id}/receive',
        headers={'Authorization': f'Bearer {token}'}
    )
    response.raise_for_status()
    return response.json()
```

## Changelog

### Version 1.0.0
- Initial release with basic red packet functionality
- Support for even and random split types
- 24-hour expiration with automatic refunds
- RESTful API endpoints
- Comprehensive error handling

## Support

For API support or questions:
- Check the main documentation at `README_WALLET_FUNDS.md`
- Review error messages for specific guidance
- Contact the development team for technical issues
