using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Geometry;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Auth;
using Microsoft.AspNetCore.Mvc;

namespace DysonNetwork.Passport;

[ApiController]
[Route("/api/ip-check")]
public class IpCheckController(GeoService geoService) : ControllerBase
{
    public class GeoIpResponse
    {
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? CountryCode { get; set; }
        public string? Subdivision { get; set; }
        public string? SubdivisionCode { get; set; }
        public string? ContinentCode { get; set; }
        public string? TimeZone { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
    }

    public class IpCheckResponse
    {
        public string? ClientIp { get; set; }
        public string? RemoteIp { get; set; }
        public string? XForwardedFor { get; set; }
        public string? XForwardedProto { get; set; }
        public string? XForwardedHost { get; set; }
        public string? XRealIp { get; set; }
        public string? CfConnectingIp { get; set; }
        public GeoIpResponse? Geo { get; set; }
        public string? Headers { get; set; }
    }
    
    /// <summary>
    /// Public geo lookup for the caller's IP (city/region/coords only).
    /// Used by clients as a weather/location fallback without device GPS.
    /// Does not require authentication.
    /// </summary>
    [HttpGet("geo")]
    public ActionResult<GeoIpResponse> GetClientGeo()
    {
        var clientIp = HttpContext.GetClientIpAddress();
        var geo = geoService.GetFromIp(clientIp);
        if (geo is null)
            return NotFound(new ApiError { Code = "PASSPORT_IP_GEO_NOT_FOUND", Message = "Could not resolve geo location for client IP.", Status = 404 });

        if (geo.Location is not { HasCoordinates: true })
            return NotFound(new ApiError { Code = "PASSPORT_IP_GEO_NO_COORDINATES", Message = "Geo location has no coordinates.", Status = 404 });

        return Ok(ToGeoResponse(geo));
    }

    /// <summary>
    /// Admin-only diagnostic endpoint with full client IP / header dump.
    /// </summary>
    [HttpGet]
    [AskPermission(PermissionKeys.AdminIpCheck)]
    public ActionResult<IpCheckResponse> GetIpCheck()
    {
        var clientIp = HttpContext.GetClientIpAddress();
        var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var xForwardedProto = Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        var xForwardedHost = Request.Headers["X-Forwarded-Host"].FirstOrDefault();
        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        var cfConnectingIp = Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        var geo = geoService.GetFromIp(clientIp);

        return Ok(new IpCheckResponse
        {
            ClientIp = clientIp,
            RemoteIp = remoteIp,
            XForwardedFor = xForwardedFor,
            XForwardedProto = xForwardedProto,
            XForwardedHost = xForwardedHost,
            XRealIp = realIp,
            CfConnectingIp = cfConnectingIp,
            Geo = geo is null ? null : ToGeoResponse(geo),
            Headers = string.Join('\n', Request.Headers.Select(h => $"{h.Key}: {h.Value}")),
        });
    }

    private static GeoIpResponse ToGeoResponse(MaxMind.GeoIP2.Responses.CityResponse geo) => new()
    {
        City = geo.City.Name,
        Country = geo.Country.Name,
        CountryCode = geo.Country.IsoCode,
        Subdivision = geo.MostSpecificSubdivision.Name,
        SubdivisionCode = geo.MostSpecificSubdivision.IsoCode,
        ContinentCode = geo.Continent.Code,
        TimeZone = geo.Location.TimeZone,
        Latitude = geo.Location.Latitude,
        Longitude = geo.Location.Longitude
    };
}
