using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Sticker;

public class StickerService(
    AppDatabase db,
    ICacheService cache
)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public async Task<SnSticker> CreateStickerAsync(SnSticker sticker)
    {
        if (sticker.Image is null) throw new ArgumentNullException(nameof(sticker.Image));

        db.Stickers.Add(sticker);
        await db.SaveChangesAsync();

        return sticker;
    }

    public async Task<SnSticker> UpdateStickerAsync(SnSticker sticker, SnCloudFileReferenceObject? newImage)
    {
        if (newImage is not null)
            sticker.Image = newImage;

        db.Stickers.Update(sticker);
        await db.SaveChangesAsync();

        // Invalidate cache for this sticker
        await PurgeStickerCache(sticker);

        return sticker;
    }

    public async Task DeleteStickerAsync(SnSticker sticker)
    {
        db.Stickers.Remove(sticker);
        await db.SaveChangesAsync();

        // Invalidate cache for this sticker
        await PurgeStickerCache(sticker);
    }

    public async Task DeleteStickerPackAsync(StickerPack pack)
    {
        var stickers = await db.Stickers
            .Where(s => s.PackId == pack.Id)
            .ToListAsync();

        db.Stickers.RemoveRange(stickers);
        db.StickerPacks.Remove(pack);
        await db.SaveChangesAsync();

        // Invalidate cache for all stickers in this pack
        foreach (var sticker in stickers)
            await PurgeStickerCache(sticker);
    }

    public async Task<SnSticker?> LookupStickerByIdentifierAsync(string identifier)
    {
        identifier = identifier.ToLower();
        // Try to get from the cache first
        var cacheKey = $"sticker:lookup:{identifier}";
        var cachedSticker = await cache.GetAsync<SnSticker>(cacheKey);
        if (cachedSticker is not null)
            return cachedSticker;

        // If not in cache, fetch from the database
        IQueryable<SnSticker> query = db.Stickers
            .Include(e => e.Pack);

        var isV2 = identifier.Contains("+");

        var identifierParts = identifier.Split('+');
        if (identifierParts.Length < 2) isV2 = false;

        if (isV2)
        {
            var packPart = identifierParts[0];
            var stickerPart = identifierParts[1];
            query = query.Where(e =>
                EF.Functions.ILike(e.Pack.Prefix, packPart) && EF.Functions.ILike(e.Slug, stickerPart));
        }
        else
        {
            query = Guid.TryParse(identifier, out var guid)
                ? query.Where(e => e.Id == guid)
                : query.Where(e => EF.Functions.ILike(e.Pack.Prefix + e.Slug, identifier));
        }


        var sticker = await query.FirstOrDefaultAsync();

        // Store in cache if found
        if (sticker != null)
            await cache.SetAsync(cacheKey, sticker, CacheDuration);

        return sticker;
    }

    private async Task PurgeStickerCache(SnSticker sticker)
    {
        // Remove both possible cache entries
        await cache.RemoveAsync($"sticker:lookup:{sticker.Id}");
        await cache.RemoveAsync($"sticker:lookup:{sticker.Pack.Prefix}{sticker.Slug}");
    }
}