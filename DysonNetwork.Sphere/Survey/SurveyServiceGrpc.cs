using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Models;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DysonNetwork.Sphere.Survey;

public class SurveyServiceGrpc(AppDatabase db, SurveyService ss) : DySurveyService.DySurveyServiceBase
{
    public override async Task<DySurvey> GetSurvey(DyGetSurveyRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.Id, out var id))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid survey id"));

        var survey = await db.Surveys
            .Include(p => p.Publisher)
            .Include(p => p.Questions)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (survey == null) throw new RpcException(new Status(StatusCode.NotFound, "survey not found"));

        return survey.ToProtoValue();
    }

    public override async Task<DyGetSurveyBatchResponse> GetSurveyBatch(DyGetSurveyBatchRequest request,
        ServerCallContext context)
    {
        var ids = request.Ids
            .Where(s => !string.IsNullOrWhiteSpace(s) && Guid.TryParse(s, out _))
            .Select(Guid.Parse)
            .ToList();

        if (ids.Count == 0) return new DyGetSurveyBatchResponse();

        var surveys = await db.Surveys
            .Include(p => p.Publisher)
            .Include(p => p.Questions)
            .Where(p => ids.Contains(p.Id))
            .ToListAsync();

        var resp = new DyGetSurveyBatchResponse();
        resp.Surveys.AddRange(surveys.Select(p => p.ToProtoValue()));
        return resp;
    }

    public override async Task<DyListSurveysResponse> ListSurveys(DyListSurveysRequest request, ServerCallContext context)
    {
        var query = db.Surveys
            .Include(p => p.Publisher)
            .Include(p => p.Questions)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.PublisherId) && Guid.TryParse(request.PublisherId, out var pid))
            query = query.Where(p => p.PublisherId == pid);

        var totalSize = await query.CountAsync();

        var pageSize = request.PageSize > 0 ? request.PageSize : 20;
        var pageToken = request.PageToken;
        var offset = string.IsNullOrEmpty(pageToken) ? 0 : int.Parse(pageToken);

        IOrderedQueryable<SnSurvey> orderedQuery;

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

        var surveys = await orderedQuery
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync();

        var nextToken = offset + pageSize < totalSize ? (offset + pageSize).ToString() : string.Empty;

        var resp = new DyListSurveysResponse();
        resp.Surveys.AddRange(surveys.Select(p => p.ToProtoValue()));
        resp.NextPageToken = nextToken;
        resp.TotalSize = totalSize;

        return resp;
    }

    public override async Task<DySurveyAnswer> GetSurveyAnswer(DyGetSurveyAnswerRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SurveyId, out var surveyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid survey id"));

        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid account id"));

        var answer = await ss.GetSurveyAnswer(surveyId, accountId);

        if (answer == null)
            throw new RpcException(new Status(StatusCode.NotFound, "answer not found"));

        return answer.ToProtoValue();
    }

    public override async Task<DyGetSurveyStatsResponse> GetSurveyStats(DyGetSurveyStatsRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.SurveyId, out var surveyId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid survey id"));

        var stats = await ss.GetSurveyStats(surveyId);

        var resp = new DyGetSurveyStatsResponse();
        foreach (var stat in stats)
        {
            var statsJson = JsonSerializer.Serialize(stat.Value);
            resp.Stats[stat.Key.ToString()] = statsJson;
        }

        return resp;
    }

    public override async Task<DyGetSurveyQuestionStatsResponse> GetSurveyQuestionStats(DyGetSurveyQuestionStatsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.QuestionId, out var questionId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "invalid question id"));

        var stats = await ss.GetSurveyQuestionStats(questionId);

        var resp = new DyGetSurveyQuestionStatsResponse();
        foreach (var stat in stats)
        {
            resp.Stats[stat.Key] = stat.Value;
        }

        return resp;
    }
}
