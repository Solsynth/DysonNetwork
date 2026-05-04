using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Wallet.Payment;

public class WalletService(AppDatabase db)
{
    public const int MaxWalletsPerAccount = 3;

    public async Task<SnWallet?> GetWalletAsync(Guid walletId)
    {
        return await db.Wallets
            .Include(w => w.Pockets)
            .FirstOrDefaultAsync(w => w.Id == walletId);
    }

    public async Task<SnWallet?> GetWalletByPublicIdAsync(string publicId)
    {
        return await db.Wallets
            .Include(w => w.Pockets)
            .FirstOrDefaultAsync(w => w.PublicId == publicId);
    }

    public async Task<List<SnWallet>> GetAccountWalletsAsync(Guid accountId)
    {
        return await db.Wallets
            .Include(w => w.Pockets)
            .Where(w => w.AccountId == accountId)
            .OrderByDescending(w => w.IsPrimary)
            .ThenBy(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<SnWallet?> GetAccountWalletAsync(Guid accountId)
    {
        return await db.Wallets
            .Include(w => w.Pockets)
            .Where(w => w.AccountId == accountId)
            .OrderByDescending(w => w.IsPrimary)
            .ThenBy(w => w.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<List<SnWallet>> GetRealmWalletsAsync(Guid realmId)
    {
        return await db.Wallets
            .Include(w => w.Pockets)
            .Where(w => w.RealmId == realmId)
            .OrderByDescending(w => w.IsPrimary)
            .ThenBy(w => w.CreatedAt)
            .ToListAsync();
    }

    public async Task<SnWallet> CreateWalletAsync(Guid? accountId = null, Guid? realmId = null, string? name = null)
    {
        if (!accountId.HasValue && !realmId.HasValue)
            throw new InvalidOperationException("Wallet must belong to an account or a realm.");

        if (accountId.HasValue && realmId.HasValue)
            throw new InvalidOperationException("Wallet cannot belong to both an account and a realm.");

        if (accountId.HasValue)
        {
            var existingWalletCount = await db.Wallets.CountAsync(w => w.AccountId == accountId.Value);
            if (existingWalletCount >= MaxWalletsPerAccount)
                throw new InvalidOperationException($"Account wallet quota exceeded. Limit is {MaxWalletsPerAccount}.");
        }

        var wallet = new SnWallet
        {
            AccountId = accountId,
            RealmId = realmId,
            Name = string.IsNullOrWhiteSpace(name)
                ? realmId.HasValue ? "Organization Wallet" : "Wallet"
                : name.Trim(),
            IsPrimary = false,
        };

        if (accountId.HasValue)
        {
            var hasPrimaryWallet = await db.Wallets.AnyAsync(w => w.AccountId == accountId.Value && w.IsPrimary);
            wallet.IsPrimary = !hasPrimaryWallet;
        }
        else if (realmId.HasValue)
        {
            var hasPrimaryWallet = await db.Wallets.AnyAsync(w => w.RealmId == realmId.Value && w.IsPrimary);
            wallet.IsPrimary = !hasPrimaryWallet;
        }

        db.Wallets.Add(wallet);
        await db.SaveChangesAsync();

        return wallet;
    }

    public async Task<SnWallet> SetPrimaryWalletAsync(Guid walletId)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == walletId)
            ?? throw new InvalidOperationException("Wallet not found.");

        if (wallet.AccountId.HasValue)
        {
            await db.Wallets
                .Where(w => w.AccountId == wallet.AccountId.Value && w.Id != walletId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsPrimary, false));
        }
        else if (wallet.RealmId.HasValue)
        {
            await db.Wallets
                .Where(w => w.RealmId == wallet.RealmId.Value && w.Id != walletId)
                .ExecuteUpdateAsync(s => s.SetProperty(w => w.IsPrimary, false));
        }

        wallet.IsPrimary = true;
        await db.SaveChangesAsync();
        return wallet;
    }

    public async Task<SnWallet> EnablePublicIdAsync(Guid walletId)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == walletId)
            ?? throw new InvalidOperationException("Wallet not found.");

        if (wallet.PublicId == null)
        {
            string generated;
            do
            {
                generated = GeneratePublicId();
            } while (await db.Wallets.AnyAsync(w => w.PublicId == generated));

            wallet.PublicId = generated;
            await db.SaveChangesAsync();
        }

        return wallet;
    }

    public async Task<SnWallet> DisablePublicIdAsync(Guid walletId)
    {
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.Id == walletId)
            ?? throw new InvalidOperationException("Wallet not found.");

        wallet.PublicId = null;
        await db.SaveChangesAsync();
        return wallet;
    }

    public async Task<(SnWalletPocket wallet, bool isNewlyCreated)> GetOrCreateWalletPocketAsync(
        Guid walletId,
        string currency,
        decimal? initialAmount = null
    )
    {
        var pocket = await db.WalletPockets.FirstOrDefaultAsync(p => p.Currency == currency && p.WalletId == walletId);
        if (pocket != null) return (pocket, false);

        pocket = new SnWalletPocket
        {
            Currency = currency,
            Amount = initialAmount ?? 0,
            WalletId = walletId
        };

        db.WalletPockets.Add(pocket);
        return (pocket, true);
    }

    private static string GeneratePublicId()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> buffer = stackalloc char[12];
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = chars[Random.Shared.Next(chars.Length)];

        return $"DNW-{new string(buffer[..4])}-{new string(buffer[4..8])}-{new string(buffer[8..12])}";
    }
}
