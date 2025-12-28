# Wallet Funds (Red Packet) System

## Overview

The Wallet Funds system implements red packet functionality for the DysonNetwork platform, allowing users to create funds that can be split among multiple recipients. Recipients must explicitly claim their portion, and unclaimed funds are automatically refunded after expiration.

## Features

- **Red Packet Creation**: Users can create funds with total amounts to be distributed
- **Split Types**: Even distribution or random (lucky draw) splitting
- **Claim System**: Recipients must actively claim their portion
- **Expiration**: Automatic refund of unclaimed funds after 24 hours
- **Multi-Recipient**: Support for distributing to multiple users simultaneously
- **Audit Trail**: Full transaction history and status tracking

## Architecture

### Models

#### SnWalletFund
```csharp
public class SnWalletFund : ModelBase
{
    public Guid Id { get; set; }
    public string Currency { get; set; }
    public decimal TotalAmount { get; set; }
    public FundSplitType SplitType { get; set; }
    public FundStatus Status { get; set; }
    public string? Message { get; set; }
    public Guid CreatorAccountId { get; set; }
    public SnAccount CreatorAccount { get; set; }
    public ICollection<SnWalletFundRecipient> Recipients { get; set; }
    public Instant ExpiredAt { get; set; }
}
```

#### SnWalletFundRecipient
```csharp
public class SnWalletFundRecipient : ModelBase
{
    public Guid Id { get; set; }
    public Guid FundId { get; set; }
    public SnWalletFund Fund { get; set; }
    public Guid RecipientAccountId { get; set; }
    public SnAccount RecipientAccount { get; set; }
    public decimal Amount { get; set; }
    public bool IsReceived { get; set; }
    public Instant? ReceivedAt { get; set; }
}
```

### Enums

#### FundSplitType
- `Even`: Equal distribution among all recipients
- `Random`: Random amounts that sum to total

#### FundStatus
- `Created`: Fund created, waiting for claims
- `PartiallyReceived`: Some recipients have claimed
- `FullyReceived`: All recipients have claimed
- `Expired`: Fund expired, unclaimed amounts refunded
- `Refunded`: Fund was refunded (legacy status)

## API Endpoints

### Create Fund
**POST** `/api/wallets/funds`

Creates a new fund (red packet) for distribution among recipients.

**Request Body:**
```json
{
  "recipientAccountIds": ["uuid1", "uuid2", "uuid3"],
  "currency": "points",
  "totalAmount": 100.00,
  "splitType": "Even",
  "message": "Happy Birthday! 🎉",
  "expirationHours": 48
}
```

**Response:** `SnWalletFund` object

**Authorization:** Required (authenticated user becomes the creator)

---

### Get Funds
**GET** `/api/wallets/funds`

Retrieves funds that the authenticated user is involved in (as creator or recipient).

**Query Parameters:**
- `offset` (int, optional): Pagination offset (default: 0)
- `take` (int, optional): Number of items to return (default: 20)
- `status` (FundStatus, optional): Filter by fund status

**Response:** Array of `SnWalletFund` objects with `X-Total` header

**Authorization:** Required

---

### Get Fund
**GET** `/api/wallets/funds/{id}`

Retrieves details of a specific fund.

**Path Parameters:**
- `id` (Guid): Fund ID

**Response:** `SnWalletFund` object with recipients

**Authorization:** Required (user must be creator or recipient)

---

### Receive Fund
**POST** `/api/wallets/funds/{id}/receive`

Claims the authenticated user's portion of a fund.

**Path Parameters:**
- `id` (Guid): Fund ID

**Response:** `SnWalletTransaction` object

**Authorization:** Required (user must be a recipient)

## Service Methods

### Creating a Fund

```csharp
// Service method
public async Task<SnWalletFund> CreateFundAsync(
    Guid creatorAccountId,
    List<Guid> recipientAccountIds,
    string currency,
    decimal totalAmount,
    FundSplitType splitType,
    string? message = null,
    Duration? expiration = null)
```

**Parameters:**
- `creatorAccountId`: Account ID of the fund creator
- `recipientAccountIds`: List of recipient account IDs
- `currency`: Currency type (e.g., "points", "golds")
- `totalAmount`: Total amount to distribute
- `splitType`: How to split the amount (Even/Random)
- `message`: Optional message for the fund
- `expiration`: Optional expiration duration (default: 24 hours)

**Example:**
```csharp
var fund = await paymentService.CreateFundAsync(
    creatorId: userId,
    recipientAccountIds: new List<Guid> { friend1Id, friend2Id, friend3Id },
    currency: "points",
    totalAmount: 100.00m,
    splitType: FundSplitType.Even,
    message: "Happy New Year!",
    expiration: Duration.FromHours(48) // Optional: 48 hours instead of default 24
);
```

### Claiming a Fund

```csharp
// Service method
public async Task<SnWalletTransaction> ReceiveFundAsync(
    Guid recipientAccountId,
    Guid fundId)
```

**Parameters:**
- `recipientAccountId`: Account ID of the recipient claiming the fund
- `fundId`: ID of the fund to claim from

**Example:**
```csharp
var transaction = await paymentService.ReceiveFundAsync(
    recipientAccountId: myAccountId,
    fundId: fundId
);
```

## Split Logic

### Even Split
Distributes the total amount equally among all recipients, handling decimal precision properly:

```csharp
private List<decimal> SplitEvenly(decimal totalAmount, int recipientCount)
{
    var baseAmount = Math.Floor(totalAmount / recipientCount * 100) / 100;
    var remainder = totalAmount - (baseAmount * recipientCount);

    var amounts = new List<decimal>();
    for (int i = 0; i < recipientCount; i++)
    {
        var amount = baseAmount;
        if (i < remainder * 100)
            amount += 0.01m; // Distribute remainder as 0.01 increments
        amounts.Add(amount);
    }
    return amounts;
}
```

**Example:** 100.00 split among 3 recipients = [33.34, 33.33, 33.33]

### Random Split
Generates random amounts that sum exactly to the total:

```csharp
private List<decimal> SplitRandomly(decimal totalAmount, int recipientCount)
{
    var random = new Random();
    var amounts = new List<decimal>();
    decimal remaining = totalAmount;

    for (int i = 0; i < recipientCount - 1; i++)
    {
        var maxAmount = remaining - (recipientCount - i - 1) * 0.01m;
        var minAmount = 0.01m;
        var amount = Math.Round((decimal)random.NextDouble() * (maxAmount - minAmount) + minAmount, 2);
        amounts.Add(amount);
        remaining -= amount;
    }

    amounts.Add(Math.Round(remaining, 2)); // Last recipient gets remainder
    return amounts;
}
```

**Example:** 100.00 split randomly among 3 recipients = [45.67, 23.45, 30.88]

## Expiration and Refunds

### Automatic Processing
Funds are processed hourly by the `FundExpirationJob`:

```csharp
public async Task ProcessExpiredFundsAsync()
{
    var now = SystemClock.Instance.GetCurrentInstant();
    var expiredFunds = await db.WalletFunds
        .Include(f => f.Recipients)
        .Where(f => f.Status == FundStatus.Created || f.Status == FundStatus.PartiallyReceived)
        .Where(f => f.ExpiredAt < now)
        .ToListAsync();

    foreach (var fund in expiredFunds)
    {
        var unclaimedAmount = fund.Recipients
            .Where(r => !r.IsReceived)
            .Sum(r => r.Amount);

        if (unclaimedAmount > 0)
        {
            // Refund to creator
            var creatorWallet = await wat.GetWalletAsync(fund.CreatorAccountId);
            if (creatorWallet != null)
            {
                await CreateTransactionAsync(
                    payerWalletId: null,
                    payeeWalletId: creatorWallet.Id,
                    currency: fund.Currency,
                    amount: unclaimedAmount,
                    remarks: $"Refund for expired fund {fund.Id}",
                    type: TransactionType.System,
                    silent: true
                );
            }
        }

        fund.Status = FundStatus.Expired;
    }

    await db.SaveChangesAsync();
}
```

### Expiration Rules
- Default expiration: 24 hours from creation
- Custom expiration can be set when creating the fund
- Only funds with status `Created` or `PartiallyReceived` are processed
- Unclaimed amounts are refunded to the creator
- Fund status changes to `Expired`

## Security & Validation

### Creation Validation
- Creator must have sufficient funds
- All recipient accounts must exist and have wallets
- At least one recipient required
- Total amount must be positive
- Creator cannot be a recipient (self-transfer not allowed)

### Claim Validation
- Fund must exist and not be expired/refunded
- Recipient must be in the recipient list
- Recipient can only claim once
- Recipient must have a valid wallet

### Error Handling
- `ArgumentException`: Invalid parameters
- `InvalidOperationException`: Business logic violations
- All errors provide descriptive messages

## Database Schema

### wallet_funds
```sql
CREATE TABLE wallet_funds (
    id UUID PRIMARY KEY,
    currency VARCHAR(128) NOT NULL,
    total_amount DECIMAL NOT NULL,
    split_type INTEGER NOT NULL,
    status INTEGER NOT NULL,
    message TEXT,
    creator_account_id UUID NOT NULL,
    expired_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    deleted_at TIMESTAMPTZ
);
```

### wallet_fund_recipients
```sql
CREATE TABLE wallet_fund_recipients (
    id UUID PRIMARY KEY,
    fund_id UUID NOT NULL REFERENCES wallet_funds(id),
    recipient_account_id UUID NOT NULL,
    amount DECIMAL NOT NULL,
    is_received BOOLEAN NOT NULL DEFAULT FALSE,
    received_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    deleted_at TIMESTAMPTZ
);
```

## Integration Points

### Wallet System
- Funds are deducted from creator's wallet pocket immediately upon creation
- Individual claims credit recipient's wallet pocket
- Refunds credit creator's wallet pocket
- All operations create audit transactions

### Notification System
- Integrates with existing push notification system
- Notifications sent for fund creation and claims
- Uses localized messages for different languages

### Scheduled Jobs
- `FundExpirationJob` runs every hour
- Processes expired funds automatically
- Handles refunds and status updates

## Usage Examples

### Red Packet for Group Event
```csharp
// Create a red packet for 5 friends totaling 500 points
var fund = await paymentService.CreateFundAsync(
    creatorId,
    friendIds, // List of 5 friend account IDs
    "points",
    500.00m,
    FundSplitType.Random, // Lucky draw
    "Happy Birthday! 🎉"
);
```

### Equal Split Bonus Distribution
```csharp
// Distribute bonus equally among team members
var fund = await paymentService.CreateFundAsync(
    managerId,
    teamMemberIds,
    "golds",
    1000.00m,
    FundSplitType.Even,
    "Monthly performance bonus"
);
```

### Claiming a Fund
```csharp
// User claims their portion
try
{
    var transaction = await paymentService.ReceiveFundAsync(userId, fundId);
    // Success - funds credited to user's wallet
}
catch (InvalidOperationException ex)
{
    // Handle error (already claimed, expired, not recipient, etc.)
}
```

## Monitoring & Maintenance

### Key Metrics
- Total funds created per period
- Claim rate (claimed vs expired)
- Average expiration time
- Popular split types

### Cleanup
- Soft-deleted records are cleaned up by `AppDatabaseRecyclingJob`
- Expired funds are processed by `FundExpirationJob`
- No manual intervention required for normal operation

## Future Enhancements

- **Fund Templates**: Pre-configured fund types
- **Recurring Funds**: Scheduled fund distributions
- **Fund Analytics**: Detailed usage statistics
- **Fund Categories**: Tagging and categorization
- **Bulk Operations**: Create funds for multiple groups
- **Fund Forwarding**: Allow recipients to forward unclaimed portions
