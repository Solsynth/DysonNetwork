using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Padlock.Models;

public class SnAccountPasskey : ModelBase
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }
    [JsonIgnore] public SnAccount Account { get; set; } = null!;

    [MaxLength(256)] public string Label { get; set; } = null!;
    [JsonIgnore] [MaxLength(4096)] public string CredentialId { get; set; } = null!;
    [JsonIgnore] [MaxLength(8196)] public string Credential { get; set; } = null!;
}
