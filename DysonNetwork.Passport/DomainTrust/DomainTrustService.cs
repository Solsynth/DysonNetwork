using System.Text.RegularExpressions;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace DysonNetwork.Passport.DomainTrust;

public class DomainValidationResult
{
    public bool IsAllowed { get; set; }
    public bool IsVerified { get; set; }
    public string? BlockReason { get; set; }
    public SnDomainBlock? MatchedRule { get; set; }
    public string? MatchedSource { get; set; }
}

public class DomainTrustService
{
    private readonly AppDatabase _db;
    private readonly DomainBlockSettings _settings;
    private readonly IClock _clock;

    public DomainTrustService(AppDatabase db, IOptions<DomainBlockSettings> settings, IClock clock)
    {
        _db = db;
        _settings = settings.Value;
        _clock = clock;
    }

    public async Task<DomainValidationResult> ValidateUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return new DomainValidationResult
            {
                IsAllowed = false,
                BlockReason = "Invalid URL format"
            };
        }

        var host = uri.Host;
        var scheme = uri.Scheme.ToLowerInvariant();
        var port = uri.IsDefaultPort ? null : (int?)uri.Port;

        if (_settings.BlockHttpByDefault && scheme == "http")
        {
            var httpRule = await _db.DomainBlocks
                .FirstOrDefaultAsync(r => r.IsActive && r.Protocol == "http" && r.DomainPattern == "*");

            return new DomainValidationResult
            {
                IsAllowed = false,
                BlockReason = "HTTP protocol is blocked by default (not secure)",
                MatchedSource = "default_http_block",
                MatchedRule = httpRule
            };
        }

        if (_settings.BlockIpAddressesByDefault && DomainBlockSettings.IsIpAddress(host))
        {
            if (_settings.BlockPrivateNetworksByDefault && DomainBlockSettings.IsPrivateNetwork(host))
            {
                return new DomainValidationResult
                {
                    IsAllowed = false,
                    BlockReason = "Private network addresses are blocked by default",
                    MatchedSource = "default_private_network_block"
                };
            }

            return new DomainValidationResult
            {
                IsAllowed = false,
                BlockReason = "Direct IP addresses are blocked by default",
                MatchedSource = "default_ip_block"
            };
        }

        var blockedPorts = _settings.AdditionalBlockedPorts ?? DomainBlockSettings.DefaultBlockedPorts;
        if (port.HasValue && blockedPorts.Contains(port.Value))
        {
            return new DomainValidationResult
            {
                IsAllowed = false,
                BlockReason = $"Port {port.Value} is blocked by default",
                MatchedSource = "default_port_block"
            };
        }

        var dbRule = await FindMatchingRuleAsync(host, scheme, port);
        if (dbRule != null)
        {
            if (dbRule.TrustLevel == DomainTrustLevel.Blocked)
            {
                return new DomainValidationResult
                {
                    IsAllowed = false,
                    IsVerified = false,
                    BlockReason = dbRule.Reason ?? "Domain is blocked",
                    MatchedRule = dbRule,
                    MatchedSource = "database_rule"
                };
            }

            return new DomainValidationResult
            {
                IsAllowed = true,
                IsVerified = dbRule.TrustLevel == DomainTrustLevel.Verified,
                MatchedRule = dbRule,
                MatchedSource = "database_rule"
            };
        }

        return new DomainValidationResult
        {
            IsAllowed = true,
            IsVerified = false
        };
    }

    public async Task<SnDomainBlock?> FindMatchingRuleAsync(string host, string? scheme = null, int? port = null)
    {
        var rules = await _db.DomainBlocks
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ToListAsync();

        foreach (var rule in rules)
        {
            if (MatchesRule(rule, host, scheme, port))
            {
                return rule;
            }
        }

        return null;
    }

    private bool MatchesRule(SnDomainBlock rule, string host, string? scheme, int? port)
    {
        if (!string.IsNullOrEmpty(rule.Protocol) &&
            !string.IsNullOrEmpty(scheme) &&
            !rule.Protocol.Equals(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rule.PortRestriction.HasValue && rule.PortRestriction != port)
        {
            return false;
        }

        return MatchesDomainPattern(rule.DomainPattern, host);
    }

    public static bool MatchesDomainPattern(string pattern, string host)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(host))
            return false;

        pattern = pattern.ToLowerInvariant().Trim();
        host = host.ToLowerInvariant().Trim();

        if (pattern == "*")
            return true;

        if (pattern.StartsWith("**."))
        {
            var baseDomain = pattern[3..];
            return host == baseDomain || host.EndsWith("." + baseDomain);
        }

        if (pattern.StartsWith("*."))
        {
            var baseDomain = pattern[2..];
            return host.EndsWith("." + baseDomain);
        }

        if (pattern.StartsWith("*."))
        {
            var suffix = pattern[1..];
            return host.EndsWith(suffix);
        }

        if (pattern.Contains('*'))
        {
            var regexPattern = "^" + Regex.Escape(pattern)
                .Replace(@"\*", ".*")
                .Replace(@"\?", ".") + "$";
            return Regex.IsMatch(host, regexPattern, RegexOptions.IgnoreCase);
        }

        return string.Equals(pattern, host, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<SnDomainBlock>> GetAllRulesAsync(int offset = 0, int limit = 50)
    {
        return await _db.DomainBlocks
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.DomainPattern)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetTotalCountAsync()
    {
        return await _db.DomainBlocks.CountAsync();
    }

    public async Task<SnDomainBlock?> GetRuleAsync(Guid id)
    {
        return await _db.DomainBlocks.FindAsync(id);
    }

    public async Task<SnDomainBlock> CreateRuleAsync(SnDomainBlock rule, Guid? creatorId = null)
    {
        rule.Id = Guid.NewGuid();
        rule.CreatedAt = _clock.GetCurrentInstant();
        rule.UpdatedAt = _clock.GetCurrentInstant();
        rule.CreatedByAccountId = creatorId;

        _db.DomainBlocks.Add(rule);
        await _db.SaveChangesAsync();

        return rule;
    }

    public async Task<SnDomainBlock?> UpdateRuleAsync(Guid id, Action<SnDomainBlock> updateAction)
    {
        var rule = await _db.DomainBlocks.FindAsync(id);
        if (rule == null) return null;

        updateAction(rule);
        rule.UpdatedAt = _clock.GetCurrentInstant();

        await _db.SaveChangesAsync();
        return rule;
    }

    public async Task<bool> DeleteRuleAsync(Guid id)
    {
        var rule = await _db.DomainBlocks.FindAsync(id);
        if (rule == null) return false;

        _db.DomainBlocks.Remove(rule);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<List<SnDomainValidationMetric>> GetDomainMetricsAsync(int offset = 0, int limit = 50)
    {
        return await _db.DomainValidationMetrics
            .OrderByDescending(x => x.CheckCount)
            .ThenBy(x => x.Domain)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> GetDomainMetricCountAsync()
    {
        return await _db.DomainValidationMetrics.CountAsync();
    }

    public async Task<SnDomainValidationMetric?> GetDomainMetricAsync(string domain)
    {
        return await _db.DomainValidationMetrics
            .FirstOrDefaultAsync(x => x.Domain == domain);
    }
}
