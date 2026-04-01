using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Nfc;

[ApiController]
[Route("/api/admin/nfc")]
[Authorize]
public class NfcAdminController(
    NfcService nfc,
    DyPermissionService.DyPermissionServiceClient permissionService,
    ILogger<NfcAdminController> logger
) : ControllerBase
{
    private async Task<bool> HasAdminPermissionAsync()
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return false;

        if (currentUser.IsSuperuser) return true;

        var response = await permissionService.HasPermissionAsync(new DyHasPermissionRequest
        {
            Actor = currentUser.Id.ToString(),
            Key = "nfc.admin"
        });

        return response.HasPermission;
    }

    public class CreateEncryptedTagRequest
    {
        [Required]
        [MaxLength(64)]
        public string Uid { get; set; } = string.Empty;

        [Required]
        public string SunKey { get; set; } = string.Empty;

        public Guid? AssignedUserId { get; set; }
    }

    public class EncryptedTagDto
    {
        public Guid Id { get; set; }
        public string Uid { get; set; } = string.Empty;
        public Guid? UserId { get; set; }
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public NodaTime.Instant? LastSeenAt { get; set; }
        public NodaTime.Instant CreatedAt { get; set; }
    }

    /// <summary>
    /// Create an encrypted NFC tag with a pre-generated SUN key (factory flow).
    /// The tag can be optionally pre-assigned to a user.
    /// Requires nfc.admin permission.
    ///
    /// SunKey accepts both Base64 and hex formats:
    /// - Base64: "AAAAAAAAAAAAAAAAAAAAAA==" (16 zero bytes)
    /// - Hex: "00000000000000000000000000000000" (16 zero bytes)
    /// Auto-detects format: if it looks like hex (all hex chars, no padding), uses hex decode.
    /// </summary>
    [HttpPost("tags")]
    public async Task<ActionResult<EncryptedTagDto>> CreateEncryptedTag(
        [FromBody] CreateEncryptedTagRequest request,
        CancellationToken cancellationToken)
    {
        if (!await HasAdminPermissionAsync())
            return Forbid();

        try
        {
            var sunKey = ParseSunKey(request.SunKey);
            var tag = await nfc.RegisterEncryptedTagAsync(
                request.Uid,
                sunKey,
                request.AssignedUserId,
                cancellationToken);

            return Ok(new EncryptedTagDto
            {
                Id = tag.Id,
                Uid = tag.Uid,
                UserId = tag.UserId == Guid.Empty ? null : tag.UserId,
                IsActive = tag.IsActive,
                IsLocked = tag.LockedAt.HasValue,
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
        catch (ArgumentException ex)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["sun_key"] = [ex.Message]
            }));
        }
        catch (FormatException)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                ["sun_key"] = ["Invalid key format. Provide Base64 or hex string."]
            }));
        }
    }

    /// <summary>
    /// Parse a SUN key from either Base64 or hex string.
    /// Auto-detects: if all chars are hex digits (0-9, A-F, a-f), treats as hex.
    /// Otherwise tries Base64 decode.
    /// </summary>
    private static byte[] ParseSunKey(string sunKey)
    {
        if (string.IsNullOrWhiteSpace(sunKey))
            throw new ArgumentException("SUN key cannot be empty.");

        // Check if it looks like hex (all hex chars, reasonable length for 16 or 32 byte key)
        var isHex = sunKey.All(c =>
            (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'));

        if (isHex && (sunKey.Length == 32 || sunKey.Length == 64))
        {
            // Treat as hex
            return Convert.FromHexString(sunKey);
        }

        // Try Base64 decode
        try
        {
            return Convert.FromBase64String(sunKey);
        }
        catch (FormatException)
        {
            throw new FormatException($"Cannot decode sun_key. Provide hex (32 or 64 chars) or Base64.");
        }
    }

    /// <summary>
    /// List all encrypted NFC tags.
    /// Requires nfc.admin permission.
    /// </summary>
    [HttpGet("tags")]
    public async Task<ActionResult<List<EncryptedTagDto>>> ListEncryptedTags(
        CancellationToken cancellationToken)
    {
        if (!await HasAdminPermissionAsync())
            return Forbid();

        var tags = await nfc.ListAllEncryptedTagsAsync(cancellationToken);

        return Ok(tags.Select(t => new EncryptedTagDto
        {
            Id = t.Id,
            Uid = t.Uid,
            UserId = t.UserId == Guid.Empty ? null : t.UserId,
            IsActive = t.IsActive,
            IsLocked = t.LockedAt.HasValue,
            LastSeenAt = t.LastSeenAt,
            CreatedAt = t.CreatedAt
        }));
    }
}
