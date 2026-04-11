using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(KeyId), nameof(DeletedAt), IsUnique = true)]
public class SnFediverseKey : ModelBase
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(512)]
    [JsonPropertyName("key_id")]
    public string KeyId { get; set; } = null!;

    [Column(TypeName = "TEXT")]
    [JsonPropertyName("key_pem")]
    public string KeyPem { get; set; } = null!;

    [Column(TypeName = "TEXT")]
    [JsonPropertyName("private_key_pem")]
    public string? PrivateKeyPem { get; set; }

    [JsonPropertyName("algorithm")]
    [MaxLength(32)]
    public string Algorithm { get; set; } = KeyAlgorithm.RSA_SHA256;

    [JsonPropertyName("publisher_id")]
    public Guid? PublisherId { get; set; }

    [JsonPropertyName("actor_id")]
    public Guid? ActorId { get; set; }

    [JsonPropertyName("actor")]
    [ForeignKey(nameof(ActorId))]
    public SnFediverseActor? Actor { get; set; }

    [JsonPropertyName("created_at")]
    public Instant CreatedAt { get; set; } = SystemClock.Instance.GetCurrentInstant();

    [JsonPropertyName("rotated_at")]
    public Instant? RotatedAt { get; set; }

    [NotMapped]
    [JsonPropertyName("is_local")]
    public bool IsLocal => PublisherId.HasValue;

    [NotMapped]
    public HashAlgorithmName HashAlgorithm => Algorithm switch
    {
        KeyAlgorithm.RSA_SHA256 => HashAlgorithmName.SHA256,
        KeyAlgorithm.RSA_SHA512 => HashAlgorithmName.SHA512,
        _ => HashAlgorithmName.SHA256
    };

    [NotMapped]
    public string HttpSignatureAlgorithm => Algorithm switch
    {
        KeyAlgorithm.RSA_SHA256 => "rsa-sha256",
        KeyAlgorithm.RSA_SHA512 => "rsa-sha512",
        _ => "rsa-sha256"
    };
}

public static class KeyAlgorithm
{
    public const string RSA_SHA256 = "rsa-sha256";
    public const string RSA_SHA512 = "rsa-sha512";
    public const string HS2019 = "hs2019";

    public static string GetActualAlgorithm(string httpAlgorithm)
    {
        return httpAlgorithm.ToLowerInvariant() switch
        {
            "hs2019" => RSA_SHA256,
            "rsa-sha256" or "sha256" => RSA_SHA256,
            "rsa-sha512" or "sha512" => RSA_SHA512,
            _ => RSA_SHA256
        };
    }
}