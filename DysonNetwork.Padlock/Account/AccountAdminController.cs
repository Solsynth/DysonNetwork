using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using SnAccountPunishment = DysonNetwork.Shared.Models.SnAccountPunishment;
using SnAccountProfile = DysonNetwork.Shared.Models.SnAccountProfile;
using PunishmentType = DysonNetwork.Shared.Models.PunishmentType;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.Account;

[ApiController]
[Route("/api/admin/accounts")]
[Authorize]
public class AccountAdminController(
    AppDatabase db,
    AccountService accounts,
    RemoteRingService ring,
    ILocalizationService localizer,
    DyProfileService.DyProfileServiceClient profiles,
    DySocialCreditService.DySocialCreditServiceClient socialCredits,
    DyPublisherService.DyPublisherServiceClient publishers,
    DyPublisherRatingService.DyPublisherRatingServiceClient publisherRatings
) : ControllerBase
{
    [HttpGet("punishments/created")]
    [Authorize]
    [AskPermission("punishments.view")]
    public async Task<ActionResult<List<SnAccountPunishment>>> GetCreatedPunishments(
        [FromQuery] int take = 50,
        [FromQuery] int offset = 0
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var userId = currentUser.Id;

        var query = db.Punishments
            .Where(a => a.CreatorId == userId);

        var total = await query.CountAsync();
        Response.Headers.Append("X-Total", total.ToString());

        var punishments = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        await accounts.HydratePunishmentAccountBatch(punishments);
        return Ok(punishments);
    }

    public class CreatePunishmentRequest
    {
        [MaxLength(8192)] public string Reason { get; set; } = string.Empty;
        public Instant? ExpiredAt { get; set; }
        public PunishmentType Type { get; set; }
        public List<string>? BlockedPermissions { get; set; }
        public double? SocialCreditReduction { get; set; }
        public double? PublisherRatingReduction { get; set; }
        public List<string>? PublisherNames { get; set; }
    }

    [HttpPost("{name}/punishments")]
    [AskPermission("punishments.create")]
    public async Task<ActionResult<SnAccountPunishment>> CreatePunishment(
        string name,
        [FromBody] CreatePunishmentRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishment = new SnAccountPunishment
        {
            AccountId = account.Id,
            CreatorId = currentUser.Id,
            Reason = request.Reason,
            ExpiredAt = request.ExpiredAt,
            Type = request.Type,
            BlockedPermissions = request.BlockedPermissions
        };

        db.Punishments.Add(punishment);
        await db.SaveChangesAsync();

        if (request.Type is PunishmentType.BlockLogin or PunishmentType.DisableAccount)
            await accounts.DeleteAllSessions(account);

        var title = request.Type switch
        {
            PunishmentType.PermissionModification => localizer.Get("punishmentTitlePermissionModification",
                account.Language),
            PunishmentType.BlockLogin => localizer.Get("punishmentTitleBlockLogin", account.Language),
            PunishmentType.DisableAccount => localizer.Get("punishmentTitleDisableAccount", account.Language),
            _ => localizer.Get("punishmentTitleStrike", account.Language)
        };
        var body = request.ExpiredAt.HasValue
            ? localizer.Get("punishmentBodyWithExpiry", locale: account.Language,
                args: new { reason = request.Reason, expiredAt = request.ExpiredAt.Value.ToString() })
            : localizer.Get("punishmentBody", locale: account.Language, args: new { reason = request.Reason });

        if (request.SocialCreditReduction is > 0)
        {
            await socialCredits.AddRecordAsync(new DyAddSocialCreditRecordRequest
            {
                AccountId = account.Id.ToString(),
                Delta = -request.SocialCreditReduction.Value,
                Reason = $"{title} {request.Reason}",
                ReasonType = "punishments",
                ExpiredAt = request.ExpiredAt?.ToTimestamp() ?? SystemClock.Instance.GetCurrentInstant()
                    .Plus(Duration.FromDays(365)).ToTimestamp(),
            });
        }

        if (request.PublisherRatingReduction is > 0 && request.PublisherNames is { Count: > 0 })
        {
            foreach (var publisherName in request.PublisherNames)
            {
                try
                {
                    var publisherResp = await publishers.GetPublisherAsync(
                        new DyGetPublisherRequest { Name = publisherName });
                    var publisherId = publisherResp.Publisher.Id;
                    await publisherRatings.AddRecordAsync(new DyAddPublisherRatingRecordRequest
                    {
                        PublisherId = publisherId,
                        Delta = -request.PublisherRatingReduction.Value,
                        Reason = $"{title} {request.Reason}",
                        ReasonType = "punishments",
                    });
                }
                catch
                {
                    // ignored - publisher may not exist
                }
            }
        }

        try
        {
            await ring.SendPushNotificationToUser(
                account.Id.ToString(),
                "account.punishment",
                title,
                localizer.Get("punishmentTitle", account.Language),
                body,
                isSavable: true
            );
        }
        catch
        {
            // ignored
        }

        var data = new List<SnAccountPunishment> { punishment };
        await accounts.HydratePunishmentAccountBatch(data);
        return Ok(data.First());
    }

    public class UpdatePunishmentRequest
    {
        [MaxLength(8192)] public string? Reason { get; set; }
        public Instant? ExpiredAt { get; set; }
        public PunishmentType? Type { get; set; }
        public List<string>? BlockedPermissions { get; set; }
    }

    [HttpPatch("{name}/punishments/{punishmentId}")]
    [AskPermission("punishments.update")]
    public async Task<ActionResult<SnAccountPunishment>> UpdatePunishment(
        string name,
        Guid punishmentId,
        [FromBody] UpdatePunishmentRequest request
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var punishment = await db.Punishments
            .FirstOrDefaultAsync(p => p.Id == punishmentId && p.AccountId == account.Id);
        if (punishment is null) return NotFound();

        if (request.Reason is not null) punishment.Reason = request.Reason;
        if (request.ExpiredAt is not null) punishment.ExpiredAt = request.ExpiredAt;
        if (request.Type is not null) punishment.Type = request.Type.Value;
        if (request.BlockedPermissions is not null) punishment.BlockedPermissions = request.BlockedPermissions;
        if (punishment.CreatorId != currentUser.Id) punishment.CreatorId = currentUser.Id;

        await db.SaveChangesAsync();

        var data = new List<SnAccountPunishment> { punishment };
        await accounts.HydratePunishmentAccountBatch(data);
        return Ok(data.First());
    }

    [HttpDelete("{name}/punishments/{punishmentId:guid}")]
    [AskPermission("punishments.delete")]
    public async Task<ActionResult> DeletePunishment(string name, Guid punishmentId)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();

        var remoteAccount = await profiles.GetAccountAsync(new DyGetAccountRequest { Id = account.Id.ToString() });
        if (remoteAccount is not null)
        {
            account.Language = remoteAccount.Language;
            account.Profile = remoteAccount.Profile is not null
                ? SnAccountProfile.FromProtoValue(remoteAccount.Profile)
                : null;
        }

        var punishment = await db.Punishments
            .FirstOrDefaultAsync(p => p.Id == punishmentId && p.AccountId == account.Id);
        if (punishment is null) return NotFound();

        var punishmentType = punishment.Type;
        db.Punishments.Remove(punishment);
        await db.SaveChangesAsync();

        var title = localizer.Get("punishmentLiftedTitle", account.Language);
        var body = localizer.Get("punishmentLiftedBody", locale: account.Language,
            args: new { type = punishmentType.ToString() });

        try
        {
            await ring.SendPushNotificationToUser(
                account.Id.ToString(),
                "account.punishment.lifted",
                title,
                null,
                body,
                isSavable: true
            );
        }
        catch
        {
            // ignored
        }

        return Ok();
    }

    [HttpDelete("{name}")]
    [AskPermission("accounts.deletion")]
    public async Task<IActionResult> AdminDeleteAccount(string name)
    {
        var account = await accounts.LookupAccount(name);
        if (account is null) return NotFound();
        await accounts.DeleteAccount(account);
        return Ok();
    }
}