using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;

namespace DysonNetwork.Shared.GeoIp;

public class GeoIpOptions
{
    public string DatabasePath { get; set; } = null!;
}

public class GeoIpService(IOptions<GeoIpOptions> options)
{
    private readonly string _databasePath = options.Value.DatabasePath;
    private readonly GeometryFactory _geometryFactory = new(new PrecisionModel(), 4326); // 4326 is the SRID for WGS84
    
    public GeoPoint? GetPointFromIp(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress))
            return null;

        try
        {
            using var reader = new DatabaseReader(_databasePath);
            var city = reader.City(ipAddress);
            
            if (city?.Location is not { HasCoordinates: true })
                return null;

            return new GeoPoint()
            {
                City = city.City.Name,
                Country = city.Country.Name,
                CountryCode = city.Country.IsoCode,
                Longitude = city.Location.Longitude,
                Latitude = city.Location.Latitude,
            };
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