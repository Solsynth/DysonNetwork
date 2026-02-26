using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using PollQuestionType = DysonNetwork.DyPollQuestionType;

namespace DysonNetwork.Sphere.Poll;

public class PollServiceGrpc(AppDatabase db, PollService ps) : DyPollService.DyPollServiceBase
{
    public override async Task<DyPoll> GetPoll(GetPollRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid poll id"));

        var poll = await db.Polls
            .Include(p => p.Publisher)
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (poll == null) throw new RpcException(new Status(StatusCode.NotFound, "poll not found"));

        return poll.ToProtoValue();
    }

    public override async Task<GetPollBatchResponse> GetPollBatch(GetPollBatchRequest request,
        ServerCallContext context)
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();

        if (ids.Count == 0) return new GetPollBatchResponse();

        var polls = await db.Polls
            .Include(p => p.Publisher)
            .Include(p => p.Questions)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        var resp = new GetPollBatchResponse();
        resp.Polls.AddRange(polls.Select(p => p.ToProtoValue()));
        return resp;
    }

    public override async Task<ListPollsResponse> ListPolls(ListPollsRequest request, ServerCallContext context)
    {
        var query = db.Polls
            .Include(p => p.Publisher)
            .Include(p => p.Questions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.PublisherId) && Guid.TryParse(request.PublisherId, out var pid))
            query = query.Where(p => p.PublisherId == pid);

        var totalSize = await query.CountAsync();

        var pageSize = request.PageSize > 0 ? request.PageSize : 20;
        var pageToken = request.PageToken;
        var offset = string.IsNullOrEmpty(pageToken) ? 0 : int.Parse(pageToken);

        IOrderedQueryable<SnPoll> orderedQuery;

        if (!string.IsNullOrEmpty(request.OrderBy))
        {
            switch (request.OrderBy)
            {
                case "title":
                    orderedQuery = request.OrderDesc
                        ? query.OrderByDescending(q => q.Title ?? string.Empty)
                        : query.OrderBy(q => q.Title ?? string.Empty);
                    break;
                case "ended_at":
                    orderedQuery = request.OrderDesc
                        ? query.OrderByDescending(q => q.EndedAt)
                        : query.OrderBy(q => q.EndedAt);
                    break;
                default:
                    orderedQuery = request.OrderDesc
                        ? query.OrderByDescending(q => q.CreatedAt)
                        : query.OrderBy(q => q.CreatedAt);
                    break;
            }
        }
        else
        {
            orderedQuery = request.OrderDesc
                ? query.OrderByDescending(q => q.CreatedAt)
                : query.OrderBy(q => q.CreatedAt);
        }

        var polls = await orderedQuery
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync();

        var nextToken = offset + pageSize < totalSize ? (offset + pageSize).ToString() : string.Empty;

        var resp = new ListPollsResponse();
        resp.Polls.AddRange(polls.Select(p => p.ToProtoValue()));
        resp.NextPageToken = nextToken;
        resp.TotalSize = totalSize;

        return resp;
    }

    public override async Task<PollAnswer> GetPollAnswer(GetPollAnswerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PollId, out var pollId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid poll id"));

        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid account id"));

        var answer = await ps.GetPollAnswer(pollId, accountId);

        if (answer == null)
            throw new RpcException(new Status(StatusCode.NotFound, "answer not found"));

        return answer.ToProtoValue();
    }

    public override async Task<GetPollStatsResponse> GetPollStats(GetPollStatsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.PollId, out var pollId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid poll id"));

        var stats = await ps.GetPollStats(pollId);

        var resp = new GetPollStatsResponse();
        foreach (var stat in stats)
        {
            var statsJson = JsonSerializer.Serialize(stat.Value);
            resp.Stats[stat.Key.ToString()] = statsJson;
        }

        return resp;
    }

    public override async Task<GetPollQuestionStatsResponse> GetPollQuestionStats(GetPollQuestionStatsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.QuestionId, out var questionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid question id"));

        var stats = await ps.GetPollQuestionStats(questionId);

        var resp = new GetPollQuestionStatsResponse();
        foreach (var stat in stats)
        {
            resp.Stats[stat.Key] = stat.Value;
        }

        return resp;
    }
}
