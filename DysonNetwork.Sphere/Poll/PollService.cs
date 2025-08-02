using System.Text.Json;
using DysonNetwork.Shared.Cache;
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

    private const string PollAnswerCachePrefix = "poll:answer:";

    public async Task<PollAnswer?> GetPollAnswer(Guid pollId, Guid accountId)
    {
        var cacheKey = $"poll:answer:{pollId}:{accountId}";
        var cachedAnswer = await cache.GetAsync<PollAnswer?>(cacheKey);
        if (cachedAnswer is not null)
            return cachedAnswer;

        var answer = await db.PollAnswers
            .Where(e => e.PollId == pollId && e.AccountId == accountId)
            .FirstOrDefaultAsync();

        if (answer is not null)
        {
            await cache.SetAsync(cacheKey, answer, TimeSpan.FromMinutes(30));
        }

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
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public async Task<PollAnswer> AnswerPoll(Guid pollId, Guid accountId, Dictionary<string, JsonElement> answer)
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
        var answerRecord = new PollAnswer
        {
            PollId = pollId,
            AccountId = accountId,
            Answer = answer
        };
        await db.PollAnswers.AddAsync(answerRecord);
        await db.SaveChangesAsync();

        // Invalidate the cache for this poll answer
        var cacheKey = $"poll:answer:{pollId}:{accountId}";
        await cache.SetAsync(cacheKey, answerRecord, TimeSpan.FromMinutes(30));

        return answerRecord;
    }

    public async Task<bool> UnAnswerPoll(Guid pollId, Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var result = await db.PollAnswers
            .Where(e => e.PollId == pollId && e.AccountId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.DeletedAt, now)) > 0;

        if (result)
        {
            // Remove the cached answer if it exists
            var cacheKey = $"poll:answer:{pollId}:{accountId}";
            await cache.RemoveAsync(cacheKey);
        }

        return result;
    }
}