using DysonNetwork.Shared.Geometry;
using Microsoft.Extensions.Options;

namespace DysonNetwork.Padlock;

public class GeoService : Shared.Geometry.GeoService
{
    public GeoService(IOptions<GeoOptions> options) : base(options)
    {
    }
}
