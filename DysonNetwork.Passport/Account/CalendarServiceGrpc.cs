using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.Protobuf;
using Duration = NodaTime.Duration;

namespace DysonNetwork.Passport.Account;

public class CalendarServiceGrpc(
    AppDatabase db,
    NotableDaysService notableDaysService,
    AccountEventService accountEventService,
    ILogger<CalendarServiceGrpc> logger
) : DyProfileService.DyProfileServiceBase
{
    public override async Task<DyGetNotableDaysResponse> GetNotableDays(
        DyGetNotableDaysRequest request,
        ServerCallContext context)
    {
        var year = request.Year;
        var region = string.IsNullOrWhiteSpace(request.Region) ? "CN" : request.Region;

        NotableDayTag? tagFilter = null;
        if (request.HasTag)
        {
            tagFilter = request.Tag switch
            {
                DyNotableDayTag.Holiday => NotableDayTag.Holiday,
                DyNotableDayTag.Event => NotableDayTag.Event,
                DyNotableDayTag.Anniversary => NotableDayTag.Anniversary,
                DyNotableDayTag.Memorial => NotableDayTag.Memorial,
                DyNotableDayTag.Festival => NotableDayTag.Festival,
                _ => null
            };
        }

        var days = await notableDaysService.GetNotableDays(year, region, tagFilter);

        var offset = Math.Max(0, request.Offset);
        var take = request.Take > 0 ? Math.Min(request.Take, 100) : 50;

        var pagedDays = days.Skip(offset).Take(take).ToList();

        var response = new DyGetNotableDaysResponse
        {
            TotalCount = days.Count
        };
        response.Days.AddRange(pagedDays.Select(MapToProto));

        return response;
    }

    public override async Task<DyGetUserCalendarEventsResponse> GetUserCalendarEvents(
        DyGetUserCalendarEventsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        Guid? viewerId = null;
        if (!string.IsNullOrWhiteSpace(request.ViewerId) && Guid.TryParse(request.ViewerId, out var parsedViewerId))
            viewerId = parsedViewerId;

        Instant? startTime = request.StartTime?.ToInstant();
        Instant? endTime = request.EndTime?.ToInstant();

        var offset = Math.Max(0, request.Offset);
        var take = request.Take > 0 ? Math.Min(request.Take, 100) : 50;

        var (events, totalCount) = await accountEventService.GetUserCalendarEventsAsync(
            accountId,
            viewerId,
            startTime,
            endTime,
            offset,
            take);

        var response = new DyGetUserCalendarEventsResponse
        {
            TotalCount = totalCount
        };
        response.Events.AddRange(events.Select(MapToProto));

        return response;
    }

    public override async Task<DyGetCountdownEventsResponse> GetCountdownEvents(
        DyGetCountdownEventsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        var snAccount = new SnAccount { Id = accountId };

        Guid? viewerId = null;
        if (!string.IsNullOrWhiteSpace(request.ViewerId) && Guid.TryParse(request.ViewerId, out var parsedViewerId))
            viewerId = parsedViewerId;

        var region = string.IsNullOrWhiteSpace(request.Region) ? "CN" : request.Region;

        NotableDayTag? tagFilter = null;
        if (request.HasTag)
        {
            tagFilter = request.Tag switch
            {
                DyNotableDayTag.Holiday => NotableDayTag.Holiday,
                DyNotableDayTag.Event => NotableDayTag.Event,
                DyNotableDayTag.Anniversary => NotableDayTag.Anniversary,
                DyNotableDayTag.Memorial => NotableDayTag.Memorial,
                DyNotableDayTag.Festival => NotableDayTag.Festival,
                _ => null
            };
        }

        var take = request.Take > 0 ? Math.Min(request.Take, 100) : 5;
        var offset = Math.Max(0, request.Offset);

        var result = await accountEventService.GetCountdownEventsAsync(
            snAccount,
            viewerId,
            region,
            notableDaysService,
            request.IncludeNotableDays,
            tagFilter,
            take,
            offset);

        var response = new DyGetCountdownEventsResponse
        {
            TotalCount = result.TotalCount
        };
        response.Events.AddRange(result.Events.Select(MapToProto));

        return response;
    }

    private static DyNotableDay MapToProto(NotableDay day)
    {
        var proto = new DyNotableDay
        {
            Name = day.GlobalName ?? day.LocalName ?? "",
            StartDate = day.Date.ToTimestamp(),
            EndDate = day.Date.Plus(Duration.FromDays(1)).ToTimestamp(),
            IsAllDay = true,
            Region = day.CountryCode ?? "",
        };

        if (!string.IsNullOrWhiteSpace(day.LocalName))
            proto.LocalName = day.LocalName;
        if (!string.IsNullOrWhiteSpace(day.LocalizableKey))
            proto.LocalizableKey = day.LocalizableKey;
        if (!string.IsNullOrWhiteSpace(day.GlobalName))
            proto.Description = day.GlobalName;

        proto.Tags.AddRange(day.Tags.Select(t => t switch
        {
            NotableDayTag.Holiday => DyNotableDayTag.Holiday,
            NotableDayTag.Event => DyNotableDayTag.Event,
            NotableDayTag.Anniversary => DyNotableDayTag.Anniversary,
            NotableDayTag.Memorial => DyNotableDayTag.Memorial,
            NotableDayTag.Festival => DyNotableDayTag.Festival,
            _ => DyNotableDayTag.Unspecified
        }));

        return proto;
    }

    private static DyUserCalendarEvent MapToProto(SnUserCalendarEvent evt)
    {
        return new DyUserCalendarEvent
        {
            Id = evt.Id.ToString(),
            Title = evt.Title,
            Description = evt.Description ?? "",
            Location = evt.Location ?? "",
            StartTime = evt.StartTime.ToTimestamp(),
            EndTime = evt.EndTime.ToTimestamp(),
            IsAllDay = evt.IsAllDay,
            Visibility = (int)evt.Visibility,
            AccountId = evt.AccountId.ToString(),
            CreatedAt = evt.CreatedAt.ToTimestamp(),
            UpdatedAt = evt.UpdatedAt.ToTimestamp(),
        };
    }

    private static DyEventCountdownItem MapToProto(EventCountdownItem item)
    {
        var proto = new DyEventCountdownItem
        {
            Title = item.Title,
            StartTime = item.StartTime.ToTimestamp(),
            EndTime = item.EndTime.ToTimestamp(),
            IsAllDay = item.IsAllDay,
            DaysRemaining = item.DaysRemaining,
            HoursRemaining = item.HoursRemaining,
            IsOngoing = item.IsOngoing,
        };

        if (item.EventId.HasValue)
            proto.EventId = item.EventId.Value.ToString();
        if (!string.IsNullOrWhiteSpace(item.Description))
            proto.Description = item.Description;
        if (!string.IsNullOrWhiteSpace(item.Location))
            proto.Location = item.Location;
        if (item.AccountId.HasValue)
            proto.AccountId = item.AccountId.Value.ToString();

        proto.Type = item.EventType switch
        {
            CalendarEventType.UserEvent => 0,
            CalendarEventType.NotableDay => 3,
            _ => 0
        };

        return proto;
    }
}
