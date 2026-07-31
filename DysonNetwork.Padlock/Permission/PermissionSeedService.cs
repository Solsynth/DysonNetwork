using System.Text.Json;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Permission;

public class PermissionSeedService(
    AppDatabase db,
    ILogger<PermissionSeedService> logger)
{
    public const string DefaultGroupKey = "default";
    public const string VerifiedGroupKey = "verified";
    public const string ModeratorGroupKey = "moderator";
    public const string DeveloperGroupKey = "developer";

    private static readonly IReadOnlySet<string> DefaultPermissionKeys = new HashSet<string>
    {
        PermissionKeys.TestsTake,
        PermissionKeys.AccountsConnectionsView,
        PermissionKeys.ChatCreate,
        PermissionKeys.ChatUpdate,
        PermissionKeys.ChatDelete,
        PermissionKeys.ChatMessagesCreate,
        PermissionKeys.ChatMessagesUpdate,
        PermissionKeys.ChatMessagesDelete,
        PermissionKeys.ChatMessagesReact,
        PermissionKeys.ChatMembersManage,
        PermissionKeys.ChatMembersTimeout,
        PermissionKeys.ChatMembersKick,
        PermissionKeys.ChatInvitesManage,
        PermissionKeys.ChatE2eeManage,
        PermissionKeys.ChatSync,
        PermissionKeys.ChatCallStart,
        PermissionKeys.ChatCallEnd,
        PermissionKeys.ChatCallInvite,
        PermissionKeys.ChatCallKick,
        PermissionKeys.ChatCallMute,
        PermissionKeys.ChatGroupsManage,
        PermissionKeys.ChatPinsManage,
        PermissionKeys.NotificationsPut,
        PermissionKeys.NotificationsReadAll,
        PermissionKeys.NotificationsPreferencesManage,
        PermissionKeys.NotificationsSubscriptionsManage,
        PermissionKeys.WalletsCreate,
        PermissionKeys.OrdersCreate,
        PermissionKeys.OrdersUpdate,
        PermissionKeys.OrdersPay,
        PermissionKeys.OrdersView,
        PermissionKeys.SubscriptionsCreate,
        PermissionKeys.SubscriptionsCancel,
        PermissionKeys.SubscriptionsCheckout,
        PermissionKeys.SubscriptionGiftsPurchase,
        PermissionKeys.SubscriptionGiftsRedeem,
        PermissionKeys.SubscriptionGiftsSend,
        PermissionKeys.SubscriptionGiftsCancel,
        PermissionKeys.AuthSessionsManage,
        PermissionKeys.AuthFactorsManage,
        PermissionKeys.AuthApiKeysManage,
        PermissionKeys.AuthAppsAuthorize,
        PermissionKeys.AuthRecover,
        PermissionKeys.AccountContactsManage,
        PermissionKeys.AccountDevicesManage,
        PermissionKeys.AccountAuthorizedAppsManage,
        PermissionKeys.E2eeKeysManage,
        PermissionKeys.E2eeMlsManage,
        PermissionKeys.E2eeDevicesManage,
        PermissionKeys.ChatReadAll,
        PermissionKeys.AccountsStatusesCreate,
        PermissionKeys.AccountsStatusesUpdate,
        PermissionKeys.NfcTagsCreate,
        PermissionKeys.NfcTagsUpdate,
        PermissionKeys.NfcTagsDelete,
        PermissionKeys.NfcTagsClaim,
        PermissionKeys.NfcTagsLock,
        PermissionKeys.CalendarEventsCreate,
        PermissionKeys.CalendarEventsUpdate,
        PermissionKeys.CalendarEventsDelete,
        PermissionKeys.CalendarSubscriptionsManage,
        PermissionKeys.CalendarCheckinManage,
        PermissionKeys.StickersPacksCreate,
        PermissionKeys.StickersPacksUpdate,
        PermissionKeys.StickersPacksDelete,
        PermissionKeys.StickersPacksOwn,
        PermissionKeys.StickersPacksOrder,
        PermissionKeys.StickersCreate,
        PermissionKeys.StickersUpdate,
        PermissionKeys.StickersDelete,
        PermissionKeys.StickersContentUpdate,
        PermissionKeys.SurveysCreate,
        PermissionKeys.SurveysUpdate,
        PermissionKeys.SurveysDelete,
        PermissionKeys.SurveysPublish,
        PermissionKeys.SurveysArchive,
        PermissionKeys.SurveysClone,
        PermissionKeys.NotableDaysCreate,
        PermissionKeys.NotableDaysUpdate,
        PermissionKeys.NotableDaysDelete
    };

    private static readonly IReadOnlySet<string> VerifiedPermissionKeys = new HashSet<string>
    {
        PermissionKeys.PostsView,
        PermissionKeys.PostsCreateBlog,
        PermissionKeys.PostsCreate,
        PermissionKeys.PostsUpdate,
        PermissionKeys.PostsDelete,
        PermissionKeys.PostsPublish,
        PermissionKeys.PostsReact,
        PermissionKeys.PostsBoost,
        PermissionKeys.PostsBookmark,
        PermissionKeys.PostsAward,
        PermissionKeys.PostsSponsor,
        PermissionKeys.PostsPin,
        PermissionKeys.PostsBatchDelete,
        PermissionKeys.PostsBatchVisibility,
        PermissionKeys.PostCollectionsCreate,
        PermissionKeys.PostCollectionsUpdate,
        PermissionKeys.PostCollectionsDelete,
        PermissionKeys.PostCollectionsPostsManage,
        PermissionKeys.PostCategoriesSubscribe,
        PermissionKeys.PostsTagsCreate,
        PermissionKeys.PostsTagsUpdate,
        PermissionKeys.PostsTagsDelete,
        PermissionKeys.PostsTagsAssign,
        PermissionKeys.PostsTagsClaim,
        PermissionKeys.PostsTagsEvent,
        PermissionKeys.PostSubscriptionsManage,
        PermissionKeys.PublishersCreate,
        PermissionKeys.PublishersUpdate,
        PermissionKeys.PublishersDelete,
        PermissionKeys.PublishersMembersManage,
        PermissionKeys.PublishersInvitesManage,
        PermissionKeys.PublishersFeaturesManage,
        PermissionKeys.PublishersFediverseManage,
        PermissionKeys.PublishersDomainsManage,
        PermissionKeys.PublishersSubscriptionsManage,
        PermissionKeys.TimelinesFeedback,
        PermissionKeys.SurveysAnswer,
        PermissionKeys.SurveysSubscribe,
        PermissionKeys.LiveStreamsCreate,
        PermissionKeys.LiveStreamsUpdate,
        PermissionKeys.LiveStreamsDelete,
        PermissionKeys.LiveStreamsStart,
        PermissionKeys.LiveStreamsEnd,
        PermissionKeys.LiveStreamsHls,
        PermissionKeys.LiveStreamsPin,
        PermissionKeys.LiveStreamsAwards,
        PermissionKeys.LiveStreamsThumbnail,
        PermissionKeys.AccountsProfileBoard,
        PermissionKeys.AccountsProfileBoardManage,
        PermissionKeys.AccountsBoardManage,
        PermissionKeys.PresencesScan,
        PermissionKeys.PresencesActivityManage,
        PermissionKeys.PresencesArtworkManage,
        PermissionKeys.RelationshipsCreate,
        PermissionKeys.RelationshipsUpdate,
        PermissionKeys.RelationshipsDelete,
        PermissionKeys.RelationshipsFriendsManage,
        PermissionKeys.RelationshipsBlockManage,
        PermissionKeys.RelationshipsMuteManage,
        PermissionKeys.RelationshipsCloseFriendsManage,
        PermissionKeys.RelationshipsAliasManage,
        PermissionKeys.RelationshipsSync,
        PermissionKeys.RealmsCreate,
        PermissionKeys.RealmsUpdate,
        PermissionKeys.RealmsDelete,
        PermissionKeys.RealmsInvitesManage,
        PermissionKeys.RealmsMembersManage,
        PermissionKeys.RealmsLabelsManage,
        PermissionKeys.RealmsBoostsManage,
        PermissionKeys.MeetCreate,
        PermissionKeys.MeetUpdate,
        PermissionKeys.MeetDelete,
        PermissionKeys.MeetComplete,
        PermissionKeys.MeetJoin,
        PermissionKeys.MeetPinManage,
        PermissionKeys.MeetVisibilityUpdate,
        PermissionKeys.LocationPinsCreate,
        PermissionKeys.LocationPinsUpdate,
        PermissionKeys.LocationPinsDelete,
        PermissionKeys.NearbyPresenceManage,
        PermissionKeys.NearbyResolve,
        PermissionKeys.RewindCreate
    };

    private static readonly IReadOnlySet<string> ModeratorPermissionKeys = new HashSet<string>
    {
        PermissionKeys.PostsModerate,
        PermissionKeys.PostsLock,
        PermissionKeys.RealmsModerate,
        PermissionKeys.TicketsCreate,
        PermissionKeys.TicketsUpdate,
        PermissionKeys.TicketsDelete,
        PermissionKeys.TicketsMessagesCreate,
        PermissionKeys.TicketsStatusUpdate,
        PermissionKeys.TicketsAssign
    };

    private static readonly IReadOnlySet<string> DeveloperPermissionKeys = new HashSet<string>
    {
        PermissionKeys.DevelopersCreate,
        PermissionKeys.DevelopersManage,
        PermissionKeys.CustomAppsCreate,
        PermissionKeys.CustomAppsUpdate,
        PermissionKeys.CustomAppsDelete,
        PermissionKeys.CustomAppsSecretsManage,
        PermissionKeys.BotAccountsCreate,
        PermissionKeys.BotAccountsUpdate,
        PermissionKeys.BotAccountsDelete,
        PermissionKeys.BotAccountsKeysManage,
        PermissionKeys.BotAccountsChatManage,
        PermissionKeys.AppProductsCreate,
        PermissionKeys.AppProductsUpdate,
        PermissionKeys.AppProductsDelete,
        PermissionKeys.DevProjectsCreate,
        PermissionKeys.DevProjectsUpdate,
        PermissionKeys.DevProjectsDelete,
        PermissionKeys.MiniAppsView,
        PermissionKeys.MiniAppsCreate,
        PermissionKeys.MiniAppsUpdate,
        PermissionKeys.MiniAppsDelete,
        PermissionKeys.MiniAppsPackageUpload
    };

    /// <summary>
    /// Synchronizes all keys defined in <see cref="PermissionKeys"/> into the default
    /// permission group. Missing keys are inserted; existing keys are skipped.
    /// Run at service startup so newly added PermissionKeys are always picked up.
    /// </summary>
    public async Task EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var legacyAllUsersGroup = await db.PermissionGroups.FirstOrDefaultAsync(x => x.Key == "all-users", cancellationToken);
        if (legacyAllUsersGroup is not null)
        {
            db.PermissionGroups.Remove(legacyAllUsersGroup);
            await db.SaveChangesAsync(cancellationToken);
        }

        var defaultGroup = await EnsureGroupAsync(DefaultGroupKey, DefaultPermissionKeys, cancellationToken);
        var verifiedGroup = await EnsureGroupAsync(VerifiedGroupKey, VerifiedPermissionKeys, cancellationToken);
        await EnsureGroupAsync(ModeratorGroupKey, ModeratorPermissionKeys, cancellationToken);
        await EnsureGroupAsync(DeveloperGroupKey, DeveloperPermissionKeys, cancellationToken);

        var accountActors = await db.Accounts.Select(x => x.Id.ToString()).ToListAsync(cancellationToken);
        await EnsureMembersAsync(defaultGroup.Id, accountActors, cancellationToken);
        var activatedActors = await db.Accounts.Where(x => x.ActivatedAt != null).Select(x => x.Id.ToString()).ToListAsync(cancellationToken);
        await EnsureMembersAsync(verifiedGroup.Id, activatedActors, cancellationToken);

        logger.LogInformation("Permission groups synchronized: {DefaultGroup}, {VerifiedGroup}, {ModeratorGroup}, and {DeveloperGroup}.", defaultGroup.Key, verifiedGroup.Key, ModeratorGroupKey, DeveloperGroupKey);
    }

    private async Task<SnPermissionGroup> EnsureGroupAsync(string key, IEnumerable<string> keys, CancellationToken cancellationToken)
    {
        var group = await db.PermissionGroups.Include(g => g.Nodes).FirstOrDefaultAsync(g => g.Key == key, cancellationToken);
        if (group is null)
        {
            group = new SnPermissionGroup { Key = key };
            db.PermissionGroups.Add(group);
            await db.SaveChangesAsync(cancellationToken);
        }
        var expected = keys.ToHashSet();
        var obsoleteNodes = group.Nodes.Where(x => !expected.Contains(x.Key)).ToList();
        db.PermissionNodes.RemoveRange(obsoleteNodes);
        var existing = group.Nodes.Select(x => x.Key).Except(obsoleteNodes.Select(x => x.Key)).ToHashSet();
        foreach (var permissionKey in keys.Except(existing))
        {
            var node = new SnPermissionNode { Actor = $"group:{key}", Type = PermissionNodeActorType.Group, Key = permissionKey, Value = JsonDocument.Parse(JsonSerializer.Serialize(true)), GroupId = group.Id, Group = group };
            db.PermissionNodes.Add(node);
            group.Nodes.Add(node);
        }
        await db.SaveChangesAsync(cancellationToken);
        return group;
    }

    private async Task EnsureMembersAsync(Guid groupId, IEnumerable<string> actors, CancellationToken cancellationToken)
    {
        var expectedActors = actors.ToHashSet();
        var currentActors = await db.PermissionGroupMembers.Where(x => x.GroupId == groupId)
            .Select(x => x.Actor).ToListAsync(cancellationToken);
        var missingActors = expectedActors.Except(currentActors).ToList();
        foreach (var actor in missingActors)
        {
            db.PermissionGroupMembers.Add(new SnPermissionGroupMember { GroupId = groupId, Actor = actor });
        }
        if (missingActors.Count > 0) await db.SaveChangesAsync(cancellationToken);
    }
}
