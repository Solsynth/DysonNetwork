using DysonNetwork.Sphere.Account;
using EFCore.BulkExtensions;

namespace DysonNetwork.Sphere.Storage.Handlers;

public class ActionLogFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<ActionLog>
{
    public async Task FlushAsync(IReadOnlyList<ActionLog> items)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();
        
        await db.BulkInsertAsync(items);
    }
}