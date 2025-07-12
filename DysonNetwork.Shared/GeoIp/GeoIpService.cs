using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Point = NetTopologySuite.Geometries.Point;

namespace DysonNetwork.Shared.GeoIp;

public class GeoIpOptions
{
    public string DatabasePath { get; set; } = null!;
}

public class GeoIpService(IOptions<GeoIpOptions> options)
{
    private readonly string _databasePath = options.Value.DatabasePath;
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326); // 4326 is the SRID for WGS84
    
    public Point? GetPointFromIp(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return null;

        try
        {
            using var reader = new DatabaseReader(_databasePath);
            var city = reader.City(ipAddress);
            
            if (city?.Location == null || !city.Location.HasCoordinates)
                return null;

            return _geometryFactory.CreatePoint(new Coordinate(
                city.Location.Longitude ?? 0,
                city.Location.Latitude ?? 0));
        }
        catch (Exception)
        {
            return null;
        }
    }
    
    public MaxMind.GeoIP2.Responses.CityResponse? GetFromIp(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return null;
    
        try
        {
            using var reader = new DatabaseReader(_databasePath);
            return reader.City(ipAddress);
        }
        catch (Exception)
        {
            return null;
        }
    }
}