# Gift Subscriptions API Documentation

## Overview

The Gift Subscriptions feature allows users to purchase subscription gifts that can be redeemed by other users, enabling social gifting and subscription sharing within the DysonNetwork platform.

If you use it through the gateway, the `/api` should be replaced with the `/id`

### Key Features

- **Purchase Gifts**: Users can buy subscriptions as gifts for specific recipients or as open gifts
- **Gift Codes**: Each gift has a unique redemption code
- **Flexible Redemption**: Open gifts can be redeemed by anyone, while targeted gifts are recipient-specific
- **Security**: Prevents duplicate subscriptions and enforces account level requirements
- **Integration**: Full integration with existing subscription, coupon, and pricing systems
- **Clean User Experience**: Unpaid gifts are hidden from users and automatically cleaned up
- **Automatic Maintenance**: Old unpaid gifts are removed after 24 hours

## API Endpoints

All endpoints are authenticated and require a valid user session. The base path for gift endpoints is `/api/gifts`.

### 1. List Sent Gifts

Retrieve gifts you have purchased.

```http
GET /api/gifts/sent?offset=0&take=20
Authorization: Bearer <token>
```

**Response**: Array of `SnWalletGift` objects

### 2. List Received Gifts

Retrieve gifts sent to you or redeemed by you (for open gifts).

```http
GET /api/gifts/received?offset=0&take=20
Authorization: Bearer <token>
```

**Response**: Array of `SnWalletGift` objects

### 3. Get Specific Gift

Retrieve details for a specific gift.

```http
GET /api/gifts/{giftId}
Authorization: Bearer <token>
```

**Parameters**:
- `giftId`: GUID of the gift

**Response**: `SnWalletGift` object

### 4. Check Gift Code

Validate if a gift code can be redeemed by the current user.

```http
GET /api/gifts/check/{giftCode}
Authorization: Bearer <token>
```

**Response**:
```json
{
  "gift_code": "ABCD1234EFGH",
  "subscription_identifier": "basic",
  "can_redeem": true,
  "error": null,
  "message": "Happy birthday!"
}
```

### 5. Purchase a Gift

Create and purchase a gift subscription.

```http
POST /api/gifts/purchase
Authorization: Bearer <token>
Content-Type: application/json

{
  "subscription_identifier": "premium",
  "recipient_id": "550e8400-e29b-41d4-a716-446655440000",  // Optional: null for open gifts
  "payment_method": "in_app_wallet",
  "payment_details": {
    "currency": "irl"
  },
  "message": "Enjoy your premium subscription!",  // Optional
  "coupon": "SAVE20",  // Optional
  "gift_duration_days": 30,  // Optional: defaults to 30
  "subscription_duration_days": 30  // Optional: defaults to 30
}
```

**Response**: `SnWalletGift` object

### 6. Redeem a Gift

Redeem a gift code to create a subscription for yourself.

```http
POST /api/gifts/redeem
Authorization: Bearer <token>
Content-Type: application/json

{
  "gift_code": "ABCD1234EFGH"
}
```

**Response**:
```json
{
  "gift": { ... },
  "subscription": { ... }
}
```

### 7. Mark Gift as Sent

Mark a gift as sent (ready for redemption).

```http
POST /api/gifts/{giftId}/send
Authorization: Bearer <token>
```

**Parameters**:
- `giftId`: GUID of the gift to mark as sent

### 8. Cancel a Gift

Cancel a gift before it has been redeemed.

```http
POST /api/gifts/{giftId}/cancel
Authorization: Bearer <token>
```

**Parameters**:
- `giftId`: GUID of the gift to cancel

## Usage Examples

### Client Implementation

Here are examples showing how to integrate gift subscriptions into your client application.

#### Example 1: Purchase a Gift for a Specific User

```javascript
async function purchaseGiftForFriend(subscriptionId, friendId, message) {
  const response = await fetch('/api/gifts/purchase', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      subscription_identifier: subscriptionId,
      recipient_id: friendId,
      payment_method: 'in_app_wallet',
      payment_details: { currency: 'irl' },
      message: message
    })
  });

  const gift = await response.json();
  return gift.gift_code; // Share this code with the friend
}
```

#### Example 2: Create an Open Gift

```javascript
async function createOpenGift(subscriptionId) {
  const response = await fetch('/api/gifts/purchase', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      subscription_identifier: subscriptionId,
      payment_method: 'in_app_wallet',
      payment_details: { currency: 'irl' },
      message: 'Redeem this anywhere!'
      // No recipient_id makes it an open gift
    })
  });

  const gift = await response.json();
  // Mark as sent to make it redeemable
  await markGiftAsSent(gift.id);
  return gift;
}
```

#### Example 3: Redeem a Gift Code

```javascript
async function redeemGiftCode(giftCode) {
  // First, check if the gift can be redeemed
  const checkResponse = await fetch(`/api/gifts/check/${giftCode}`, {
    headers: {
      'Authorization': `Bearer ${token}`
    }
  });

  const checkResult = await checkResponse.json();

  if (!checkResult.canRedeem) {
    throw new Error(checkResult.error);
  }

  // If valid, redeem it
  const redeemResponse = await fetch('/api/gifts/redeem', {
    method: 'POST',
    headers: {
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify({
      gift_code: giftCode
    })
  });

  const result = await redeemResponse.json();
  return result.subscription; // The newly created subscription
}
```

#### Example 4: Display User's Gift History

```javascript
async function getGiftHistory() {
  // Get gifts I sent
  const sentResponse = await fetch('/api/gifts/sent', {
    headers: { 'Authorization': `Bearer ${token}` }
  });
  const sentGifts = await sentResponse.json();

  // Get gifts I received
  const receivedResponse = await fetch('/api/gifts/received', {
    headers: { 'Authorization': `Bearer ${token}` }
  });
  const receivedGifts = await receivedResponse.json();

  return { sent: sentGifts, received: receivedGifts };
}
```

## Gift Status Lifecycle

Gifts follow this status lifecycle:

1. **Created**: Initially purchased, can be cancelled or marked as sent
   - **Note**: Gifts in "Created" status are not visible to users and are automatically cleaned up after 24 hours if unpaid
2. **Sent**: Made available for redemption, can be cancelled
3. **Redeemed**: Successfully redeemed, creates a subscription
4. **Cancelled**: Permanently cancelled, refund may be processed
5. **Expired**: Expired without redemption

## Automatic Maintenance

The system includes automatic cleanup to maintain data integrity:

- **Unpaid Gift Cleanup**: Gifts that remain in "Created" status (unpaid) for more than 24 hours are automatically removed from the database
- **User Visibility**: Only gifts that have been successfully paid and sent are visible in user gift lists
- **Background Processing**: Cleanup runs hourly via scheduled jobs

This ensures a clean user experience while preventing accumulation of abandoned gift purchases.

## Validation Rules

### Purchase Validation
- Subscription must exist and be valid
- If coupon provided, it must be valid and applicable
- Recipient account must exist (if specified)
- User must meet level requirements for the subscription

### Redemption Validation
- Gift code must exist
- Gift must be in "Sent" status
- Gift must not be expired
- User must meet level requirements
- User must not already have an active subscription of the same type
- For targeted gifts, user must be the specified recipient

## Pricing & Payments

Gifts use the same pricing system as regular subscriptions:

- Base price from subscription template
- Coupon discounts applied
- Currency conversion as needed
- Payment processing through existing payment methods

## Notification Events

The system sends push notifications for:

- **gifts.redeemed**: When someone redeems your gift
- **gifts.claimed**: When the recipient redeems your targeted gift

Notifications include gift and subscription details for rich UI updates.

## Error Handling

Common error responses:

- `400 Bad Request`: Invalid parameters, validation failures
- `401 Unauthorized`: Missing or invalid authentication
- `403 Forbidden`: Insufficient permissions
- `404 Not Found`: Gift or subscription not found
- `409 Conflict`: Business logic violations (duplicate subscriptions, etc.)

## Integration Notes

### Database Schema
The feature adds a `wallet_gifts` table with relationships to:
- `accounts` (gifter, recipient, redeemer)
- `wallet_subscriptions` (created subscription)
- `wallet_coupons` (applied discounts)

### Backwards Compatibility
- No changes to existing subscription endpoints
- New gift-related endpoints are additive
- Existing payment flows remain unchanged

### Performance Considerations
- Gift codes are indexed for fast lookups
- Status filters optimize database queries
- Caching integrated with existing subscription caching

## Support

For implementation questions or issues, refer to the DysonNetwork API documentation or contact the development team.
