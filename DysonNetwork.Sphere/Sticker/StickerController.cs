using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Sticker;

[ApiController]
[Route("/api/stickers")]
public class StickerController(
    AppDatabase db,
    StickerService st,
    Publisher.PublisherService ps,
    FileService.FileServiceClient files
) : ControllerBase
{
    private async Task<IActionResult> _CheckStickerPackPermissions(
        Guid packId,
        Account currentUser,
        Publisher.PublisherMemberRole requiredRole
    )
    {
        var pack = await db.StickerPacks
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == packId);

        if (pack is null)
            return NotFound("Sticker pack not found");

        var accountId = Guid.Parse(currentUser.Id);
        if (!await ps.IsMemberWithRole(accountId, pack.PublisherId, requiredRole))
            return StatusCode(403, "You are not a member of this publisher");

        return Ok();
    }

    [HttpGet]
    public async Task<ActionResult<List<StickerPack>>> ListStickerPacks(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery(Name = "pub")] string? pubName = null,
        [FromQuery(Name = "order")] string? order = null
    )
    {
        Publisher.Publisher? publisher = null;
        if (pubName is not null)
            publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);

        var queryable = db.StickerPacks
            .If(publisher is not null, q => q.Where(f => f.PublisherId == publisher!.Id));

        if (order is not null)
        {
            queryable = order switch
            {
                "usage" => queryable.OrderByDescending(p => p.Ownerships.Count),
                _ => queryable.OrderByDescending(p => p.CreatedAt)
            };
        }

        var totalCount = await queryable
            .CountAsync();
        var packs = await queryable
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
        return Ok(packs);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<List<StickerPackOwnership>>> ListStickerPacksOwned()
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var ownershipsId = await db.StickerPackOwnerships
            .Where(p => p.AccountId == accountId)
            .Select(p => p.PackId)
            .ToListAsync();
        var packs = await db.StickerPacks
            .Where(p => ownershipsId.Contains(p.Id))
            .Include(p => p.Stickers)
            .ToListAsync();

        return Ok(packs);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StickerPack>> GetStickerPack(Guid id)
    {
        var pack = await db.StickerPacks
            .FirstOrDefaultAsync(p => p.Id == id);

        if (pack is null) return NotFound();
        return Ok(pack);
    }

    public class StickerPackRequest
    {
        [MaxLength(1024)] public string? Name { get; set; }
        [MaxLength(4096)] public string? Description { get; set; }
        [MaxLength(128)] public string? Prefix { get; set; }
    }

    [HttpPost]
    [RequiredPermission("global", "stickers.packs.create")]
    public async Task<ActionResult<StickerPack>> CreateStickerPack(
        [FromBody] StickerPackRequest request,
        [FromQuery(Name = "pub")] string publisherName
    )
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        if (string.IsNullOrEmpty(request.Name))
            return BadRequest("Name is required");
        if (string.IsNullOrEmpty(request.Prefix))
            return BadRequest("Prefix is required");

        var accountId = Guid.Parse(currentUser.Id);
        var publisher =
            await db.Publishers.FirstOrDefaultAsync(p => p.Name == publisherName && p.AccountId == accountId);
        if (publisher == null)
            return BadRequest("Publisher not found");

        var pack = new StickerPack
        {
            Name = request.Name!,
            Description = request.Description ?? string.Empty,
            Prefix = request.Prefix!,
            PublisherId = publisher.Id
        };

        db.StickerPacks.Add(pack);
        await db.SaveChangesAsync();
        return Ok(pack);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<StickerPack>> UpdateStickerPack(Guid id, [FromBody] StickerPackRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var pack = await db.StickerPacks
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (pack is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.PublisherMembers
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.PublisherId == pack.PublisherId);
        if (member is null)
            return StatusCode(403, "You are not a member of this publisher");
        if (member.Role < Publisher.PublisherMemberRole.Editor)
            return StatusCode(403, "You need to be at least an editor to update sticker packs");

        if (request.Name is not null)
            pack.Name = request.Name;
        if (request.Description is not null)
            pack.Description = request.Description;
        if (request.Prefix is not null)
            pack.Prefix = request.Prefix;

        db.StickerPacks.Update(pack);
        await db.SaveChangesAsync();
        return Ok(pack);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteStickerPack(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var pack = await db.StickerPacks
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (pack is null)
            return NotFound();

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.PublisherMembers
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.PublisherId == pack.PublisherId);
        if (member is null)
            return StatusCode(403, "You are not a member of this publisher");
        if (member.Role < Publisher.PublisherMemberRole.Editor)
            return StatusCode(403, "You need to be an editor to delete sticker packs");

        await st.DeleteStickerPackAsync(pack);
        return NoContent();
    }

    [HttpGet("{packId:guid}/content")]
    public async Task<ActionResult<List<Sticker>>> ListStickers(Guid packId)
    {
        var stickers = await db.Stickers
            .Where(s => s.Pack.Id == packId)
            .Include(e => e.Pack)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync();

        return Ok(stickers);
    }

    [HttpGet("lookup/{identifier}")]
    public async Task<ActionResult<Sticker>> GetStickerByIdentifier(string identifier)
    {
        var sticker = await st.LookupStickerByIdentifierAsync(identifier);

        if (sticker is null) return NotFound();
        return Ok(sticker);
    }

    [HttpGet("lookup/{identifier}/open")]
    public async Task<ActionResult<Sticker>> OpenStickerByIdentifier(string identifier)
    {
        var sticker = await st.LookupStickerByIdentifierAsync(identifier);

        if (sticker?.Image is null) return NotFound();
        return Redirect($"/drive/files/{sticker.Image.Id}?original=true");
    }

    [HttpGet("{packId:guid}/content/{id:guid}")]
    public async Task<ActionResult<Sticker>> GetSticker(Guid packId, Guid id)
    {
        var sticker = await db.Stickers
            .Where(s => s.PackId == packId && s.Id == id)
            .Include(e => e.Pack)
            .FirstOrDefaultAsync();
        if (sticker is null) return NotFound();

        return Ok(sticker);
    }

    public class StickerRequest
    {
        [MaxLength(128)] public string? Slug { get; set; } = null!;
        public string? ImageId { get; set; }
    }

    [HttpPatch("{packId:guid}/content/{id:guid}")]
    public async Task<IActionResult> UpdateSticker(Guid packId, Guid id, [FromBody] StickerRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var permissionCheck =
            await _CheckStickerPackPermissions(packId, currentUser, Publisher.PublisherMemberRole.Editor);
        if (permissionCheck is not OkResult)
            return permissionCheck;

        var sticker = await db.Stickers
            .Include(s => s.Pack)
            .ThenInclude(p => p.Publisher)
            .FirstOrDefaultAsync(e => e.Id == id && e.Pack.Id == packId);

        if (sticker is null)
            return NotFound();

        if (request.Slug is not null)
            sticker.Slug = request.Slug;

        CloudFileReferenceObject? image = null;
        if (request.ImageId is not null)
        {
            var file = await files.GetFileAsync(new GetFileRequest { Id = request.ImageId });
            if (file is null)
                return BadRequest("Image not found");
            sticker.ImageId = request.ImageId;
            sticker.Image = CloudFileReferenceObject.FromProtoValue(file);
        }

        sticker = await st.UpdateStickerAsync(sticker, image);
        return Ok(sticker);
    }

    [HttpDelete("{packId:guid}/content/{id:guid}")]
    public async Task<IActionResult> DeleteSticker(Guid packId, Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var permissionCheck =
            await _CheckStickerPackPermissions(packId, currentUser, Publisher.PublisherMemberRole.Editor);
        if (permissionCheck is not OkResult)
            return permissionCheck;

        var sticker = await db.Stickers
            .Include(s => s.Pack)
            .ThenInclude(p => p.Publisher)
            .FirstOrDefaultAsync(e => e.Id == id && e.Pack.Id == packId);

        if (sticker is null)
            return NotFound();

        await st.DeleteStickerAsync(sticker);
        return NoContent();
    }

    public const int MaxStickersPerPack = 24;

    [HttpPost("{packId:guid}/content")]
    [RequiredPermission("global", "stickers.create")]
    public async Task<IActionResult> CreateSticker(Guid packId, [FromBody] StickerRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Slug))
            return BadRequest("Slug is required.");
        if (request.ImageId is null)
            return BadRequest("Image is required.");

        var permissionCheck =
            await _CheckStickerPackPermissions(packId, currentUser, Publisher.PublisherMemberRole.Editor);
        if (permissionCheck is not OkResult)
            return permissionCheck;

        var pack = await db.StickerPacks
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(e => e.Id == packId);
        if (pack is null)
            return BadRequest("Sticker pack was not found.");

        var stickersCount = await db.Stickers.CountAsync(s => s.PackId == packId);
        if (stickersCount >= MaxStickersPerPack)
            return BadRequest($"Sticker pack has reached maximum capacity of {MaxStickersPerPack} stickers.");

        var file = await files.GetFileAsync(new GetFileRequest { Id = request.ImageId });
        if (file is null)
            return BadRequest("Image not found.");

        var sticker = new Sticker
        {
            Slug = request.Slug,
            ImageId = file.Id,
            Image = CloudFileReferenceObject.FromProtoValue(file),
            Pack = pack
        };

        sticker = await st.CreateStickerAsync(sticker);
        return Ok(sticker);
    }

    [HttpGet("{packId:guid}/own")]
    [Authorize]
    public async Task<ActionResult<StickerPackOwnership>> GetStickerPackOwnership(Guid packId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var ownership = await db.StickerPackOwnerships
            .Where(p => p.PackId == packId && p.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (ownership is null) return NotFound();
        return Ok(ownership);
    }

    [HttpPost("{packId:guid}/own")]
    [Authorize]
    public async Task<ActionResult<StickerPackOwnership>> AcquireStickerPack([FromRoute] Guid packId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();

        var pack = await db.StickerPacks
            .Where(p => p.Id == packId)
            .FirstOrDefaultAsync();
        if (pack is null) return NotFound();

        var existingOwnership = await db.StickerPackOwnerships
            .Where(p => p.PackId == packId && p.AccountId == Guid.Parse(currentUser.Id))
            .FirstOrDefaultAsync();
        if (existingOwnership is not null) return Ok(existingOwnership);

        var ownership = new StickerPackOwnership
        {
            PackId = packId,
            AccountId = Guid.Parse(currentUser.Id)
        };
        db.StickerPackOwnerships.Add(ownership);
        await db.SaveChangesAsync();

        return Ok(ownership);
    }

    [HttpDelete("{packId:guid}/own")]
    [Authorize]
    public async Task<IActionResult> ReleaseStickerPack([FromRoute] Guid packId)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser)
            return Unauthorized();
        var accountId = Guid.Parse(currentUser.Id);

        var ownership = await db.StickerPackOwnerships
            .Where(p => p.PackId == packId && p.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (ownership is null) return NotFound();

        db.Remove(ownership);
        await db.SaveChangesAsync();

        return NoContent();
    }
}