using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

[ApiController]
[Route("/api/relationships")]
public class RelationshipController(AppDatabase db, RelationshipService rls, ActionLogService als, AccountService accounts) : ControllerBase
{
    public class RelationshipActionRequest
    {
        /// <summary>
        /// Duration string like "1h", "24h", "7d", "30d". Null = permanent.
        /// </summary>
        public string? ExpiresIn { get; set; }

        /// <summary>
        /// Status to degrade to on expiry. Null = remove entirely.
        /// Only meaningful when ExpiresIn is set.
        /// </summary>
        public RelationshipStatus? DegradeTo { get; set; }
    }

    private static Duration? ParseExpiresIn(string? expiresIn)
    {
        if (string.IsNullOrWhiteSpace(expiresIn))
            return null;

        var trimmed = expiresIn.Trim().ToLowerInvariant();
        if (trimmed.EndsWith("d") && int.TryParse(trimmed[..^1], out var days))
            return Duration.FromDays(days);
        if (trimmed.EndsWith("h") && int.TryParse(trimmed[..^1], out var hours))
            return Duration.FromHours(hours);
        if (trimmed.EndsWith("m") && int.TryParse(trimmed[..^1], out var minutes))
            return Duration.FromMinutes(minutes);

        throw new ArgumentException($"Invalid ExpiresIn format: '{expiresIn}'. Use '1h', '24h', '7d', '30d', etc.");
    }

    private async Task HydrateRelationshipAsync(SnAccountRelationship relationship)
    {
        relationship.Account = await accounts.GetAccount(relationship.AccountId)
            ?? throw new InvalidOperationException($"Account {relationship.AccountId} was not found.");
        relationship.Related = await accounts.GetAccount(relationship.RelatedId)
            ?? throw new InvalidOperationException($"Related account {relationship.RelatedId} was not found.");
    }

    private async Task HydrateRelationshipsAsync(IEnumerable<SnAccountRelationship> relationships)
    {
        foreach (var relationship in relationships)
            await HydrateRelationshipAsync(relationship);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnAccountRelationship>>> ListRelationships([FromQuery] int offset = 0,
        [FromQuery] int take = 20)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        var accountId = currentUser.Id;

        var query = db.AccountRelationships.AsQueryable()
            .OrderByDescending(r => r.CreatedAt)
            .Where(r => r.Status != RelationshipStatus.Pending)
            .Where(r => r.AccountId == accountId);
        var totalCount = await query.CountAsync();
        var relationships = await query
            .Skip(offset)
            .Take(take)
            .ToListAsync();
        await HydrateRelationshipsAsync(relationships);

        Response.Headers["X-Total"] = totalCount.ToString();

        return relationships;
    }

    [HttpGet("requests")]
    [Authorize]
    public async Task<ActionResult<List<SnAccountRelationship>>> ListRelationshipRequests()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relationships = await db.AccountRelationships
            .Where(r => r.Status == RelationshipStatus.Pending)
            .Where(r => r.AccountId == currentUser.Id || r.RelatedId == currentUser.Id)
            .ToListAsync();
        await HydrateRelationshipsAsync(relationships);

        return relationships;
    }

    public class RelationshipRequest
    {
        [Required] public RelationshipStatus Status { get; set; }
    }

    [HttpPost("{accountId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsCreate)]
    public async Task<ActionResult<SnAccountRelationship>> CreateRelationship(Guid accountId,
        [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            var relationship = await rls.CreateRelationship(
                currentUser, relatedUser, request.Status
            );

            als.CreateActionLogFromRequest(
                "relationships.create",
                new Dictionary<string, object>
                {
                    { "related_account_id", relatedUser.Id.ToString() },
                    { "status", request.Status.ToString() }
                },
                Request
            );

            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_RELATIONSHIP_CREATE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPatch("{accountId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsUpdate)]
    public async Task<ActionResult<SnAccountRelationship>> UpdateRelationship(Guid accountId,
        [FromBody] RelationshipRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var relationship = await rls.UpdateRelationship(currentUser.Id, accountId, request.Status);

            als.CreateActionLogFromRequest(
                "relationships.update",
                new Dictionary<string, object>
                {
                    { "related_account_id", accountId.ToString() },
                    { "new_status", request.Status.ToString() }
                },
                Request
            );

            return relationship;
        }
        catch (ArgumentException err)
        {
            return NotFound(new ApiError { Code = "PASSPORT_RELATIONSHIP_NOT_FOUND", Message = err.Message, Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_RELATIONSHIP_UPDATE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpDelete("{accountId:guid}")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsDelete)]
    public async Task<ActionResult<SnAccountRelationship>> DeleteRelationship(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var relationship = await rls.DeleteRelationship(currentUser.Id, accountId);

            als.CreateActionLogFromRequest(
                "relationships.delete",
                new Dictionary<string, object>
                {
                    { "related_account_id", accountId.ToString() }
                },
                Request
            );

            return Ok(relationship);
        }
        catch (ArgumentException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_RELATIONSHIP_DELETE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_RELATIONSHIP_DELETE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet("{accountId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnAccountRelationship>> GetRelationship(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var queries = db.AccountRelationships.AsQueryable()
            .Where(r => r.AccountId == currentUser.Id && r.RelatedId == accountId)
            .Where(r => r.ExpiredAt == null || r.ExpiredAt > now);
        var relationship = await queries
            .FirstOrDefaultAsync();
        if (relationship is null) return NotFound(new ApiError { Code = "PASSPORT_RELATIONSHIP_NOT_FOUND", Message = "Relationship not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        await HydrateRelationshipAsync(relationship);
        return Ok(relationship);
    }

    [HttpPost("{accountId:guid}/friends")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsFriendsManage)]
    public async Task<ActionResult<SnAccountRelationship>> SendFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        var existing = await db.AccountRelationships.FirstOrDefaultAsync(r =>
            (r.AccountId == currentUser.Id && r.RelatedId == accountId) ||
            (r.AccountId == accountId && r.RelatedId == currentUser.Id));
        if (existing != null) return BadRequest(new ApiError { Code = "PASSPORT_RELATIONSHIP_ALREADY_EXISTS", Message = "Relationship already exists.", Status = 400, TraceId = HttpContext.TraceIdentifier });

        try
        {
            var relationship = await rls.SendFriendRequest(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_FRIEND_REQUEST_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpDelete("{accountId:guid}/friends")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsFriendsManage)]
    public async Task<ActionResult> DeleteFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            await rls.DeleteFriendRequest(currentUser.Id, accountId);
            return NoContent();
        }
        catch (ArgumentException err)
        {
            return NotFound(new ApiError { Code = "PASSPORT_FRIEND_REQUEST_NOT_FOUND", Message = err.Message, Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{accountId:guid}/friends/accept")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsFriendsManage)]
    public async Task<ActionResult<SnAccountRelationship>> AcceptFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relationship = await rls.GetRelationship(accountId, currentUser.Id, RelationshipStatus.Pending);
        if (relationship is null) return NotFound(new ApiError { Code = "PASSPORT_FRIEND_REQUEST_NOT_FOUND", Message = "Friend request was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            relationship = await rls.AcceptFriendRelationship(relationship);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_FRIEND_ACCEPT_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{accountId:guid}/friends/decline")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsFriendsManage)]
    public async Task<ActionResult<SnAccountRelationship>> DeclineFriendRequest(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relationship = await rls.GetRelationship(accountId, currentUser.Id, RelationshipStatus.Pending);
        if (relationship is null) return NotFound(new ApiError { Code = "PASSPORT_FRIEND_REQUEST_NOT_FOUND", Message = "Friend request was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            relationship = await rls.AcceptFriendRelationship(relationship, status: RelationshipStatus.Blocked);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_FRIEND_DECLINE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{accountId:guid}/block")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsBlockManage)]
    public async Task<ActionResult<SnAccountRelationship>> BlockUser(
        Guid accountId,
        [FromBody] RelationshipActionRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            Duration? expiresIn = null;
            if (request?.ExpiresIn is not null)
                expiresIn = ParseExpiresIn(request.ExpiresIn);

            var relationship = await rls.BlockAccount(currentUser, relatedUser, expiresIn, request?.DegradeTo);
            return relationship;
        }
        catch (ArgumentException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_BLOCK_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_BLOCK_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpDelete("{accountId:guid}/block")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsBlockManage)]
    public async Task<ActionResult<SnAccountRelationship>> UnblockUser(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            var relationship = await rls.UnblockAccount(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_UNBLOCK_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{accountId:guid}/mute")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsMuteManage)]
    public async Task<ActionResult<SnAccountRelationship>> MuteUser(
        Guid accountId,
        [FromBody] RelationshipActionRequest? request = null
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            Duration? expiresIn = null;
            if (request?.ExpiresIn is not null)
                expiresIn = ParseExpiresIn(request.ExpiresIn);

            var relationship = await rls.MuteAccount(currentUser, relatedUser, expiresIn, request?.DegradeTo);
            return relationship;
        }
        catch (ArgumentException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_MUTE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_MUTE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpDelete("{accountId:guid}/mute")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsMuteManage)]
    public async Task<ActionResult<SnAccountRelationship>> UnmuteUser(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            var relationship = await rls.UnmuteAccount(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_UNMUTE_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpPost("{accountId:guid}/close-friend")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsCloseFriendsManage)]
    public async Task<ActionResult<SnAccountRelationship>> AddCloseFriend(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            var relationship = await rls.AddCloseFriend(currentUser, relatedUser);
            return relationship;
        }
        catch (InvalidOperationException err)
        {
            return BadRequest(new ApiError { Code = "PASSPORT_CLOSE_FRIEND_ADD_FAILED", Message = err.Message, Status = 400, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpDelete("{accountId:guid}/close-friend")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsCloseFriendsManage)]
    public async Task<ActionResult<SnAccountRelationship>> RemoveCloseFriend(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var relatedUser = await accounts.GetAccount(accountId);
        if (relatedUser is null) return NotFound(new ApiError { Code = "PASSPORT_RELATED_ACCOUNT_NOT_FOUND", Message = "Account was not found.", Status = 404, TraceId = HttpContext.TraceIdentifier });

        try
        {
            var relationship = await rls.RemoveCloseFriend(currentUser, relatedUser);
            return relationship;
        }
        catch (ArgumentException err)
        {
            return NotFound(new ApiError { Code = "PASSPORT_CLOSE_FRIEND_NOT_FOUND", Message = err.Message, Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet("close-friends")]
    [Authorize]
    public async Task<ActionResult<List<SnAccount>>> ListCloseFriends()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var closeFriendIds = await rls.ListCloseFriends(currentUser.Id);
        if (closeFriendIds.Count == 0)
            return Ok(new List<SnAccount>());

        var accountsList = new List<SnAccount>();
        foreach (var id in closeFriendIds)
        {
            var account = await accounts.GetAccount(id);
            if (account is not null)
                accountsList.Add(account);
        }
        return Ok(accountsList);
    }

    public class InspectRelationshipResponse
    {
        public List<SnAccount> Friends { get; set; } = [];
        public List<SnAccount> Blocked { get; set; } = [];
        public List<SnAccount> Muted { get; set; } = [];
        public List<SnAccount> Pending { get; set; } = [];
        public List<SnAccount> CloseFriends { get; set; } = [];
    }

    [HttpGet("inspect/{accountId:guid}")]
    [Authorize]
    [AskPermission("relationships.inspect")]
    public async Task<ActionResult<InspectRelationshipResponse>> InspectRelationship(Guid accountId)
    {
        var relationships = await db.AccountRelationships
            .Where(r => r.AccountId == accountId)
            .ToListAsync();
        await HydrateRelationshipsAsync(relationships);
        var grouped = relationships
            .GroupBy(r => r.Status)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Related).ToList());

        return Ok(new InspectRelationshipResponse
        {
            Friends = grouped.TryGetValue(RelationshipStatus.Friends, out var friends) ? friends : [],
            Blocked = grouped.TryGetValue(RelationshipStatus.Blocked, out var blocked) ? blocked : [],
            Muted = grouped.TryGetValue(RelationshipStatus.Muted, out var muted) ? muted : [],
            Pending = grouped.TryGetValue(RelationshipStatus.Pending, out var pending) ? pending : [],
            CloseFriends = grouped.TryGetValue(RelationshipStatus.CloseFriend, out var closeFriends) ? closeFriends : []
        });
    }

    public class AliasRequest
    {
        [MaxLength(128)] public string? Alias { get; set; }
    }

    [HttpPatch("{accountId:guid}/alias")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsAliasManage)]
    public async Task<ActionResult<SnAccountRelationship>> UpdateAlias(Guid accountId, [FromBody] AliasRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        try
        {
            var relationship = await rls.UpdateAlias(currentUser.Id, accountId, request.Alias);
            await HydrateRelationshipAsync(relationship);
            return Ok(relationship);
        }
        catch (ArgumentException err)
        {
            return NotFound(new ApiError { Code = "PASSPORT_RELATIONSHIP_NOT_FOUND", Message = err.Message, Status = 404, TraceId = HttpContext.TraceIdentifier });
        }
    }

    [HttpGet("{accountId:guid}/mutual-friends")]
    [Authorize]
    public async Task<ActionResult<List<SnAccount>>> GetMutualFriends(Guid accountId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var mutualIds = await rls.GetMutualFriends(currentUser.Id, accountId);
        if (mutualIds.Count == 0)
            return Ok(new List<SnAccount>());

        var result = new List<SnAccount>();
        foreach (var id in mutualIds)
        {
            var account = await accounts.GetAccount(id);
            if (account is not null)
                result.Add(account);
        }
        return Ok(result);
    }

    public class SyncRequest
    {
        [Required] public long LastSyncTimestamp { get; set; }
    }

    public class RelationshipSyncResponse
    {
        public List<SnAccountRelationship> Added { get; set; } = [];
        public List<SnAccountRelationship> Updated { get; set; } = [];
        public List<Guid> Removed { get; set; } = [];
        public Instant ServerTimestamp { get; set; }
    }

    [HttpPost("sync")]
    [Authorize]
    [AskPermission(PermissionKeys.RelationshipsSync)]
    public async Task<ActionResult<RelationshipSyncResponse>> SyncRelationships([FromBody] SyncRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var since = Instant.FromUnixTimeMilliseconds(request.LastSyncTimestamp);
        var delta = await rls.GetRelationshipDelta(currentUser.Id, since);

        await HydrateRelationshipsAsync(delta.Added);
        await HydrateRelationshipsAsync(delta.Updated);

        return Ok(new RelationshipSyncResponse
        {
            Added = delta.Added,
            Updated = delta.Updated,
            Removed = delta.Removed,
            ServerTimestamp = delta.ServerTimestamp
        });
    }
}
