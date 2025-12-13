using MaxMind.GeoIP2;
using Microsoft.Extensions.Options;

namespace DysonNetwork.Shared.Geometry;

public class GeoOptions
{
    public string DatabasePath { get; set; } = null!;
}

public class GeoService(IOptions<GeoOptions> options)
{
    private readonly string _databasePath = options.Value.DatabasePath;
    
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