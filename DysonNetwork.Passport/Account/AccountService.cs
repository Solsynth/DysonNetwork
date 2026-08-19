using DysonNetwork.Passport.Affiliation;
using DysonNetwork.Passport.Mailer;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Queue;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Passport.Account;

public class AccountService(
    AppDatabase db,
    MagicSpellService spells,
    ICacheService cache,
    ILogger<AccountService> logger,
    DyAccountService.DyAccountServiceClient accounts,
    AccountBoardService boards,
    IEventBus eventBus
)
{
    public const string AccountCachePrefix = "account:";
    private static readonly TimeSpan AccountCacheTtl = TimeSpan.FromMinutes(5);

    public Task PurgeAccountCache(SnAccount account)
    {
        return PurgeAccountCache(account.Id);
    }

    public async Task PurgeAccountCache(Guid accountId)
    {
        await cache.RemoveGroupAsync($"{AccountCachePrefix}{accountId}");
    }

    public async Task<List<SnAccount>> GetAllSuperusersAsync()
    {
        var response = await accounts.ListSuperusersAsync(new Google.Protobuf.WellKnownTypes.Empty());
        return response.Accounts.Select(a => SnAccount.FromProtoValue(a)).ToList();
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
            // Contract: every account carries a profile (the old Passport
            // table hydration). Stargate hydrates server-side; the proxy
            // guarantees non-null even when it does not.
            account.Profile ??= await GetOrCreateAccountProfileAsync(id);
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

        return SnAccount.FromProtoValue(matched);
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
        var account = await GetAccount(accountId);
        return account?.Profile?.Level;
    }

    public async Task<SnAccountProfile> GetOrCreateAccountProfileAsync(Guid accountId)
    {
        // The account_profiles row moved to Stargate; the RPC guarantees a row
        // exists (Stargate creates on read), so this is a pure proxy.
        SnAccountProfile? profile;
        try
        {
            var account = await accounts.GetAccountAsync(new DyGetAccountRequest { Id = accountId.ToString() });
            profile = account.Profile is null ? null : SnAccountProfile.FromProtoValue(account.Profile);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            profile = null;
        }
        profile ??= new SnAccountProfile { AccountId = accountId };
        await boards.HydrateBoardAsync(profile);
        return profile;
    }


    /// <summary>
    /// Account activity statistics derived from Stargate's accounts/profiles
    /// (the profile rows moved there). Pages through all accounts once;
    /// admin-only, low frequency.
    /// </summary>

    /// <summary>
    /// Pages all accounts once (hydrated profiles from Stargate) and returns
    /// the (id, timezone) pairs that are active: an active presence lease or
    /// a last-seen newer than <paramref name="recentThreshold"/>.
    /// </summary>
    public async Task<List<(Guid AccountId, string? TimeZone)>> GetActiveProfilesAsync(
        HashSet<Guid> presenceActiveIds, Instant recentThreshold)
    {
        var result = new List<(Guid, string?)>();
        string? pageToken = null;
        do
        {
            var page = await accounts.ListAccountsAsync(new DyListAccountsRequest
            {
                PageSize = 200,
                PageToken = pageToken ?? string.Empty,
                Filter = string.Empty,
                OrderBy = string.Empty
            });
            foreach (var account in page.Accounts)
            {
                if (!Guid.TryParse(account.Id, out var id)) continue;
                var lastSeen = account.Profile?.LastSeenAt;
                if (presenceActiveIds.Contains(id) ||
                    (lastSeen is not null && lastSeen.ToInstant() >= recentThreshold))
                {
                    result.Add((id, account.Profile?.TimeZone));
                }
            }
            pageToken = string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor;
        } while (pageToken is not null);
        return result;
    }

    public async Task<AccountActivityStats> GetAccountActivityStatsAsync(Instant now)
    {
        var currentDayStartedAt = now.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
        var currentWeekStartedAt = currentDayStartedAt - Duration.FromDays(6);
        var currentMonthStartedAt = currentDayStartedAt - Duration.FromDays(29);
        var previousDayStartedAt = currentDayStartedAt - Duration.FromDays(1);
        var previousWeekStartedAt = currentWeekStartedAt - Duration.FromDays(7);
        var previousMonthStartedAt = currentMonthStartedAt - Duration.FromDays(30);
        var rollingDayAgo = now - Duration.FromDays(1);
        var rollingWeekAgo = now - Duration.FromDays(7);
        var rollingMonthAgo = now - Duration.FromDays(30);

        var stats = new AccountActivityStats();
        string? pageToken = null;
        do
        {
            var page = await accounts.ListAccountsAsync(new DyListAccountsRequest
            {
                PageSize = 200,
                PageToken = pageToken ?? string.Empty,
                Filter = string.Empty,
                OrderBy = string.Empty
            });
            foreach (var account in page.Accounts)
            {
                var createdAt = account.CreatedAt.ToInstant();
                var lastSeenAt = account.Profile?.LastSeenAt?.ToInstant();

                stats.TotalAccounts++;
                if (createdAt >= currentDayStartedAt) stats.NewAccountsToday++;
                if (createdAt >= currentWeekStartedAt) stats.NewAccountsThisWeek++;
                if (createdAt >= currentMonthStartedAt) stats.NewAccountsThisMonth++;
                if (createdAt >= rollingDayAgo) stats.NewAccountsLastDay++;
                if (createdAt >= rollingWeekAgo) stats.NewAccountsLastWeek++;
                if (createdAt >= rollingMonthAgo) stats.NewAccountsLastMonth++;

                if (lastSeenAt is null) continue;
                if (lastSeenAt >= currentDayStartedAt) stats.ActiveUsersToday++;
                if (lastSeenAt >= currentWeekStartedAt) stats.ActiveUsersThisWeek++;
                if (lastSeenAt >= currentMonthStartedAt) stats.ActiveUsersThisMonth++;
                if (lastSeenAt >= previousDayStartedAt && lastSeenAt < currentDayStartedAt) stats.ActiveUsersPreviousDay++;
                if (lastSeenAt >= previousWeekStartedAt && lastSeenAt < currentWeekStartedAt) stats.ActiveUsersPreviousWeek++;
                if (lastSeenAt >= previousMonthStartedAt && lastSeenAt < currentMonthStartedAt) stats.ActiveUsersPreviousMonth++;
                if (lastSeenAt >= rollingDayAgo) stats.ActiveUsersLastDay++;
                if (lastSeenAt >= rollingWeekAgo) stats.ActiveUsersLastWeek++;
                if (lastSeenAt >= rollingMonthAgo) stats.ActiveUsersLastMonth++;
            }
            pageToken = string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor;
        } while (pageToken is not null);

        stats.CurrentDayStartedAt = currentDayStartedAt;
        return stats;
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
        await spells.NotifyMagicSpell(spell, true);
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

        db.Remove(badge);
        await db.SaveChangesAsync();

        var profile = await GetOrCreateAccountProfileAsync(account.Id);
        if (profile.ActiveBadge is not null && profile.ActiveBadge.Id == badge.Id)
        {
            await eventBus.PublishAsync(new ProfileFieldUpdatedEvent
            {
                AccountId = account.Id,
                ActiveBadge = null
            });
            await PurgeAccountCache(account);
        }
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

            await eventBus.PublishAsync(new ProfileFieldUpdatedEvent
            {
                AccountId = account.Id,
                ActiveBadge = badge.ToReference()
            });
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

/// <summary>Account activity statistics (account_profiles moved to Stargate).</summary>
public class AccountActivityStats
{
    public Instant CurrentDayStartedAt { get; set; }
    public int TotalAccounts { get; set; }
    public int ActiveUsersToday { get; set; }
    public int ActiveUsersThisWeek { get; set; }
    public int ActiveUsersThisMonth { get; set; }
    public int ActiveUsersPreviousDay { get; set; }
    public int ActiveUsersPreviousWeek { get; set; }
    public int ActiveUsersPreviousMonth { get; set; }
    public int ActiveUsersLastDay { get; set; }
    public int ActiveUsersLastWeek { get; set; }
    public int ActiveUsersLastMonth { get; set; }
    public int NewAccountsToday { get; set; }
    public int NewAccountsThisWeek { get; set; }
    public int NewAccountsThisMonth { get; set; }
    public int NewAccountsLastDay { get; set; }
    public int NewAccountsLastWeek { get; set; }
    public int NewAccountsLastMonth { get; set; }
}
