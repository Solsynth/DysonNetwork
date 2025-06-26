using System;

namespace DysonNetwork.Sphere.Realm;

public class RealmTag : ModelBase
{
    public Guid RealmId { get; set; }
    public Realm Realm { get; set; } = null!;

    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
