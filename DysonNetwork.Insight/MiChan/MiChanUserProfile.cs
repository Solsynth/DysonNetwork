using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Insight.MiChan;

[Table("user_profiles")]
[Index(nameof(AccountId), IsUnique = true)]
public class MiChanUserProfile : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AccountId { get; set; }

    [Column(TypeName = "text")]
    public string? ProfileSummary { get; set; }

    [Column(TypeName = "text")]
    public string? ImpressionSummary { get; set; }

    [Column(TypeName = "text")]
    public string? RelationshipSummary { get; set; }

    [Column(TypeName = "jsonb")]
    public List<string> Tags { get; set; } = [];

    public int Favorability { get; set; } = 0;

    public int TrustLevel { get; set; } = 0;

    public int IntimacyLevel { get; set; } = 0;

    public int InteractionCount { get; set; } = 0;

    public Instant? LastInteractionAt { get; set; }

    public Instant? LastProfileUpdateAt { get; set; }

    public string ToPrompt()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"accountId={AccountId}");
        builder.AppendLine($"favorability={Favorability}; trust={TrustLevel}; intimacy={IntimacyLevel}; interactions={InteractionCount}");

        if (Tags.Count > 0)
            builder.AppendLine($"tags={string.Join(", ", Tags)}");

        if (!string.IsNullOrWhiteSpace(ProfileSummary))
            builder.AppendLine($"profile={ProfileSummary.Trim()}");

        if (!string.IsNullOrWhiteSpace(ImpressionSummary))
            builder.AppendLine($"impression={ImpressionSummary.Trim()}");

        if (!string.IsNullOrWhiteSpace(RelationshipSummary))
            builder.AppendLine($"relationship={RelationshipSummary.Trim()}");

        return builder.ToString().TrimEnd();
    }
}
