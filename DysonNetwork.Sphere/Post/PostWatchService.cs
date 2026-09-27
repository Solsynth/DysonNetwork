using DysonNetwork.Sphere.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Post;

public sealed record PostWatchPreferenceUpdate(
    PostWatchSource Source,
    bool? NotifyReactions,
    bool? NotifyReplies,
    bool? NotifyChains,
    bool? NotifyForwards,
    bool? NotifyEdits
);

public class PostWatchService(AppDatabase db)
{
    public static readonly PostWatchSource[] AllSources =
    [
        PostWatchSource.Bookmark,
        PostWatchSource.Reaction,
        PostWatchSource.Reply,
    ];

    public async Task<List<Guid>> ResolveWatcherAccountIdsAsync(
        IReadOnlyCollection<Guid> postIds,
        PostWatchEvent evt,
        CancellationToken cancellationToken = default
    )
    {
        if (postIds.Count == 0)
            return [];

        var bookmarkers = await db.PostBookmarks
            .AsNoTracking()
            .Where(b => postIds.Contains(b.PostId))
            .Select(b => b.AccountId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var reactors = await db.PostReactions
            .AsNoTracking()
            .Where(r => postIds.Contains(r.PostId) && r.AccountId != null)
            .Select(r => r.AccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var repliers = await db.Posts
            .AsNoTracking()
            .Where(p => p.RepliedPostId != null
                && postIds.Contains(p.RepliedPostId.Value)
                && p.Publisher != null
                && p.Publisher.AccountId != null)
            .Select(p => p.Publisher!.AccountId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var sources = new Dictionary<Guid, HashSet<PostWatchSource>>();
        AddSources(sources, bookmarkers, PostWatchSource.Bookmark);
        AddSources(sources, reactors, PostWatchSource.Reaction);
        AddSources(sources, repliers, PostWatchSource.Reply);
        if (sources.Count == 0)
            return [];

        var accountIds = sources.Keys.ToList();
        var stored = await db.PostWatchPreferences
            .AsNoTracking()
            .Where(p => accountIds.Contains(p.AccountId))
            .ToListAsync(cancellationToken);
        var storedMap = stored.ToDictionary(p => (p.AccountId, p.Source));

        var result = new List<Guid>();
        foreach (var (accountId, accountSources) in sources)
        {
            foreach (var source in accountSources)
            {
                var preference = storedMap.TryGetValue((accountId, source), out var row)
                    ? row
                    : SnPostWatchPreference.DefaultFor(accountId, source);
                if (!preference.Allows(evt))
                    continue;
                result.Add(accountId);
                break;
            }
        }

        return result;
    }

    public async Task<List<SnPostWatchPreference>> GetEffectivePreferencesAsync(
        Guid accountId,
        CancellationToken cancellationToken = default
    )
    {
        var stored = await db.PostWatchPreferences
            .AsNoTracking()
            .Where(p => p.AccountId == accountId)
            .ToListAsync(cancellationToken);
        var map = stored.ToDictionary(p => p.Source);

        return AllSources
            .Select(source =>
                map.TryGetValue(source, out var row)
                    ? row
                    : SnPostWatchPreference.DefaultFor(accountId, source)
            )
            .ToList();
    }

    public async Task<List<SnPostWatchPreference>> UpdatePreferencesAsync(
        Guid accountId,
        IReadOnlyList<PostWatchPreferenceUpdate> updates,
        CancellationToken cancellationToken = default
    )
    {
        if (updates.Count == 0)
            return await GetEffectivePreferencesAsync(accountId, cancellationToken);

        var requestedSources = updates.Select(u => u.Source).ToList();
        var stored = await db.PostWatchPreferences
            .Where(p => p.AccountId == accountId && requestedSources.Contains(p.Source))
            .ToListAsync(cancellationToken);
        var map = stored.ToDictionary(p => p.Source);

        foreach (var update in updates)
        {
            if (!map.TryGetValue(update.Source, out var row))
            {
                row = SnPostWatchPreference.DefaultFor(accountId, update.Source);
                db.PostWatchPreferences.Add(row);
            }

            row.NotifyReactions = update.NotifyReactions ?? row.NotifyReactions;
            row.NotifyReplies = update.NotifyReplies ?? row.NotifyReplies;
            row.NotifyChains = update.NotifyChains ?? row.NotifyChains;
            row.NotifyForwards = update.NotifyForwards ?? row.NotifyForwards;
            row.NotifyEdits = update.NotifyEdits ?? row.NotifyEdits;
        }

        await db.SaveChangesAsync(cancellationToken);

        return await GetEffectivePreferencesAsync(accountId, cancellationToken);
    }

    public async Task<List<Guid>> GetChainPostIdsAsync(
        Guid chainHeadId,
        CancellationToken cancellationToken = default
    )
    {
        return await db.Posts
            .AsNoTracking()
            .Where(p => p.Id == chainHeadId || p.ChainedPostId == chainHeadId)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    private static void AddSources(
        Dictionary<Guid, HashSet<PostWatchSource>> sources,
        IEnumerable<Guid> accountIds,
        PostWatchSource source
    )
    {
        foreach (var accountId in accountIds)
        {
            if (!sources.TryGetValue(accountId, out var set))
            {
                set = [];
                sources[accountId] = set;
            }

            set.Add(source);
        }
    }
}
