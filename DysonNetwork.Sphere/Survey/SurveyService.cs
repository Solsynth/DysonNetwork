using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Survey;

public class SurveyService(AppDatabase db, ICacheService cache, DyFileService.DyFileServiceClient files)
{
    /// <summary>
    /// Resolves a list of cloud-file IDs into denormalized <see cref="SnCloudFileReferenceObject"/>
    /// snapshots (mirrors <c>PostService.PostAsync</c>). Order is preserved and IDs that the file
    /// service cannot resolve are dropped, so callers can pass user input directly.
    /// </summary>
    public async Task<List<SnCloudFileReferenceObject>> ResolveAttachmentsAsync(List<string>? ids)
    {
        if (ids is null or { Count: 0 })
            return [];

        var queryRequest = new DyGetFileBatchRequest();
        queryRequest.Ids.AddRange(ids);
        var queryResponse = await files.GetFileBatchAsync(queryRequest);

        var resolved = queryResponse
            .Files.Select(SnCloudFileReferenceObject.FromProtoValue)
            .ToList();

        // Re-order to match the requested id order; drop unreachable IDs.
        return ids
            .Distinct()
            .Select(id => resolved.FirstOrDefault(a => a.Id == id))
            .Where(a => a is not null)
            .Cast<SnCloudFileReferenceObject>()
            .ToList();
    }

    public void ValidateSurvey(SnSurvey survey)
    {
        var errors = new Dictionary<string, List<string>>();
        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
                errors[field] = list = new List<string>();
            list.Add(message);
        }

        if (survey.Questions.Count == 0)
            AddError("questions", "Survey must have at least one question");

        foreach (var question in survey.Questions)
        {
            var field = $"questions[{question.Id}]";
            if (string.IsNullOrWhiteSpace(question.Title))
                AddError($"{field}.title", "Question title is required");

            switch (question.Type)
            {
                case SurveyQuestionType.SingleChoice:
                case SurveyQuestionType.MultipleChoice:
                    if (question.Options is null || question.Options.Count < 2)
                        AddError($"{field}.options", "Choice question must have at least two options");
                    else
                    {
                        for (var i = 0; i < question.Options.Count; i++)
                        {
                            if (string.IsNullOrWhiteSpace(question.Options[i].Label))
                                AddError($"{field}.options[{i}].label", "Option label cannot be empty");
                        }
                    }

                    if (question.MaxSelections.HasValue)
                    {
                        if (question.MaxSelections.Value < 1)
                            AddError($"{field}.max_selections", "max_selections must be at least 1");
                        else if (question.Options is { Count: > 0 } && question.MaxSelections.Value > question.Options.Count)
                            AddError($"{field}.max_selections", "max_selections cannot exceed the number of options");
                    }

                    break;
                case SurveyQuestionType.FreeText:
                    if (question.MaxLength.HasValue && question.MaxLength.Value <= 0)
                        AddError($"{field}.max_length", "max_length must be greater than 0");
                    break;
                case SurveyQuestionType.Rating:
                    if (question.MinValue.HasValue && question.MaxValue.HasValue &&
                        question.MinValue.Value >= question.MaxValue.Value)
                        AddError($"{field}.range", "min_value must be less than max_value");
                    break;
                case SurveyQuestionType.YesNo:
                default:
                    break;
            }
        }

        if (errors.Count > 0)
            throw new SurveyValidationException(
                errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray())
            );
    }

    public async Task<SnSurvey?> GetSurvey(Guid id)
    {
        var survey = await db.Surveys
            .Where(e => e.Id == id)
            .Include(e => e.Questions)
            .FirstOrDefaultAsync();
        return survey;
    }

    private const string SurveyAnswerCachePrefix = "survey:answer:";

    public async Task<SnSurveyAnswer?> GetSurveyAnswer(Guid surveyId, Guid accountId)
    {
        var cacheKey = $"{SurveyAnswerCachePrefix}{surveyId}:{accountId}";
        var cachedAnswer = await cache.GetAsync<SnSurveyAnswer?>(cacheKey);
        if (cachedAnswer is not null)
            return cachedAnswer;

        var answer = await db.SurveyAnswers
            .Where(e => e.SurveyId == surveyId && e.AccountId == accountId)
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (answer is not null)
            answer.Survey = null;

        // Set the answer even it is null, which stands for unanswered
        await cache.SetAsync(cacheKey, answer, TimeSpan.FromMinutes(30));

        return answer;
    }

    private async Task ValidateSurveyAnswer(Guid surveyId, Dictionary<string, JsonElement> answer)
    {
        var questions = await db.SurveyQuestions
            .Where(e => e.SurveyId == surveyId)
            .ToListAsync();
        if (questions is null)
            throw new SurveyValidationException("answer", "Survey has no questions");

        var errors = new Dictionary<string, List<string>>();
        void AddError(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
                errors[field] = list = new List<string>();
            list.Add(message);
        }

        foreach (var question in questions)
        {
            var questionId = question.Id.ToString();
            var field = $"answers[{questionId}]";
            if (question.IsRequired && (!answer.TryGetValue(questionId, out var answerValue) || !HasAnsweredValue(question, answerValue)))
            {
                AddError(field, $"Missing required answer for: {question.Title}");
                continue;
            }
            if (!answer.TryGetValue(questionId, out answerValue) || answerValue.ValueKind == JsonValueKind.Null)
                continue;

            switch (question.Type)
            {
                case SurveyQuestionType.Rating when answerValue.ValueKind != JsonValueKind.Number:
                    AddError(field, $"Answer for question '{question.Title}' must be a number");
                    break;
                case SurveyQuestionType.Rating when answerValue.TryGetDouble(out var rating):
                    if (question.MinValue.HasValue && rating < question.MinValue.Value)
                        AddError(field, $"Answer for question '{question.Title}' is below the minimum ({question.MinValue.Value})");
                    if (question.MaxValue.HasValue && rating > question.MaxValue.Value)
                        AddError(field, $"Answer for question '{question.Title}' exceeds the maximum ({question.MaxValue.Value})");
                    break;
                case SurveyQuestionType.FreeText when answerValue.ValueKind != JsonValueKind.String:
                    AddError(field, $"Answer for question '{question.Title}' must be a string");
                    break;
                case SurveyQuestionType.FreeText:
                    {
                        var text = answerValue.GetString() ?? "";
                        if (question.MaxLength.HasValue && text.Length > question.MaxLength.Value)
                            AddError(field, $"Answer for question '{question.Title}' exceeds max_length ({question.MaxLength.Value})");
                        break;
                    }
                case SurveyQuestionType.SingleChoice when question.Options is not null:
                    if (answerValue.ValueKind != JsonValueKind.String)
                        AddError(field, $"Answer for question '{question.Title}' must be a string (option id)");
                    else if (question.Options.All(e => e.Id.ToString() != answerValue.GetString()))
                        AddError(field, $"Answer for question '{question.Title}' references an unknown option");
                    break;
                case SurveyQuestionType.MultipleChoice when question.Options is not null:
                    if (answerValue.ValueKind != JsonValueKind.Array)
                        AddError(field, $"Answer for question '{question.Title}' must be an array of option ids");
                    else
                    {
                        var selections = answerValue.EnumerateArray().ToList();
                        if (question.MaxSelections.HasValue && selections.Count > question.MaxSelections.Value)
                            AddError(field, $"Answer for question '{question.Title}' selects too many options (max {question.MaxSelections.Value})");
                        if (selections.Any(option => question.Options.All(e => e.Id.ToString() != option.GetString())))
                            AddError(field, $"Answer for question '{question.Title}' references an unknown option");
                    }
                    break;
                case SurveyQuestionType.YesNo when answerValue.ValueKind != JsonValueKind.True &&
                                                  answerValue.ValueKind != JsonValueKind.False:
                    AddError(field, $"Answer for question '{question.Title}' must be a boolean");
                    break;
            }
        }

        if (errors.Count > 0)
            throw new SurveyValidationException(
                errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray())
            );
    }

    private static bool HasAnsweredValue(SnSurveyQuestion question, JsonElement answerValue)
    {
        return question.Type switch
        {
            SurveyQuestionType.SingleChoice => answerValue.ValueKind == JsonValueKind.String &&
                                             !string.IsNullOrWhiteSpace(answerValue.GetString()),
            SurveyQuestionType.MultipleChoice => answerValue.ValueKind == JsonValueKind.Array &&
                                               answerValue.EnumerateArray().Any(),
            SurveyQuestionType.FreeText => answerValue.ValueKind == JsonValueKind.String &&
                                         !string.IsNullOrWhiteSpace(answerValue.GetString()),
            SurveyQuestionType.YesNo => answerValue.ValueKind == JsonValueKind.True ||
                                       answerValue.ValueKind == JsonValueKind.False,
            SurveyQuestionType.Rating => answerValue.ValueKind == JsonValueKind.Number,
            _ => answerValue.ValueKind != JsonValueKind.Null &&
                 answerValue.ValueKind != JsonValueKind.Undefined
        };
    }

    public async Task<SnSurveyAnswer> AnswerSurvey(Guid surveyId, Guid accountId, Dictionary<string, JsonElement> answer)
    {
        // Validation
        var survey = await db.Surveys
            .Where(e => e.Id == surveyId)
            .FirstOrDefaultAsync();
        if (survey is null)
            throw new InvalidOperationException("Survey not found");
        if (survey.Status != SurveyStatus.Published)
            throw new InvalidOperationException($"Survey is not accepting responses (status: {survey.Status}).");
        if (survey.EndedAt.HasValue && survey.EndedAt < SystemClock.Instance.GetCurrentInstant())
            throw new InvalidOperationException("Survey has ended");

        await ValidateSurveyAnswer(surveyId, answer);

        // Remove the existing answer
        var existingAnswer = await db.SurveyAnswers
            .Where(e => e.SurveyId == surveyId && e.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (existingAnswer is not null)
            await UnAnswerSurvey(surveyId, accountId);

        // Save the new answer
        var answerRecord = new SnSurveyAnswer
        {
            SurveyId = surveyId,
            AccountId = accountId,
            Answer = answer
        };
        await db.SurveyAnswers.AddAsync(answerRecord);
        await db.SaveChangesAsync();

        // Update cache for this survey answer and invalidate stats cache
        var answerCacheKey = $"survey:answer:{surveyId}:{accountId}";
        await cache.SetAsync(answerCacheKey, answerRecord, TimeSpan.FromMinutes(30));

        // Invalidate all stats cache for this survey since answers have changed
        await cache.RemoveGroupAsync(SurveyCacheGroupPrefix + surveyId);

        return answerRecord;
    }

    public async Task<bool> UnAnswerSurvey(Guid surveyId, Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var result = await db.SurveyAnswers
            .Where(e => e.SurveyId == surveyId && e.AccountId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAt, now)) > 0;

        if (!result) return result;

        // Remove the cached answer if it exists
        var answerCacheKey = $"survey:answer:{surveyId}:{accountId}";
        await cache.RemoveAsync(answerCacheKey);

        // Invalidate all stats cache for this survey since answers have changed
        await cache.RemoveGroupAsync(SurveyCacheGroupPrefix + surveyId);

        return result;
    }

    private const string SurveyStatsCachePrefix = "survey:stats:";
    private const string SurveyCacheGroupPrefix = "survey:";

    // Returns stats for a single question (option id -> count)
    public async Task<Dictionary<string, int>> GetSurveyQuestionStats(Guid questionId)
    {
        var cacheKey = $"{SurveyStatsCachePrefix}{questionId}";

        // Try to get from cache first
        var (found, cachedStats) = await cache.GetAsyncWithStatus<Dictionary<string, int>>(cacheKey);
        if (found && cachedStats != null)
        {
            return cachedStats;
        }

        var question = await db.SurveyQuestions
            .Where(q => q.Id == questionId)
            .FirstOrDefaultAsync();

        if (question == null)
            throw new Exception("Question not found");

        var answers = await db.SurveyAnswers
            .Where(a => a.SurveyId == question.SurveyId && a.DeletedAt == null)
            .ToListAsync();

        var stats = new Dictionary<string, int>();

        foreach (var answer in answers)
        {
            if (answer.Answer?.TryGetValue(questionId.ToString(), out var value) != true)
                continue;

            switch (question.Type)
            {
                case SurveyQuestionType.SingleChoice:
                    if (value.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(value.GetString(), out var selected))
                    {
                        stats.TryGetValue(selected.ToString(), out var count);
                        stats[selected.ToString()] = count + 1;
                    }

                    break;

                case SurveyQuestionType.MultipleChoice:
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in value.EnumerateArray())
                        {
                            if (element.ValueKind != JsonValueKind.String ||
                                !Guid.TryParse(element.GetString(), out var opt)) continue;
                            stats.TryGetValue(opt.ToString(), out var count);
                            stats[opt.ToString()] = count + 1;
                        }
                    }

                    break;

                case SurveyQuestionType.YesNo:
                    var id = value.ValueKind switch
                    {
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => "neither"
                    };

                    stats.TryGetValue(id, out var ynCount);
                    stats[id] = ynCount + 1;
                    break;

                case SurveyQuestionType.Rating:
                    double sum = 0;
                    var countRating = 0;

                    foreach (var rating in answers)
                    {
                        if (rating.Answer?.TryGetValue(questionId.ToString(), out var ratingValue) != true)
                            continue;

                        if (ratingValue.ValueKind == JsonValueKind.Number &&
                            ratingValue.TryGetDouble(out var ratingNumber))
                        {
                            sum += ratingNumber;
                            countRating++;
                        }
                    }

                    if (countRating > 0)
                    {
                        var avgRating = sum / countRating;
                        stats["rating"] = (int)Math.Round(avgRating);
                    }

                    break;

                case SurveyQuestionType.FreeText:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Cache the result with a 1-hour expiration and add to the survey cache group
        await cache.SetWithGroupsAsync(
            cacheKey,
            stats,
            [SurveyCacheGroupPrefix + question.SurveyId],
            TimeSpan.FromHours(1));

        return stats;
    }

    // Returns stats for all questions in a survey (question id -> (option id -> count))
    public async Task<Dictionary<Guid, Dictionary<string, int>>> GetSurveyStats(Guid surveyId)
    {
        var questions = await db.SurveyQuestions
            .Where(q => q.SurveyId == surveyId)
            .ToListAsync();

        var result = new Dictionary<Guid, Dictionary<string, int>>();

        foreach (var question in questions)
        {
            var stats = await GetSurveyQuestionStats(question.Id);
            result[question.Id] = stats;
        }

        return result;
    }

    public async Task<SurveyEmbed> MakeSurveyEmbed(Guid surveyId)
    {
        // Do not read the cache here
        var survey = await db.Surveys
            .Where(e => e.Id == surveyId)
            .FirstOrDefaultAsync();
        return survey is null ? throw new Exception("Survey not found") : new SurveyEmbed { Id = survey.Id };
    }

    // ---- Lifecycle management -------------------------------------------------

    public async Task<SnSurvey> PublishSurveyAsync(Guid surveyId)
    {
        var survey = await db.Surveys
            .Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == surveyId)
            ?? throw new InvalidOperationException("Survey not found");

        if (survey.Status != SurveyStatus.Draft)
            throw new InvalidOperationException($"Cannot publish a survey in {survey.Status} status; clone a new draft to revise.");

        // Re-run validation so we never publish a structurally invalid survey.
        ValidateSurvey(survey);

        survey.Status = SurveyStatus.Published;
        survey.PublishedAt ??= SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
        return survey;
    }

    public async Task<SnSurvey> ArchiveSurveyAsync(Guid surveyId)
    {
        var survey = await db.Surveys.FirstOrDefaultAsync(e => e.Id == surveyId)
                     ?? throw new InvalidOperationException("Survey not found");

        if (survey.Status == SurveyStatus.Archived)
            throw new InvalidOperationException("Survey is already archived");
        if (survey.Status == SurveyStatus.Draft)
            throw new InvalidOperationException("Drafts cannot be archived directly; publish or delete instead");

        survey.Status = SurveyStatus.Archived;
        await db.SaveChangesAsync();
        return survey;
    }

    public async Task<SnSurvey> CloneSurveyAsync(Guid surveyId)
    {
        var source = await db.Surveys
            .Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.Id == surveyId)
            ?? throw new InvalidOperationException("Survey not found");

        var clone = source.CloneAsDraft();
        db.Surveys.Add(clone);
        await db.SaveChangesAsync();
        return clone;
    }
}

public class SurveyValidationException : Exception
{
    public Dictionary<string, string[]> FieldErrors { get; }

    public SurveyValidationException(Dictionary<string, string[]> fieldErrors, string? message = null)
        : base(message ?? "One or more validation errors occurred.")
    {
        FieldErrors = fieldErrors;
    }

    public SurveyValidationException(string field, string error)
        : this(new Dictionary<string, string[]> { { field, new[] { error } } })
    {
    }
}
