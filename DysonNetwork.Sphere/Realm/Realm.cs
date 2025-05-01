using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Sphere.Storage;

namespace DysonNetwork.Sphere.Realm;

public class Realm : ModelBase
{
    public long Id { get; set; }
    [MaxLength(1024)] public string Name { get; set; } = string.Empty;
    [MaxLength(4096)] public string Description { get; set; } = string.Empty;

    public CloudFile? Picture { get; set; }
    public CloudFile? Background { get; set; }

    public long? AccountId { get; set; }
    [JsonIgnore] public Account.Account? Account { get; set; }
}