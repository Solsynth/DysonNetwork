namespace DysonNetwork.Shared.Models;

public abstract class WebSocketPacketType
{
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Error = "error";
    public const string MessageNew = "messages.new";
    public const string MessageDelivered = "messages.delivered";
    public const string MessageUpdate = "messages.update";
    public const string MessageDelete = "messages.delete";
    public const string MessageReactionAdded = "messages.reaction.added";
    public const string MessageReactionRemoved = "messages.reaction.removed";
    public const string MessagePinned = "messages.pinned";
    public const string MessageUnpinned = "messages.unpinned";
    public const string MessageRead = "messages.read";
    public const string CallParticipantsUpdate = "call.participants.update";
    public const string ProgressionCompleted = "progression.completed";
    public const string AccountStatusUpdated = "account.status.updated";
    public const string ChatPresenceUpdated = "chat.presence.updated";
    public const string AccountPresenceActivitiesUpdated = "account.presence.activities.updated";
    public const string ChatPresenceActivitiesUpdated = "chat.presence.activities.updated";
    public const string AuthChallengePending = "auth.challenge.pending";
    public const string AuthChallengeApproved = "auth.challenge.approved";
    public const string AuthChallengeDeclined = "auth.challenge.declined";
    public const string PlaceholderUpdate = "messages.placeholder.update";
    public const string PlaceholderFinalize = "messages.placeholder.finalize";
    public const string PlaceholderExpired = "messages.placeholder.expired";
    public const string WalletTransactionCreated = "wallet.transaction.created";
    public const string WalletTransactionConfirmed = "wallet.transaction.confirmed";
    public const string WalletTransactionRefunded = "wallet.transaction.refunded";
    public const string WalletTransactionExpired = "wallet.transaction.expired";
    public const string WalletPocketUpdated = "wallet.pocket.updated";
    public const string WalletFundContributed = "wallet.fund.contributed";
    public const string WalletFundCompleted = "wallet.fund.completed";
    public const string QrLoginScanned = "auth.qr.scanned";
    public const string QrLoginApproved = "auth.qr.approved";
    public const string QrLoginDeclined = "auth.qr.declined";
}
