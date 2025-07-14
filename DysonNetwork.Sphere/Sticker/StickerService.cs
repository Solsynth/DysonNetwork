using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Sticker;

public class StickerService(
    AppDatabase db,
    FileService.FileServiceClient files,
    FileReferenceService.FileReferenceServiceClient fileRefs,
    ICacheService cache
)
{
    public const string StickerFileUsageIdentifier = "sticker";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);

    public async Task<Sticker> CreateStickerAsync(Sticker sticker)
    {
        if (sticker.Image is null) throw new ArgumentNullException(nameof(sticker.Image));

        db.Stickers.Add(sticker);
        await db.SaveChangesAsync();

        var stickerResourceId = $"sticker:{sticker.Id}";
        await fileRefService.CreateReferenceAsync(
            sticker.Image.Id,
            StickerFileUsageIdentifier,
            stickerResourceId
        );

        return sticker;
    }

    public async Task<Sticker> UpdateStickerAsync(Sticker sticker, CloudFileReferenceObject? newImage)
    {
        if (newImage is not null)
        {
            var stickerResourceId = $"sticker:{sticker.Id}";

            // Delete old references
            var oldRefs =
                await fileRefService.GetResourceReferencesAsync(stickerResourceId, StickerFileUsageIdentifier);
            foreach (var oldRef in oldRefs)
            {
                await fileRefService.DeleteReferenceAsync(oldRef.Id);
            }

            sticker.Image = newImage.ToReferenceObject();

            // Create new reference
            await fileRefService.CreateReferenceAsync(
                newImage.Id,
                StickerFileUsageIdentifier,
                stickerResourceId
            );
        }

        db.Stickers.Update(sticker);
        await db.SaveChangesAsync();

        // Invalidate cache for this sticker
        await PurgeStickerCache(sticker);

        return sticker;
    }

    public async Task DeleteStickerAsync(Sticker sticker)
    {
        var stickerResourceId = $"sticker:{sticker.Id}";

        // Delete all file references for this sticker
        await fileRefService.DeleteResourceReferencesAsync(stickerResourceId);

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

        var images = stickers.Select(s => s.Image).ToList();

        // Delete all file references for each sticker in the pack
        foreach (var stickerResourceId in stickers.Select(sticker => $"sticker:{sticker.Id}"))
        {
            await fileRefService.DeleteResourceReferencesAsync(stickerResourceId);
        }

        // Delete any references for the pack itself
        var packResourceId = $"stickerpack:{pack.Id}";
        await fileRefService.DeleteResourceReferencesAsync(packResourceId);

        db.Stickers.RemoveRange(stickers);
        db.StickerPacks.Remove(pack);
        await db.SaveChangesAsync();

        // Invalidate cache for all stickers in this pack
        foreach (var sticker in stickers)
            await PurgeStickerCache(sticker);
    }

    public async Task<Sticker?> LookupStickerByIdentifierAsync(string identifier)
    {
        identifier = identifier.ToLower();
        // Try to get from the cache first
        var cacheKey = $"sticker:lookup:{identifier}";
        var cachedSticker = await cache.GetAsync<Sticker>(cacheKey);
        if (cachedSticker is not null)
            return cachedSticker;

        // If not in cache, fetch from the database
        IQueryable<Sticker> query = db.Stickers
            .Include(e => e.Pack);
        query = Guid.TryParse(identifier, out var guid)
            ? query.Where(e => e.Id == guid)
            : query.Where(e => EF.Functions.ILike(e.Pack.Prefix + e.Slug, identifier));

        var sticker = await query.FirstOrDefaultAsync();

        // Store in cache if found
        if (sticker != null)
            await cache.SetAsync(cacheKey, sticker, CacheDuration);

        return sticker;
    }

    private async Task PurgeStickerCache(Sticker sticker)
    {
        // Remove both possible cache entries
        await cache.RemoveAsync($"sticker:lookup:{sticker.Id}");
        await cache.RemoveAsync($"sticker:lookup:{sticker.Pack.Prefix}{sticker.Slug}");
    }
}