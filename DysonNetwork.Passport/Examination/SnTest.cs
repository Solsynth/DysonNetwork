using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Passport.Examination;

public enum TestQuestionType { SingleChoice, MultipleChoice, FreeText }
public enum TestQuestionGradingMode { Auto, Manual }
public enum TestAttemptStatus { InProgress, PendingReview, Passed, Failed, Expired }

public class SnTest : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Key { get; set; } = null!;
    [MaxLength(256)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public bool IsPublished { get; set; }
    public bool IsListed { get; set; } = true;
    public bool ShuffleQuestions { get; set; }
    public int? RandomQuestionCount { get; set; }
    public bool IsArchived { get; set; }
    public double PassingScore { get; set; } = 100;
    public int? MaxAttempts { get; set; }
    public int AttemptPeriodDays { get; set; } = 365;
    public int? TimeLimitSeconds { get; set; }
    [MaxLength(1024)] public string? GrantedPermissionGroupKey { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Config { get; set; } = new();
    public List<SnTestQuestionGroupAssignment> QuestionGroups { get; set; } = [];
}

public class SnTestQuestion : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionGroupId { get; set; }
    public SnTestQuestionGroup QuestionGroup { get; set; } = null!;
    public int SortOrder { get; set; }
    [MaxLength(8192)] public string Content { get; set; } = null!;
    public TestQuestionType Type { get; set; }
    public TestQuestionGradingMode GradingMode { get; set; }
    public int Difficulty { get; set; }
    public double Points { get; set; } = 1;
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Config { get; set; } = new();
    public List<SnTestChoice> Choices { get; set; } = [];
}

public class SnTestQuestionGroup : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(128)] public string Key { get; set; } = null!;
    [MaxLength(256)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Config { get; set; } = new();
    public List<SnTestQuestion> Questions { get; set; } = [];
    public List<SnTestQuestionGroupAssignment> TestAssignments { get; set; } = [];
}

public class SnTestQuestionGroupAssignment : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestId { get; set; }
    public SnTest Test { get; set; } = null!;
    public Guid QuestionGroupId { get; set; }
    public SnTestQuestionGroup QuestionGroup { get; set; } = null!;
    public int SortOrder { get; set; }
}

public class SnTestChoice : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public SnTestQuestion Question { get; set; } = null!;
    public int SortOrder { get; set; }
    [MaxLength(4096)] public string Content { get; set; } = null!;
    public bool IsCorrect { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Config { get; set; } = new();
}

public class SnTestAttempt : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestId { get; set; }
    public Guid AccountId { get; set; }
    public TestAttemptStatus Status { get; set; } = TestAttemptStatus.InProgress;
    public Instant StartedAt { get; set; }
    public Instant? SubmittedAt { get; set; }
    public Instant? DeadlineAt { get; set; }
    public Instant? ReviewedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public double? Score { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Snapshot { get; set; } = new();
    public List<SnTestAnswer> Answers { get; set; } = [];
}

public class SnTestAnswer : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public SnTestAttempt Attempt { get; set; } = null!;
    public Guid QuestionId { get; set; }
    [Column(TypeName = "jsonb")] public Dictionary<string, object?> Value { get; set; } = new();
    public bool? IsCorrect { get; set; }
    public double? AwardedPoints { get; set; }
    [MaxLength(4096)] public string? ReviewNote { get; set; }
    public Instant? ReviewedAt { get; set; }
    public Guid? ReviewedById { get; set; }
}
