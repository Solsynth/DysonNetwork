using System.Text.Json;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Passport.Affiliation;
using DysonNetwork.Passport.Leveling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NodaTime;

namespace DysonNetwork.Passport.Examination;

public class TestService(
    AppDatabase db,
    RemoteAccountContactService contacts,
    IEventBus eventBus,
    DyAccountService.DyAccountServiceClient accounts,
    IOptions<AccountActivationOptions> activationOptions,
    ExperienceService experience,
    IClock clock)
{
    private static readonly Duration SubmissionClockSkewGrace = Duration.FromSeconds(10);
    private readonly AccountActivationOptions _activation = activationOptions.Value;

    public async Task<ActivationRequirementState> GetActivationRequirements(Guid accountId, CancellationToken cancellationToken = default)
    {
        var state = new ActivationRequirementState { TestsEnabled = _activation.TestsEnabled, RequireVerifiedContact = _activation.RequireVerifiedContact };
        var account = SnAccount.FromProtoValue(await accounts.GetAccountAsync(
            new DyGetAccountRequest { Id = accountId.ToString() }, cancellationToken: cancellationToken));
        state.IsActivated = account.ActivatedAt is not null;
        if (_activation.RequireVerifiedContact)
            state.HasVerifiedContact = (await contacts.ListContactsAsync(accountId, verifiedOnly: true, cancellationToken: cancellationToken)).Count > 0;

        var affiliationResults = await db.AffiliationResults.Include(x => x.Spell)
            .Where(x => x.ResourceIdentifier == $"account:{accountId}" && x.Spell.Type == AffiliationSpellType.RegistrationInvite)
            .ToListAsync(cancellationToken);
        state.TestsBypassed = affiliationResults.Any(x => AffiliationSpellService.SkipsTests(x.Spell));

        foreach (var key in (_activation.TestsEnabled && !state.TestsBypassed ? _activation.RequiredTestKeys : []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var test = await db.Tests.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
            var passed = test is not null && await db.TestAttempts.AnyAsync(
                x => x.AccountId == accountId && x.TestId == test.Id && x.Status == TestAttemptStatus.Passed, cancellationToken);
            var title = test?.Title ?? key;
            var maxAttempts = test?.MaxAttempts;
            var usedAttemptCount = 0;
            if (test is not null && maxAttempts.HasValue)
            {
                var now = clock.GetCurrentInstant();
                var periodStart = now - Duration.FromDays(test.AttemptPeriodDays);
                usedAttemptCount = await db.TestAttempts.CountAsync(
                    x => x.AccountId == accountId && x.TestId == test.Id && !x.IsTrial &&
                         x.StartedAt >= periodStart && x.Status != TestAttemptStatus.InProgress, cancellationToken);
            }
            state.Tests.Add(new ActivationTestRequirement { Key = key, Title = title, Passed = passed, Available = test?.IsPublished == true, MaxAttempts = maxAttempts, UsedAttemptCount = usedAttemptCount });
        }
        return state;
    }

    public async Task<bool> TryActivateAccount(Guid accountId, CancellationToken cancellationToken = default)
    {
        var state = await GetActivationRequirements(accountId, cancellationToken);
        if (state.IsActivated || !state.IsSatisfied) return state.IsActivated;
        await eventBus.PublishAsync(AccountActivatedEvent.Type, new AccountActivatedEvent
        {
            AccountId = accountId,
            ActivatedAt = clock.GetCurrentInstant()
        });
        return true;
    }

    public async Task<bool> TryActivateAfterContactVerification(Guid accountId, CancellationToken cancellationToken = default)
    {
        if (_activation.TestsEnabled && _activation.RequiredTestKeys.Count > 0)
            return await TryActivateAccount(accountId, cancellationToken);
        var account = SnAccount.FromProtoValue(await accounts.GetAccountAsync(
            new DyGetAccountRequest { Id = accountId.ToString() }, cancellationToken: cancellationToken));
        if (account.ActivatedAt is not null) return false;
        await eventBus.PublishAsync(AccountActivatedEvent.Type, new AccountActivatedEvent
        {
            AccountId = accountId,
            ActivatedAt = clock.GetCurrentInstant()
        }, cancellationToken);
        return true;
    }

    public async Task<SnTestAttempt> StartAttempt(Guid accountId, SnTest test, bool isTrial = false, Guid? trialId = null, CancellationToken cancellationToken = default)
    {
        var now = clock.GetCurrentInstant();
        var active = await db.TestAttempts.Include(x => x.Answers)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.TestId == test.Id && x.IsTrial == isTrial && x.TrialId == trialId && x.Status == TestAttemptStatus.InProgress, cancellationToken);
        if (active is not null && (active.DeadlineAt is null || active.DeadlineAt + SubmissionClockSkewGrace > now)) return active;
        if (active is not null)
        {
            active.Status = TestAttemptStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
        }

        var periodStart = now - Duration.FromDays(test.AttemptPeriodDays);
        var used = await db.TestAttempts.CountAsync(x => x.AccountId == accountId && x.TestId == test.Id && !x.IsTrial && x.StartedAt >= periodStart && x.Status != TestAttemptStatus.InProgress, cancellationToken);
        if (test.MaxAttempts.HasValue && used >= test.MaxAttempts.Value)
            throw new InvalidOperationException("The maximum number of attempts has been reached.");

        var previousSnapshots = await db.TestAttempts.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.TestId == test.Id && x.IsTrial == isTrial && x.TrialId == trialId && x.Status != TestAttemptStatus.InProgress)
            .Select(x => x.Snapshot)
            .ToListAsync(cancellationToken);
        var previouslyAskedQuestionIds = previousSnapshots
            .Select(x => DeserializeSnapshot(x))
            .SelectMany(x => x.Questions)
            .Select(x => x.Id)
            .ToHashSet();
        var snapshot = TestSnapshot.FromTest(test, previouslyAskedQuestionIds);
        var attempt = new SnTestAttempt
        {
            AccountId = accountId,
            TestId = test.Id,
            IsTrial = isTrial,
            TrialId = trialId,
            StartedAt = now,
            DeadlineAt = test.TimeLimitSeconds.HasValue ? now + Duration.FromSeconds(test.TimeLimitSeconds.Value) : null,
            Snapshot = SerializeSnapshot(snapshot)
        };
        db.TestAttempts.Add(attempt);
        await db.SaveChangesAsync(cancellationToken);
        return attempt;
    }

    public async Task<SnTestAttempt> SubmitAttempt(Guid accountId, SnTestAttempt attempt, List<TestAnswerInput> inputs, CancellationToken cancellationToken = default)
    {
        if (attempt.AccountId != accountId || attempt.Status != TestAttemptStatus.InProgress)
            throw new InvalidOperationException("This attempt cannot be submitted.");
        var now = clock.GetCurrentInstant();
        if (attempt.DeadlineAt is not null && attempt.DeadlineAt + SubmissionClockSkewGrace < now)
        {
            attempt.Status = TestAttemptStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
            return attempt;
        }

        var snapshot = DeserializeSnapshot(attempt.Snapshot);
        var inputByQuestion = inputs.GroupBy(x => x.QuestionId).ToDictionary(x => x.Key, x => x.Last());
        foreach (var question in snapshot.Questions)
        {
            inputByQuestion.TryGetValue(question.Id, out var input);
            var wasAnswered = input?.ChoiceIds?.Count > 0 || !string.IsNullOrWhiteSpace(input?.Text);
            var answer = new SnTestAnswer
            {
                AttemptId = attempt.Id,
                QuestionId = question.Id,
                Value = new Dictionary<string, object?> { ["choice_ids"] = input?.ChoiceIds ?? [], ["text"] = input?.Text }
            };
            if (question.GradingMode == TestQuestionGradingMode.Auto && wasAnswered)
            {
                var selected = (input?.ChoiceIds ?? []).Order().ToArray();
                var correct = question.Choices.Where(x => x.IsCorrect).Select(x => x.Id).Order().ToArray();
                answer.IsCorrect = selected.SequenceEqual(correct);
                answer.AwardedPoints = answer.IsCorrect.Value ? question.Points : -question.Points;
            }
            else if (!wasAnswered) answer.AwardedPoints = 0;
            db.TestAnswers.Add(answer);
            attempt.Answers.Add(answer);
        }
        attempt.SubmittedAt = now;
        attempt.Status = snapshot.Questions.Any(x => x.GradingMode == TestQuestionGradingMode.Manual && attempt.Answers.Any(a => a.QuestionId == x.Id && a.AwardedPoints is null))
            ? TestAttemptStatus.PendingReview : ResolveFinalStatus(attempt, snapshot);
        await db.SaveChangesAsync(cancellationToken);
        if (!attempt.IsTrial && attempt.Status == TestAttemptStatus.Passed)
        {
            await GrantPermissionGroup(attempt, snapshot, cancellationToken);
            await TryActivateAccount(accountId, cancellationToken);
        }
        if (!attempt.IsTrial && attempt.Status is TestAttemptStatus.Passed or TestAttemptStatus.Failed)
            await GrantExperienceReward(attempt, snapshot);
        return attempt;
    }

    public async Task<SnTestAttempt> ReviewAnswer(Guid reviewerId, SnTestAnswer answer, bool isCorrect, double awardedPoints, string? note, CancellationToken cancellationToken = default)
    {
        var attempt = await db.TestAttempts.Include(x => x.Answers).FirstAsync(x => x.Id == answer.AttemptId, cancellationToken);
        if (attempt.Status != TestAttemptStatus.PendingReview) throw new InvalidOperationException("This attempt is not awaiting review.");
        var question = DeserializeSnapshot(attempt.Snapshot).Questions.FirstOrDefault(x => x.Id == answer.QuestionId)
            ?? throw new InvalidOperationException("The answer does not belong to the attempt snapshot.");
        if (question.GradingMode != TestQuestionGradingMode.Manual) throw new InvalidOperationException("Only manual answers can be reviewed.");
        answer.IsCorrect = isCorrect;
        answer.AwardedPoints = Math.Clamp(awardedPoints, 0, question.Points);
        answer.ReviewNote = note;
        answer.ReviewedAt = clock.GetCurrentInstant();
        answer.ReviewedById = reviewerId;
        await db.SaveChangesAsync(cancellationToken);

        if (attempt.Answers.Where(x => DeserializeSnapshot(attempt.Snapshot).Questions.Any(q => q.Id == x.QuestionId && q.GradingMode == TestQuestionGradingMode.Manual)).All(x => x.AwardedPoints.HasValue))
        {
            attempt.Status = ResolveFinalStatus(attempt, DeserializeSnapshot(attempt.Snapshot));
            attempt.ReviewedAt = clock.GetCurrentInstant();
            attempt.ReviewedById = reviewerId;
            await db.SaveChangesAsync(cancellationToken);
            if (!attempt.IsTrial && attempt.Status == TestAttemptStatus.Passed)
            {
                await GrantPermissionGroup(attempt, DeserializeSnapshot(attempt.Snapshot), cancellationToken);
                await TryActivateAccount(attempt.AccountId, cancellationToken);
            }
            if (!attempt.IsTrial && attempt.Status is TestAttemptStatus.Passed or TestAttemptStatus.Failed)
                await GrantExperienceReward(attempt, DeserializeSnapshot(attempt.Snapshot));
        }
        return attempt;
    }

    private static TestAttemptStatus ResolveFinalStatus(SnTestAttempt attempt, TestSnapshot snapshot)
    {
        attempt.Score = CalculateScore(snapshot, attempt.Answers);
        return attempt.Score >= snapshot.PassingScore ? TestAttemptStatus.Passed : TestAttemptStatus.Failed;
    }

    internal static double CalculateScore(TestSnapshot snapshot, IEnumerable<SnTestAnswer> answers)
    {
        var possible = snapshot.Questions.Sum(x => x.Points);
        if (possible == 0) return 100;

        var awardedByQuestion = answers
            .GroupBy(x => x.QuestionId)
            .ToDictionary(x => x.Key, x => x.Last().AwardedPoints ?? 0);
        var awarded = snapshot.Questions.Sum(question => awardedByQuestion.GetValueOrDefault(question.Id));
        return Math.Min(100, awarded / possible * 100);
    }

    private Task GrantPermissionGroup(SnTestAttempt attempt, TestSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.GrantedPermissionGroupKey)) return Task.CompletedTask;
        return eventBus.PublishAsync(AccountTestPassedPermissionGroupEvent.Type, new AccountTestPassedPermissionGroupEvent
        {
            AccountId = attempt.AccountId,
            TestId = attempt.TestId,
            AttemptId = attempt.Id,
            PermissionGroupKey = snapshot.GrantedPermissionGroupKey
        }, cancellationToken);
    }

    private async Task GrantExperienceReward(SnTestAttempt attempt, TestSnapshot snapshot)
    {
        if (snapshot.RewardExperience is not > 0) return;
        var reason = $"test:{attempt.TestId}";
        if (await db.ExperienceRecords.AnyAsync(x => x.AccountId == attempt.AccountId && x.ReasonType == "test.completed" && x.Reason == reason)) return;
        await experience.AddRecord("test.completed", reason, snapshot.RewardExperience.Value, attempt.AccountId);
    }

    private static Dictionary<string, object?> SerializeSnapshot(TestSnapshot snapshot) => JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(snapshot))!;
    private static TestSnapshot DeserializeSnapshot(Dictionary<string, object?> snapshot) => TestSnapshot.FromDictionary(snapshot);
}

public class ActivationRequirementState
{
    public bool IsActivated { get; set; }
    public bool TestsEnabled { get; set; }
    public bool TestsBypassed { get; set; }
    public bool RequireVerifiedContact { get; set; }
    public bool HasVerifiedContact { get; set; }
    public List<ActivationTestRequirement> Tests { get; set; } = [];
    public bool IsSatisfied => (!RequireVerifiedContact || HasVerifiedContact) && Tests.All(x => x.Passed);
    public int RequiredRequirementCount => (RequireVerifiedContact ? 1 : 0) + Tests.Count;
    public int CompletedRequirementCount => (RequireVerifiedContact && HasVerifiedContact ? 1 : 0) + Tests.Count(x => x.Passed);
}
public class ActivationTestRequirement { public string Key { get; set; } = null!; public string Title { get; set; } = null!; public bool Available { get; set; } public bool Passed { get; set; } public int? MaxAttempts { get; set; } public int UsedAttemptCount { get; set; } }
public class TestAnswerInput { public Guid QuestionId { get; set; } public List<Guid>? ChoiceIds { get; set; } public string? Text { get; set; } }
public class TestSnapshot
{
    public string Key { get; set; } = null!;
    public string Title { get; set; } = null!;
    public double PassingScore { get; set; }
    public long? RewardExperience { get; set; }
    public string? GrantedPermissionGroupKey { get; set; }
    public List<TestQuestionSnapshot> Questions { get; set; } = [];
    public static TestSnapshot FromDictionary(Dictionary<string, object?> snapshot) => JsonSerializer.Deserialize<TestSnapshot>(JsonSerializer.Serialize(snapshot))!;
    public static TestSnapshot FromTest(SnTest test, ISet<Guid>? previouslyAskedQuestionIds = null) => new()
    {
        Key = test.Key,
        Title = test.Title,
        PassingScore = test.PassingScore,
        RewardExperience = test.RewardExperience,
        GrantedPermissionGroupKey = test.GrantedPermissionGroupKey,
        Questions = TestQuestionSelector.Select(test, previouslyAskedQuestionIds).Select(x => new TestQuestionSnapshot
        {
            Id = x.Id, Content = x.Content, Type = x.Type, GradingMode = x.GradingMode, Difficulty = x.Difficulty, Points = x.Points,
            Choices = TestQuestionSelector.Shuffle(x.Choices).Select(c => new TestChoiceSnapshot { Id = c.Id, Content = c.Content, IsCorrect = c.IsCorrect }).ToList()
        }).ToList()
    };
}
public class TestQuestionSnapshot { public Guid Id { get; set; } public string Content { get; set; } = null!; public TestQuestionType Type { get; set; } public TestQuestionGradingMode GradingMode { get; set; } public int Difficulty { get; set; } public double Points { get; set; } public List<TestChoiceSnapshot> Choices { get; set; } = []; }
public class TestChoiceSnapshot { public Guid Id { get; set; } public string Content { get; set; } = null!; public bool IsCorrect { get; set; } }

public static class TestQuestionSelector
{
    public static IEnumerable<SnTestQuestion> Select(SnTest test, ISet<Guid>? previouslyAskedQuestionIds = null)
    {
        var questions = test.QuestionGroups.OrderBy(x => x.SortOrder).SelectMany(x => x.QuestionGroup.Questions.OrderBy(q => q.SortOrder)).ToList();
        if (!test.ShuffleQuestions) return questions;
        var count = test.RandomQuestionCount ?? questions.Count;
        if (count >= questions.Count) return Shuffle(questions);
        var unseenQuestions = previouslyAskedQuestionIds is { Count: > 0 }
            ? questions.Where(x => !previouslyAskedQuestionIds.Contains(x.Id)).ToList()
            : questions;
        var selected = PickQuestions(unseenQuestions, Math.Min(count, unseenQuestions.Count), test.SimpleQuestionPercentage);
        if (selected.Count < count)
            selected.AddRange(PickQuestions(questions.Except(selected), count - selected.Count, test.SimpleQuestionPercentage));
        return Shuffle(selected);
    }

    private static List<SnTestQuestion> PickQuestions(IEnumerable<SnTestQuestion> source, int count, int simpleQuestionPercentage)
    {
        var questions = source.ToList();
        var simpleTarget = (int)Math.Round(count * simpleQuestionPercentage / 100d, MidpointRounding.AwayFromZero);
        var simple = PickBalancedByCategory(questions.Where(x => x.Difficulty <= 2), simpleTarget);
        var hard = PickBalancedByCategory(questions.Where(x => x.Difficulty >= 3).Except(simple), count - simple.Count);
        var selected = simple.Concat(hard).ToList();
        if (selected.Count < count) selected.AddRange(PickBalancedByCategory(questions.Except(selected), count - selected.Count));
        return selected;
    }

    public static List<T> Shuffle<T>(IEnumerable<T> source)
    {
        var shuffled = source.ToList();
        for (var index = shuffled.Count - 1; index > 0; index--)
        {
            var replacement = Random.Shared.Next(index + 1);
            (shuffled[index], shuffled[replacement]) = (shuffled[replacement], shuffled[index]);
        }
        return shuffled;
    }

    private static List<SnTestQuestion> PickBalancedByCategory(IEnumerable<SnTestQuestion> source, int count)
    {
        var buckets = source.GroupBy(x => x.Category?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => Shuffle(group)).Where(group => group.Count > 0).ToList();
        var selected = new List<SnTestQuestion>();
        while (selected.Count < count && buckets.Count > 0)
        {
            foreach (var bucket in Shuffle(buckets))
            {
                if (selected.Count == count) break;
                selected.Add(bucket[0]);
                bucket.RemoveAt(0);
            }
            buckets.RemoveAll(bucket => bucket.Count == 0);
        }
        return selected;
    }
}
