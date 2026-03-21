using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Grpc.Core;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

[ApiController]
[Route("/api/meets")]
public class MeetController(
    MeetService meetService,
    MeetSubscriptionHub subscriptions,
    IOptions<JsonOptions> jsonOptions
) : ControllerBase
{
    public class CreateMeetRequest
    {
        public MeetVisibility Visibility { get; set; } = MeetVisibility.Private;
        [MaxLength(8192)] public string? Notes { get; set; }
        [MaxLength(512)] public string? ImageId { get; set; }
        [MaxLength(256)] public string? LocationName { get; set; }
        [MaxLength(1024)] public string? LocationAddress { get; set; }
        public string? LocationWkt { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        [Range(60, 86400)] public int? ExpiresInSeconds { get; set; }
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnMeet>> CreateMeet([FromBody] CreateMeetRequest request, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            Geometry? location = null;
            if (!string.IsNullOrWhiteSpace(request.LocationWkt))
            {
                try
                {
                    location = new WKTReader().Read(request.LocationWkt);
                    location.SRID = 4326;
                }
                catch (Exception)
                {
                    return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
                    {
                        [nameof(request.LocationWkt)] = ["Invalid WKT geometry."]
                    }, traceId: HttpContext.TraceIdentifier));
                }
            }

            var meet = await meetService.CreateMeetAsync(
                currentUser.Id,
                request.Visibility,
                request.Notes,
                request.ImageId,
                request.LocationName,
                request.LocationAddress,
                location,
                request.Metadata,
                request.ExpiresInSeconds.HasValue ? Duration.FromSeconds(request.ExpiresInSeconds.Value) : null,
                cancellationToken
            );

            return Ok(meet);
        }
        catch (RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(CreateMeetRequest.ImageId)] = ["Image was not found."]
            }, traceId: HttpContext.TraceIdentifier));
        }
    }

    [HttpGet("nearby")]
    [Authorize]
    public async Task<ActionResult<List<SnMeet>>> ListNearbyMeets(
        [FromQuery] string locationWkt,
        [FromQuery] double distanceMeters = 1000,
        [FromQuery] MeetStatus? status = MeetStatus.Active,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        if (string.IsNullOrWhiteSpace(locationWkt))
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(locationWkt)] = ["Location WKT is required."]
            }, traceId: HttpContext.TraceIdentifier));
        }

        if (distanceMeters <= 0)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(distanceMeters)] = ["Distance must be greater than zero."]
            }, traceId: HttpContext.TraceIdentifier));
        }

        Geometry location;
        try
        {
            location = new WKTReader().Read(locationWkt);
            location.SRID = 4326;
        }
        catch (Exception)
        {
            return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
            {
                [nameof(locationWkt)] = ["Invalid WKT geometry."]
            }, traceId: HttpContext.TraceIdentifier));
        }

        var meets = await meetService.ListNearbyMeetsAsync(
            currentUser.Id,
            location,
            distanceMeters,
            status,
            offset,
            take,
            cancellationToken
        );

        return Ok(meets);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<List<SnMeet>>> ListMeets(
        [FromQuery] MeetStatus? status = null,
        [FromQuery] bool hostOnly = false,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var meets = await meetService.ListMeetsAsync(currentUser.Id, status, hostOnly, offset, take, cancellationToken);
        return Ok(meets);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnMeet>> GetMeet(Guid id, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        var meet = await meetService.GetMeetAsync(id, currentUser.Id, cancellationToken);
        if (meet is null)
            return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));

        return Ok(meet);
    }

    [HttpPost("{id:guid}/complete")]
    [Authorize]
    public async Task<ActionResult<SnMeet>> CompleteMeet(Guid id, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        try
        {
            var meet = await meetService.CompleteMeetAsync(id, currentUser.Id, cancellationToken);
            return Ok(meet);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, ApiError.Unauthorized(ex.Message, forbidden: true, traceId: HttpContext.TraceIdentifier));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new ApiError
            {
                Code = "MEET_NOT_ACTIVE",
                Message = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                TraceId = HttpContext.TraceIdentifier
            });
        }
    }

    [HttpPost("{id:guid}/join")]
    [Authorize]
    public async Task JoinMeet(Guid id, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        try
        {
            var subscriptionId = Guid.NewGuid();
            var reader = subscriptions.Subscribe(id, subscriptionId);

            try
            {
                var meet = await meetService.JoinMeetAsync(id, currentUser.Id, cancellationToken);

                Response.StatusCode = StatusCodes.Status200OK;
                Response.Headers.ContentType = "text/event-stream";
                Response.Headers.CacheControl = "no-cache";
                Response.Headers.Connection = "keep-alive";

                await WriteEventAsync("snapshot", new MeetStreamEvent
                {
                    Type = "snapshot",
                    SentAt = SystemClock.Instance.GetCurrentInstant(),
                    Meet = meet
                }, cancellationToken);

                while (!cancellationToken.IsCancellationRequested)
                {
                    var hasData = await reader.WaitToReadAsync(cancellationToken);
                    if (!hasData) break;

                    while (reader.TryRead(out var evt))
                    {
                        await WriteEventAsync(evt.Type, evt, cancellationToken);
                        if (evt.Meet.IsFinal)
                            return;
                    }
                }
            }
            finally
            {
                subscriptions.Unsubscribe(id, subscriptionId);
            }
        }
        catch (KeyNotFoundException)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteJsonBodyAsync(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier), cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            await WriteJsonBodyAsync(ApiError.Unauthorized(ex.Message, forbidden: true, traceId: HttpContext.TraceIdentifier), cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteJsonBodyAsync(new ApiError
            {
                Code = "MEET_NOT_ACTIVE",
                Message = ex.Message,
                Status = StatusCodes.Status400BadRequest,
                TraceId = HttpContext.TraceIdentifier
            }, cancellationToken);
        }
    }

    private async Task WriteEventAsync(string eventName, MeetStreamEvent evt, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(evt, jsonOptions.Value.JsonSerializerOptions);
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteJsonBodyAsync(object payload, CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(Response.Body, payload, jsonOptions.Value.JsonSerializerOptions, cancellationToken);
    }
}
