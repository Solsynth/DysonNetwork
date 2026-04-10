using System.Text.Json;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Sphere.ActivityPub;

public class ServerSigningKeyData
{
    public string KeyId { get; set; } = null!;
    public string PublicKeyPem { get; set; } = null!;
    public string PrivateKeyPem { get; set; } = null!;
    public string Algorithm { get; set; } = KeyAlgorithm.RSA_SHA256;
    public Instant CreatedAt { get; set; }
}

public class ServerSigningKeyService : IServerSigningKeyService
{
    private readonly string _domain;
    private readonly string _keyPath;
    private readonly string _keyId;
    private readonly ILogger<ServerSigningKeyService> _logger;
    private ServerSigningKeyData? _cachedKey;

    public ServerSigningKeyService(IConfiguration configuration, ILogger<ServerSigningKeyService> logger)
    {
        _logger = logger;
        _domain = configuration["ActivityPub:Domain"] ?? "localhost";
        _keyPath = configuration["ActivityPub:ServerKeyPath"] ?? "./Keys/server-key.json";
        _keyId = $"https://{_domain}/actor#main-key";
    }

    public string KeyId => _keyId;
    public string ActorUri => $"https://{_domain}/actor";

    public async Task<(string publicKey, string privateKey)> GetOrCreateKeyAsync()
    {
        if (_cachedKey != null)
            return (_cachedKey.PublicKeyPem, _cachedKey.PrivateKeyPem);

        var key = await LoadKeyAsync();
        if (key != null)
        {
            _cachedKey = key;
            return (key.PublicKeyPem, key.PrivateKeyPem);
        }

        _logger.LogInformation("No server signing key found, generating new one at {Path}", _keyPath);

        var (publicKey, privateKey) = HttpSignature.GenerateKeyPair();

        key = new ServerSigningKeyData
        {
            KeyId = _keyId,
            PublicKeyPem = publicKey,
            PrivateKeyPem = privateKey,
            Algorithm = KeyAlgorithm.RSA_SHA256,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        await SaveKeyAsync(key);
        _cachedKey = key;

        _logger.LogInformation("Generated new server signing key: {KeyId}", _keyId);
        return (publicKey, privateKey);
    }

    public async Task<string?> GetPublicKeyAsync()
    {
        var key = await LoadKeyAsync();
        return key?.PublicKeyPem;
    }

    public async Task InvalidateCacheAsync()
    {
        _cachedKey = null;
    }

    public async Task RotateKeyAsync()
    {
        _logger.LogInformation("Rotating server signing key");

        var (publicKey, privateKey) = HttpSignature.GenerateKeyPair();

        var key = new ServerSigningKeyData
        {
            KeyId = _keyId,
            PublicKeyPem = publicKey,
            PrivateKeyPem = privateKey,
            Algorithm = KeyAlgorithm.RSA_SHA256,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        await SaveKeyAsync(key);
        _cachedKey = key;

        _logger.LogInformation("Server signing key rotated: {KeyId}", _keyId);
    }

    private async Task<ServerSigningKeyData?> LoadKeyAsync()
    {
        try
        {
            if (!File.Exists(_keyPath))
                return null;

            var json = await File.ReadAllTextAsync(_keyPath);
            return JsonSerializer.Deserialize<ServerSigningKeyData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load server signing key from {Path}", _keyPath);
            return null;
        }
    }

    private async Task SaveKeyAsync(ServerSigningKeyData key)
    {
        try
        {
            var directory = Path.GetDirectoryName(_keyPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(key, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_keyPath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save server signing key to {Path}", _keyPath);
            throw;
        }
    }
}
