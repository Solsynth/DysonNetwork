using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public enum DiscoveryTargetKind
{
    Publisher,
    Realm,
    Account,
}

public enum DiscoveryPreferenceState
{
    Uninterested,
}

[Index(nameof(AccountId), nameof(Kind), nameof(ReferenceId), nameof(DeletedAt), IsUnique = true)]
public class SnDiscoveryPreference : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public DiscoveryTargetKind Kind { get; set; }
    public Guid ReferenceId { get; set; }
    public DiscoveryPreferenceState State { get; set; } = DiscoveryPreferenceState.Uninterested;

    [MaxLength(256)]
    public string? Reason { get; set; }

    public Instant? AppliedAt { get; set; }
}

[NotMapped]
public class SnDiscoveryInterestEntry
{
    public string Kind { get; set; } = string.Empty;
    public Guid ReferenceId { get; set; }
    public string Label { get; set; } = string.Empty;
    public double Score { get; set; }
    public int InteractionCount { get; set; }
    public Instant? LastInteractedAt { get; set; }
    public string? LastSignalType { get; set; }
}

[NotMapped]
public class SnDiscoverySuggestion
{
    public DiscoveryTargetKind Kind { get; set; }
    public Guid ReferenceId { get; set; }
    public string Label { get; set; } = string.Empty;
    public double Score { get; set; }
    public List<string> Reasons { get; set; } = [];
    public object? Data { get; set; }
}

[NotMapped]
public class SnPublisherDiscoveryRef
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public SnCloudFileReferenceObject? Picture { get; set; }
    public SnCloudFileReferenceObject? Background { get; set; }
}

[NotMapped]
public class SnAccountDiscoveryRef
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Nick { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? FirstName { get; set; }
    public string? MiddleName { get; set; }
    public string? LastName { get; set; }
    public string? Pronouns { get; set; }
    public string? Location { get; set; }
    public SnVerificationMark? Verification { get; set; }
    public SnAccountBadgeRef? ActiveBadge { get; set; }
    public SnCloudFileReferenceObject? Picture { get; set; }
    public SnCloudFileReferenceObject? Background { get; set; }
}

[NotMapped]
public class SnRealmDiscoveryRef
{
    public Guid Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SnCloudFileReferenceObject? Picture { get; set; }
    public SnCloudFileReferenceObject? Background { get; set; }
}

[NotMapped]
public class SnDiscoveryProfile
{
    public Instant GeneratedAt { get; set; }
    public List<SnDiscoveryInterestEntry> Interests { get; set; } = [];
    public List<SnDiscoverySuggestion> SuggestedPublishers { get; set; } = [];
    public List<SnDiscoverySuggestion> SuggestedAccounts { get; set; } = [];
    public List<SnDiscoverySuggestion> SuggestedRealms { get; set; } = [];
    public List<SnDiscoverySuggestion> Suppressed { get; set; } = [];
}

[NotMapped]
public class DiscoveryPreferenceRequest
{
    [MaxLength(32)]
    public string Kind { get; set; } = string.Empty;

    public Guid ReferenceId { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }
}

public enum RecommendationFeedbackValue
{
    Positive,
    Negative,
}

[NotMapped]
public class RecommendationFeedbackRequest
{
    [MaxLength(32)]
    public string Kind { get; set; } = string.Empty;

    public Guid ReferenceId { get; set; }

    [MaxLength(16)]
    public string Feedback { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? Reason { get; set; }

    public bool Suppress { get; set; }
}

[NotMapped]
public class RecommendationWeightChangeRequest
{
    [MaxLength(32)]
    public string Kind { get; set; } = string.Empty;

    public Guid ReferenceId { get; set; }

    public double ScoreDelta { get; set; }

    public int InteractionCount { get; set; } = 1;

    [MaxLength(64)]
    public string? SignalType { get; set; }
}

[NotMapped]
public class RecommendationFeedbackResult
{
    public List<SnPostInterestProfile> UpdatedProfiles { get; set; } = [];
    public SnDiscoveryPreference? Preference { get; set; }
}
