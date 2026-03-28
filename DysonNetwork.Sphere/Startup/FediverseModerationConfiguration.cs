using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Startup;

public class FediverseModerationRuleConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Domain { get; set; }
    public string? KeywordPattern { get; set; }
    public bool IsRegex { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int? ReportThreshold { get; set; }
    public int Priority { get; set; } = 0;
    public bool IsSystemRule { get; set; } = false;
}

public class FediverseModerationConfigSection
{
    public List<FediverseModerationRuleConfig>? Rules { get; set; }
}

public static class FediverseModerationConfiguration
{
    public static async Task InitializeFediverseModerationRulesAsync(IServiceProvider services, IConfiguration configuration)
    {
        var configSection = configuration.GetSection("FediverseModeration");
        if (!configSection.Exists())
            return;

        var config = configSection.Get<FediverseModerationConfigSection>();
        if (config?.Rules == null || config.Rules.Count == 0)
            return;

        var serviceProvider = services;
        var db = serviceProvider.GetRequiredService<AppDatabase>();

        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var now = SystemClock.Instance.GetCurrentInstant();

            foreach (var ruleConfig in config.Rules)
            {
                var existingRule = await db.FediverseModerationRules
                    .FirstOrDefaultAsync(r => r.Name == ruleConfig.Name);

                var ruleType = ParseRuleType(ruleConfig.Type);
                var ruleAction = ParseRuleAction(ruleConfig.Action);

                if (existingRule is null)
                {
                    var newRule = new SnFediverseModerationRule
                    {
                        Id = Guid.NewGuid(),
                        Name = ruleConfig.Name,
                        Description = ruleConfig.Description,
                        Type = ruleType,
                        Action = ruleAction,
                        Domain = ruleConfig.Domain,
                        KeywordPattern = ruleConfig.KeywordPattern,
                        IsRegex = ruleConfig.IsRegex,
                        IsEnabled = ruleConfig.IsEnabled,
                        ReportThreshold = ruleConfig.ReportThreshold,
                        Priority = ruleConfig.Priority,
                        IsSystemRule = ruleConfig.IsSystemRule,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.FediverseModerationRules.Add(newRule);
                }
                else
                {
                    existingRule.Description = ruleConfig.Description;
                    existingRule.Type = ruleType;
                    existingRule.Action = ruleAction;
                    existingRule.Domain = ruleConfig.Domain;
                    existingRule.KeywordPattern = ruleConfig.KeywordPattern;
                    existingRule.IsRegex = ruleConfig.IsRegex;
                    existingRule.IsEnabled = ruleConfig.IsEnabled;
                    existingRule.ReportThreshold = ruleConfig.ReportThreshold;
                    existingRule.Priority = ruleConfig.Priority;
                    existingRule.IsSystemRule = ruleConfig.IsSystemRule;
                    existingRule.UpdatedAt = now;
                }
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static FediverseModerationRuleType ParseRuleType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "domainblock" or "domain_block" or "block" => FediverseModerationRuleType.DomainBlock,
            "domainallow" or "domain_allow" or "allow" => FediverseModerationRuleType.DomainAllow,
            "keywordblock" or "keyword_block" or "keyword" => FediverseModerationRuleType.KeywordBlock,
            "keywordallow" or "keyword_allow" => FediverseModerationRuleType.KeywordAllow,
            "reportthreshold" or "report_threshold" => FediverseModerationRuleType.ReportThreshold,
            _ => FediverseModerationRuleType.DomainBlock
        };
    }

    private static FediverseModerationAction ParseRuleAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "block" => FediverseModerationAction.Block,
            "allow" => FediverseModerationAction.Allow,
            "silence" => FediverseModerationAction.Silence,
            "suspend" => FediverseModerationAction.Suspend,
            "flag" => FediverseModerationAction.Flag,
            "derank" => FediverseModerationAction.Derank,
            _ => FediverseModerationAction.Flag
        };
    }
}
