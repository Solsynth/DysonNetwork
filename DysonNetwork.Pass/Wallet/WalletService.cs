using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Wallet;

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
        var existingWallet = await db.Wallets.FirstOrDefaultAsync(w => w.AccountId == accountId);
        if (existingWallet != null)
        {
            throw new InvalidOperationException($"Wallet already exists for account {accountId}");
        }

        var wallet = new Wallet { AccountId = accountId };

        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();

        return wallet;
    }

    public async Task<(WalletPocket wallet, bool isNewlyCreated)> GetOrCreateWalletPocketAsync(
        Guid walletId,
        string currency,
        decimal? initialAmount = null
    )
    {
        var pocket = await db.WalletPockets.FirstOrDefaultAsync(p => p.Currency == currency && p.WalletId == walletId);
        if (pocket != null) return (pocket, false);

        pocket = new WalletPocket
        {
            Currency = currency,
            Amount = initialAmount ?? 0,
            WalletId = walletId
        };

        db.WalletPockets.Add(pocket);
        return (pocket, true);
    }
}