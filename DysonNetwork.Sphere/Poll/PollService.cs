using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Poll;

public class PollService(AppDatabase db, ICacheService cache)
{
    public void ValidatePoll(Poll poll)
    {
        if (poll.Questions.Count == 0)
            throw new Exception("Poll must have at least one question");
        foreach (var question in poll.Questions)
        {
            switch (question.Type)
            {
                case PollQuestionType.SingleChoice:
                case PollQuestionType.MultipleChoice:
                    if (question.Options is null)
                        throw new Exception("Poll question must have options");
                    if (question.Options.Count <= 1)
                        throw new Exception("Poll question must have at least two options");
                    break;
                case PollQuestionType.YesNo:
                case PollQuestionType.Rating:
                case PollQuestionType.FreeText:
                default:
                    continue;
            }
        }
    }

    public async Task<Poll?> GetPoll(Guid id)
    {
        var poll = await db.Polls
            .Where(e => e.Id == id)
            .Include(e => e.Questions)
            .FirstOrDefaultAsync();
        return poll;
    }

    private const string PollAnswerCachePrefix = "poll:answer:";

    public async Task<SnPollAnswer?> GetPollAnswer(Guid pollId, Guid accountId)
    {
        var cacheKey = $"{PollAnswerCachePrefix}{pollId}:{accountId}";
        var cachedAnswer = await cache.GetAsync<SnPollAnswer?>(cacheKey);
        if (cachedAnswer is not null)
            return cachedAnswer;

        var answer = await db.PollAnswers
            .Where(e => e.PollId == pollId && e.AccountId == accountId)
            .AsNoTracking()
            .FirstOrDefaultAsync();
        if (answer is not null)
            answer.Poll = null;

        // Set the answer even it is null, which stands for unanswered
        await cache.SetAsync(cacheKey, answer, TimeSpan.FromMinutes(30));

        return answer;
    }

    private async Task ValidatePollAnswer(Guid pollId, Dictionary<string, JsonElement> answer)
    {
        var questions = await db.PollQuestions
            .Where(e => e.PollId == pollId)
            .ToListAsync();
        if (questions is null)
            throw new Exception("Poll has no questions");

        foreach (var question in questions)
        {
            var questionId = question.Id.ToString();
            if (question.IsRequired && !answer.ContainsKey(questionId))
                throw new Exception($"Missing required field: {question.Title}");
            if (!answer.ContainsKey(questionId))
                continue;
            switch (question.Type)
            {
                case PollQuestionType.Rating when answer[questionId].ValueKind != JsonValueKind.Number:
                    throw new Exception($"Answer for question {question.Title} expected to be a number");
                case PollQuestionType.FreeText when answer[questionId].ValueKind != JsonValueKind.String:
                    throw new Exception($"Answer for question {question.Title} expected to be a string");
                case PollQuestionType.SingleChoice when question.Options is not null:
                    if (answer[questionId].ValueKind != JsonValueKind.String)
                        throw new Exception($"Answer for question {question.Title} expected to be a string");
                    if (question.Options.All(e => e.Id.ToString() != answer[questionId].GetString()))
                        throw new Exception($"Answer for question {question.Title} is invalid");
                    break;
                case PollQuestionType.MultipleChoice when question.Options is not null:
                    if (answer[questionId].ValueKind != JsonValueKind.Array)
                        throw new Exception($"Answer for question {question.Title} expected to be an array");
                    if (answer[questionId].EnumerateArray().Any(option =>
                            question.Options.All(e => e.Id.ToString() != option.GetString())))
                        throw new Exception($"Answer for question {question.Title} is invalid");
                    break;
                case PollQuestionType.YesNo when answer[questionId].ValueKind != JsonValueKind.True &&
                                                 answer[questionId].ValueKind != JsonValueKind.False:
                    throw new Exception($"Answer for question {question.Title} expected to be a boolean");
            }
        }
    }

    public async Task<SnPollAnswer> AnswerPoll(Guid pollId, Guid accountId, Dictionary<string, JsonElement> answer)
    {
        // Validation
        var poll = await db.Polls
            .Where(e => e.Id == pollId)
            .FirstOrDefaultAsync();
        if (poll is null)
            throw new Exception("Poll not found");
        if (poll.EndedAt < SystemClock.Instance.GetCurrentInstant())
            throw new Exception("Poll has ended");

        await ValidatePollAnswer(pollId, answer);

        // Remove the existing answer
        var existingAnswer = await db.PollAnswers
            .Where(e => e.PollId == pollId && e.AccountId == accountId)
            .FirstOrDefaultAsync();
        if (existingAnswer is not null)
            await UnAnswerPoll(pollId, accountId);

        // Save the new answer
        var answerRecord = new SnPollAnswer
        {
            PollId = pollId,
            AccountId = accountId,
            Answer = answer
        };
        await db.PollAnswers.AddAsync(answerRecord);
        await db.SaveChangesAsync();

        // Update cache for this poll answer and invalidate stats cache
        var answerCacheKey = $"poll:answer:{pollId}:{accountId}";
        await cache.SetAsync(answerCacheKey, answerRecord, TimeSpan.FromMinutes(30));

        // Invalidate all stats cache for this poll since answers have changed
        await cache.RemoveGroupAsync(PollCacheGroupPrefix + pollId);

        return answerRecord;
    }

    public async Task<bool> UnAnswerPoll(Guid pollId, Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var result = await db.PollAnswers
            .Where(e => e.PollId == pollId && e.AccountId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAt, now)) > 0;

        if (!result) return result;

        // Remove the cached answer if it exists
        var answerCacheKey = $"poll:answer:{pollId}:{accountId}";
        await cache.RemoveAsync(answerCacheKey);

        // Invalidate all stats cache for this poll since answers have changed
        await cache.RemoveGroupAsync(PollCacheGroupPrefix + pollId);

        return result;
    }

    private const string PollStatsCachePrefix = "poll:stats:";
    private const string PollCacheGroupPrefix = "poll:";

    // Returns stats for a single question (option id -> count)
    public async Task<Dictionary<string, int>> GetPollQuestionStats(Guid questionId)
    {
        var cacheKey = $"{PollStatsCachePrefix}{questionId}";

        // Try to get from cache first
        var (found, cachedStats) = await cache.GetAsyncWithStatus<Dictionary<string, int>>(cacheKey);
        if (found && cachedStats != null)
        {
            return cachedStats;
        }

        var question = await db.PollQuestions
            .Where(q => q.Id == questionId)
            .FirstOrDefaultAsync();

        if (question == null)
            throw new Exception("Question not found");

        var answers = await db.PollAnswers
            .Where(a => a.PollId == question.PollId && a.DeletedAt == null)
            .ToListAsync();

        var stats = new Dictionary<string, int>();

        foreach (var answer in answers)
        {
            if (!answer.Answer.TryGetValue(questionId.ToString(), out var value))
                continue;

            switch (question.Type)
            {
                case PollQuestionType.SingleChoice:
                    if (value.ValueKind == JsonValueKind.String &&
                        Guid.TryParse(value.GetString(), out var selected))
                    {
                        stats.TryGetValue(selected.ToString(), out var count);
                        stats[selected.ToString()] = count + 1;
                    }

                    break;

                case PollQuestionType.MultipleChoice:
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

                case PollQuestionType.YesNo:
                    var id = value.ValueKind switch
                    {
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => "neither"
                    };

                    stats.TryGetValue(id, out var ynCount);
                    stats[id] = ynCount + 1;
                    break;

                case PollQuestionType.Rating:
                    double sum = 0;
                    var countRating = 0;

                    foreach (var rating in answers)
                    {
                        if (!rating.Answer.TryGetValue(questionId.ToString(), out var ratingValue))
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

                case PollQuestionType.FreeText:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        // Cache the result with a 1-hour expiration and add to the poll cache group
        await cache.SetWithGroupsAsync(
            cacheKey,
            stats,
            [PollCacheGroupPrefix + question.PollId],
            TimeSpan.FromHours(1));

        return stats;
    }

    // Returns stats for all questions in a poll (question id -> (option id -> count))
    public async Task<Dictionary<Guid, Dictionary<string, int>>> GetPollStats(Guid pollId)
    {
        var questions = await db.PollQuestions
            .Where(q => q.PollId == pollId)
            .ToListAsync();

        var result = new Dictionary<Guid, Dictionary<string, int>>();

        foreach (var question in questions)
        {
            var stats = await GetPollQuestionStats(question.Id);
            result[question.Id] = stats;
        }

        return result;
    }

    public async Task<PollEmbed> MakePollEmbed(Guid pollId)
    {
        // Do not read the cache here
        var poll = await db.Polls
            .Where(e => e.Id == pollId)
            .FirstOrDefaultAsync();
        if (poll is null)
            throw new Exception("Poll not found");
        return new PollEmbed { Id = poll.Id };
    }

    public async Task<PollEmbed> LoadPollEmbed(Guid pollId, Guid? accountId)
    {
        var poll = await GetPoll(pollId);
        if (poll is null)
            throw new Exception("Poll not found");
        var pollWithStats = PollWithStats.FromPoll(poll);
        pollWithStats.Stats = await GetPollStats(poll.Id);
        if (accountId is not null)
            pollWithStats.UserAnswer = await GetPollAnswer(poll.Id, accountId.Value);
        return new PollEmbed { Id = poll.Id, Poll = pollWithStats };
    }
}
