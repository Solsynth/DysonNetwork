using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

/// <summary>
/// Produces publisher objects that are safe to embed in post payloads. A hand written field list
/// used to live in PostService, which silently dropped every field added later — that is how
/// fediverse mirrors lost their <see cref="SnPublisher.Picture"/> / <see cref="SnPublisher.Background"/>.
/// Keep this list in sync with the persisted members of SnPublisher.
/// </summary>
public static class PublisherSnapshot
{
    public static SnPublisher Clone(SnPublisher publisher)
    {
        return new SnPublisher
        {
            Id = publisher.Id,
            Type = publisher.Type,
            Name = publisher.Name,
            Nick = publisher.Nick,
            Bio = publisher.Bio,
            Picture = publisher.Picture,
            Background = publisher.Background,
            Verification = publisher.Verification,
            Meta = publisher.Meta,
            Metadata = publisher.Metadata,

            Uri = publisher.Uri,
            ActorType = publisher.ActorType,
            Username = publisher.Username,
            InstanceId = publisher.InstanceId,
            InstanceDomain = publisher.InstanceDomain,
            InboxUri = publisher.InboxUri,
            OutboxUri = publisher.OutboxUri,
            FollowersUri = publisher.FollowersUri,
            FollowingUri = publisher.FollowingUri,
            FeaturedUri = publisher.FeaturedUri,
            PublicKeyId = publisher.PublicKeyId,
            PublicKey = publisher.PublicKey,
            IsBot = publisher.IsBot,
            IsLocked = publisher.IsLocked,
            IsDiscoverable = publisher.IsDiscoverable,
            IsCommunity = publisher.IsCommunity,
            LastFetchedAt = publisher.LastFetchedAt,
            LastActivityAt = publisher.LastActivityAt,
            OutboxFetchedAt = publisher.OutboxFetchedAt,
            FollowersCount = publisher.FollowersCount,
            FollowingCount = publisher.FollowingCount,
            PostCount = publisher.PostCount,
            TotalPostCount = publisher.TotalPostCount,

            AccountId = publisher.AccountId,
            RealmId = publisher.RealmId,
            PayoutWalletId = publisher.PayoutWalletId,
            Realm = publisher.Realm,
            Account = publisher.Account,
            RealmNick = publisher.RealmNick,
            RealmBio = publisher.RealmBio,
            RealmExperience = publisher.RealmExperience,
            RealmLevel = publisher.RealmLevel,
            RealmLevelingProgress = publisher.RealmLevelingProgress,
            RealmLabel = publisher.RealmLabel,

            ShadowbanReason = publisher.ShadowbanReason,
            ShadowbannedAt = publisher.ShadowbannedAt,
            GatekeptFollows = publisher.GatekeptFollows,
            ModerateSubscription = publisher.ModerateSubscription,
            Rating = publisher.Rating,
            CreatedAt = publisher.CreatedAt,
            UpdatedAt = publisher.UpdatedAt,
            DeletedAt = publisher.DeletedAt,
        };
    }
}
