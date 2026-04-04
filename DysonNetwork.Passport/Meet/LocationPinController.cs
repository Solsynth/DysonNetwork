using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NodaTime;

namespace DysonNetwork.Passport.Meet;

[ApiController]
[Route("/api/pins")]
public class LocationPinController(
    LocationPinService locationPinService,
    LocationPinSubscriptionHub subscriptions,
    IOptions<JsonOptions> jsonOptions
) : ControllerBase
{
    public class CreatePinRequest
    {
        public LocationVisibility Visibility { get; set; } = LocationVisibility.Private;
        [MaxLength(256)] public string? LocationName { get; set; }
        [MaxLength(1024)] public string? LocationAddress { get; set; }
        public string? LocationWkt { get; set; }
        public Dictionary<string, object>? Metadata { get; set; }
        public bool KeepOnDisconnect { get; set; } = true;
    }

    public class UpdateLocationRequest
    {
        public string? LocationName { get; set; }
        public string? LocationAddress { get; set; }
        public string? LocationWkt { get; set; }
    }

    public class DisconnectRequest
    {
        public bool KeepOnDisconnect { get; set; } = true;
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<SnLocationPin>> CreatePin(
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        [FromBody] CreatePinRequest request,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("Device ID is required.");

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

        var pin = await locationPinService.CreatePinAsync(
            currentUser.Id,
            deviceId,
            request.Visibility,
            request.LocationName,
            request.LocationAddress,
            location,
            request.KeepOnDisconnect,
            request.Metadata,
            cancellationToken
        );

        pin.Account = currentUser;
        return Ok(pin);
    }

    [HttpPut("{id:guid}/location")]
    [Authorize]
    public async Task<ActionResult<SnLocationPin>> UpdateLocation(
        Guid id,
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        [FromBody] UpdateLocationRequest request,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("Device ID is required.");

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

        var pin = await locationPinService.UpdateLocationAsync(
            currentUser.Id,
            deviceId,
            location,
            request.LocationName,
            request.LocationAddress,
            cancellationToken
        );

        if (pin == null)
        {
            return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));
        }

        return Ok(pin);
    }

    [HttpDelete("{id:guid}")]
    [Authorize]
    public async Task<ActionResult> RemovePin(
        Guid id,
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("Device ID is required.");

        var removed = await locationPinService.RemovePinAsync(currentUser.Id, deviceId, cancellationToken);
        if (!removed)
        {
            return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));
        }

        return NoContent();
    }

    [HttpGet("nearby")]
    [Authorize]
    public async Task<ActionResult<List<SnLocationPin>>> ListNearbyPins(
        [FromQuery] string? locationWkt = null,
        [FromQuery] LocationVisibility? visibility = null,
        [FromQuery] int offset = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        Geometry? location = null;
        if (!string.IsNullOrWhiteSpace(locationWkt))
        {
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
        }

        var pins = await locationPinService.ListNearbyPinsAsync(
            currentUser.Id,
            location,
            visibility,
            offset,
            take,
            cancellationToken
        );

        return Ok(pins);
    }

    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<SnLocationPin>> GetPin(
        Guid id,
        [FromQuery] string? locationWkt = null,
        CancellationToken cancellationToken = default
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();

        Geometry? userLocation = null;
        if (!string.IsNullOrWhiteSpace(locationWkt))
        {
            try
            {
                userLocation = new WKTReader().Read(locationWkt);
                userLocation.SRID = 4326;
            }
            catch (Exception)
            {
                return BadRequest(ApiError.Validation(new Dictionary<string, string[]>
                {
                    [nameof(locationWkt)] = ["Invalid WKT geometry."]
                }, traceId: HttpContext.TraceIdentifier));
            }
        }

        var pin = await locationPinService.GetPinAsync(id, currentUser.Id, userLocation, cancellationToken);
        if (pin == null)
        {
            return NotFound(ApiError.NotFound(id.ToString(), traceId: HttpContext.TraceIdentifier));
        }

        return Ok(pin);
    }

    [HttpPost("{id:guid}/disconnect")]
    [Authorize]
    public async Task<ActionResult> DisconnectPin(
        Guid id,
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        [FromBody] DisconnectRequest request,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("Device ID is required.");

        await locationPinService.DisconnectPinAsync(
            currentUser.Id,
            deviceId,
            request.KeepOnDisconnect,
            cancellationToken
        );

        return Ok();
    }

    [HttpGet("{id:guid}/stream")]
    [Authorize]
    public async Task StreamPin(
        Guid id,
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var pin = await locationPinService.GetPinAsync(id, currentUser.Id, null, cancellationToken);
        if (pin == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var subscriptionId = Guid.NewGuid();
        var reader = subscriptions.SubscribeToPin(id, subscriptionId);

        try
        {
            Response.StatusCode = StatusCodes.Status200OK;
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers.Connection = "keep-alive";

            await WriteEventAsync("snapshot", new LocationPinStreamEvent
            {
                Type = "snapshot",
                SentAt = SystemClock.Instance.GetCurrentInstant(),
                Pin = pin
            }, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var hasData = await reader.WaitToReadAsync(cancellationToken);
                if (!hasData) break;

                while (reader.TryRead(out var evt))
                {
                    await WriteEventAsync(evt.Type, evt, cancellationToken);
                }
            }
        }
        finally
        {
            subscriptions.UnsubscribeFromPin(id, subscriptionId);
        }
    }

    [HttpGet("my-pins")]
    [Authorize]
    public async Task<ActionResult<List<SnLocationPin>>> GetMyPins(
        [FromHeader(Name = "X-Device-Id")] string deviceId,
        CancellationToken cancellationToken
    )
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser) return Unauthorized();
        if (string.IsNullOrWhiteSpace(deviceId)) return BadRequest("Device ID is required.");

        var pins = await locationPinService.ListNearbyPinsAsync(
            currentUser.Id,
            null,
            null,
            0,
            100,
            cancellationToken
        );

        var myPins = pins.Where(p => p.AccountId == currentUser.Id).ToList();
        return Ok(myPins);
    }

    private async Task WriteEventAsync(string eventName, LocationPinStreamEvent evt, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(evt, jsonOptions.Value.JsonSerializerOptions);
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
