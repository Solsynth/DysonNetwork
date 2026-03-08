using DysonNetwork.Passport.Affiliation;
using DysonNetwork.Passport.Mailer;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Passport.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    DyFileService.DyFileServiceClient files,
    AccountUsernameService uname,
    AffiliationSpellService ars,
    EmailService mailer,
    DyRingService.DyRingServiceClient pusher,
    ILocalizationService localizer,
    ICacheService cache,
    ILogger<AccountService> logger,
    Shared.EventBus.IEventBus eventBus,
    DyAccountService.DyAccountServiceClient accounts
)
{
    public const string AccountCachePrefix = "account:";
    private static readonly TimeSpan AccountCacheTtl = TimeSpan.FromMinutes(5);

    public async Task PurgeAccountCache(SnAccount account)
    {
        await cache.RemoveGroupAsync($"{AccountCachePrefix}{account.Id}");
    }

    public async Task<List<SnAccount>> GetAllSuperusersAsync()
    {
        var actorIds = await db.PermissionGroupMembers
            .Include(m => m.Group)
            .Where(m => m.Group.Key == "superuser" || m.Group.Key == "root")
            .Select(m => m.Actor)
            .Distinct()
            .ToListAsync();

        var accountsList = new List<SnAccount>();
        foreach (var actorId in actorIds)
        {
            if (!Guid.TryParse(actorId, out var accountId)) continue;
            var account = await GetAccount(accountId);
            if (account is not null)
                accountsList.Add(account);
        }

        return accountsList;
    }

    public async Task<SnAccount?> GetAccount(Guid id)
    {
        var cacheKey = $"{AccountCachePrefix}{id}:hydrated";
        var (found, cached) = await cache.GetAsyncWithStatus<SnAccount>(cacheKey);
        if (found && cached is not null) return cached;

        try
        {
            var remote = await accounts.GetAccountAsync(new DyGetAccountRequest { Id = id.ToString() });
            var account = SnAccount.FromProtoValue(remote);
            account.Profile = await GetOrCreateAccountProfileAsync(id);
            await cache.SetWithGroupsAsync(cacheKey, account, [$"{AccountCachePrefix}{id}"], AccountCacheTtl);
            return account;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<SnAccount?> LookupAccount(string probe)
    {
        var matches = (await accounts.SearchAccountAsync(new DySearchAccountRequest { Query = probe })).Accounts;
        var matched = matches.FirstOrDefault(a =>
            string.Equals(a.Name, probe, StringComparison.OrdinalIgnoreCase));
        matched ??= matches.FirstOrDefault();
        if (matched is null) return null;

        var account = SnAccount.FromProtoValue(matched);
        account.Profile = await GetOrCreateAccountProfileAsync(account.Id);
        return account;
    }

    public async Task<SnAccount?> LookupAccountByConnection(string identifier, string provider)
    {
        logger.LogWarning(
            "LookupAccountByConnection in Passport is deprecated after Padlock split (provider={Provider}). Returning null.",
            provider
        );
        await Task.CompletedTask;
        return null;
    }

    public async Task<int?> GetAccountLevel(Guid accountId)
    {
        var profile = await db.AccountProfiles
            .Where(a => a.AccountId == accountId)
            .FirstOrDefaultAsync();
        return profile?.Level;
    }

    public async Task<SnAccountProfile> GetOrCreateAccountProfileAsync(Guid accountId)
    {
        var profile = await db.AccountProfiles
            .FirstOrDefaultAsync(p => p.AccountId == accountId);
        if (profile is not null) return profile;

        profile = new SnAccountProfile
        {
            AccountId = accountId
        };

        db.AccountProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync();
            return profile;
        }
        catch (DbUpdateException)
        {
            // Handle concurrent create race by reloading; if still missing, retry create once.
            var existing = await db.AccountProfiles.FirstOrDefaultAsync(p => p.AccountId == accountId);
            if (existing is not null) return existing;

            profile = new SnAccountProfile { AccountId = accountId };
            db.AccountProfiles.Add(profile);
            await db.SaveChangesAsync();
            return profile;
        }
    }

    public async Task<bool> CheckAccountNameHasTaken(string name)
    {
        var matches = (await accounts.SearchAccountAsync(new DySearchAccountRequest { Query = name })).Accounts;
        return matches.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> CheckEmailHasBeenUsed(string email)
    {
        var candidates = (await accounts.SearchAccountAsync(new DySearchAccountRequest { Query = email })).Accounts;
        foreach (var candidate in candidates)
        {
            if (!Guid.TryParse(candidate.Id, out var accountId)) continue;
            var contacts = await accounts.ListContactsAsync(new DyListContactsRequest
            {
                AccountId = accountId.ToString(),
                Type = DyAccountContactType.DyEmail,
                VerifiedOnly = false
            });
            if (contacts.Contacts.Any(c => string.Equals(c.Content, email, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    public async Task RequestAccountDeletion(SnAccount account)
    {
        var spell = await spells.CreateMagicSpell(
            account,
            MagicSpellType.AccountRemoval,
            new Dictionary<string, object>(),
            SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(24)),
            preventRepeat: true
        );
        await spells.NotifyMagicSpell(spell);
    }

    public async Task RequestPasswordReset(SnAccount account)
    {
        var spell = await spells.CreateMagicSpell(
            account,
            MagicSpellType.AuthPasswordReset,
            new Dictionary<string, object>(),
            SystemClock.Instance.GetCurrentInstant().Plus(Duration.FromHours(24)),
            preventRepeat: true
        );
        await spells.NotifyMagicSpell(spell);
    }

    /// <summary>
    /// This method will grant a badge to the account.
    /// Shouldn't be exposed to normal user and the user itself.
    /// </summary>
    public async Task<SnAccountBadge> GrantBadge(SnAccount account, SnAccountBadge badge)
    {
        badge.AccountId = account.Id;
        db.Badges.Add(badge);
        await db.SaveChangesAsync();
        return badge;
    }

    /// <summary>
    /// This method will revoke a badge from the account.
    /// Shouldn't be exposed to normal user and the user itself.
    /// </summary>
    public async Task RevokeBadge(SnAccount account, Guid badgeId)
    {
        var badge = await db.Badges
            .Where(b => b.AccountId == account.Id && b.Id == badgeId)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync() ?? throw new InvalidOperationException("Badge was not found.");
        var profile = await db.AccountProfiles
            .Where(p => p.AccountId == account.Id)
            .FirstOrDefaultAsync();
        if (profile?.ActiveBadge is not null && profile.ActiveBadge.Id == badge.Id)
            profile.ActiveBadge = null;

        db.Remove(badge);
        await db.SaveChangesAsync();
    }

    public async Task ActiveBadge(SnAccount account, Guid badgeId)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var badge = await db.Badges
                .Where(b => b.AccountId == account.Id && b.Id == badgeId)
                .OrderByDescending(b => b.CreatedAt)
                .FirstOrDefaultAsync();
            if (badge is null) throw new InvalidOperationException("Badge was not found.");

            await db.Badges
                .Where(b => b.AccountId == account.Id && b.Id != badgeId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ActivatedAt, p => null));

            badge.ActivatedAt = SystemClock.Instance.GetCurrentInstant();
            db.Update(badge);
            await db.SaveChangesAsync();

            await db.AccountProfiles
                .Where(p => p.AccountId == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.ActiveBadge, badge.ToReference()));
            await PurgeAccountCache(account);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
