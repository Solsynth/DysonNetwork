using DysonNetwork.Sphere.Storage;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Sticker;

public class StickerService(AppDatabase db, FileService fs)
{
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

        return sticker;
    }

    public async Task DeleteStickerAsync(Sticker sticker)
    {
        db.Stickers.Remove(sticker);
        await db.SaveChangesAsync();
        await fs.MarkUsageAsync(sticker.Image, -1);
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
    }
}