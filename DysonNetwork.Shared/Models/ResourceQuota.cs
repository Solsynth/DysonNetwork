namespace DysonNetwork.Shared.Models;

public static class ResourceQuotaCalculator
{
    public static int GetPublisherQuota(int level, int perkLevel)
    {
        var baseQuota = level >= 30 ? 3 : 2;
        return baseQuota + (2 * perkLevel);
    }

    public static int GetTieredQuota(int level, int perkLevel)
    {
        var baseQuota = level switch
        {
            >= 90 => 3,
            >= 60 => 2,
            >= 30 => 1,
            _ => 0
        };

        return baseQuota + perkLevel;
    }

    public static int GetProtectedTagQuota(int level, int perkLevel)
    {
        return 3 + 3 * perkLevel;
    }
}

public class ResourceQuotaResponse<TRecord>
{
    public int Total { get; set; }
    public int Used { get; set; }
    public int Remaining { get; set; }
    public int Level { get; set; }
    public int PerkLevel { get; set; }
    public List<TRecord> Records { get; set; } = [];
}

public class PublisherQuotaRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public PublisherType Type { get; set; }
    public Guid? RealmId { get; set; }
}

public class RealmQuotaRecord
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class DeveloperBotQuotaRecord
{
    public Guid BotId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public Guid DeveloperId { get; set; }
    public string DeveloperName { get; set; } = string.Empty;
}

public class ProtectedTagQuotaRecord
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string? Name { get; set; }
}
