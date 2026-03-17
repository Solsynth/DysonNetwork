using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Wallet.Payment.PaymentHandlers;

public class AppleStorePaymentHandler(
    ILogger<AppleStorePaymentHandler> logger,
    IConfiguration configuration
)
{
    private readonly ILogger<AppleStorePaymentHandler> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> TransactionGrantingNotificationTypes =
    [
        "SUBSCRIBED",
        "DID_RENEW",
        "DID_RECOVER",
        "OFFER_REDEEMED",
        "RENEWAL_EXTENDED",
        "RENEWAL_EXTENSION"
    ];

    public AppleAppStoreTransaction ParseSignedTransaction(string signedTransactionInfo)
    {
        if (string.IsNullOrWhiteSpace(signedTransactionInfo))
            throw new ArgumentException("Signed transaction info is required.", nameof(signedTransactionInfo));

        var transaction = VerifyAndDecodeJws<AppleSignedTransactionPayload>(signedTransactionInfo).Payload;
        ValidateBundleId(transaction.BundleId);
        ValidateEnvironment(transaction.Environment);

        return new AppleAppStoreTransaction(transaction, signedTransactionInfo);
    }

    public async Task<AppleStoreWebhookResponse> HandleWebhook(
        HttpRequest request,
        Func<AppleAppStoreTransaction, Task>? processTransactionAction,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Received webhook request from Apple App Store...");

        string rawBody;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("Apple webhook request body is empty");
            return AppleStoreWebhookResponse.Invalid;
        }

        AppleWebhookRequest? webhookRequest;
        try
        {
            webhookRequest = JsonSerializer.Deserialize<AppleWebhookRequest>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Apple webhook request");
            return AppleStoreWebhookResponse.Invalid;
        }

        if (string.IsNullOrWhiteSpace(webhookRequest?.SignedPayload))
        {
            _logger.LogWarning("Apple webhook payload missing signedPayload");
            return AppleStoreWebhookResponse.Invalid;
        }

        try
        {
            var notification = VerifyAndDecodeJws<AppleNotificationPayload>(webhookRequest.SignedPayload).Payload;
            ValidateBundleId(notification.Data?.BundleId);
            ValidateEnvironment(notification.Data?.Environment);

            var signedTransactionInfo = notification.Data?.SignedTransactionInfo;
            if (string.IsNullOrWhiteSpace(signedTransactionInfo))
            {
                _logger.LogInformation(
                    "Ignoring Apple notification {NotificationType}; no signed transaction info was attached",
                    notification.NotificationType
                );
                return AppleStoreWebhookResponse.Success;
            }

            if (!ShouldProcessNotification(notification.NotificationType))
            {
                _logger.LogInformation(
                    "Ignoring unsupported Apple notification type {NotificationType}",
                    notification.NotificationType
                );
                return AppleStoreWebhookResponse.Success;
            }

            var transaction = ParseSignedTransaction(signedTransactionInfo);
            if (processTransactionAction is not null)
                await processTransactionAction(transaction);

            return AppleStoreWebhookResponse.Success;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or InvalidOperationException or ArgumentException)
        {
            _logger.LogError(ex, "Failed to validate Apple webhook payload");
            return AppleStoreWebhookResponse.Invalid;
        }
    }

    private bool ShouldProcessNotification(string? notificationType)
    {
        if (string.IsNullOrWhiteSpace(notificationType))
            return false;

        return TransactionGrantingNotificationTypes.Contains(notificationType.Trim().ToUpperInvariant());
    }

    private VerifiedJwsPayload<T> VerifyAndDecodeJws<T>(string compactJws)
    {
        var parts = compactJws.Split('.');
        if (parts.Length != 3)
            throw new InvalidOperationException("Apple signed payload must be a compact JWS with three parts.");

        var headerBytes = DecodeBase64Url(parts[0]);
        var payloadBytes = DecodeBase64Url(parts[1]);
        var signatureBytes = DecodeBase64Url(parts[2]);

        var header = JsonSerializer.Deserialize<AppleJwsHeader>(headerBytes, JsonOptions)
            ?? throw new InvalidOperationException("Apple JWS header could not be parsed.");
        if (!string.Equals(header.Alg, "ES256", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported Apple JWS alg {header.Alg}.");
        if (header.X5c is null || header.X5c.Count == 0)
            throw new InvalidOperationException("Apple JWS header missing x5c certificate chain.");

        var payload = JsonSerializer.Deserialize<T>(payloadBytes, JsonOptions)
            ?? throw new InvalidOperationException("Apple JWS payload could not be parsed.");
        var signedAt = ResolveSignedAt(payloadBytes);

        using var leafCertificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(header.X5c[0]));
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.VerificationTime = signedAt.ToDateTimeUtc();

        foreach (var certificate in header.X5c.Skip(1))
        {
            chain.ChainPolicy.ExtraStore.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificate)));
        }

        if (!chain.Build(leafCertificate))
        {
            var statuses = string.Join(", ", chain.ChainStatus.Select(x => x.StatusInformation.Trim()));
            throw new CryptographicException($"Apple certificate chain validation failed: {statuses}");
        }

        if (signedAt < Instant.FromDateTimeUtc(leafCertificate.NotBefore.ToUniversalTime()) ||
            signedAt > Instant.FromDateTimeUtc(leafCertificate.NotAfter.ToUniversalTime()))
        {
            throw new CryptographicException("Apple signing certificate was not valid at the signed date.");
        }

        using var ecdsa = leafCertificate.GetECDsaPublicKey()
            ?? throw new CryptographicException("Apple signing certificate does not contain an ECDSA public key.");

        var signedContent = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var isValidSignature = ecdsa.VerifyData(
            signedContent,
            signatureBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
        if (!isValidSignature)
            throw new CryptographicException("Apple JWS signature verification failed.");

        return new VerifiedJwsPayload<T>(payload, signedAt);
    }

    private Instant ResolveSignedAt(byte[] payloadBytes)
    {
        using var document = JsonDocument.Parse(payloadBytes);
        if (!document.RootElement.TryGetProperty("signedDate", out var signedDateElement))
            return SystemClock.Instance.GetCurrentInstant();

        var epochMilliseconds = ParseAppleUnixMilliseconds(signedDateElement);
        return Instant.FromUnixTimeMilliseconds(epochMilliseconds);
    }

    private void ValidateBundleId(string? bundleId)
    {
        var configuredBundleId = _configuration["Payment:Auth:AppleStore:BundleId"];
        if (string.IsNullOrWhiteSpace(configuredBundleId) || string.IsNullOrWhiteSpace(bundleId))
            return;

        if (!string.Equals(configuredBundleId, bundleId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Apple bundle id mismatch. Expected {configuredBundleId}, got {bundleId}.");
    }

    private void ValidateEnvironment(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment))
            return;

        var normalizedActual = environment.Trim().ToUpperInvariant();
        var acceptedEnvironments = GetAcceptedEnvironments();
        if (acceptedEnvironments.Contains(normalizedActual))
            return;
        if (normalizedActual == "PRODUCTION" && acceptedEnvironments.Contains("PROD"))
            return;

        throw new InvalidOperationException(
            $"Apple environment mismatch. Accepted environments: {string.Join(", ", acceptedEnvironments)}, got {environment}."
        );
    }

    private HashSet<string> GetAcceptedEnvironments()
    {
        var configuredEnvironment = _configuration["Payment:Auth:AppleStore:Environment"];
        if (string.IsNullOrWhiteSpace(configuredEnvironment))
            return ["SANDBOX", "PROD"];

        var values = configuredEnvironment
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToUpperInvariant())
            .ToList();
        if (values.Count == 0)
            return ["SANDBOX", "PROD"];
        if (values.Contains("BOTH") || values.Contains("ALL") || values.Contains("ANY"))
            return ["SANDBOX", "PROD"];

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            normalized.Add(value == "PRODUCTION" ? "PROD" : value);
        }

        return normalized.Count == 0 ? ["SANDBOX", "PROD"] : normalized;
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        switch (normalized.Length % 4)
        {
            case 2:
                normalized += "==";
                break;
            case 3:
                normalized += "=";
                break;
        }

        return Convert.FromBase64String(normalized);
    }

    private static long ParseAppleUnixMilliseconds(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetInt64(),
            JsonValueKind.String when long.TryParse(element.GetString(), out var parsed) => parsed,
            _ => throw new InvalidOperationException("Apple timestamp must be expressed as unix milliseconds.")
        };
    }
}

public sealed class AppleAppStoreTransaction : ISubscriptionOrder
{
    public AppleAppStoreTransaction(AppleSignedTransactionPayload payload, string signedTransactionInfo)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        SignedTransactionInfo = signedTransactionInfo ?? throw new ArgumentNullException(nameof(signedTransactionInfo));
    }

    [JsonIgnore] public AppleSignedTransactionPayload Payload { get; }

    [JsonIgnore] public string SignedTransactionInfo { get; }

    [JsonIgnore] public string Id => Payload.TransactionId ?? Payload.OriginalTransactionId ?? string.Empty;

    [JsonIgnore] public string SubscriptionId => Payload.ProductId ?? string.Empty;

    [JsonIgnore] public Instant BegunAt => Instant.FromUnixTimeMilliseconds(Payload.PurchaseDate ?? Payload.ExpiresDate ?? 0);

    [JsonIgnore]
    public Duration Duration
    {
        get
        {
            if (Payload.PurchaseDate.HasValue && Payload.ExpiresDate.HasValue && Payload.ExpiresDate > Payload.PurchaseDate)
                return Duration.FromMilliseconds(Payload.ExpiresDate.Value - Payload.PurchaseDate.Value);

            return Duration.FromDays(30);
        }
    }

    [JsonIgnore] public string Provider => SubscriptionPaymentMethod.AppleStore;

    [JsonIgnore] public string AccountId => Payload.AppAccountToken ?? string.Empty;
}

public class AppleStoreWebhookResponse
{
    public bool IsSuccess { get; set; }

    public static AppleStoreWebhookResponse Invalid => new() { IsSuccess = false };

    public static AppleStoreWebhookResponse Success => new() { IsSuccess = true };
}

public class AppleWebhookRequest
{
    [JsonPropertyName("signedPayload")] public string SignedPayload { get; set; } = null!;
}

public class AppleJwsHeader
{
    [JsonPropertyName("alg")] public string Alg { get; set; } = null!;

    [JsonPropertyName("x5c")] public List<string> X5c { get; set; } = [];
}

public class AppleNotificationPayload
{
    [JsonPropertyName("notificationType")] public string? NotificationType { get; set; }

    [JsonPropertyName("subtype")] public string? Subtype { get; set; }

    [JsonPropertyName("data")] public AppleNotificationData? Data { get; set; }
}

public class AppleNotificationData
{
    [JsonPropertyName("appAppleId")] public long? AppAppleId { get; set; }

    [JsonPropertyName("bundleId")] public string? BundleId { get; set; }

    [JsonPropertyName("bundleVersion")] public string? BundleVersion { get; set; }

    [JsonPropertyName("environment")] public string? Environment { get; set; }

    [JsonPropertyName("signedRenewalInfo")] public string? SignedRenewalInfo { get; set; }

    [JsonPropertyName("signedTransactionInfo")] public string? SignedTransactionInfo { get; set; }
}

public class AppleSignedTransactionPayload
{
    [JsonPropertyName("transactionId")] public string? TransactionId { get; set; }

    [JsonPropertyName("originalTransactionId")] public string? OriginalTransactionId { get; set; }

    [JsonPropertyName("webOrderLineItemId")] public string? WebOrderLineItemId { get; set; }

    [JsonPropertyName("bundleId")] public string? BundleId { get; set; }

    [JsonPropertyName("productId")] public string? ProductId { get; set; }

    [JsonPropertyName("subscriptionGroupIdentifier")] public string? SubscriptionGroupIdentifier { get; set; }

    [JsonPropertyName("purchaseDate")] public long? PurchaseDate { get; set; }

    [JsonPropertyName("originalPurchaseDate")] public long? OriginalPurchaseDate { get; set; }

    [JsonPropertyName("expiresDate")] public long? ExpiresDate { get; set; }

    [JsonPropertyName("signedDate")] public long? SignedDate { get; set; }

    [JsonPropertyName("environment")] public string? Environment { get; set; }

    [JsonPropertyName("appAccountToken")] public string? AppAccountToken { get; set; }

    [JsonPropertyName("offerType")] public int? OfferType { get; set; }

    [JsonPropertyName("offerIdentifier")] public string? OfferIdentifier { get; set; }

    [JsonPropertyName("inAppOwnershipType")] public string? InAppOwnershipType { get; set; }

    [JsonPropertyName("type")] public string? Type { get; set; }

    [JsonPropertyName("revocationDate")] public long? RevocationDate { get; set; }

    [JsonPropertyName("revocationReason")] public int? RevocationReason { get; set; }
}

public sealed record VerifiedJwsPayload<T>(T Payload, Instant SignedAt);
