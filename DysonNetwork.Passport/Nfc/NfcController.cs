using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Nfc;

[ApiController]
[Route("/api/nfc")]
public class NfcController(NfcService nfc) : ControllerBase
{
    public class NfcResolveResponse
    {
        public NfcUserDto User { get; set; } = null!;
        public bool IsFriend { get; set; }
        public List<string> Actions { get; set; } = [];
    }

    public class NfcUserDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Nick { get; set; }
        public SnCloudFileReferenceObject? Picture { get; set; }
        public string? Bio { get; set; }
    }

    public class NfcTagDto
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public NodaTime.Instant? LastSeenAt { get; set; }
        public NodaTime.Instant CreatedAt { get; set; }
    }

    public class RegisterTagRequest
    {
        [Required]
        [MaxLength(32)]
        public string Uid { get; set; } = string.Empty;

        [Required]
        public string SunKey { get; set; } = string.Empty;

        [MaxLength(64)]
        public string? Label { get; set; }
    }

    public class UpdateTagRequest
    {
        [MaxLength(64)]
        public string? Label { get; set; }

        public bool? IsActive { get; set; }
    }

    /// <summary>
    /// Public endpoint: resolve SUN URL parameters to a user profile.
    /// Called when an NFC tag is scanned. No authentication required.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<NfcResolveResponse>> Resolve(
        [FromQuery] string e,
        [FromQuery] string c,
        [FromQuery] string mac,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(e) || string.IsNullOrWhiteSpace(c) || string.IsNullOrWhiteSpace(mac))
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["e"] = ["Parameter 'e' is required."],
                ["c"] = ["Parameter 'c' is required."],
                ["mac"] = ["Parameter 'mac' is required."]
            }));

        Guid? observerUserId = null;
        if (HttpContext.Items["CurrentUser"] is SnAccount currentUser)
            observerUserId = currentUser.Id;

        var result = await nfc.ResolveTagAsync(e, c, mac, observerUserId, cancellationToken);

        if (result is null)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found or verification failed."));

        return Ok(new NfcResolveResponse
        {
            User = new NfcUserDto
            {
                Id = result.User.Id,
                Name = result.User.Name,
                Nick = result.User.Nick,
                Picture = result.Profile?.Picture,
                Bio = result.Profile?.Bio
            },
            IsFriend = result.IsFriend,
            Actions = result.Actions
        });
    }

    /// <summary>
    /// List all registered NFC tags for the current user.
    /// </summary>
    [HttpGet("tags")]
    [Authorize]
    public async Task<ActionResult<List<NfcTagDto>>> ListTags(
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var tags = await nfc.ListTagsAsync(currentUser.Id, cancellationToken);

        return Ok(tags.Select(t => new NfcTagDto
        {
            Id = t.Id,
            Label = t.Label,
            IsActive = t.IsActive,
            IsLocked = t.LockedAt.HasValue,
            LastSeenAt = t.LastSeenAt,
            CreatedAt = t.CreatedAt
        }));
    }

    /// <summary>
    /// Register a new NFC tag for the current user.
    /// </summary>
    [HttpPost("tags")]
    [Authorize]
    public async Task<ActionResult<NfcTagDto>> RegisterTag(
        [FromBody] RegisterTagRequest request,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        byte[] sunKey;
        try
        {
            sunKey = Convert.FromBase64String(request.SunKey);
        }
        catch (FormatException)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["sun_key"] = ["SUN key must be a valid base64-encoded 16-byte value."]
            }));
        }

        try
        {
            var tag = await nfc.RegisterTagAsync(
                currentUser.Id,
                request.Uid.ToUpperInvariant(),
                sunKey,
                request.Label,
                cancellationToken);

            return Ok(new NfcTagDto
            {
                Id = tag.Id,
                Label = tag.Label,
                IsActive = tag.IsActive,
                IsLocked = tag.LockedAt.HasValue,
                LastSeenAt = tag.LastSeenAt,
                CreatedAt = tag.CreatedAt
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["sun_key"] = [ex.Message]
            }));
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
    /// Update an NFC tag (label, active status).
    /// </summary>
    [HttpPatch("tags/{tagId:guid}")]
    [Authorize]
    public async Task<ActionResult<NfcTagDto>> UpdateTag(
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

        return Ok(new NfcTagDto
        {
            Id = tag.Id,
            Label = tag.Label,
            IsActive = tag.IsActive,
            IsLocked = tag.LockedAt.HasValue,
            LastSeenAt = tag.LastSeenAt,
            CreatedAt = tag.CreatedAt
        });
    }

    /// <summary>
    /// Lock an NFC tag to prevent physical reprogramming.
    /// </summary>
    [HttpPost("tags/{tagId:guid}/lock")]
    [Authorize]
    public async Task<ActionResult<NfcTagDto>> LockTag(
        Guid tagId,
        CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized();

        var tag = await nfc.LockTagAsync(currentUser.Id, tagId, cancellationToken);

        if (tag is null)
            return NotFound(ApiError.NotFound("nfc_tag", "NFC tag not found."));

        return Ok(new NfcTagDto
        {
            Id = tag.Id,
            Label = tag.Label,
            IsActive = tag.IsActive,
            IsLocked = tag.LockedAt.HasValue,
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
