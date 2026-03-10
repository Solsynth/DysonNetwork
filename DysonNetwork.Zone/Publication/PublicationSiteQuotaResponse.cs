using DysonNetwork.Shared.Models;

namespace DysonNetwork.Zone.Publication;

public class PublicationSiteQuotaRecord
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid PublisherId { get; set; }
    public PublicationSiteMode Mode { get; set; }
}

public class PublicationSiteQuotaResponse
{
    public int Total { get; set; }
    public int Used { get; set; }
    public int Remaining { get; set; }
    public int Level { get; set; }
    public int PerkLevel { get; set; }
    public bool IsUnlimited { get; set; }
    public List<PublicationSiteQuotaRecord> Records { get; set; } = [];
}
