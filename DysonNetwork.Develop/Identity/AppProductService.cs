using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class AppProductService(AppDatabase db, DyFileService.DyFileServiceClient files)
{
    public async Task<SnCloudFileReferenceObject?> ResolveFileAsync(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId)) return null;
        var file = await files.GetFileAsync(new DyGetFileRequest { Id = fileId });
        if (file is null)
            throw new InvalidOperationException("Invalid file id.");
        return SnCloudFileReferenceObject.FromProtoValue(file);
    }

    public async Task<List<SnAppProduct>> GetProductsByAppAsync(Guid appId)
    {
        return await db.AppProducts
            .Where(p => p.AppId == appId)
            .OrderBy(p => p.Identifier)
            .ToListAsync();
    }

    public async Task<SnAppProduct?> GetProductAsync(Guid id, Guid appId)
    {
        return await db.AppProducts
            .FirstOrDefaultAsync(p => p.Id == id && p.AppId == appId);
    }

    public async Task<SnAppProduct?> GetProductByIdentifierAsync(Guid appId, string identifier)
    {
        return await db.AppProducts
            .FirstOrDefaultAsync(p => p.AppId == appId && p.Identifier == identifier);
    }

    public async Task<SnAppProduct> CreateProductAsync(Guid appId, SnAppProduct product)
    {
        product.Id = Guid.NewGuid();
        product.AppId = appId;
        db.AppProducts.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task<SnAppProduct?> UpdateProductAsync(SnAppProduct product)
    {
        db.AppProducts.Update(product);
        await db.SaveChangesAsync();
        return product;
    }

    public async Task<bool> DeleteProductAsync(Guid id, Guid appId)
    {
        var product = await db.AppProducts
            .FirstOrDefaultAsync(p => p.Id == id && p.AppId == appId);
        if (product is null) return false;
        db.AppProducts.Remove(product);
        await db.SaveChangesAsync();
        return true;
    }
}
