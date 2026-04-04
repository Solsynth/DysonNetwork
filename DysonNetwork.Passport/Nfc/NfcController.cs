using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Nfc;

[ApiController]
[Route("/api/nfc")]
public class NfcController(
    NfcService nfc,
    AppDatabase db,
    AccountService accountService,
    RemoteSubscriptionService remoteSubscription
) : ControllerBase
{
    public class NfcResolveResponse
    {
        public Guid Id { get; set; }
        public SnAccount Account { get; set; } = null!;
        public bool IsFriend { get; set; }
        public bool IsClaimed { get; set; }
        public List<string> Actions { get; set; } = [];
    }

    public class NfcTagResponse
    {
        public Guid Id { get; set; }
        public string Uid { get; set; } = string.Empty;
        public string? Label { get; set; }
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public bool IsEncrypted { get; set; }
        public string? SunKey { get; set; }
        public Guid? UserId { get; set; }
        public NodaTime.Instant? LastSeenAt { get; set; }
        public NodaTime.Instant CreatedAt { get; set; }
    }

    public class RegisterTagRequest
    {
        [Required]
        [MaxLength(64)]
        public string Uid { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? Label { get; set; }
    }

    public class UpdateTagRequest
    {
        [MaxLength(64)]
        public string? Label { get; set; }

        public bool? IsActive { get; set; }
    }

    public class ClaimTagRequest
    {
        [Required]
        [MaxLength(64)]
        public string Uid { get; set; } = string.Empty;
    }

    private async Task<ActionResult<NfcResolveResponse>> ToResponseAsync(NfcResolveResult result)
    {
        var account = await accountService.GetAccount(result.Account.Id);
        if (account is null)
            return StatusCode(500, ApiError.Server("Failed to load account data."));

        await EnsureProfileAsync(account);
        account.Badges = await db.Badges.Where(b => b.AccountId == account.Id).ToListAsync();
        account.Contacts = [];

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
            Console.WriteLine($"Failed to populate PerkSubscription for account {account.Id}: {ex.Message}");
        }

        return Ok(new NfcResolveResponse
        {
            Account = account,
            IsFriend = result.IsFriend,
            IsClaimed = result.IsClaimed,
            Actions = result.Actions
        });
    }

    private async Task EnsureProfileAsync(SnAccount account)
    {
        if (account.Profile is not null) return;
        account.Profile = await accountService.GetOrCreateAccountProfileAsync(account.Id);
    }

    /// <summary>
    /// Resolve an NFC tag to a user profile.
    /// The uid parameter works for both encrypted and unencrypted tags:
    /// - If it looks like encrypted PICCData (32+ hex chars), attempts SDM decryption
    /// - Otherwise, looks up by tag UID directly
    /// No authentication required. If caller is authenticated, relationship info is included.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<NfcResolveResponse>> Resolve(
        [FromQuery] string uid,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(uid))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["uid"] = ["Parameter 'uid' is required."]
            }));

        Guid? observerUserId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            observerUserId = currentUser.Id;

        // Check if this looks like encrypted PICCData (32+ hex chars)
        if (uid.Length >= 32 && IsHexString(uid))
        {
            // Try encrypted scan
            try
            {
                var result = await nfc.ValidateSunAsync(uid, observerUserId, cancellationToken);

                if (result is not null)
                {
                    // Handle unclaimed/pre-assigned tag states
                    if (result.ClaimStatus == NfcTagClaimStatus.NeedsAuth)
                    {
                        return StatusCode(403, new ApiError
                        {
                            Code = "TAG_UNCLAIMED",
                            Message = "This tag is not yet associated with an account. Please sign in to claim it.",
                            Status = 403
                        });
                    }

                    if (result.ClaimStatus == NfcTagClaimStatus.Unclaimed)
                    {
                        // Return 200 with null user - client will show "unclaimed" and prompt to claim
                        return Ok(new NfcResolveResponse
                        {
                            Account = null!,
                            IsFriend = false,
                            IsClaimed = false,
                            Actions = result.Actions
                        });
                    }

                    if (result.ClaimStatus == NfcTagClaimStatus.PreAssignedMismatch)
                    {
                        return StatusCode(403, new ApiError
                        {
                            Code = "TAG_PRE_ASSIGNED",
                            Message = "This tag is assigned to a different account.",
                            Status = 403
                        });
                    }

                    if (result.Account is null)
                        return NotFound(ApiError.NotFound("nfc_tag", "Tag owner not found."));

                    var account = await accountService.GetAccount(result.Account.Id);
                    if (account is null)
                        return StatusCode(500, ApiError.Server("Failed to load account data."));

                    await EnsureProfileAsync(account);
                    account.Badges = await db.Badges.Where(b => b.AccountId == account.Id).ToListAsync();
                    account.Contacts = [];

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
                        Console.WriteLine($"Failed to populate PerkSubscription for account {account.Id}: {ex.Message}");
                    }

                    return Ok(new NfcResolveResponse
                    {
                        Account = account,
                        IsFriend = result.IsFriend,
                        IsClaimed = result.IsClaimed,
                        Actions = result.Actions
                    });
                }
                // If encrypted scan didn't match, fall through to unencrypted lookup
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
                {
                    ["counter"] = [ex.Message]
                }));
            }
        }

        // Unencrypted scan: look up by UID
        {
            var result = await nfc.ResolveAsync(uid, observerUserId, cancellationToken);

            if (result is null)
                return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

            return await ToResponseAsync(result);
        }
    }

    /// <summary>
    /// Check if a string contains only hexadecimal characters.
    /// </summary>
    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Look up a tag by UID (admin/debug/testing only).
    /// </summary>
    [HttpGet("lookup")]
    public async Task<ActionResult<NfcResolveResponse>> Lookup(
        [FromQuery] string uid,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(uid))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["uid"] = ["Parameter 'uid' is required."]
            }));

        Guid? observerUserId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            observerUserId = currentUser.Id;

        var result = await nfc.LookupByUidAsync(uid, observerUserId, cancellationToken);

        if (result is null)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

        return await ToResponseAsync(result);
    }

    /// <summary>
    /// Look up a tag by its database entry ID (for unencrypted/plain tags).
    /// </summary>
    [HttpGet("tags/{id:guid}")]
    public async Task<ActionResult<NfcResolveResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken
    )
    {
        Guid? observerUserId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            observerUserId = currentUser.Id;

        var result = await nfc.LookupByIdAsync(id, observerUserId, cancellationToken);

        if (result is null)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

        return await ToResponseAsync(result);
    }

    /// <summary>
    /// List all registered NFC tags for the current user.
    /// </summary>
    [HttpGet("tags")]
    [Authorize]
    public async Task<ActionResult<List<NfcTagResponse>>> ListTags(
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var tags = await nfc.ListTagsAsync(currentUser.Id, cancellationToken);

        return Ok(tags.Select(t => new NfcTagResponse
        {
            Id = t.Id,
            Uid = t.Uid,
            Label = t.Label,
            IsActive = t.IsActive,
            IsLocked = t.LockedAt.HasValue,
            IsEncrypted = t.IsEncrypted,
            LastSeenAt = t.LastSeenAt,
            CreatedAt = t.CreatedAt
        }));
    }

    /// <summary>
    /// Register a new NFC tag for the current user.
    /// </summary>
    [HttpPost("tags")]
    [Authorize]
    public async Task<ActionResult<NfcTagResponse>> RegisterTag(
        [FromBody] RegisterTagRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        try
        {
            var tag = await nfc.RegisterTagAsync(
                currentUser.Id,
                request.Uid,
                request.Label,
                cancellationToken);

            return Ok(new NfcTagResponse
            {
                Id = tag.Id,
                Uid = tag.Uid,
                Label = tag.Label,
                IsActive = tag.IsActive,
                IsLocked = tag.LockedAt.HasValue,
                IsEncrypted = tag.IsEncrypted,
                LastSeenAt = tag.LastSeenAt,
                CreatedAt = tag.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ApiError
            {
                Code = "NFC_TAG_EXISTS",
                Message = ex.Message,
                Status = 409
            });
        }
    }

    /// <summary>
    /// Claim an unclaimed encrypted NFC tag by UID (without scanning).
    /// For factory-produced tags where the user knows the tag's UID.
    /// </summary>
    [HttpPost("tags/claim")]
    [Authorize]
    public async Task<ActionResult<NfcTagResponse>> ClaimTag(
        [FromBody] ClaimTagRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        try
        {
            var tag = await nfc.ClaimTagByUidAsync(
                request.Uid,
                currentUser.Id,
                cancellationToken);

            return Ok(new NfcTagResponse
            {
                Id = tag.Id,
                Uid = tag.Uid,
                Label = tag.Label,
                IsActive = tag.IsActive,
                IsLocked = tag.LockedAt.HasValue,
                IsEncrypted = tag.IsEncrypted,
                LastSeenAt = tag.LastSeenAt,
                CreatedAt = tag.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError
            {
                Code = "TAG_CLAIM_FAILED",
                Message = ex.Message,
                Status = 400
            });
        }
    }

    /// <summary>
    /// Update an NFC tag (label, active status).
    /// </summary>
    [HttpPatch("tags/{tagId:guid}")]
    [Authorize]
    public async Task<ActionResult<NfcTagResponse>> UpdateTag(
        Guid tagId,
        [FromBody] UpdateTagRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var tag = await nfc.UpdateTagAsync(
            currentUser.Id,
            tagId,
            request.Label,
            request.IsActive,
            cancellationToken);

        if (tag is null)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

        return Ok(new NfcTagResponse
        {
            Id = tag.Id,
            Uid = tag.Uid,
            Label = tag.Label,
            IsActive = tag.IsActive,
            IsLocked = tag.LockedAt.HasValue,
            IsEncrypted = tag.IsEncrypted,
            LastSeenAt = tag.LastSeenAt,
            CreatedAt = tag.CreatedAt
        });
    }

    /// <summary>
    /// Lock an NFC tag to prevent physical reprogramming.
    /// </summary>
    [HttpPost("tags/{tagId:guid}/lock")]
    [Authorize]
    public async Task<ActionResult<NfcTagResponse>> LockTag(
        Guid tagId,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var tag = await nfc.LockTagAsync(currentUser.Id, tagId, cancellationToken);

        if (tag is null)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

        return Ok(new NfcTagResponse
        {
            Id = tag.Id,
            Uid = tag.Uid,
            Label = tag.Label,
            IsActive = tag.IsActive,
            IsLocked = tag.LockedAt.HasValue,
            IsEncrypted = tag.IsEncrypted,
            LastSeenAt = tag.LastSeenAt,
            CreatedAt = tag.CreatedAt
        });
    }

    /// <summary>
    /// Unregister (soft-delete) an NFC tag.
    /// </summary>
    [HttpDelete("tags/{tagId:guid}")]
    [Authorize]
    public async Task<ActionResult> UnregisterTag(
        Guid tagId,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var deleted = await nfc.UnregisterTagAsync(currentUser.Id, tagId, cancellationToken);
        if (!deleted)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

        return NoContent();
    }
}
