using System.ComponentModel.DataAnnotations;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Padlock.Auth;

[ApiController]
[Route("/api/auth/qr")]
[ApiFeature("auth.qr-login", Revision = 1)]
public class QrLoginController(
    AppDatabase db,
    ICacheService cache,
    GeoService geo,
    RemoteWebSocketService ws
) : ControllerBase
{
    private const string QrCachePrefix = "auth:qr:";
    private const string QrToAuthPrefix = "auth:qr:auth:";
    private static readonly TimeSpan QrChallengeTtl = TimeSpan.FromMinutes(5);

    public class QrGenerateRequest
    {
        [Required] [MaxLength(512)] public string DeviceId { get; set; } = null!;
        [MaxLength(1024)] public string? DeviceName { get; set; }
        [Required] public ClientPlatform Platform { get; set; }
        public List<string> Audiences { get; set; } = [];
        public List<string> Scopes { get; set; } = [];
    }

    public class QrGenerateResponse
    {
        public Guid QrChallengeId { get; set; }
        public Guid AuthChallengeId { get; set; }
        public string QrData { get; set; } = null!;
        public Instant ExpiresAt { get; set; }
        public int ExpiresInSeconds { get; set; }
    }

    [HttpPost("generate")]
    public async Task<ActionResult<QrGenerateResponse>> GenerateQrChallenge([FromBody] QrGenerateRequest request)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var expiresAt = now.Plus(Duration.FromTimeSpan(QrChallengeTtl));
        var ipAddress = HttpContext.GetClientIpAddress();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        var authChallenge = new SnAuthChallenge
        {
            StepTotal = 1,
            StepRemain = 1,
            DeviceId = request.DeviceId,
            DeviceName = request.DeviceName ?? userAgent,
            Platform = request.Platform,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Location = ipAddress is not null ? geo.GetPointFromIp(ipAddress) : null,
            Audiences = request.Audiences,
            Scopes = request.Scopes,
            AccountId = Guid.Empty,
            ExpiredAt = expiresAt,
            CreatedAt = now,
        };

        db.AuthChallenges.Add(authChallenge);
        await db.SaveChangesAsync();

        var qrChallenge = new QrLoginChallenge(
            Id: Guid.NewGuid(),
            AuthChallengeId: authChallenge.Id,
            AccountId: Guid.Empty,
            DeviceId: request.DeviceId,
            DeviceName: request.DeviceName,
            Platform: request.Platform,
            Status: QrLoginStatus.Pending,
            CreatedAt: now,
            ExpiresAt: expiresAt,
            ApprovedAt: null,
            ApprovedBySessionId: null,
            ApprovedDeviceId: null
        );

        await cache.SetAsync($"{QrCachePrefix}{qrChallenge.Id}", qrChallenge, QrChallengeTtl);
        await cache.SetAsync($"{QrToAuthPrefix}{authChallenge.Id}", qrChallenge.Id, QrChallengeTtl);

        return Ok(new QrGenerateResponse
        {
            QrChallengeId = qrChallenge.Id,
            AuthChallengeId = authChallenge.Id,
            QrData = $"solian://auth/qr/{qrChallenge.Id}",
            ExpiresAt = expiresAt,
            ExpiresInSeconds = (int)QrChallengeTtl.TotalSeconds
        });
    }

    public class QrStatusResponse
    {
        public Guid QrChallengeId { get; set; }
        public Guid AuthChallengeId { get; set; }
        public QrLoginStatus Status { get; set; }
        public Instant ExpiresAt { get; set; }
        public Instant? ApprovedAt { get; set; }
        public string? ApprovedDeviceId { get; set; }
        public string? DeviceName { get; set; }
        public ClientPlatform Platform { get; set; }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QrStatusResponse>> GetQrStatus(Guid id)
    {
        var (found, qrChallenge) = await cache.GetAsyncWithStatus<QrLoginChallenge>($"{QrCachePrefix}{id}");

        if (!found || qrChallenge is null)
            return NotFound(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_FOUND", Message = "QR challenge not found or expired.", Status = 404 });

        return Ok(new QrStatusResponse
        {
            QrChallengeId = qrChallenge.Id,
            AuthChallengeId = qrChallenge.AuthChallengeId,
            Status = qrChallenge.Status,
            ExpiresAt = qrChallenge.ExpiresAt,
            ApprovedAt = qrChallenge.ApprovedAt,
            ApprovedDeviceId = qrChallenge.ApprovedDeviceId,
            DeviceName = qrChallenge.DeviceName,
            Platform = qrChallenge.Platform
        });
    }

    [Authorize]
    [RequireInteractiveSession]
    [HttpPost("{id:guid}/scan")]
    public async Task<IActionResult> ScanQrChallenge(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var (found, qrChallenge) = await cache.GetAsyncWithStatus<QrLoginChallenge>($"{QrCachePrefix}{id}");

        if (!found || qrChallenge is null)
            return NotFound(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_FOUND", Message = "QR challenge not found or expired.", Status = 404 });

        if (qrChallenge.Status != QrLoginStatus.Pending)
            return BadRequest(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_PENDING", Message = "QR challenge is no longer pending.", Status = 400 });

        var now = SystemClock.Instance.GetCurrentInstant();
        if (now > qrChallenge.ExpiresAt)
        {
            await cache.RemoveAsync($"{QrCachePrefix}{id}");
            return BadRequest(new ApiError { Code = "PADLOCK_QR_CHALLENGE_EXPIRED", Message = "QR challenge has expired.", Status = 400 });
        }

        var scanned = qrChallenge with { Status = QrLoginStatus.Scanned };
        var remainingTtl = qrChallenge.ExpiresAt - now;
        await cache.SetAsync($"{QrCachePrefix}{id}", scanned, remainingTtl.ToTimeSpan());

        var scanPayload = InfraObjectCoder.ConvertObjectToByteString(new
        {
            qr_challenge_id = qrChallenge.Id,
            scanned_by_device = HttpContext.Items["CurrentSession"] is SnAuthSession s ? s.Id.ToString() : null
        }).ToByteArray();

        await ws.PushWebSocketPacket(
            currentUser.Id.ToString(),
            WebSocketPacketType.QrLoginScanned,
            scanPayload,
            [qrChallenge.DeviceId]
        );

        return Ok();
    }

    [Authorize]
    [RequireInteractiveSession]
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> ApproveQrChallenge(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var hasQrLoginFactor = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id)
            .Where(f => f.Type == AccountAuthFactorType.QrLogin)
            .Where(f => f.EnabledAt != null)
            .AnyAsync();

        if (!hasQrLoginFactor)
            return BadRequest(new ApiError { Code = "PADLOCK_QR_FACTOR_NOT_ENABLED", Message = "QR login factor is not enabled for this account.", Status = 400 });

        var (found, qrChallenge) = await cache.GetAsyncWithStatus<QrLoginChallenge>($"{QrCachePrefix}{id}");

        if (!found || qrChallenge is null)
            return NotFound(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_FOUND", Message = "QR challenge not found or expired.", Status = 404 });

        if (qrChallenge.Status is not (QrLoginStatus.Pending or QrLoginStatus.Scanned))
            return BadRequest(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_PENDING", Message = "QR challenge is no longer pending.", Status = 400 });

        var now = SystemClock.Instance.GetCurrentInstant();
        if (now > qrChallenge.ExpiresAt)
        {
            await cache.RemoveAsync($"{QrCachePrefix}{id}");
            return BadRequest(new ApiError { Code = "PADLOCK_QR_CHALLENGE_EXPIRED", Message = "QR challenge has expired.", Status = 400 });
        }

        var authChallenge = await db.AuthChallenges.FindAsync(qrChallenge.AuthChallengeId);
        if (authChallenge is null)
            return BadRequest(new ApiError { Code = "PADLOCK_AUTH_CHALLENGE_NOT_FOUND", Message = "Associated auth challenge not found.", Status = 400 });

        authChallenge.AccountId = currentUser.Id;
        authChallenge.StepRemain = 0;
        authChallenge.ApprovedAt = now;
        authChallenge.ApprovedBySessionId = currentSession.Id;
        db.AuthChallenges.Update(authChallenge);
        await db.SaveChangesAsync();

        var approved = qrChallenge with
        {
            AccountId = currentUser.Id,
            Status = QrLoginStatus.Approved,
            ApprovedAt = now,
            ApprovedBySessionId = currentSession.Id,
            ApprovedDeviceId = currentSession.ClientId?.ToString()
        };
        var remainingTtl = qrChallenge.ExpiresAt - now;
        await cache.SetAsync($"{QrCachePrefix}{id}", approved, remainingTtl.ToTimeSpan());

        var approvedPayload = InfraObjectCoder.ConvertObjectToByteString(new
        {
            qr_challenge_id = qrChallenge.Id,
            auth_challenge_id = authChallenge.Id,
            approved_by_device = currentSession.Id.ToString()
        }).ToByteArray();

        await ws.PushWebSocketPacket(
            currentUser.Id.ToString(),
            WebSocketPacketType.QrLoginApproved,
            approvedPayload,
            [qrChallenge.DeviceId]
        );

        return Ok();
    }

    [Authorize]
    [RequireInteractiveSession]
    [HttpPost("{id:guid}/decline")]
    public async Task<IActionResult> DeclineQrChallenge(Guid id)
    {
        if (HttpContext.Items["CurrentUser"] is not SnAccount currentUser)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });
        if (HttpContext.Items["CurrentSession"] is not SnAuthSession currentSession)
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Authentication is required.", Status = 401 });

        var hasQrLoginFactor = await db.AccountAuthFactors
            .Where(f => f.AccountId == currentUser.Id)
            .Where(f => f.Type == AccountAuthFactorType.QrLogin)
            .Where(f => f.EnabledAt != null)
            .AnyAsync();

        if (!hasQrLoginFactor)
            return BadRequest(new ApiError { Code = "PADLOCK_QR_FACTOR_NOT_ENABLED", Message = "QR login factor is not enabled for this account.", Status = 400 });

        var (found, qrChallenge) = await cache.GetAsyncWithStatus<QrLoginChallenge>($"{QrCachePrefix}{id}");

        if (!found || qrChallenge is null)
            return NotFound(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_FOUND", Message = "QR challenge not found or expired.", Status = 404 });

        if (qrChallenge.Status is not (QrLoginStatus.Pending or QrLoginStatus.Scanned))
            return BadRequest(new ApiError { Code = "PADLOCK_QR_CHALLENGE_NOT_PENDING", Message = "QR challenge is no longer pending.", Status = 400 });

        var now = SystemClock.Instance.GetCurrentInstant();
        if (now > qrChallenge.ExpiresAt)
        {
            await cache.RemoveAsync($"{QrCachePrefix}{id}");
            return BadRequest(new ApiError { Code = "PADLOCK_QR_CHALLENGE_EXPIRED", Message = "QR challenge has expired.", Status = 400 });
        }

        var declined = qrChallenge with
        {
            Status = QrLoginStatus.Declined,
            ApprovedAt = now,
            ApprovedBySessionId = currentSession.Id
        };
        var remainingTtl = qrChallenge.ExpiresAt - now;
        await cache.SetAsync($"{QrCachePrefix}{id}", declined, remainingTtl.ToTimeSpan());

        var declinedPayload = InfraObjectCoder.ConvertObjectToByteString(new
        {
            qr_challenge_id = qrChallenge.Id,
            declined_by_device = currentSession.Id.ToString()
        }).ToByteArray();

        await ws.PushWebSocketPacket(
            currentUser.Id.ToString(),
            WebSocketPacketType.QrLoginDeclined,
            declinedPayload,
            [qrChallenge.DeviceId]
        );

        return Ok();
    }
}
