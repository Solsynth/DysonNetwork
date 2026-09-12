using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DysonNetwork.Sphere.Models;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Automod;

public class AutomodRuleResult
{
    public Guid RuleId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public AutomodRuleType Type { get; set; }
    public AutomodRuleAction Action { get; set; }
    public int DerankWeight { get; set; }
    public string MatchedText { get; set; } = string.Empty;
}

public sealed class AutomodPenaltyEntry
{
    public double Penalty { get; set; }
    public bool ShouldHide { get; set; }
}

public class AutomodService(
    AppDatabase db,
    ICacheService cache
)
{
    private static readonly TimeSpan RulesCacheTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PenaltyCacheTtl = TimeSpan.FromMinutes(2);
    private const string RulesCacheKey = "automod:rules:enabled";
    private const string PenaltyCacheKeyPrefix = "automod:penalty:";
    private static readonly ConcurrentDictionary<string, Regex> CompiledRegexCache = new();

    public async Task<List<SnAutomodRule>> GetEnabledRulesAsync()
    {
        var cachedRules = await cache.GetAsync<List<SnAutomodRule>>(RulesCacheKey);
        if (cachedRules is not null)
            return cachedRules;

        var now = SystemClock.Instance.GetCurrentInstant();
        var rules = await db.AutomodRules
            .Where(r => r.IsEnabled)
            .Where(r => r.ExpiresAt == null || r.ExpiresAt > now)
            .OrderBy(r => r.Priority)
            .ToListAsync();

        await cache.SetAsync(RulesCacheKey, rules, RulesCacheTtl);
        return rules;
    }

    public async Task InvalidateRulesCacheAsync()
    {
        await cache.RemoveAsync(RulesCacheKey);
    }

    public async Task<List<AutomodRuleResult>> EvaluatePostAsync(SnPost post)
    {
        var rules = await GetEnabledRulesAsync();
        var results = new List<AutomodRuleResult>();

        if (rules.Count == 0)
            return results;

        var contentToCheck = BuildContentString(post);

        foreach (var rule in rules)
        {
            var matchedText = MatchRule(rule, contentToCheck);
            if (matchedText is not null)
            {
                results.Add(new AutomodRuleResult
                {
                    RuleId = rule.Id,
                    RuleName = rule.Name,
                    Type = rule.Type,
                    Action = rule.DefaultAction,
                    DerankWeight = rule.DerankWeight,
                    MatchedText = matchedText
                });
            }
        }

        return results;
    }

    public async Task<Dictionary<Guid, (double Penalty, bool ShouldHide)>> GetAutomodPenaltiesAsync(
        List<SnPost> posts
    )
    {
        if (posts.Count == 0)
            return [];

        var rules = await GetEnabledRulesAsync();
        if (rules.Count == 0)
            return posts.ToDictionary(p => p.Id, _ => (0d, false));

        var fingerprint = BuildRulesFingerprint(rules);
        var postIds = posts.Select(p => p.Id).ToList();

        var cachedEntries = await Task.WhenAll(
            postIds.Select(id => cache.GetAsync<AutomodPenaltyEntry>(GetPenaltyCacheKey(fingerprint, id)))
        );

        var result = new Dictionary<Guid, (double Penalty, bool ShouldHide)>(postIds.Count);
        var missingIds = new List<Guid>();

        for (var i = 0; i < postIds.Count; i++)
        {
            var entry = cachedEntries[i];
            if (entry is not null)
                result[postIds[i]] = (entry.Penalty, entry.ShouldHide);
            else
                missingIds.Add(postIds[i]);
        }

        if (missingIds.Count > 0)
        {
            var missingPosts = posts.Where(p => missingIds.Contains(p.Id)).ToList();
            var computed = EvaluatePenalties(missingPosts, rules);

            await Task.WhenAll(
                computed.Select(kv =>
                {
                    var (penalty, shouldHide) = kv.Value;
                    return cache.SetAsync(
                        GetPenaltyCacheKey(fingerprint, kv.Key),
                        new AutomodPenaltyEntry { Penalty = penalty, ShouldHide = shouldHide },
                        PenaltyCacheTtl
                    );
                })
            );

            foreach (var kv in computed)
                result[kv.Key] = kv.Value;
        }

        var hiddenCount = result.Count(r => r.Value.ShouldHide);
        var derankedCount = result.Count(r => r.Value.Penalty > 0 && !r.Value.ShouldHide);
        Console.WriteLine($"[Automod] GetAutomodPenaltiesAsync: posts={posts.Count}, rules={rules.Count}, hidden={hiddenCount}, deranked={derankedCount}");

        return result;
    }

    private static Dictionary<Guid, (double Penalty, bool ShouldHide)> EvaluatePenalties(
        List<SnPost> posts,
        List<SnAutomodRule> rules
    )
    {
        var result = new Dictionary<Guid, (double Penalty, bool ShouldHide)>(posts.Count);

        foreach (var post in posts)
        {
            var content = BuildContentString(post);
            double totalPenalty = 0;
            bool shouldHide = false;

            foreach (var rule in rules)
            {
                var matchedText = MatchRule(rule, content);
                if (matchedText is null)
                    continue;

                if (rule.DefaultAction == AutomodRuleAction.Hide)
                {
                    shouldHide = true;
                    totalPenalty += 100d;
                }
                else if (rule.DefaultAction == AutomodRuleAction.Derank)
                {
                    totalPenalty += rule.DerankWeight;
                }
            }

            result[post.Id] = (totalPenalty, shouldHide);
        }

        return result;
    }

    private static string BuildRulesFingerprint(IReadOnlyCollection<SnAutomodRule> rules)
    {
        var sb = new StringBuilder();
        foreach (var rule in rules)
        {
            sb.Append(rule.Id).Append(':').Append(rule.DefaultAction).Append(':')
              .Append(rule.DerankWeight).Append(':').Append(rule.IsRegex).Append(':')
              .Append(rule.Pattern).Append('|');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    private static string GetPenaltyCacheKey(string fingerprint, Guid postId)
    {
        return $"{PenaltyCacheKeyPrefix}{fingerprint}:{postId}";
    }

    private static string BuildContentString(SnPost post)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(post.Title))
            parts.Add(post.Title);
        if (!string.IsNullOrWhiteSpace(post.Description))
            parts.Add(post.Description);
        if (!string.IsNullOrWhiteSpace(post.Content))
            parts.Add(post.Content);

        foreach (var mention in post.Mentions ?? [])
        {
            if (!string.IsNullOrWhiteSpace(mention.Url))
                parts.Add(mention.Url);
        }

        return string.Join(" ", parts);
    }

    private static string? MatchRule(SnAutomodRule rule, string content)
    {
        try
        {
            if (rule.IsRegex)
            {
                var regex = CompiledRegexCache.GetOrAdd(
                    rule.Pattern,
                    pattern => new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled)
                );
                var match = regex.Match(content);
                return match.Success ? match.Value : null;
            }
            else
            {
                return content.Contains(rule.Pattern, StringComparison.OrdinalIgnoreCase)
                    ? rule.Pattern
                    : null;
            }
        }
        catch (RegexParseException)
        {
            return null;
        }
    }
}