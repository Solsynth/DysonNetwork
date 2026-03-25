using System.Text.RegularExpressions;
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

public class AutomodService(
    AppDatabase db,
    ICacheService cache
)
{
    private static readonly TimeSpan RulesCacheTtl = TimeSpan.FromMinutes(5);
    private const string RulesCacheKey = "automod:rules:enabled";

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

        var postContents = posts.ToDictionary(
            p => p.Id,
            p => BuildContentString(p)
        );

        var result = new Dictionary<Guid, (double Penalty, bool ShouldHide)>();

        foreach (var post in posts)
        {
            var content = postContents[post.Id];
            double totalPenalty = 0;
            bool shouldHide = false;

            foreach (var rule in rules)
            {
                var matchedText = MatchRule(rule, content);
                if (matchedText is not null)
                {
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
            }

            result[post.Id] = (totalPenalty, shouldHide);
        }

        return result;
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
                var options = RegexOptions.IgnoreCase | RegexOptions.Compiled;
                var match = Regex.Match(content, rule.Pattern, options);
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