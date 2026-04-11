using System.Text.RegularExpressions;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub.Services;

public class FediverseModerationResult
{
    public bool IsBlocked { get; set; }
    public bool IsSilenced { get; set; }
    public bool IsSuspended { get; set; }
    public bool IsDeranked { get; set; }
    public bool ShouldFlag { get; set; }
    public string? MatchedRuleName { get; set; }
    public string? MatchedDomain { get; set; }
    public string? MatchedKeyword { get; set; }
    public FediverseModerationAction? Action { get; set; }
}

public class FediverseModerationService
{
    private readonly AppDatabase db;
    private readonly ILogger<FediverseModerationService> logger;
    private readonly IClock clock;
    private List<SnFediverseModerationRule>? cachedRules;

    public FediverseModerationService(
        AppDatabase db,
        ILogger<FediverseModerationService> logger,
        IClock clock)
    {
        this.db = db;
        this.logger = logger;
        this.clock = clock;
    }

    public async Task<FediverseModerationResult> CheckDomainAsync(string domain)
    {
        await EnsureRulesCachedAsync();

        var result = new FediverseModerationResult();

        if (string.IsNullOrEmpty(domain))
            return result;

        var domainLower = domain.ToLowerInvariant();

        foreach (var rule in cachedRules!.Where(r => r.IsEnabled && !r.IsSystemRule).OrderBy(r => r.Priority))
        {
            if (rule.Type == FediverseModerationRuleType.DomainBlock && !string.IsNullOrEmpty(rule.Domain))
            {
                if (MatchesDomain(domainLower, rule.Domain.ToLowerInvariant(), rule.IsRegex))
                {
                    result.IsBlocked = true;
                    result.MatchedRuleName = rule.Name;
                    result.MatchedDomain = domain;
                    result.Action = rule.Action;
                    ApplyAction(rule.Action, result);
                    
                    logger.LogDebug("Domain {Domain} matched rule {RuleName} (DomainBlock)", domain, rule.Name);
                    return result;
                }
            }
        }

        return result;
    }

    public async Task<FediverseModerationResult> CheckActorAsync(string? actorUri, string? content, string? actorDomain)
    {
        await EnsureRulesCachedAsync();

        var result = new FediverseModerationResult();

        var domain = actorDomain ?? ExtractDomainFromUri(actorUri);
        if (!string.IsNullOrEmpty(domain))
        {
            var domainResult = await CheckDomainAsync(domain);
            if (domainResult.IsBlocked || domainResult.IsSilenced || domainResult.IsSuspended)
            {
                return domainResult;
            }
        }

        if (!string.IsNullOrEmpty(content))
        {
            foreach (var rule in cachedRules!.Where(r => r.IsEnabled && !r.IsSystemRule).OrderBy(r => r.Priority))
            {
                if ((rule.Type == FediverseModerationRuleType.KeywordBlock || rule.Type == FediverseModerationRuleType.KeywordAllow)
                    && !string.IsNullOrEmpty(rule.KeywordPattern))
                {
                    if (MatchesKeyword(content, rule.KeywordPattern, rule.IsRegex))
                    {
                        result.ShouldFlag = true;
                        result.MatchedRuleName = rule.Name;
                        result.MatchedKeyword = rule.KeywordPattern;
                        result.Action = rule.Action;
                        ApplyAction(rule.Action, result);
                        
                        logger.LogDebug("Content matched rule {RuleName} (KeywordBlock)", rule.Name);
                        return result;
                    }
                }
            }
        }

        return result;
    }

    public async Task<FediverseModerationResult> CheckInstanceAsync(string domain)
    {
        await EnsureRulesCachedAsync();

        var result = new FediverseModerationResult();

        if (string.IsNullOrEmpty(domain))
            return result;

        var domainLower = domain.ToLowerInvariant();

        foreach (var rule in cachedRules!.Where(r => r.IsEnabled).OrderBy(r => r.Priority))
        {
            if (rule.Type == FediverseModerationRuleType.DomainBlock && !string.IsNullOrEmpty(rule.Domain))
            {
                if (MatchesDomain(domainLower, rule.Domain.ToLowerInvariant(), rule.IsRegex))
                {
                    result.IsBlocked = true;
                    result.MatchedRuleName = rule.Name;
                    result.MatchedDomain = domain;
                    result.Action = rule.Action;
                    ApplyAction(rule.Action, result);
                    
                    logger.LogInformation("Instance {Domain} blocked by rule {RuleName}", domain, rule.Name);
                    return result;
                }
            }
            else if (rule.Type == FediverseModerationRuleType.DomainAllow)
            {
                if (MatchesDomain(domainLower, rule.Domain.ToLowerInvariant(), rule.IsRegex))
                {
                    logger.LogDebug("Instance {Domain} explicitly allowed by rule {RuleName}", domain, rule.Name);
                    return result;
                }
            }
        }

        return result;
    }

    private void ApplyAction(FediverseModerationAction action, FediverseModerationResult result)
    {
        switch (action)
        {
            case FediverseModerationAction.Block:
                result.IsBlocked = true;
                break;
            case FediverseModerationAction.Silence:
                result.IsSilenced = true;
                break;
            case FediverseModerationAction.Suspend:
                result.IsSuspended = true;
                break;
            case FediverseModerationAction.Flag:
                result.ShouldFlag = true;
                break;
            case FediverseModerationAction.Derank:
                result.IsDeranked = true;
                break;
        }
    }

    private bool MatchesDomain(string input, string pattern, bool isRegex)
    {
        if (isRegex)
        {
            try
            {
                return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return input.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private bool MatchesKeyword(string content, string pattern, bool isRegex)
    {
        if (isRegex)
        {
            try
            {
                return Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        return content.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractDomainFromUri(string? uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        try
        {
            var uriObj = new Uri(uri);
            return uriObj.Host;
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureRulesCachedAsync()
    {
        if (cachedRules == null)
        {
            var now = clock.GetCurrentInstant();
            cachedRules = await db.FediverseModerationRules
                .Where(r => r.ExpiresAt == null || r.ExpiresAt > now)
                .OrderBy(r => r.Priority)
                .ToListAsync();
        }
    }

    public void InvalidateCache()
    {
        cachedRules = null;
    }

    public async Task<List<SnFediverseModerationRule>> GetAllRulesAsync()
    {
        return await db.FediverseModerationRules
            .OrderBy(r => r.Priority)
            .ToListAsync();
    }

    public async Task<SnFediverseModerationRule?> GetRuleByIdAsync(Guid id)
    {
        return await db.FediverseModerationRules.FindAsync(id);
    }

    public async Task<SnFediverseModerationRule> CreateRuleAsync(SnFediverseModerationRule rule)
    {
        rule.Id = Guid.NewGuid();
        rule.CreatedAt = clock.GetCurrentInstant();
        rule.UpdatedAt = rule.CreatedAt;
        
        db.FediverseModerationRules.Add(rule);
        await db.SaveChangesAsync();
        
        InvalidateCache();
        return rule;
    }

    public async Task<SnFediverseModerationRule?> UpdateRuleAsync(Guid id, SnFediverseModerationRule updates)
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule == null)
            return null;

        rule.Name = updates.Name;
        rule.Description = updates.Description;
        rule.Type = updates.Type;
        rule.Action = updates.Action;
        rule.Domain = updates.Domain;
        rule.KeywordPattern = updates.KeywordPattern;
        rule.IsRegex = updates.IsRegex;
        rule.IsEnabled = updates.IsEnabled;
        rule.ReportThreshold = updates.ReportThreshold;
        rule.Priority = updates.Priority;
        rule.ExpiresAt = updates.ExpiresAt;
        rule.UpdatedAt = clock.GetCurrentInstant();

        await db.SaveChangesAsync();
        
        InvalidateCache();
        return rule;
    }

    public async Task<bool> DeleteRuleAsync(Guid id)
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule == null)
            return false;

        if (rule.IsSystemRule)
        {
            logger.LogWarning("Attempted to delete system rule {RuleId}", id);
            return false;
        }

        db.FediverseModerationRules.Remove(rule);
        await db.SaveChangesAsync();
        
        InvalidateCache();
        return true;
    }

    public async Task<bool> ToggleRuleAsync(Guid id, bool enabled)
    {
        var rule = await db.FediverseModerationRules.FindAsync(id);
        if (rule == null)
            return false;

        rule.IsEnabled = enabled;
        rule.UpdatedAt = clock.GetCurrentInstant();
        
        await db.SaveChangesAsync();
        InvalidateCache();
        
        return true;
    }
}
