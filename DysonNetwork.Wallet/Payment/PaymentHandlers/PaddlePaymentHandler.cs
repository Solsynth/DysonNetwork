using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Models;
using NodaTime;

namespace DysonNetwork.Wallet.Payment.PaymentHandlers;

public class PaddlePaymentHandler(
    IHttpClientFactory httpClientFactory,
    ILogger<PaddlePaymentHandler> logger,
    IConfiguration configuration
)
{
    private const string LiveBaseApiUrl = "https://api.paddle.com";
    private const string SandboxBaseApiUrl = "https://sandbox-api.paddle.com";

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    private readonly ILogger<PaddlePaymentHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private readonly IConfiguration _configuration =
        configuration ?? throw new ArgumentNullException(nameof(configuration));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public async Task<PaddleTransaction?> GetTransactionAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transactionId))
        {
            _logger.LogWarning("Transaction ID cannot be null or empty");
            return null;
        }

        var apiKey = _configuration["Payment:Auth:Paddle:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Payment:Auth:Paddle:ApiKey is not configured.");

        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{GetBaseApiUrl(apiKey)}/transactions/{transactionId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Add("Accept", "application/json");

        var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Failed to fetch Paddle transaction {TransactionId}: {StatusCode} {Body}",
                transactionId,
                response.StatusCode,
                await response.Content.ReadAsStringAsync(cancellationToken)
            );
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<PaddleEntityResponse<PaddleTransaction>>(stream, JsonOptions, cancellationToken);
        return result?.Data;
    }

    public async Task<PaddleWebhookResponse> HandleWebhook(
        HttpRequest request,
        Func<PaddleTransaction, Task>? processTransactionAction,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Received webhook request from Paddle...");

        var signatureHeader = request.Headers["Paddle-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            _logger.LogWarning("Missing Paddle-Signature header");
            return PaddleWebhookResponse.Invalid;
        }

        string rawBody;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true))
        {
            rawBody = await reader.ReadToEndAsync(cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            _logger.LogWarning("Paddle webhook request body is empty");
            return PaddleWebhookResponse.Invalid;
        }

        if (!VerifySignature(signatureHeader, rawBody))
        {
            _logger.LogWarning("Paddle webhook signature verification failed");
            return PaddleWebhookResponse.Invalid;
        }

        PaddleWebhookEvent? webhook;
        try
        {
            webhook = JsonSerializer.Deserialize<PaddleWebhookEvent>(rawBody, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Paddle webhook");
            return PaddleWebhookResponse.Invalid;
        }

        if (webhook?.Data is null)
        {
            _logger.LogWarning("Paddle webhook payload missing data");
            return PaddleWebhookResponse.Invalid;
        }

        if (!string.Equals(webhook.EventType, "transaction.completed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Ignoring unsupported Paddle event type {EventType}", webhook.EventType);
            return PaddleWebhookResponse.Success;
        }

        if (!string.Equals(webhook.Data.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Ignoring Paddle transaction {TransactionId} with status {Status}",
                webhook.Data.Id,
                webhook.Data.Status
            );
            return PaddleWebhookResponse.Success;
        }

        if (processTransactionAction != null)
            await processTransactionAction(webhook.Data);

        return PaddleWebhookResponse.Success;
    }

    public string? GetSubscriptionPlanId(string priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
            return null;

        var planId = _configuration[$"Payment:Subscriptions:Paddle:{priceId}"];
        if (!string.IsNullOrWhiteSpace(planId))
            return planId;

        _logger.LogWarning("Unknown Paddle price id: {PriceId}", priceId);
        return null;
    }

    private bool VerifySignature(string signatureHeader, string rawBody)
    {
        var secret = _configuration["Payment:Auth:Paddle:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Payment:Auth:Paddle:WebhookSecret is not configured.");

        var pairs = signatureHeader
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(part => part.Length == 2)
            .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);

        if (!pairs.TryGetValue("ts", out var ts) || !pairs.TryGetValue("h1", out var h1))
            return false;

        if (!long.TryParse(ts, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timestamp))
            return false;

        var currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(currentTimestamp - timestamp) > 30)
        {
            _logger.LogWarning("Paddle webhook timestamp outside tolerance window: {Timestamp}", timestamp);
            return false;
        }

        var signedPayload = $"{ts}:{rawBody}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computedHash = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(h1.ToLowerInvariant())
        );
    }

    private string GetBaseApiUrl(string apiKey)
    {
        var configuredEnvironment = _configuration["Payment:Auth:Paddle:Environment"];
        if (!string.IsNullOrWhiteSpace(configuredEnvironment))
        {
            return configuredEnvironment.Trim().ToLowerInvariant() switch
            {
                "sandbox" => SandboxBaseApiUrl,
                "live" => LiveBaseApiUrl,
                "production" => LiveBaseApiUrl,
                _ => throw new InvalidOperationException(
                    "Payment:Auth:Paddle:Environment must be 'sandbox', 'live', or 'production'."
                )
            };
        }

        if (apiKey.Contains("_sdbx_", StringComparison.OrdinalIgnoreCase))
            return SandboxBaseApiUrl;

        return LiveBaseApiUrl;
    }
}

public class PaddleEntityResponse<T>
{
    [JsonPropertyName("data")] public T Data { get; set; } = default!;
}

public class PaddleWebhookEvent
{
    [JsonPropertyName("event_id")] public string EventId { get; set; } = null!;

    [JsonPropertyName("event_type")] public string EventType { get; set; } = null!;

    [JsonPropertyName("occurred_at")] public DateTimeOffset OccurredAt { get; set; }

    [JsonPropertyName("notification_id")] public string NotificationId { get; set; } = null!;

    [JsonPropertyName("data")] public PaddleTransaction Data { get; set; } = null!;
}

public class PaddleTransaction : ISubscriptionOrder
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;

    [JsonPropertyName("status")] public string Status { get; set; } = null!;

    [JsonPropertyName("subscription_id")] public string? PaddleSubscriptionId { get; set; }

    [JsonPropertyName("custom_data")] public Dictionary<string, JsonElement>? CustomData { get; set; }

    [JsonPropertyName("items")] public List<PaddleTransactionItem> Items { get; set; } = [];

    [JsonPropertyName("billed_at")] public DateTimeOffset? BilledAt { get; set; }

    [JsonPropertyName("created_at")] public DateTimeOffset CreatedAt { get; set; }

    [JsonIgnore]
    public string SubscriptionId
    {
        get
        {
            var customSubscriptionIdentifier = GetCustomDataString("subscription_identifier");
            if (!string.IsNullOrWhiteSpace(customSubscriptionIdentifier))
                return customSubscriptionIdentifier;

            var customPriceId = GetCustomDataString("price_id");
            if (!string.IsNullOrWhiteSpace(customPriceId))
                return customPriceId;

            return Items
                .Select(i => i.Price?.Id)
                .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id))
                ?? PaddleSubscriptionId
                ?? string.Empty;
        }
    }

    [JsonIgnore]
    public Instant BegunAt => Instant.FromDateTimeOffset(BilledAt ?? CreatedAt);

    [JsonIgnore]
    public Duration Duration
    {
        get
        {
            var interval = Items
                .Select(i => i.Price?.BillingCycle)
                .FirstOrDefault(cycle => cycle is not null);
            return interval?.ToDuration() ?? Duration.FromDays(30);
        }
    }

    [JsonIgnore]
    public string Provider => SubscriptionPaymentMethod.Paddle;

    [JsonIgnore]
    public string AccountId =>
        GetCustomDataString("account_id")
        ?? GetCustomDataString("user_id")
        ?? string.Empty;

    private string? GetCustomDataString(string key)
    {
        if (CustomData is null || !CustomData.TryGetValue(key, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => value.ToString()
        };
    }
}

public class PaddleTransactionItem
{
    [JsonPropertyName("price")] public PaddlePrice? Price { get; set; }
}

public class PaddlePrice
{
    [JsonPropertyName("id")] public string Id { get; set; } = null!;

    [JsonPropertyName("billing_cycle")] public PaddleBillingCycle? BillingCycle { get; set; }
}

public class PaddleBillingCycle
{
    [JsonPropertyName("interval")] public string Interval { get; set; } = null!;

    [JsonPropertyName("frequency")] public int Frequency { get; set; }

    public Duration ToDuration() => Interval.ToLowerInvariant() switch
    {
        "day" => Duration.FromDays(Frequency),
        "week" => Duration.FromDays(Frequency * 7),
        "month" => Duration.FromDays(Frequency * 30),
        "year" => Duration.FromDays(Frequency * 365),
        _ => Duration.FromDays(30)
    };
}

public class PaddleWebhookResponse
{
    public bool IsSuccess { get; set; }

    public static PaddleWebhookResponse Invalid => new() { IsSuccess = false };

    public static PaddleWebhookResponse Success => new() { IsSuccess = true };
}
