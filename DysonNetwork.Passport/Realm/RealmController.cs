using System.ComponentModel.DataAnnotations;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Globalization;
using ActionLogService = DysonNetwork.Passport.Account.ActionLogService;

namespace DysonNetwork.Passport.Realm;

[ApiController]
[Route("/api/realms")]
public class RealmController(
    AppDatabase db,
    RealmService rs,
    RealmQuotaService quotaService,
    AccountService accounts,
    DyFileService.DyFileServiceClient files,
    ActionLogService als,
    RelationshipService rels,
    AccountEventService accountEvents,
    RemotePaymentService payments
) : Controller
{
    [HttpGet("quota")]
    [Authorize]
    public async Task<ActionResult<ResourceQuotaResponse<RealmQuotaRecord>>> GetQuota()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var account = await accounts.GetAccount(currentUser.Id);
        if (account is null) return Unauthorized();
        if (account.PerkLevel == 0 && currentUser.PerkLevel > 0) account.PerkLevel = currentUser.PerkLevel;

        return Ok(await quotaService.GetQuotaAsync(account));
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<SnRealm>> GetRealm(string slug)
    {
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();

        return Ok(realm);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnRealm>>> ListJoinedRealms()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var members = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .Include(e => e.Realm)
            .Select(m => m.Realm)
            .ToListAsync();

        return members.ToList();
    }

    [HttpGet("invites")]
    [Authorize]
    public async Task<ActionResult<List<SnRealmMember>>> ListInvites()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var members = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.JoinedAt == null && m.LeaveAt == null)
            .Include(e => e.Realm)
            .ToListAsync();

        return await rs.LoadMemberAccounts(members);
    }

    public class RealmMemberRequest
    {
        [Required] public Guid RelatedUserId { get; set; }
        [Required] public int Role { get; set; }
    }

    public class RealmMemberProfileRequest
    {
        [MaxLength(1024)] public string? Nick { get; set; }
        [MaxLength(4096)] public string? Bio { get; set; }
    }

    public class RealmLabelRequest
    {
        [Required, MaxLength(1024)] public string Name { get; set; } = string.Empty;
        [MaxLength(4096)] public string? Description { get; set; }
        [MaxLength(64)] public string? Color { get; set; }
        [MaxLength(256)] public string? Icon { get; set; }
    }

    public class RealmBoostRequest
    {
        [Range(1, 1000000)] public int Shares { get; set; }
        [MaxLength(128)] public string? Currency { get; set; }
    }

    public class RealmBoostResponse
    {
        public Guid OrderId { get; set; }
        public int Shares { get; set; }
        public string Currency { get; set; } = RealmBoostPolicy.DefaultCurrency;
        public decimal Amount { get; set; }
    }

    public class RealmLabelAssignmentRequest
    {
        public Guid? LabelId { get; set; }
    }

    [HttpPost("invites/{slug}")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> InviteMember(string slug,
        [FromBody] RealmMemberRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var relatedUser = await accounts.GetAccount(request.RelatedUserId);
        if (relatedUser == null) return BadRequest("Related user was not found");

        var hasBlocked = await rels.HasRelationshipWithStatus(
            currentUser.Id,
            request.RelatedUserId,
            RelationshipStatus.Blocked
        );
        if (hasBlocked)
            return StatusCode(403, "You cannot invite a user that blocked you.");

        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (request.Role > RealmMemberRole.Normal && realm.BoostLevel < 2)
            return StatusCode(403, "Realm boost level 2 is required to invite promoted members.");

        if (!await rs.IsMemberWithRole(realm.Id, accountId, request.Role))
            return StatusCode(403, "You cannot invite member has higher permission than yours.");

        var existingMember = await db.RealmMembers
            .Where(m => m.AccountId == relatedUser.Id)
            .Where(m => m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (existingMember != null)
        {
            if (existingMember.LeaveAt == null)
                return BadRequest("This user already in the realm cannot be invited again.");

            existingMember.LeaveAt = null;
            existingMember.JoinedAt = null;
            db.RealmMembers.Update(existingMember);
            await db.SaveChangesAsync();
            await rs.SendInviteNotify(existingMember);

            als.CreateActionLogFromRequest(
                "realms.members.invite",
                new Dictionary<string, object>()
                {
                    { "realm_id", Value.ForString(realm.Id.ToString()) },
                    { "account_id", Value.ForString(existingMember.AccountId.ToString()) },
                    { "role", Value.ForNumber(request.Role) }
                },
                Request
            );

            return Ok(existingMember);
        }

        var member = new SnRealmMember
        {
            AccountId = relatedUser.Id,
            RealmId = realm.Id,
            Role = request.Role,
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();
        
        als.CreateActionLogFromRequest(
            "realms.members.invite",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "role", Value.ForNumber(request.Role) }
            },
            Request
        );

        member.AccountId = relatedUser.Id;
        member.Realm = realm;
        await rs.SendInviteNotify(member);

        return Ok(member);
    }

    [HttpPost("invites/{slug}/accept")]
    [Authorize]
    public async Task<ActionResult<SnRealm>> AcceptMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow);
        db.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.join",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) }
            },
            Request
        );

        return Ok(member);
    }

    [HttpPost("invites/{slug}/decline")]
    [Authorize]
    public async Task<ActionResult> DeclineMemberInvite(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.decline_invite",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "decliner_id", Value.ForString(currentUser.Id.ToString()) }
            },
            Request
        );

        return NoContent();
    }


    [HttpGet("{slug}/members")]
    public async Task<ActionResult<List<SnRealmMember>>> ListMembers(
        string slug,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] bool withStatus = false
    )
    {
        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Normal))
                return StatusCode(403, "You must be a member to view this realm's members.");
        }

        // The query should include the unjoined ones, to show the invites.
        var query = db.RealmMembers
            .Include(m => m.Label)
            .Where(m => m.RealmId == realm.Id)
            .Where(m => m.LeaveAt == null);

        if (withStatus)
        {
            var members = await query
                .OrderByDescending(m => m.Experience)
                .ThenBy(m => m.JoinedAt)
                .ToListAsync();

            var memberStatuses = await accountEvents.GetStatuses(
                members.Select(m => m.AccountId).ToList()
            );

            members = members
                .Select(m =>
                {
                    m.Status = memberStatuses.TryGetValue(m.AccountId, out var s) ? s : null;
                    return m;
                })
                .OrderByDescending(m => m.Status?.IsOnline ?? false)
                .ThenByDescending(m => m.Level)
                .ThenByDescending(m => m.Experience)
                .ToList();

            var total = members.Count;
            Response.Headers.Append("X-Total", total.ToString());

            var result = members.Skip(offset).Take(take).ToList();

            members = await rs.LoadMemberAccounts(result);

            return Ok(members.Where(m => m.Account is not null).ToList());
        }
        else
        {
            var total = await query.CountAsync();
            Response.Headers["X-Total"] = total.ToString();

            var members = await query
                .OrderByDescending(m => m.Experience)
                .ThenBy(m => m.CreatedAt)
                .Skip(offset)
                .Take(take)
                .ToListAsync();
            members = await rs.LoadMemberAccounts(members);

            return Ok(members.Where(m => m.Account is not null).ToList());
        }
    }


    [HttpGet("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> GetCurrentIdentity(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();

        if (member is null) return NotFound();
        return Ok(await rs.LoadMemberAccount(member));
    }

    [HttpPatch("{slug}/members/me/profile")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> UpdateCurrentIdentity(string slug, [FromBody] RealmMemberProfileRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (currentUser.PerkLevel < 2)
            return StatusCode(403, "Perk level 2 is required to customize realm profile.");

        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();

        var member = await rs.GetActiveMember(realm.Id, currentUser.Id);
        if (member is null) return NotFound();

        member.Nick = request.Nick;
        member.Bio = request.Bio;
        await db.SaveChangesAsync();

        return Ok(await rs.LoadMemberAccount(member));
    }

    [HttpGet("{slug}/labels")]
    public async Task<ActionResult<List<SnRealmLabel>>> ListLabels(string slug)
    {
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();

        if (!realm.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Normal))
                return StatusCode(403, "You must be a member to view this realm.");
        }

        var labels = await db.RealmLabels
            .Where(l => l.RealmId == realm.Id)
            .OrderBy(l => l.Name)
            .ToListAsync();

        return Ok(labels);
    }

    [HttpPost("{slug}/labels")]
    [Authorize]
    public async Task<ActionResult<SnRealmLabel>> CreateLabel(string slug, [FromBody] RealmLabelRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator))
            return StatusCode(403, "You do not have permission to manage labels.");
        if (realm.BoostLevel < 1)
            return StatusCode(403, "Realm boost level 1 is required to manage labels.");

        var labelCap = RealmBoostPolicy.GetLabelCap(realm.BoostLevel);
        var labelCount = await rs.GetRealmLabelCount(realm.Id);
        if (labelCount >= labelCap)
            return BadRequest("Realm label limit reached for current boost level.");

        var label = new SnRealmLabel
        {
            RealmId = realm.Id,
            Name = request.Name,
            Description = request.Description,
            Color = request.Color,
            Icon = request.Icon,
            CreatedByAccountId = currentUser.Id
        };
        db.RealmLabels.Add(label);
        await db.SaveChangesAsync();
        return Ok(label);
    }

    [HttpPatch("{slug}/labels/{labelId:guid}")]
    [Authorize]
    public async Task<ActionResult<SnRealmLabel>> UpdateLabel(string slug, Guid labelId, [FromBody] RealmLabelRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator))
            return StatusCode(403, "You do not have permission to manage labels.");
        if (realm.BoostLevel < 1)
            return StatusCode(403, "Realm boost level 1 is required to manage labels.");

        var label = await db.RealmLabels.FirstOrDefaultAsync(l => l.Id == labelId && l.RealmId == realm.Id);
        if (label is null) return NotFound();

        label.Name = request.Name;
        label.Description = request.Description;
        label.Color = request.Color;
        label.Icon = request.Icon;
        await db.SaveChangesAsync();

        return Ok(label);
    }

    [HttpDelete("{slug}/labels/{labelId:guid}")]
    [Authorize]
    public async Task<ActionResult> DeleteLabel(string slug, Guid labelId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator))
            return StatusCode(403, "You do not have permission to manage labels.");
        if (realm.BoostLevel < 1)
            return StatusCode(403, "Realm boost level 1 is required to manage labels.");

        var label = await db.RealmLabels.FirstOrDefaultAsync(l => l.Id == labelId && l.RealmId == realm.Id);
        if (label is null) return NotFound();

        await db.RealmMembers
            .Where(m => m.RealmId == realm.Id && m.LabelId == label.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.LabelId, m => (Guid?)null));
        db.RealmLabels.Remove(label);
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{slug}/members/{memberId:guid}/label")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> UpdateMemberLabel(string slug, Guid memberId, [FromBody] RealmLabelAssignmentRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator))
            return StatusCode(403, "You do not have permission to manage labels.");
        if (realm.BoostLevel < 1)
            return StatusCode(403, "Realm boost level 1 is required to manage labels.");

        var member = await rs.GetActiveMember(realm.Id, memberId);
        if (member is null) return NotFound();

        if (request.LabelId.HasValue)
        {
            var label = await db.RealmLabels.FirstOrDefaultAsync(l => l.Id == request.LabelId.Value && l.RealmId == realm.Id);
            if (label is null) return BadRequest("Label does not belong to this realm.");
            member.LabelId = label.Id;
        }
        else
        {
            member.LabelId = null;
        }

        await db.SaveChangesAsync();
        return Ok(await rs.LoadMemberAccount(member));
    }

    [HttpPost("{slug}/boosts")]
    [Authorize]
    public async Task<ActionResult<RealmBoostResponse>> BoostRealm(string slug, [FromBody] RealmBoostRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Normal))
            return StatusCode(403, "You must be a member to boost this realm.");

        string currency;
        try
        {
            currency = RealmBoostPolicy.NormalizeCurrency(request.Currency);
        }
        catch (ArgumentException)
        {
            return BadRequest("Unsupported boost currency. Use golds or points.");
        }

        var amount = RealmBoostPolicy.GetAmountForShares(currency, request.Shares);
        var order = await payments.CreateOrder(
            currency: currency,
            amount: amount.ToString(CultureInfo.InvariantCulture),
            productIdentifier: "realms.boost",
            remarks: $"Boost realm {realm.Name}",
            meta: InfraObjectCoder.ConvertObjectToByteString(
                new Dictionary<string, object?>
                {
                    ["realm_id"] = realm.Id,
                    ["account_id"] = currentUser.Id,
                    ["shares"] = request.Shares,
                    ["currency"] = currency,
                    ["amount"] = amount.ToString(CultureInfo.InvariantCulture)
                }
            ).ToByteArray()
        );

        return Ok(new RealmBoostResponse
        {
            OrderId = Guid.Parse(order.Id),
            Shares = request.Shares,
            Currency = currency,
            Amount = amount
        });
    }

    [HttpGet("{slug}/boosts")]
    public async Task<ActionResult<object>> GetBoostStatus(string slug)
    {
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();

        return Ok(new
        {
            boost_points = realm.BoostPoints,
            boost_level = realm.BoostLevel,
            label_cap = RealmBoostPolicy.GetLabelCap(realm.BoostLevel),
            expires_after_days = RealmBoostPolicy.ExpirationDays,
            supported_currencies = new[]
            {
                RealmBoostPolicy.GoldsCurrency,
                RealmBoostPolicy.PointsCurrency
            },
            default_currency = RealmBoostPolicy.DefaultCurrency
        });
    }

    [HttpGet("{slug}/boosts/leaderboard")]
    public async Task<ActionResult<object>> GetBoostLeaderboard(string slug, [FromQuery] int take = 20)
    {
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        var cutoff = RealmBoostPolicy.GetActiveCutoff(SystemClock.Instance.GetCurrentInstant());

        var contributions = await db.RealmBoostContributions
            .Where(c => c.RealmId == realm.Id)
            .Where(c => c.CreatedAt >= cutoff)
            .ToListAsync();
        var leaderboard = contributions
            .GroupBy(c => c.AccountId)
            .Select(g => new
            {
                account_id = g.Key,
                amount_golds = g
                    .Where(x => RealmBoostPolicy.NormalizeCurrency(x.Currency) == RealmBoostPolicy.GoldsCurrency)
                    .Sum(x => x.Amount),
                amount_points = g
                    .Where(x => RealmBoostPolicy.NormalizeCurrency(x.Currency) == RealmBoostPolicy.PointsCurrency)
                    .Sum(x => x.Amount),
                shares = g.Sum(x => x.Shares),
                boosts = g.Count(),
                last_boosted_at = g.Max(x => x.CreatedAt)
            })
            .OrderByDescending(x => x.shares)
            .ThenByDescending(x => x.last_boosted_at)
            .Take(take)
            .ToList();

        var accountDict = new Dictionary<Guid, SnAccount?>();
        foreach (var row in leaderboard)
            accountDict[row.account_id] = await accounts.GetAccount(row.account_id);

        return Ok(leaderboard.Select(row => new
        {
            row.account_id,
            account = accountDict.GetValueOrDefault(row.account_id),
            row.amount_golds,
            row.amount_points,
            row.shares,
            row.boosts,
            row.last_boosted_at
        }));
    }

    [HttpGet("{slug}/members/{memberId:guid}/experience")]
    public async Task<ActionResult<List<SnRealmExperienceRecord>>> GetMemberExperience(string slug, Guid memberId, [FromQuery] int take = 50)
    {
        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();

        if (!realm.IsPublic)
        {
            if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
            if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Normal))
                return StatusCode(403, "You must be a member to view this realm.");
        }

        var records = await db.RealmExperienceRecords
            .Where(r => r.RealmId == realm.Id && r.AccountId == memberId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync();

        return Ok(records);
    }

    [HttpDelete("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult> LeaveRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        var accountId = currentUser.Id;

        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId)
            .Where(m => m.Realm.Slug == slug)
            .Where(m => m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (member.Role == RealmMemberRole.Owner)
            return StatusCode(403, "Owner cannot leave their own realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.leave",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(member.RealmId.ToString()) },
                { "account_id", Value.ForString(member.AccountId.ToString()) },
                { "leaver_id", Value.ForString(currentUser.Id.ToString()) }
            },
            Request
        );

        return NoContent();
    }

    public class RealmRequest
    {
        [MaxLength(1024)] public string? Slug { get; set; }
        [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        public string? PictureId { get; set; }
        public string? BackgroundId { get; set; }
        public bool? IsCommunity { get; set; }
        public bool? IsPublic { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnRealm>> CreateRealm(RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("You cannot create a realm without a name.");
        if (string.IsNullOrWhiteSpace(request.Slug)) return BadRequest("You cannot create a realm without a slug.");

        var account = await accounts.GetAccount(currentUser.Id);
        if (account is null) return Unauthorized();
        if (account.PerkLevel == 0 && currentUser.PerkLevel > 0) account.PerkLevel = currentUser.PerkLevel;

        var quota = await quotaService.GetQuotaAsync(account);
        if (quota.Used >= quota.Total)
            return StatusCode(403, $"Realm quota exceeded ({quota.Used}/{quota.Total}).");

        var slugExists = await db.Realms.AnyAsync(r => r.Slug == request.Slug);
        if (slugExists) return BadRequest("Realm with this slug already exists.");

        var realm = new SnRealm
        {
            Name = request.Name!,
            Slug = request.Slug!,
            Description = request.Description!,
            AccountId = currentUser.Id,
            IsCommunity = request.IsCommunity ?? false,
            IsPublic = request.IsPublic ?? false,
            Members = new List<SnRealmMember>
            {
                new()
                {
                    Role = RealmMemberRole.Owner,
                    AccountId = currentUser.Id,
                    JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        if (request.PictureId is not null)
        {
            var pictureResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            if (pictureResult is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");
            realm.Picture = SnCloudFileReferenceObject.FromProtoValue(pictureResult);
        }

        if (request.BackgroundId is not null)
        {
            var backgroundResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (backgroundResult is null) return BadRequest("Invalid background id, unable to find the file on cloud.");
            realm.Background = SnCloudFileReferenceObject.FromProtoValue(backgroundResult);
        }

        db.Realms.Add(realm);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.create",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "name", Value.ForString(realm.Name) },
                { "slug", Value.ForString(realm.Slug) },
                { "is_community", Value.ForBool(realm.IsCommunity) },
                { "is_public", Value.ForBool(realm.IsPublic) }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpPatch("{slug}")]
    [Authorize]
    public async Task<ActionResult<SnRealm>> Update(string slug, [FromBody] RealmRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var accountId = currentUser.Id;
        var member = await db.RealmMembers
            .Where(m => m.AccountId == accountId && m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null || member.Role < RealmMemberRole.Moderator)
            return StatusCode(403, "You do not have permission to update this realm.");

        if (request.Slug is not null && request.Slug != realm.Slug)
        {
            var slugExists = await db.Realms.AnyAsync(r => r.Slug == request.Slug);
            if (slugExists) return BadRequest("Realm with this slug already exists.");
            realm.Slug = request.Slug;
        }

        if (request.Name is not null)
            realm.Name = request.Name;
        if (request.Description is not null)
            realm.Description = request.Description;
        if (request.IsCommunity is not null)
            realm.IsCommunity = request.IsCommunity.Value;
        if (request.IsPublic is not null)
            realm.IsPublic = request.IsPublic.Value;

        if (request.PictureId is not null)
        {
            var pictureResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.PictureId });
            if (pictureResult is null) return BadRequest("Invalid picture id, unable to find the file on cloud.");

            realm.Picture = SnCloudFileReferenceObject.FromProtoValue(pictureResult);
        }

        if (request.BackgroundId is not null)
        {
            var backgroundResult = await files.GetFileAsync(new DyGetFileRequest { Id = request.BackgroundId });
            if (backgroundResult is null) return BadRequest("Invalid background id, unable to find the file on cloud.");

            realm.Background = SnCloudFileReferenceObject.FromProtoValue(backgroundResult);
        }

        db.Realms.Update(realm);
        await db.SaveChangesAsync();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.update",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "name_updated", Value.ForBool(request.Name != null) },
                { "slug_updated", Value.ForBool(request.Slug != null) },
                { "description_updated", Value.ForBool(request.Description != null) },
                { "picture_updated", Value.ForBool(request.PictureId != null) },
                { "background_updated", Value.ForBool(request.BackgroundId != null) },
                { "is_community_updated", Value.ForBool(request.IsCommunity != null) },
                { "is_public_updated", Value.ForBool(request.IsPublic != null) }
            },
            Request
        );

        return Ok(realm);
    }

    [HttpPost("{slug}/members/me")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> JoinRealm(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!realm.IsCommunity)
            return StatusCode(403, "Only community realms can be joined without invitation.");

        var existingMember = await db.RealmMembers
            .Where(m => m.AccountId == currentUser.Id && m.RealmId == realm.Id)
            .FirstOrDefaultAsync();
        if (existingMember is not null)
        {
            if (existingMember.LeaveAt == null)
                return BadRequest("You are already a member of this realm.");

            existingMember.LeaveAt = null;
            existingMember.JoinedAt = SystemClock.Instance.GetCurrentInstant();

            db.Update(existingMember);
            await db.SaveChangesAsync();

            als.CreateActionLogFromRequest(
                "realms.members.join",
                new Dictionary<string, object>()
                {
                    { "realm_id", Value.ForString(existingMember.RealmId.ToString()) },
                    { "account_id", Value.ForString(currentUser.Id.ToString()) },
                    { "is_community", Value.ForBool(realm.IsCommunity) }
                },
                Request
            );

            return Ok(existingMember);
        }

        var member = new SnRealmMember
        {
            AccountId = currentUser.Id,
            RealmId = realm.Id,
            Role = RealmMemberRole.Normal,
            JoinedAt = NodaTime.Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        db.RealmMembers.Add(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.join",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(currentUser.Id.ToString()) },
                { "is_community", Value.ForBool(realm.IsCommunity) }
            },
            Request
        );

        return Ok(member);
    }

    [HttpDelete("{slug}/members/{memberId:guid}")]
    [Authorize]
    public async Task<ActionResult> RemoveMember(string slug, Guid memberId)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator, member.Role))
            return StatusCode(403, "You do not have permission to remove members from this realm.");

        member.LeaveAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.kick",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(memberId.ToString()) },
                { "kicker_id", Value.ForString(currentUser.Id.ToString()) }
            },
            Request
        );

        return NoContent();
    }

    [HttpPatch("{slug}/members/{memberId:guid}/role")]
    [Authorize]
    public async Task<ActionResult<SnRealmMember>> UpdateMemberRole(string slug, Guid memberId, [FromBody] int newRole)
    {
        if (newRole >= RealmMemberRole.Owner) return BadRequest("Unable to set realm member to owner or greater role.");
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var realm = await rs.GetBySlug(slug);
        if (realm is null) return NotFound();
        if (newRole > RealmMemberRole.Normal && realm.BoostLevel < 2)
            return StatusCode(403, "Realm boost level 2 is required to promote members.");

        var member = await db.RealmMembers
            .Where(m => m.AccountId == memberId && m.RealmId == realm.Id && m.JoinedAt != null && m.LeaveAt == null)
            .FirstOrDefaultAsync();
        if (member is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Moderator, member.Role,
                newRole))
            return StatusCode(403, "You do not have permission to update member roles in this realm.");

        member.Role = newRole;
        db.RealmMembers.Update(member);
        await db.SaveChangesAsync();

        als.CreateActionLogFromRequest(
            "realms.members.role_update",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "account_id", Value.ForString(memberId.ToString()) },
                { "new_role", Value.ForNumber(newRole) },
                { "updater_id", Value.ForString(currentUser.Id.ToString()) }
            },
            Request
        );

        return Ok(member);
    }

    [HttpDelete("{slug}")]
    [Authorize]
    public async Task<ActionResult> Delete(string slug)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var transaction = await db.Database.BeginTransactionAsync();

        var realm = await db.Realms
            .Where(r => r.Slug == slug)
            .FirstOrDefaultAsync();
        if (realm is null) return NotFound();

        if (!await rs.IsMemberWithRole(realm.Id, currentUser.Id, RealmMemberRole.Owner))
            return StatusCode(403, "Only the owner can delete this realm.");

        try
        {
            db.Realms.Remove(realm);
            await db.SaveChangesAsync();

            var now = SystemClock.Instance.GetCurrentInstant();
            await db.RealmMembers
                .Where(m => m.RealmId == realm.Id)
                .ExecuteUpdateAsync(m => m.SetProperty(m => m.DeletedAt, now));
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            throw;
        }

        als.CreateActionLogFromRequest(
            "realms.delete",
            new Dictionary<string, object>()
            {
                { "realm_id", Value.ForString(realm.Id.ToString()) },
                { "realm_name", Value.ForString(realm.Name) },
                { "realm_slug", Value.ForString(realm.Slug) }
            },
            Request
        );

        return NoContent();
    }
}
