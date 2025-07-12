using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Pass.Account;

public class AccountServiceGrpc(
    AppDatabase db,
    IClock clock,
    ILogger<AccountServiceGrpc> logger
)
    : Shared.Proto.AccountService.AccountServiceBase
{
    private readonly AppDatabase _db = db ?? throw new ArgumentNullException(nameof(db));
    private readonly IClock _clock = clock ?? throw new ArgumentNullException(nameof(clock));

    private readonly ILogger<AccountServiceGrpc>
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // Helper methods for conversion between protobuf and domain models
    private static Shared.Proto.Account ToProtoAccount(Account account) => new()
    {
        Id = account.Id.ToString(),
        Name = account.Name,
        Nick = account.Nick,
        Language = account.Language,
        ActivatedAt = account.ActivatedAt?.ToTimestamp(),
        IsSuperuser = account.IsSuperuser,
        Profile = ToProtoProfile(account.Profile)
        // Note: Collections are not included by default to avoid large payloads
        // They should be loaded on demand via specific methods
    };

    private static Shared.Proto.AccountProfile ToProtoProfile(AccountProfile profile) => new()
    {
        Id = profile.Id.ToString(),
        FirstName = profile.FirstName,
        MiddleName = profile.MiddleName,
        LastName = profile.LastName,
        Bio = profile.Bio,
        Gender = profile.Gender,
        Pronouns = profile.Pronouns,
        TimeZone = profile.TimeZone,
        Location = profile.Location,
        Birthday = profile.Birthday?.ToTimestamp(),
        LastSeenAt = profile.LastSeenAt?.ToTimestamp(),
        Experience = profile.Experience,
        Level = profile.Level,
        LevelingProgress = profile.LevelingProgress,
        AccountId = profile.AccountId.ToString(),
        PictureId = profile.PictureId,
        BackgroundId = profile.BackgroundId,
        Picture = profile.Picture?.ToProtoValue(),
        Background = profile.Background?.ToProtoValue()
    };

    private static Shared.Proto.AccountContact ToProtoContact(AccountContact contact) => new()
    {
        Id = contact.Id.ToString(),
        Type = contact.Type switch
        {
            AccountContactType.Address => Shared.Proto.AccountContactType.Address,
            AccountContactType.PhoneNumber => Shared.Proto.AccountContactType.PhoneNumber,
            AccountContactType.Email => Shared.Proto.AccountContactType.Email,
            _ => Shared.Proto.AccountContactType.Unspecified
        },
        VerifiedAt = contact.VerifiedAt?.ToTimestamp(),
        IsPrimary = contact.IsPrimary,
        Content = contact.Content,
        AccountId = contact.AccountId.ToString()
    };

    private static Shared.Proto.AccountBadge ToProtoBadge(AccountBadge badge) => new()
    {
        Id = badge.Id.ToString(),
        Type = badge.Type,
        Label = badge.Label,
        Caption = badge.Caption,
        ActivatedAt = badge.ActivatedAt?.ToTimestamp(),
        ExpiredAt = badge.ExpiredAt?.ToTimestamp(),
        AccountId = badge.AccountId.ToString()
    };

// Implementation of gRPC service methods
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

        return ToProtoAccount(account);
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
        return ToProtoAccount(account);
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
        return ToProtoAccount(account);
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

        response.Accounts.AddRange(accounts.Select(ToProtoAccount));
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

        return ToProtoProfile(profile);
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
        return ToProtoProfile(profile);
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

        return ToProtoContact(contact);
    }

// Implement other contact operations...

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

        return ToProtoBadge(badge);
    }

// Implement other badge operations...
}