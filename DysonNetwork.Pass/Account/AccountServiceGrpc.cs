using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Account;

public class AccountServiceGrpc(
    AppDatabase db,
    RelationshipService relationships,
    IClock clock,
    ILogger<AccountServiceGrpc> logger
)
    : Shared.Proto.AccountService.AccountServiceBase
{
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<AccountServiceGrpc>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public override async Task<Shared.Proto.Account> GetAccount(GetAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _db.Accounts
            .AsNoTracking()
            .Include(a => a.Profile)
            .FirstOrDefaultAsync(a => a.Id == accountId);

        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        return account.ToProtoValue();
    }

    public override async Task<GetAccountBatchResponse> GetAccountBatch(GetAccountBatchRequest request,
        ServerCallContext context)
    {
        var accountIds = request.Id
            .Select(id => Guid.TryParse(id, out var accountId) ? accountId : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync();

        var response = new GetAccountBatchResponse();
        response.Accounts.AddRange(accounts.Select(a => a.ToProtoValue()));
        return response;
    }

    public override async Task<Shared.Proto.Account> CreateAccount(CreateAccountRequest request,
        ServerCallContext context)
    {
        // Map protobuf request to domain model
        var account = new Account
        {
            Name = request.Name,
            Nick = request.Nick,
            Language = request.Language,
            IsSuperuser = request.IsSuperuser,
            ActivatedAt = request.Profile != null ? null : _clock.GetCurrentInstant(),
            Profile = new AccountProfile
            {
                FirstName = request.Profile?.FirstName,
                LastName = request.Profile?.LastName,
                // Initialize other profile fields as needed
            }
        };

        // Add to database
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Created new account with ID {AccountId}", account.Id);
        return account.ToProtoValue();
    }

    public override async Task<Shared.Proto.Account> UpdateAccount(UpdateAccountRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _db.Accounts.FindAsync(accountId);
        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        // Update fields if they are provided in the request
        if (request.Name != null) account.Name = request.Name;
        if (request.Nick != null) account.Nick = request.Nick;
        if (request.Language != null) account.Language = request.Language;
        if (request.IsSuperuser != null) account.IsSuperuser = request.IsSuperuser.Value;

        await _db.SaveChangesAsync();
        return account.ToProtoValue();
    }

    public override async Task<Empty> DeleteAccount(DeleteAccountRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var account = await _db.Accounts.FindAsync(accountId);
        if (account == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, $"Account {request.Id} not found"));

        _db.Accounts.Remove(account);

        await _db.SaveChangesAsync();
        return new Empty();
    }

    public override async Task<ListAccountsResponse> ListAccounts(ListAccountsRequest request,
        ServerCallContext context)
    {
        var query = _db.Accounts.AsNoTracking();

        // Apply filters if provided
        if (!string.IsNullOrEmpty(request.Filter))
        {
            // Implement filtering logic based on request.Filter
            // This is a simplified example
            query = query.Where(a => a.Name.Contains(request.Filter) || a.Nick.Contains(request.Filter));
        }

        // Apply ordering
        query = request.OrderBy switch
        {
            "name" => query.OrderBy(a => a.Name),
            "name_desc" => query.OrderByDescending(a => a.Name),
            _ => query.OrderBy(a => a.Id)
        };

        // Get total count for pagination
        var totalCount = await query.CountAsync();

        // Apply pagination
        var accounts = await query
            .Skip(request.PageSize * (request.PageToken != null ? int.Parse(request.PageToken) : 0))
            .Take(request.PageSize)
            .Include(a => a.Profile)
            .ToListAsync();

        var response = new ListAccountsResponse
        {
            TotalSize = totalCount,
            NextPageToken = (accounts.Count == request.PageSize)
                ? ((request.PageToken != null ? int.Parse(request.PageToken) : 0) + 1).ToString()
                : ""
        };

        response.Accounts.AddRange(accounts.Select(x => x.ToProtoValue()));
        return response;
    }

// Implement other service methods following the same pattern...

// Profile operations
    public override async Task<Shared.Proto.AccountProfile> GetProfile(GetProfileRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var profile = await _db.AccountProfiles
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId);

        if (profile == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound,
                $"Profile for account {request.AccountId} not found"));

        return profile.ToProtoValue();
    }

    public override async Task<Shared.Proto.AccountProfile> UpdateProfile(UpdateProfileRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var profile = await _db.AccountProfiles
            .FirstOrDefaultAsync(p => p.AccountId == accountId);

        if (profile == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound,
                $"Profile for account {request.AccountId} not found"));

        // Update only the fields specified in the field mask
        if (request.UpdateMask == null || request.UpdateMask.Paths.Contains("first_name"))
            profile.FirstName = request.Profile.FirstName;

        if (request.UpdateMask == null || request.UpdateMask.Paths.Contains("last_name"))
            profile.LastName = request.Profile.LastName;

        // Update other fields similarly...

        await _db.SaveChangesAsync();
        return profile.ToProtoValue();
    }

// Contact operations
    public override async Task<Shared.Proto.AccountContact> AddContact(AddContactRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var contact = new AccountContact
        {
            AccountId = accountId,
            Type = (AccountContactType)request.Type,
            Content = request.Content,
            IsPrimary = request.IsPrimary,
            VerifiedAt = null
        };

        _db.AccountContacts.Add(contact);
        await _db.SaveChangesAsync();

        return contact.ToProtoValue();
    }

    public override async Task<Empty> RemoveContact(RemoveContactRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        if (!Guid.TryParse(request.Id, out var contactId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid contact ID format"));

        var contact = await _db.AccountContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.AccountId == accountId);
        if (contact == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Contact not found."));

        _db.AccountContacts.Remove(contact);
        await _db.SaveChangesAsync();

        return new Empty();
    }

    public override async Task<ListContactsResponse> ListContacts(ListContactsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = _db.AccountContacts.AsNoTracking().Where(c => c.AccountId == accountId);

        if (request.VerifiedOnly)
            query = query.Where(c => c.VerifiedAt != null);

        var contacts = await query.ToListAsync();

        var response = new ListContactsResponse();
        response.Contacts.AddRange(contacts.Select(c => c.ToProtoValue()));

        return response;
    }

    public override async Task<Shared.Proto.AccountContact> VerifyContact(VerifyContactRequest request,
        ServerCallContext context)
    {
        // This is a placeholder implementation. In a real-world scenario, you would
        // have a more robust verification mechanism (e.g., sending a code to the
        // user's email or phone).
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        if (!Guid.TryParse(request.Id, out var contactId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid contact ID format"));

        var contact = await _db.AccountContacts.FirstOrDefaultAsync(c => c.Id == contactId && c.AccountId == accountId);
        if (contact == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Contact not found."));

        contact.VerifiedAt = _clock.GetCurrentInstant();
        await _db.SaveChangesAsync();

        return contact.ToProtoValue();
    }

// Badge operations
    public override async Task<Shared.Proto.AccountBadge> AddBadge(AddBadgeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var badge = new AccountBadge
        {
            AccountId = accountId,
            Type = request.Type,
            Label = request.Label,
            Caption = request.Caption,
            ActivatedAt = _clock.GetCurrentInstant(),
            ExpiredAt = request.ExpiredAt?.ToInstant(),
            Meta = request.Meta.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value)
        };

        _db.Badges.Add(badge);
        await _db.SaveChangesAsync();

        return badge.ToProtoValue();
    }

    public override async Task<Empty> RemoveBadge(RemoveBadgeRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        if (!Guid.TryParse(request.Id, out var badgeId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid badge ID format"));

        var badge = await _db.Badges.FirstOrDefaultAsync(b => b.Id == badgeId && b.AccountId == accountId);
        if (badge == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Badge not found."));

        _db.Badges.Remove(badge);
        await _db.SaveChangesAsync();

        return new Empty();
    }

    public override async Task<ListBadgesResponse> ListBadges(ListBadgesRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = _db.Badges.AsNoTracking().Where(b => b.AccountId == accountId);

        if (request.ActiveOnly)
            query = query.Where(b => b.ExpiredAt == null || b.ExpiredAt > _clock.GetCurrentInstant());

        var badges = await query.ToListAsync();

        var response = new ListBadgesResponse();
        response.Badges.AddRange(badges.Select(b => b.ToProtoValue()));

        return response;
    }

    public override async Task<Shared.Proto.AccountProfile> SetActiveBadge(SetActiveBadgeRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var profile = await _db.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
        if (profile == null)
            throw new RpcException(new Grpc.Core.Status(StatusCode.NotFound, "Profile not found."));

        if (!string.IsNullOrEmpty(request.BadgeId) && !Guid.TryParse(request.BadgeId, out var badgeId))
            throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Invalid badge ID format"));

        await _db.SaveChangesAsync();

        return profile.ToProtoValue();
    }

    public override async Task<ListUserRelationshipSimpleResponse> ListFriends(
        ListUserRelationshipSimpleRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var relationship = await relationships.ListAccountFriends(accountId);
        var resp = new ListUserRelationshipSimpleResponse();
        resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
        return resp;
    }

    public override async Task<ListUserRelationshipSimpleResponse> ListBlocked(
        ListUserRelationshipSimpleRequest request, ServerCallContext context)
    {
        var accountId = Guid.Parse(request.AccountId);
        var relationship = await relationships.ListAccountBlocked(accountId);
        var resp = new ListUserRelationshipSimpleResponse();
        resp.AccountsId.AddRange(relationship.Select(x => x.ToString()));
        return resp;
    }

    public override async Task<GetRelationshipResponse> GetRelationship(GetRelationshipRequest request,
        ServerCallContext context)
    {
        var relationship = await relationships.GetRelationship(
            Guid.Parse(request.AccountId),
            Guid.Parse(request.RelatedId),
            status: (RelationshipStatus?)request.Status
        );
        return new GetRelationshipResponse
        {
            Relationship = relationship?.ToProtoValue()
        };
    }

    public override async Task<BoolValue> HasRelationship(GetRelationshipRequest request, ServerCallContext context)
    {
        var hasRelationship = false;
        if (!request.HasStatus)
            hasRelationship = await relationships.HasExistingRelationship(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId)
            );
        else
            hasRelationship = await relationships.HasRelationshipWithStatus(
                Guid.Parse(request.AccountId),
                Guid.Parse(request.RelatedId),
                (RelationshipStatus)request.Status
            );
        return new BoolValue { Value = hasRelationship };
    }
}