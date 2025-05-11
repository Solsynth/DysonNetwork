using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DysonNetwork.Sphere.Sticker;
public class StickerService(AppDatabase db, FileService fs, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    
    public async Task<Sticker> CreateStickerAsync(Sticker sticker)
    {
        db.Stickers.Add(sticker);
        await db.SaveChangesAsync();
        
        await fs.MarkUsageAsync(sticker.Image, 1);

        return sticker;
    }
    public async Task<Sticker> UpdateStickerAsync(Sticker sticker, CloudFile? newImage)
    {
        if (newImage != null)
        {
            await fs.MarkUsageAsync(sticker.Image, -1);
            sticker.Image = newImage;
            await fs.MarkUsageAsync(sticker.Image, 1);
        }

        db.Stickers.Update(sticker);
        await db.SaveChangesAsync();
        
        // Invalidate cache for this sticker
        InvalidateStickerCache(sticker);

        return sticker;
    }
    public async Task DeleteStickerAsync(Sticker sticker)
    {
        db.Stickers.Remove(sticker);
        await db.SaveChangesAsync();
        await fs.MarkUsageAsync(sticker.Image, -1);
        
        // Invalidate cache for this sticker
        InvalidateStickerCache(sticker);
    }
    public async Task DeleteStickerPackAsync(StickerPack pack)
    {
        var stickers = await db.Stickers
            .Include(s => s.Image)
            .Where(s => s.PackId == pack.Id)
            .ToListAsync();
    
        var images = stickers.Select(s => s.Image).ToList();
        
        db.Stickers.RemoveRange(stickers);
        db.StickerPacks.Remove(pack);
        await db.SaveChangesAsync();
        
        await fs.MarkUsageRangeAsync(images, -1);
        
        // Invalidate cache for all stickers in this pack
        foreach (var sticker in stickers)
        {
            InvalidateStickerCache(sticker);
        }
    }
    
    public async Task<Sticker?> LookupStickerByIdentifierAsync(string identifier)
    {
        // Try to get from cache first
        string cacheKey = $"StickerLookup_{identifier}";
        if (cache.TryGetValue(cacheKey, out Sticker? cachedSticker))
        {
            return cachedSticker;
        }
        
        // If not in cache, fetch from database
        IQueryable<Sticker> query = db.Stickers
            .Include(e => e.Pack)
            .Include(e => e.Image);
            
        query = Guid.TryParse(identifier, out var guid)
            ? query.Where(e => e.Id == guid)
            : query.Where(e => e.Pack.Prefix + e.Slug == identifier);
            
        var sticker = await query.FirstOrDefaultAsync();
        
        // Store in cache if found
        if (sticker != null)
        {
            cache.Set(cacheKey, sticker, CacheDuration);
        }
        
        return sticker;
    }
    
    private void InvalidateStickerCache(Sticker sticker)
    {
        // Remove both possible cache entries
        cache.Remove($"StickerLookup_{sticker.Id}");
        cache.Remove($"StickerLookup_{sticker.Pack.Prefix}{sticker.Slug}");
    }
}