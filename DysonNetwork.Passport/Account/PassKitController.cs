using DysonNetwork.Shared.Networking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport.Account;

[ApiController]
[AllowAnonymous]
[Route("/api/passkit/v1")]
public class PassKitController(ApplePassService passService) : ControllerBase
{
    public class PassRegistrationRequest
    {
        public string PushToken { get; set; } = string.Empty;
    }

    public class PassSerialNumbersResponse
    {
        public List<string> SerialNumbers { get; set; } = [];
        public string LastUpdated { get; set; } = string.Empty;
    }

    public class PassLogRequest
    {
        public List<string>? Logs { get; set; }
    }

    [HttpPost("devices/{deviceLibraryIdentifier}/registrations/{passTypeIdentifier}/{serialNumber}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiError>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RegisterDevice(
        string deviceLibraryIdentifier,
        string passTypeIdentifier,
        string serialNumber,
        [FromBody] PassRegistrationRequest request,
        CancellationToken cancellationToken
    )
    {
        var pass = await RequireAuthorizedPassAsync(passTypeIdentifier, serialNumber, cancellationToken);
        if (pass is null) return Unauthorized(ApiError.Unauthorized(traceId: HttpContext.TraceIdentifier));

        if (string.IsNullOrWhiteSpace(request.PushToken))
            return BadRequest(ApiError.Validation(
                new Dictionary<string, string[]> { [nameof(request.PushToken)] = ["Push token is required."] },
                traceId: HttpContext.TraceIdentifier
            ));

        await passService.RegisterDeviceAsync(pass, deviceLibraryIdentifier, request.PushToken, cancellationToken);
        return Created(string.Empty, null);
    }

    [HttpDelete("devices/{deviceLibraryIdentifier}/registrations/{passTypeIdentifier}/{serialNumber}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> UnregisterDevice(
        string deviceLibraryIdentifier,
        string passTypeIdentifier,
        string serialNumber,
        CancellationToken cancellationToken
    )
    {
        var pass = await RequireAuthorizedPassAsync(passTypeIdentifier, serialNumber, cancellationToken);
        if (pass is null) return Unauthorized(ApiError.Unauthorized(traceId: HttpContext.TraceIdentifier));

        await passService.UnregisterDeviceAsync(pass, deviceLibraryIdentifier, cancellationToken);
        return Ok();
    }

    [HttpGet("devices/{deviceLibraryIdentifier}/registrations/{passTypeIdentifier}")]
    [ProducesResponseType<PassSerialNumbersResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<PassSerialNumbersResponse>> GetUpdatedSerialNumbers(
        string deviceLibraryIdentifier,
        string passTypeIdentifier,
        [FromQuery(Name = "passesUpdatedSince")] string? passesUpdatedSince,
        CancellationToken cancellationToken
    )
    {
        var authHeader = Request.Headers.Authorization.ToString();
        var (serialNumbers, lastUpdated) = await passService.GetUpdatedSerialNumbersAsync(
            deviceLibraryIdentifier,
            passTypeIdentifier,
            passesUpdatedSince,
            cancellationToken
        );

        if (serialNumbers.Count == 0) return NoContent();

        var firstPass = await passService.GetPassBySerialAsync(passTypeIdentifier, serialNumbers[0], cancellationToken);
        if (firstPass is null || !passService.IsAuthorized(firstPass, authHeader))
            return Unauthorized(ApiError.Unauthorized(traceId: HttpContext.TraceIdentifier));

        return Ok(new PassSerialNumbersResponse
        {
            SerialNumbers = serialNumbers,
            LastUpdated = lastUpdated
        });
    }

    [HttpGet("passes/{passTypeIdentifier}/{serialNumber}")]
    [Produces("application/vnd.apple.pkpass")]
    public async Task<ActionResult> GetLatestPass(
        string passTypeIdentifier,
        string serialNumber,
        CancellationToken cancellationToken
    )
    {
        var pass = await RequireAuthorizedPassAsync(passTypeIdentifier, serialNumber, cancellationToken);
        if (pass is null) return Unauthorized(ApiError.Unauthorized(traceId: HttpContext.TraceIdentifier));

        var bytes = await passService.GenerateMemberPassAsync(pass.AccountId, cancellationToken);
        Response.Headers.ETag = $"\"{pass.LastUpdatedTag}\"";
        return File(bytes, "application/vnd.apple.pkpass", "member.pkpass");
    }

    [HttpPost("log")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Log([FromBody] PassLogRequest request, ILogger<PassKitController> logger)
    {
        if (request.Logs is { Count: > 0 })
        {
            foreach (var logLine in request.Logs)
                logger.LogInformation("Apple Wallet log: {LogLine}", logLine);
        }

        return Ok();
    }

    private async Task<SnApplePass?> RequireAuthorizedPassAsync(
        string passTypeIdentifier,
        string serialNumber,
        CancellationToken cancellationToken
    )
    {
        var pass = await passService.GetPassBySerialAsync(passTypeIdentifier, serialNumber, cancellationToken);
        if (pass is null) return null;

        return passService.IsAuthorized(pass, Request.Headers.Authorization.ToString())
            ? pass
            : null;
    }
}
