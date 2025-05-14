using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Wallet;

public class WalletService(AppDatabase db)
{
    public async Task<Wallet?> GetWalletAsync(Guid accountId)
    {
        return await db.Wallets
            .Include(w => w.Pockets)
            .FirstOrDefaultAsync(w => w.AccountId == accountId);
    }
    
    public async Task<Wallet> CreateWalletAsync(Guid accountId)
    {
        var wallet = new Wallet
        {
            AccountId = accountId
        };

        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();
        
        return wallet;
    }

    public async Task<WalletPocket> GetOrCreateWalletPocketAsync(Guid accountId, string currency)
    {
        var wallet = await db.Wallets
            .Include(w => w.Pockets)
            .FirstOrDefaultAsync(w => w.AccountId == accountId);

        if (wallet == null)
        {
            throw new InvalidOperationException($"Wallet not found for account {accountId}");
        }

        var pocket = wallet.Pockets.FirstOrDefault(p => p.Currency == currency);

        if (pocket != null) return pocket;
        
        pocket = new WalletPocket
        {
            Currency = currency,
            Amount = 0,
            WalletId = wallet.Id
        };
            
        wallet.Pockets.Add(pocket);
        await db.SaveChangesAsync();

        return pocket;
    }
}