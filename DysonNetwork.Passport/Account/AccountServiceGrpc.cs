using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using Duration = NodaTime.Duration;

namespace DysonNetwork.Passport.Account;

public class AccountServiceGrpc(
    AppDatabase db,
    AccountEventService accountEvents,
    RelationshipService relationships,
    RemoteSubscriptionService remoteSubscription,
    RemoteAccountContactService remoteContacts,
    AccountBoardService boardService,
    AccountService accountService,
    DyAccountService.DyAccountServiceClient padlockAccounts,
    NotableDaysService notableDaysService,
    ILogger<AccountServiceGrpc> logger
)
    : DyProfileService.DyProfileServiceBase
{
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly AccountService _accountService =
        accountService ?? throw new ArgumentNullException(nameof(accountService));
    private readonly ILogger<AccountServiceGrpc>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task<DyAccount> GetAccount(DyGetAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _accountService.GetAccount(accountId);
        if (account is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        await PopulatePerkSubscriptionAsync(account);
        await remoteContacts.PopulateContactsAsync(account, cancellationToken: context.CancellationToken);

        return account.ToProtoValue();
    }

    public override async Task<DyAccount> GetBotAccount(DyGetBotAccountRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AutomatedId, out var automatedId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid automated ID format"));

        DyAccount remote;
        try
        {
            remote = await padlockAccounts.GetBotAccountAsync(request, cancellationToken: context.CancellationToken);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Account with automated ID {request.AutomatedId} not found"));
        }

        var account = SnAccount.FromProtoValue(remote);
        account.Profile = await _accountService.GetOrCreateAccountProfileAsync(account.Id);

        await PopulatePerkSubscriptionAsync(account);
        await remoteContacts.PopulateContactsAsync(account, cancellationToken: context.CancellationToken);

        return account.ToProtoValue();
    }

    public override async Task<DyGetAccountBatchResponse> GetAccountBatch(DyGetAccountBatchRequest request,
        ServerCallContext context)
    {
        var remote = await padlockAccounts.GetAccountBatchAsync(request, cancellationToken: context.CancellationToken);
        var accounts = await HydrateAccountsAsync(remote.Accounts, context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }


    public override async Task<DyGetAccountBatchResponse> GetBotAccountBatch(DyGetBotAccountBatchRequest request,
        ServerCallContext context)
    {
        var remote = await padlockAccounts.GetBotAccountBatchAsync(request, cancellationToken: context.CancellationToken);
        var accounts = await HydrateAccountsAsync(remote.Accounts, context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyAccountStatus> GetAccountStatus(DyGetAccountRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.Id);
        var status = await accountEvents.GetStatus(accountId);
        return status.ToProtoValue();
    }

    public override async Task<DyGetAccountStatusBatchResponse> GetAccountStatusBatch(DyGetAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Id
            .Select(id => Guid.TryParse(id, out var accountId) ? accountId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
        var statuses = await accountEvents.GetStatuses(accountIds);
        var response = new DyGetAccountStatusBatchResponse();
        response.Statuses.AddRange(statuses.Select(s => s.Value.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetAccountBatchResponse> LookupAccountBatch(DyLookupAccountBatchRequest request,
        ServerCallContext context)
    {
        var remote = await padlockAccounts.LookupAccountBatchAsync(request, cancellationToken: context.CancellationToken);
        var accounts = await HydrateAccountsAsync(remote.Accounts, context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetAccountBatchResponse> SearchAccount(DySearchAccountRequest request,
        ServerCallContext context)
    {
        var remote = await padlockAccounts.SearchAccountAsync(request, cancellationToken: context.CancellationToken);
        var accounts = await HydrateAccountsAsync(remote.Accounts, context.CancellationToken);

        var response = new DyGetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<DyListAccountsResponse> ListAccounts(DyListAccountsRequest request,
        ServerCallContext context)
    {
        var remote = await padlockAccounts.ListAccountsAsync(request, cancellationToken: context.CancellationToken);
        var accounts = await HydrateAccountsAsync(remote.Accounts, context.CancellationToken);

        var response = new DyListAccountsResponse
        {
            TotalSize = remote.TotalSize,
            NextPageToken = remote.NextPageToken
        };

        response.Accounts.AddRange(accounts.Select(x => x.ToProtoValue()));
        return response;
    }

    public override async Task<DyListRelationshipSimpleResponse> ListFriends(
        DyListRelationshipSimpleRequest request,
        ServerCallContext context
    )
    {
        var resp = new DyListRelationshipSimpleResponse();
        switch (request.RelationIdentifierCase)
        {
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.AccountId:
                var accountId = Guid.Parse(request.AccountId);
                var relationship = await relationships.ListAccountFriends(accountId);
                resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
                return resp;
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.RelatedId:
                var relatedId = Guid.Parse(request.RelatedId);
                var relatedRelationship = await relationships.ListAccountFriends(relatedId, true);
                resp.AccountsId.AddRange(relatedRelationship.Select(x => x.ToString()));
                return resp;
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.None:
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "The relationship identifier must be provided."));
        }
    }

    public override async Task<DyListRelationshipSimpleResponse> ListBlocked(
        DyListRelationshipSimpleRequest request,
        ServerCallContext context)
    {
        var resp = new DyListRelationshipSimpleResponse();
        switch (request.RelationIdentifierCase)
        {
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.AccountId:
                var accountId = Guid.Parse(request.AccountId);
                var relationship = await relationships.ListAccountBlocked(accountId);
                resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
                return resp;
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.RelatedId:
                var relatedId = Guid.Parse(request.RelatedId);
                var relatedRelationship = await relationships.ListAccountBlocked(relatedId, true);
                resp.AccountsId.AddRange(relatedRelationship.Select(x => x.ToString()));
                return resp;
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.None:
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "The relationship identifier must be provided."));
        }
    }

    public override async Task<DyListRelationshipSimpleResponse> ListMuted(
        DyListRelationshipSimpleRequest request,
        ServerCallContext context)
    {
        var resp = new DyListRelationshipSimpleResponse();
        switch (request.RelationIdentifierCase)
        {
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.AccountId:
                var accountId = Guid.Parse(request.AccountId);
                var mutedIds = await relationships.ListAccountMuted(accountId);
                resp.AccountsId.AddRange(mutedIds.Select(x => x.ToString()));
                return resp;
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.None:
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "The account_id must be provided."));
        }
    }

    public override async Task<DyListRelationshipSimpleResponse> ListCloseFriends(
        DyListRelationshipSimpleRequest request,
        ServerCallContext context)
    {
        var resp = new DyListRelationshipSimpleResponse();
        switch (request.RelationIdentifierCase)
        {
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.AccountId:
                var accountId = Guid.Parse(request.AccountId);
                var closeFriendIds = await relationships.ListCloseFriends(accountId);
                resp.AccountsId.AddRange(closeFriendIds.Select(x => x.ToString()));
                return resp;
            case DyListRelationshipSimpleRequest.RelationIdentifierOneofCase.None:
            default:
                throw new RpcException(new Status(StatusCode.InvalidArgument,
                    "The account_id must be provided."));
        }
    }

    public override async Task<DyAccountProfile> GetProfile(DyGetProfileRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var profile = await _accountService.GetOrCreateAccountProfileAsync(accountId);
        return profile.ToProtoValue();
    }

    public override async Task<DyAccountProfile> UpdateProfile(DyUpdateProfileRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var profile = await _accountService.GetOrCreateAccountProfileAsync(accountId);
        var incoming = SnAccountProfile.FromProtoValue(request.Profile);

        var hasMask = request.UpdateMask is not null && request.UpdateMask.Paths.Count > 0;
        if (!hasMask)
        {
            ApplyAllProfileFields(profile, incoming);
        }
        else
        {
            foreach (var path in request.UpdateMask!.Paths)
            {
                switch (path)
                {
                    case "first_name":
                        profile.FirstName = incoming.FirstName;
                        break;
                    case "middle_name":
                        profile.MiddleName = incoming.MiddleName;
                        break;
                    case "last_name":
                        profile.LastName = incoming.LastName;
                        break;
                    case "bio":
                        profile.Bio = incoming.Bio;
                        break;
                    case "gender":
                        profile.Gender = incoming.Gender;
                        break;
                    case "pronouns":
                        profile.Pronouns = incoming.Pronouns;
                        break;
                    case "time_zone":
                        profile.TimeZone = incoming.TimeZone;
                        break;
                    case "location":
                        profile.Location = incoming.Location;
                        break;
                    case "birthday":
                        profile.Birthday = incoming.Birthday;
                        break;
                    case "verification":
                        profile.Verification = incoming.Verification;
                        break;
                    case "active_badge":
                        profile.ActiveBadge = incoming.ActiveBadge;
                        break;
                    case "picture":
                        profile.Picture = incoming.Picture;
                        break;
                    case "background":
                        profile.Background = incoming.Background;
                        break;
                    case "username_color":
                        profile.UsernameColor = incoming.UsernameColor;
                        break;
                    case "links":
                        profile.Links = incoming.Links;
                        break;
                    case "experience":
                        profile.Experience = incoming.Experience;
                        break;
                    case "social_credits":
                        profile.SocialCredits = incoming.SocialCredits;
                        break;
                    case "last_seen_at":
                        profile.LastSeenAt = incoming.LastSeenAt;
                        break;
                }
            }
        }

        _db.Update(profile);
        await _db.SaveChangesAsync(context.CancellationToken);

        return profile.ToProtoValue();
    }

    public override async Task<DyAccountBoardItem> UpdateBoardItemPayload(
        DyUpdateBoardItemPayloadRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));
        if (!Guid.TryParse(request.BoardItemId, out var boardItemId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid board item ID format"));
        if (!Guid.TryParse(request.CustomAppId, out var customAppId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid custom app ID format"));
        if (string.IsNullOrWhiteSpace(request.CustomAppWidgetKey))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "custom_app_widget_key is required"));

        var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonFormatter.Default.Format(request.Payload),
            Shared.Data.InfraObjectCoder.SerializerOptions
        ) ?? [];

        try
        {
            var item = await boardService.UpdateCustomAppPayloadAsync(
                accountId,
                boardItemId,
                customAppId,
                request.CustomAppWidgetKey,
                payload,
                context.CancellationToken
            );
            return item.ToProtoValue();
        }
        catch (KeyNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
    }

    public override async Task<DyListBadgesResponse> ListBadges(DyListBadgesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = _db.Badges.AsNoTracking().Where(b => b.AccountId == accountId);

        if (!string.IsNullOrWhiteSpace(request.Type))
            query = query.Where(b => b.Type == request.Type);

        if (request.ActiveOnly)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            query = query.Where(b => b.ActivatedAt != null && (b.ExpiredAt == null || b.ExpiredAt > now));
        }

        var badges = await query.OrderByDescending(b => b.CreatedAt).ToListAsync(context.CancellationToken);
        var response = new DyListBadgesResponse();
        response.Badges.AddRange(badges.Select(b => b.ToProtoValue()));
        return response;
    }

    public override async Task<DyGetRelationshipResponse> GetRelationship(
        DyGetRelationshipRequest request,
        ServerCallContext context
    )
    {
        var relationship = await relationships.GetRelationship(
            Guid.Parse(request.AccountId),
            Guid.Parse(request.RelatedId),
            status: (RelationshipStatus?)request.Status
        );
        return new DyGetRelationshipResponse
        {
            Relationship = relationship?.ToProtoValue()
        };
    }

    public override async Task<BoolValue> HasRelationship(DyGetRelationshipRequest request, ServerCallContext context)
    {
        if (request.EitherDirection && request.HasStatus
            && (Shared.Models.RelationshipStatus)request.Status == Shared.Models.RelationshipStatus.Blocked)
        {
            var isBlocked = await relationships.IsBlockedEitherDirection(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId)
            );
            return new BoolValue { Value = isBlocked };
        }

        bool hasRelationship;
        if (!request.HasStatus)
            hasRelationship = await relationships.HasExistingRelationship(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId)
            );
        else
            hasRelationship = await relationships.HasRelationshipWithStatus(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId),
                (Shared.Models.RelationshipStatus)request.Status
            );
        return new BoolValue { Value = hasRelationship };
    }

    public override async Task<DyGrantBadgeResponse> GrantBadge(DyGrantBadgeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _accountService.GetAccount(accountId);

        if (account == null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Account {request.AccountId} not found"));

        var badge = SnAccountBadge.FromProtoValue(request.Badge);
        var grantedBadge = await _accountService.GrantBadge(account, badge);
        await SyncActiveBadgeReferenceAsync(accountId, grantedBadge, context.CancellationToken);

        return new DyGrantBadgeResponse
        {
            Badge = grantedBadge.ToProtoValue()
        };
    }

    public override async Task<DyGetBadgeResponse> GetBadge(DyGetBadgeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        if (!Guid.TryParse(request.BadgeId, out var badgeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid badge ID format"));

        var badge = await _db.Badges
            .Where(b => b.AccountId == accountId && b.Id == badgeId)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (badge == null)
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Badge {request.BadgeId} not found for account {request.AccountId}"));

        return new DyGetBadgeResponse
        {
            Badge = badge.ToProtoValue()
        };
    }

    public override async Task<DyUpdateBadgeResponse> UpdateBadge(DyUpdateBadgeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        if (!Guid.TryParse(request.BadgeId, out var badgeId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid badge ID format"));

        var badge = await _db.Badges
            .Where(b => b.AccountId == accountId && b.Id == badgeId)
            .FirstOrDefaultAsync(context.CancellationToken);

        if (badge == null)
            throw new RpcException(new Status(StatusCode.NotFound,
                $"Badge {request.BadgeId} not found for account {request.AccountId}"));

        var updatedBadge = SnAccountBadge.FromProtoValue(request.Badge);

        if (request.UpdateMask != null && request.UpdateMask.Paths.Count > 0)
        {
            foreach (var path in request.UpdateMask.Paths)
            {
                switch (path.ToLowerInvariant())
                {
                    case "type":
                        badge.Type = updatedBadge.Type;
                        break;
                    case "label":
                        badge.Label = updatedBadge.Label;
                        break;
                    case "caption":
                        badge.Caption = updatedBadge.Caption;
                        break;
                    case "meta":
                        badge.Meta = updatedBadge.Meta;
                        break;
                    case "activated_at":
                        badge.ActivatedAt = updatedBadge.ActivatedAt;
                        break;
                    case "expired_at":
                        badge.ExpiredAt = updatedBadge.ExpiredAt;
                        break;
                }
            }
        }
        else
        {
            badge.Type = updatedBadge.Type;
            badge.Label = updatedBadge.Label;
            badge.Caption = updatedBadge.Caption;
            badge.Meta = updatedBadge.Meta;
            badge.ActivatedAt = updatedBadge.ActivatedAt;
            badge.ExpiredAt = updatedBadge.ExpiredAt;
        }

        badge.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
        _db.Badges.Update(badge);
        await _db.SaveChangesAsync(context.CancellationToken);
        await SyncActiveBadgeReferenceAsync(accountId, badge, context.CancellationToken);

        return new DyUpdateBadgeResponse
        {
            Badge = badge.ToProtoValue()
        };
    }

    public override async Task<DyGetNotableDaysResponse> GetNotableDays(
        DyGetNotableDaysRequest request,
        ServerCallContext context)
    {
        var year = request.Year;
        var region = string.IsNullOrWhiteSpace(request.Region) ? "CN" : request.Region;

        NotableDayTag? tagFilter = null;
        if (request.HasTag)
        {
            tagFilter = request.Tag switch
            {
                DyNotableDayTag.Holiday => NotableDayTag.Holiday,
                DyNotableDayTag.Event => NotableDayTag.Event,
                DyNotableDayTag.Anniversary => NotableDayTag.Anniversary,
                DyNotableDayTag.Memorial => NotableDayTag.Memorial,
                DyNotableDayTag.Festival => NotableDayTag.Festival,
                _ => null
            };
        }

        var days = await notableDaysService.GetNotableDays(year, region, tagFilter);

        var offset = Math.Max(0, request.Offset);
        var take = request.Take > 0 ? Math.Min(request.Take, 100) : 50;

        var pagedDays = days.Skip(offset).Take(take).ToList();

        var response = new DyGetNotableDaysResponse
        {
            TotalCount = days.Count
        };
        response.Days.AddRange(pagedDays.Select(MapNotableDayToProto));

        return response;
    }

    public override async Task<DyGetUserCalendarEventsResponse> GetUserCalendarEvents(
        DyGetUserCalendarEventsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        Guid? viewerId = null;
        if (!string.IsNullOrWhiteSpace(request.ViewerId) && Guid.TryParse(request.ViewerId, out var parsedViewerId))
            viewerId = parsedViewerId;

        Instant? startTime = request.StartTime?.ToInstant();
        Instant? endTime = request.EndTime?.ToInstant();

        var offset = Math.Max(0, request.Offset);
        var take = request.Take > 0 ? Math.Min(request.Take, 100) : 50;

        var (events, totalCount) = await accountEvents.GetUserCalendarEventsAsync(
            accountId,
            viewerId,
            startTime,
            endTime,
            offset,
            take);

        var response = new DyGetUserCalendarEventsResponse
        {
            TotalCount = totalCount
        };
        response.Events.AddRange(events.Select(MapCalendarEventToProto));

        return response;
    }

    public override async Task<DyGetCountdownEventsResponse> GetCountdownEvents(
        DyGetCountdownEventsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        var snAccount = new SnAccount { Id = accountId };

        Guid? viewerId = null;
        if (!string.IsNullOrWhiteSpace(request.ViewerId) && Guid.TryParse(request.ViewerId, out var parsedViewerId))
            viewerId = parsedViewerId;

        var region = string.IsNullOrWhiteSpace(request.Region) ? "CN" : request.Region;

        NotableDayTag? tagFilter = null;
        if (request.HasTag)
        {
            tagFilter = request.Tag switch
            {
                DyNotableDayTag.Holiday => NotableDayTag.Holiday,
                DyNotableDayTag.Event => NotableDayTag.Event,
                DyNotableDayTag.Anniversary => NotableDayTag.Anniversary,
                DyNotableDayTag.Memorial => NotableDayTag.Memorial,
                DyNotableDayTag.Festival => NotableDayTag.Festival,
                _ => null
            };
        }

        var take = request.Take > 0 ? Math.Min(request.Take, 100) : 5;
        var offset = Math.Max(0, request.Offset);

        var result = await accountEvents.GetCountdownEventsAsync(
            snAccount,
            viewerId,
            region,
            notableDaysService,
            request.IncludeNotableDays,
            tagFilter,
            take,
            offset);

        var response = new DyGetCountdownEventsResponse
        {
            TotalCount = result.TotalCount
        };
        response.Events.AddRange(result.Events.Select(MapCountdownItemToProto));

        return response;
    }

    private static DyNotableDay MapNotableDayToProto(NotableDay day)
    {
        var proto = new DyNotableDay
        {
            Name = day.GlobalName ?? day.LocalName ?? "",
            StartDate = day.Date.ToTimestamp(),
            EndDate = day.Date.Plus(Duration.FromDays(1)).ToTimestamp(),
            IsAllDay = true,
            Region = day.CountryCode ?? "",
        };

        if (!string.IsNullOrWhiteSpace(day.LocalName))
            proto.LocalName = day.LocalName;
        if (!string.IsNullOrWhiteSpace(day.LocalizableKey))
            proto.LocalizableKey = day.LocalizableKey;
        if (!string.IsNullOrWhiteSpace(day.GlobalName))
            proto.Description = day.GlobalName;

        proto.Tags.AddRange(day.Tags.Select(t => t switch
        {
            NotableDayTag.Holiday => DyNotableDayTag.Holiday,
            NotableDayTag.Event => DyNotableDayTag.Event,
            NotableDayTag.Anniversary => DyNotableDayTag.Anniversary,
            NotableDayTag.Memorial => DyNotableDayTag.Memorial,
            NotableDayTag.Festival => DyNotableDayTag.Festival,
            _ => DyNotableDayTag.Unspecified
        }));

        return proto;
    }

    private static DyUserCalendarEvent MapCalendarEventToProto(SnUserCalendarEvent evt)
    {
        return new DyUserCalendarEvent
        {
            Id = evt.Id.ToString(),
            Title = evt.Title,
            Description = evt.Description ?? "",
            Location = evt.Location ?? "",
            StartTime = evt.StartTime.ToTimestamp(),
            EndTime = evt.EndTime.ToTimestamp(),
            IsAllDay = evt.IsAllDay,
            Visibility = (int)evt.Visibility,
            AccountId = evt.AccountId.ToString(),
            CreatedAt = evt.CreatedAt.ToTimestamp(),
            UpdatedAt = evt.UpdatedAt.ToTimestamp(),
        };
    }

    private static DyEventCountdownItem MapCountdownItemToProto(EventCountdownItem item)
    {
        var proto = new DyEventCountdownItem
        {
            Title = item.Title,
            StartTime = item.StartTime.ToTimestamp(),
            EndTime = item.EndTime.ToTimestamp(),
            IsAllDay = item.IsAllDay,
            DaysRemaining = item.DaysRemaining,
            HoursRemaining = item.HoursRemaining,
            IsOngoing = item.IsOngoing,
        };

        if (item.EventId.HasValue)
            proto.EventId = item.EventId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(item.Description))
            proto.Description = item.Description;
        if (!string.IsNullOrWhiteSpace(item.Location))
            proto.Location = item.Location;
        if (item.AccountId.HasValue)
            proto.AccountId = item.AccountId.Value.ToString();

        proto.Type = item.EventType switch
        {
            CalendarEventType.UserEvent => 0,
            CalendarEventType.NotableDay => 3,
            _ => 0
        };

        return proto;
    }

    private async Task SyncActiveBadgeReferenceAsync(
        Guid accountId,
        SnAccountBadge badge,
        CancellationToken cancellationToken
    )
    {
        if (badge.ActivatedAt is null)
            return;

        await _db.AccountProfiles
            .Where(p => p.AccountId == accountId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(p => p.ActiveBadge, badge.ToReference()),
                cancellationToken
            );
    }

    private static void ApplyAllProfileFields(SnAccountProfile profile, SnAccountProfile incoming)
    {
        profile.FirstName = incoming.FirstName;
        profile.MiddleName = incoming.MiddleName;
        profile.LastName = incoming.LastName;
        profile.Bio = incoming.Bio;
        profile.Gender = incoming.Gender;
        profile.Pronouns = incoming.Pronouns;
        profile.TimeZone = incoming.TimeZone;
        profile.Location = incoming.Location;
        profile.Birthday = incoming.Birthday;
        profile.Verification = incoming.Verification;
        profile.ActiveBadge = incoming.ActiveBadge;
        profile.Picture = incoming.Picture;
        profile.Background = incoming.Background;
        profile.UsernameColor = incoming.UsernameColor;
        profile.Links = incoming.Links;
        profile.Experience = incoming.Experience;
        profile.SocialCredits = incoming.SocialCredits;
        profile.LastSeenAt = incoming.LastSeenAt;
    }

    private async Task<List<SnAccount>> HydrateAccountsAsync(
        IEnumerable<DyAccount> remoteAccounts,
        CancellationToken cancellationToken)
    {
        var accounts = remoteAccounts.Select(SnAccount.FromProtoValue).ToList();

        foreach (var account in accounts)
        {
            account.Profile = await _accountService.GetOrCreateAccountProfileAsync(account.Id);
        }

        await PopulatePerkSubscriptionsAsync(accounts);
        await PopulateContactsAsync(accounts, cancellationToken);

        return accounts;
    }

    private async Task PopulatePerkSubscriptionAsync(SnAccount account)
    {
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is not null)
            {
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
                account.PerkLevel = account.PerkSubscription.PerkLevel;
            }
            else
            {
                account.PerkSubscription = null;
                account.PerkLevel = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate PerkSubscription for account {AccountId} in gRPC service",
                account.Id);
        }
    }

    private async Task PopulatePerkSubscriptionsAsync(List<SnAccount> accounts)
    {
        if (accounts.Count == 0) return;

        try
        {
            var accountIds = accounts.Select(a => a.Id).ToList();
            var subscriptions = await remoteSubscription.GetPerkSubscriptions(accountIds);

            var subscriptionDict = subscriptions
                .ToDictionary(
                    s => Guid.Parse(s.AccountId),
                    s => SnWalletSubscription.FromProtoValue(s).ToReference()
                );

            foreach (var account in accounts)
            {
                if (subscriptionDict.TryGetValue(account.Id, out var subscription))
                {
                    account.PerkSubscription = subscription;
                    account.PerkLevel = subscription.PerkLevel;
                }
                else
                {
                    account.PerkSubscription = null;
                    account.PerkLevel = 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to populate PerkSubscriptions for {Count} accounts in gRPC service",
                accounts.Count);
        }
    }

    private async Task PopulateContactsAsync(List<SnAccount> accounts, CancellationToken cancellationToken)
    {
        foreach (var account in accounts)
        {
            await remoteContacts.PopulateContactsAsync(account, cancellationToken: cancellationToken);
        }
    }
}
