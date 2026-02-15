# Lottery System API Documentation

## Overview

The DysonNetwork Lottery System provides a daily lottery where users can purchase tickets with custom number selections. Each day features a new draw with random winning numbers. Users purchase tickets using ISP (Dyson Network Points), with results announced each morning.

The API is handled by the DysonNetwork.Pass service. Which means if you use it with the Gateway the `/api` should be replaced with `/pass`

### Key Features

- **Daily Draws**: Automated draws at midnight UTC
- **Custom Number Selection**: Users choose 5 unique numbers (0-99) + 1 special number (0-99)
- **Flexible Pricing**: Base cost 10 ISP + extra ISP per multiplier (e.g., multiplier=2 costs 20 ISP)
- **Daily Limits**: One ticket purchase per user per day
- **Prize System**: Multiple prize tiers based on matches
- **Instant Payment**: Tickets purchased using in-app points
- **Historical Records**: Complete draw history and statistics

## Data Models

### LotteryDrawStatus Enum
```csharp
public enum LotteryDrawStatus
{
    Pending = 0,    // Ticket awaiting draw
    Drawn = 1       // Ticket has been processed in draw
}
```

### SnLottery Model
```csharp
public class SnLottery : ModelBase
{
    public Guid Id { get; set; }
    public SnAccount Account { get; set; } = null!;
    public Guid AccountId { get; set; }
    public List<int> RegionOneNumbers { get; set; } = new(); // 5 numbers (0-99)
    public int RegionTwoNumber { get; set; }                    // Special number (0-99)
    public int Multiplier { get; set; } = 1;                     // Prize multiplier (≥1)
    public LotteryDrawStatus DrawStatus { get; set; }
    public DateTime? DrawDate { get; set; }                      // Date when drawn
}
```

### SnLotteryRecord Model
```csharp
public class SnLotteryRecord : ModelBase
{
    public Guid Id { get; set; }
    public DateTime DrawDate { get; set; }
    public List<int> WinningRegionOneNumbers { get; set; } = new(); // 5 winning numbers
    public int WinningRegionTwoNumber { get; set; }                   // Winning special number
    public int TotalTickets { get; set; }                             // Total tickets processed
    public int TotalPrizesAwarded { get; set; }                       // Number of winning tickets
    public long TotalPrizeAmount { get; set; }                        // Total ISP prize amount
}
```

## Prize Structure

| Region 1 Matches | Base Prize (ISP) | Notes |
|-----------------|------------------|-------|
| 0 | 0 | No prize |
| 1 | 10 | Minimum win |
| 2 | 20 | Double minimum |
| 3 | 50 | Five times minimum |
| 4 | 100 | Ten times minimum |
| 5 | 1000 | Maximum prize |

**Special Number Bonus**: If Region 2 number matches, multiply any prize by 10x.

## API Endpoints

All endpoints require authentication via Bearer token.

### Purchase Ticket
**POST** `/api/lotteries`

Creates a lottery order and deducts ISP from user's wallet.

**Request Body:**
```json
{
  "RegionOneNumbers": [5, 23, 47, 68, 89],
  "RegionTwoNumber": 42,
  "Multiplier": 1
}
```

**Response:**
```json
{
  "id": "guid",
  "accountId": "guid",
  "createdAt": "2025-10-24T00:00:00Z",
  "status": "Paid",
  "currency": "isp",
  "amount": 10,
  "productIdentifier": "lottery"
}
```

**Validation Rules:**
- `RegionOneNumbers`: Exactly 5 unique integers between 0-99
- `RegionTwoNumber`: Single integer between 0-99
- `Multiplier`: Integer ≥ 1
- User can only purchase 1 ticket per day

**Pricing:**
- Base cost: 10 ISP
- Additional cost: (Multiplier - 1) × 10 ISP
- Total cost = (Multiplier × 10) ISP

### Get User Tickets
**GET** `/api/lotteries`

Retrieves user's lottery tickets with pagination.

**Query Parameters:**
- `offset` (optional, default 0): Page offset
- `limit` (optional, default 20, max 100): Items per page

**Response:**
```json
[
  {
    "id": "guid",
    "regionOneNumbers": [5, 23, 47, 68, 89],
    "regionTwoNumber": 42,
    "multiplier": 1,
    "drawStatus": "Pending",
    "drawDate": null,
    "createdAt": "2025-10-24T10:30:00Z"
  }
]
```

**Response Headers:**
```
X-Total: 42  // Total number of user's tickets
```

### Get Specific Ticket
**GET** `/api/lotteries/{id}`

Retrieves a specific lottery ticket by ID.

**Response:**
Same structure as individual items from Get User Tickets.

**Error Responses:**
- `404 Not Found`: Ticket doesn't exist or user doesn't own it

### Get Lottery Records
**GET** `/api/lotteries/records`

Retrieves historical lottery draw results.

**Query Parameters:**
- `startDate` (optional): Filter by draw date (YYYY-MM-DD)
- `endDate` (optional): Filter by draw date (YYYY-MM-DD)
- `offset` (optional, default 0): Page offset
- `limit` (optional, default 20): Items per page

**Response:**
```json
[
  {
    "id": "guid",
    "drawDate": "2025-10-24T00:00:00Z",
    "winningRegionOneNumbers": [7, 15, 23, 46, 82],
    "winningRegionTwoNumber": 19,
    "totalTickets": 245,
    "totalPrizesAwarded": 23,
    "totalPrizeAmount": 4820
  }
]
```

## Integration Examples

### Frontend Integration (JavaScript/React)

```javascript
// Purchase a lottery ticket
async function purchaseLottery(numbers, specialNumber, multiplier = 1) {
  try {
    const response = await fetch('/api/lotteries', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${userToken}`
      },
      body: JSON.stringify({
        RegionOneNumbers: numbers,      // Array of 5 unique numbers 0-99
        RegionTwoNumber: specialNumber, // Number 0-99
        Multiplier: multiplier          // Optional, defaults to 1
      })
    });

    const order = await response.json();

    if (response.ok) {
      console.log('Ticket purchased successfully!', order);
      // Refresh user ISP balance
      updateWalletBalance();
    } else {
      console.error('Purchase failed:', order);
    }
  } catch (error) {
    console.error('Network error:', error);
  }
}

// Get user's tickets
async function getUserTickets() {
  try {
    const response = await fetch('/api/lotteries?limit=20', {
      headers: {
        'Authorization': `Bearer ${userToken}`
      }
    });

    const tickets = await response.json();
    const totalTickets = response.headers.get('X-Total');

    return { tickets, total: parseInt(totalTickets) };
  } catch (error) {
    console.error('Error fetching tickets:', error);
  }
}

// Get draw history
async function getDrawHistory() {
  try {
    const response = await fetch('/api/lotteries/records', {
      headers: {
        'Authorization': `Bearer ${userToken}`
      }
    });

    return await response.json();
  } catch (error) {
    console.error('Error fetching history:', error);
  }
}
```

### Mobile Integration (React Native/TypeScript)

```typescript
interface LotteryTicket {
  id: string;
  regionOneNumbers: number[];
  regionTwoNumber: number;
  multiplier: number;
  drawStatus: 'Pending' | 'Drawn';
  drawDate?: string;
  createdAt: string;
}

interface PurchaseRequest {
  RegionOneNumbers: number[];
  RegionTwoNumber: number;
  Multiplier: number;
}

class LotteryService {
  private apiUrl = 'https://your-api-domain.com/api/lotteries';

  async purchaseTicket(
    ticket: Omit<PurchaseRequest, 'RegionOneNumbers'> & { numbers: number[] },
    token: string
  ): Promise<any> {
    const request: PurchaseRequest = {
      RegionOneNumbers: ticket.numbers,
      RegionTwoNumber: ticket.RegionTwoNumber,
      Multiplier: ticket.Multiplier
    };

    const response = await fetch(this.apiUrl, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify(request)
    });

    return response.json();
  }

  async getTickets(token: string, offset = 0, limit = 20): Promise<LotteryTicket[]> {
    const response = await fetch(`${this.apiUrl}?offset=${offset}&limit=${limit}`, {
      headers: { 'Authorization': `Bearer ${token}` }
    });

    return response.json();
  }

  async getDrawRecords(token: string): Promise<any[]> {
    const response = await fetch(`${this.apiUrl}/records`, {
      headers: { 'Authorization': `Bearer ${token}` }
    });

    return response.json();
  }
}
```

### Number Validation

```javascript
function validateLotteryNumbers(numbers, specialNumber, multiplier = 1) {
  // Validate region one numbers
  if (!Array.isArray(numbers) || numbers.length !== 5) {
    return { valid: false, error: 'Must select exactly 5 numbers' };
  }

  const uniqueNumbers = new Set(numbers);
  if (uniqueNumbers.size !== 5) {
    return { valid: false, error: 'Numbers must be unique' };
  }

  // Check range 0-99
  for (const num of numbers) {
    if (!Number.isInteger(num) || num < 0 || num > 99) {
      return { valid: false, error: 'Numbers must be integers between 0-99' };
    }
  }

  // Validate special number
  if (!Number.isInteger(specialNumber) || specialNumber < 0 || specialNumber > 99) {
    return { valid: false, error: 'Special number must be between 0-99' };
  }

  // Validate multiplier
  if (!Number.isInteger(multiplier) || multiplier < 1) {
    return { valid: false, error: 'Multiplier must be 1 or greater' };
  }

  return { valid: true };
}

// Example usage
const validation = validateLotteryNumbers([5, 12, 23, 47, 89], 42, 2);
if (!validation.valid) {
  console.error(validation.error);
}
```

## Daily Draw Schedule

- **Draw Time**: Every midnight UTC (00:00 UTC)
- **Processing**: Only tickets from the previous day are included
- **Prize Distribution**: Winners automatically receive ISP credits
- **History**: Draws are preserved indefinitely

## Error Handling

### Common Error Codes
- `400 Bad Request`: Invalid request data (bad numbers, duplicate purchase, etc.)
- `401 Unauthorized`: Missing or invalid authentication token
- `404 Not Found`: Ticket doesn't exist or access denied
- `403 Forbidden`: Insufficient permissions (admin endpoints)

### Error Response Format
```json
{
  "message": "You can only purchase one lottery per day.",
  "type": "ArgumentException",
  "statusCode": 400
}
```

## Testing Guidelines

### Test Cases
1. **Valid Purchase**: Select valid numbers, verify wallet deduction
2. **Invalid Numbers**: Try duplicate region one numbers, out-of-range values
3. **Daily Limit**: Attempt second purchase in same day
4. **Insufficient Funds**: Try purchase without enough ISP
5. **Draw Processing**: Verify winning tickets receive correct prizes
6. **Historical Data**: Check draw records match processed tickets

### Test Data Examples
```javascript
// Valid ticket
{ numbers: [1, 15, 23, 67, 89], special: 42, multiplier: 1 }

// Invalid - duplicate numbers
{ numbers: [1, 15, 23, 15, 89], special: 42, multiplier: 1 }

// Invalid - out of range
{ numbers: [1, 15, 23, 67, 150], special: 42, multiplier: 1 }
```

## Support

For API integration questions or support:
- Check network documentation for authentication details
- Contact Dyson Network development team for assistance
- Monitor API response headers for pagination metadata
