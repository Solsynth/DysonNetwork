using System.Text.Json;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Startup;

public class AutomodRuleConfig
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public bool IsRegex { get; set; }
    public int DerankWeight { get; set; } = 10;
    public bool IsEnabled { get; set; } = true;
    public int Priority { get; set; } = 0;
}

public class AutomodConfigSection
{
    public List<AutomodRuleConfig>? Rules { get; set; }
}

public static class AutomodConfiguration
{
    public static async Task InitializeAutomodRulesAsync(IServiceProvider services, IConfiguration configuration)
    {
        var configSection = configuration.GetSection("Automod");
        if (!configSection.Exists())
            return;

        var config = configSection.Get<AutomodConfigSection>();
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
                var existingRule = await db.AutomodRules
                    .FirstOrDefaultAsync(r => r.Name == ruleConfig.Name);

                var ruleType = ParseRuleType(ruleConfig.Type);
                var ruleAction = ParseRuleAction(ruleConfig.Action);

                if (existingRule is null)
                {
                    var newRule = new SnAutomodRule
                    {
                        Id = Guid.NewGuid(),
                        Name = ruleConfig.Name,
                        Description = ruleConfig.Description,
                        Type = ruleType,
                        DefaultAction = ruleAction,
                        Pattern = ruleConfig.Pattern,
                        IsRegex = ruleConfig.IsRegex,
                        DerankWeight = ruleConfig.DerankWeight,
                        IsEnabled = ruleConfig.IsEnabled,
                        Priority = ruleConfig.Priority,
                        CreatedAt = now,
                        UpdatedAt = now
                    };
                    db.AutomodRules.Add(newRule);
                }
                else
                {
                    existingRule.Description = ruleConfig.Description;
                    existingRule.Type = ruleType;
                    existingRule.DefaultAction = ruleAction;
                    existingRule.Pattern = ruleConfig.Pattern;
                    existingRule.IsRegex = ruleConfig.IsRegex;
                    existingRule.DerankWeight = ruleConfig.DerankWeight;
                    existingRule.IsEnabled = ruleConfig.IsEnabled;
                    existingRule.Priority = ruleConfig.Priority;
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

    private static AutomodRuleType ParseRuleType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "linkblocklist" or "link_blocklist" or "link" => AutomodRuleType.LinkBlocklist,
            "keywordblocklist" or "keyword_blocklist" or "keyword" => AutomodRuleType.KeywordBlocklist,
            _ => AutomodRuleType.KeywordBlocklist
        };
    }

    private static AutomodRuleAction ParseRuleAction(string action)
    {
        return action.ToLowerInvariant() switch
        {
            "derank" => AutomodRuleAction.Derank,
            "hide" => AutomodRuleAction.Hide,
            "flag" => AutomodRuleAction.Flag,
            _ => AutomodRuleAction.Derank
        };
    }
}