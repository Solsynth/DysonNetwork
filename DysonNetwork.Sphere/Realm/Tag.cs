using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Sphere.Realm;

public class Tag : ModelBase
{
    public Guid Id { get; set; }
    [MaxLength(64)]
    public string Name { get; set; } = string.Empty;

    public ICollection<RealmTag> RealmTags { get; set; } = new List<RealmTag>();
}
