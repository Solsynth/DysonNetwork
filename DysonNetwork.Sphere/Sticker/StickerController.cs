using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Sphere.Publisher;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Sticker;

[ApiController]
[Route("/api/stickers")]
public class StickerController(AppDatabase db, StickerService st, FileService.FileServiceClient files) : ControllerBase
{
    private async Task<IActionResult> _CheckStickerPackPermissions(
        Guid packId,
        Account currentUser,
        PublisherMemberRole requiredRole
    )
    {
        var pack = await db.StickerPacks
            .Include(p => p.Publisher)
            .FirstOrDefaultAsync(p => p.Id == packId);

        if (pack is null)
            return NotFound("Sticker pack not found");

        var accountId = Guid.Parse(currentUser.Id);
        var member = await db.PublisherMembers
            .FirstOrDefaultAsync(m => m.AccountId == accountId && m.PublisherId == pack.PublisherId);
        if (member is null)
            return StatusCode(403, "You are not a member of this publisher");
        if (member.Role < requiredRole)
            return StatusCode(403, $"You need to be at least a {requiredRole} to perform this action");

        return Ok();
    }

    [HttpGet]
    public async Task<ActionResult<List<StickerPack>>> ListStickerPacks(
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        [FromQuery] string? pubName = null
    )
    {
        Publisher.Publisher? publisher = null;
        if (pubName is not null)
            publisher = await db.Publishers.FirstOrDefaultAsync(p => p.Name == pubName);

        var totalCount = await db.StickerPacks
            .If(publisher is not null, q => q.Where(f => f.PublisherId == publisher!.Id))
            .CountAsync();
        var packs = await db.StickerPacks
            .If(publisher is not null, q => q.Where(f => f.PublisherId == publisher!.Id))
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        Response.Headers["X-Total"] = totalCount.ToString();
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
    public async Task<ActionResult<StickerPack>> CreateStickerPack([FromBody] StickerPackRequest request)
    {
        if (HttpContext.Items["CurrentUser"] is not Account currentUser) return Unauthorized();

        if (string.IsNullOrEmpty(request.Name))
            return BadRequest("Name is required");
        if (string.IsNullOrEmpty(request.Prefix))
            return BadRequest("Prefix is required");

        var publisherName = Request.Headers["X-Pub"].ToString();
        if (string.IsNullOrEmpty(publisherName))
            return BadRequest("Publisher name is required in X-Pub header");

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
        if (member.Role < PublisherMemberRole.Editor)
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
        if (member.Role < PublisherMemberRole.Editor)
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
        return Redirect($"/files/{sticker.Image.Id}");
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

        var permissionCheck = await _CheckStickerPackPermissions(packId, currentUser, PublisherMemberRole.Editor);
        if (permissionCheck is not OkResult)
            return permissionCheck;

        var sticker = await db.Stickers
            .Include(s => s.Image)
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

        var permissionCheck = await _CheckStickerPackPermissions(packId, currentUser, PublisherMemberRole.Editor);
        if (permissionCheck is not OkResult)
            return permissionCheck;

        var sticker = await db.Stickers
            .Include(s => s.Image)
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

        var permissionCheck = await _CheckStickerPackPermissions(packId, currentUser, PublisherMemberRole.Editor);
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
}