using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class AppProductService(
    AppDatabase db,
    DyFileService.DyFileServiceClient files,
    DyPaymentService.DyPaymentServiceClient wallet
)
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

        // Sync to Wallet subscription catalog if recurring
        await SyncSubscriptionDefinitionAsync(product);

        return product;
    }

    public async Task<SnAppProduct?> UpdateProductAsync(SnAppProduct product)
    {
        db.AppProducts.Update(product);
        await db.SaveChangesAsync();

        await SyncSubscriptionDefinitionAsync(product);

        return product;
    }

    public async Task<bool> DeleteProductAsync(Guid id, Guid appId)
    {
        var product = await db.AppProducts
            .FirstOrDefaultAsync(p => p.Id == id && p.AppId == appId);
        if (product is null) return false;
        db.AppProducts.Remove(product);
        await db.SaveChangesAsync();

        // Remove from Wallet subscription catalog
        await RemoveSubscriptionDefinitionAsync(product);

        return true;
    }

    private async Task SyncSubscriptionDefinitionAsync(SnAppProduct product)
    {
        if (product.Recurrence == ProductRecurrence.None) return;

        var app = await db.CustomApps.Include(a => a.Project).FirstOrDefaultAsync(a => a.Id == product.AppId);
        if (app is null) return;

        var cycleDays = product.Recurrence switch
        {
            ProductRecurrence.Weekly => 7,
            ProductRecurrence.Monthly => 30,
            ProductRecurrence.Yearly => 365,
            _ => 30
        };

        try
        {
            await wallet.RegisterAppSubscriptionDefinitionAsync(new DyRegisterAppSubscriptionDefinitionRequest
            {
                Identifier = product.Identifier,
                AppIdentifier = app.ResourceIdentifier,
                DisplayName = product.DisplayName,
                Currency = product.Currency,
                BasePrice = product.Price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GroupIdentifier = product.GroupIdentifier ?? string.Empty,
                CycleDurationDays = cycleDays
            });
        }
        catch (Grpc.Core.RpcException)
        {
            // ponytail: Wallet unreachable — definition will be stale; next product update syncs it
        }
    }

    private async Task RemoveSubscriptionDefinitionAsync(SnAppProduct product)
    {
        if (product.Recurrence == ProductRecurrence.None) return;

        var app = await db.CustomApps.FirstOrDefaultAsync(a => a.Id == product.AppId);
        if (app is null) return;

        try
        {
            await wallet.RegisterAppSubscriptionDefinitionAsync(new DyRegisterAppSubscriptionDefinitionRequest
            {
                Identifier = product.Identifier,
                AppIdentifier = app.ResourceIdentifier,
                Remove = true
            });
        }
        catch (Grpc.Core.RpcException) { }
    }
}
