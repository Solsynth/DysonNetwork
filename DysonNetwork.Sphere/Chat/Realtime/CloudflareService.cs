using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Chat.Realtime;

public class CloudflareRealtimeService : IRealtimeService
{
    private readonly AppDatabase _db;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private RSA? _publicKey;

    public CloudflareRealtimeService(
        AppDatabase db,
        HttpClient httpClient,
        IConfiguration configuration
    )
    {
        _db = db;
        _httpClient = httpClient;
        _configuration = configuration;
        var apiKey = _configuration["Realtime:Cloudflare:ApiKey"];
        var apiSecret = _configuration["Realtime:Cloudflare:ApiSecret"]!;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:{apiSecret}"));
        _httpClient.BaseAddress = new Uri("https://rtk.realtime.cloudflare.com/v2/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public string ProviderName => "Cloudflare";

    public async Task<RealtimeSessionConfig> CreateSessionAsync(Guid roomId, Dictionary<string, object> metadata)
    {
        var roomName = $"Room Call #{roomId.ToString().Replace("-", "")}";
        var requestBody = new
        {
            title = roomName,
            preferred_region = _configuration["Realtime:Cloudflare:PreferredRegion"],
            data = metadata
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("meetings", content);

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync();
        var meetingResponse = JsonSerializer.Deserialize<CfMeetingResponse>(responseContent);
        if (meetingResponse is null) throw new Exception("Failed to create meeting with cloudflare");

        return new RealtimeSessionConfig
        {
            SessionId = meetingResponse.Data.Id,
            Parameters = new Dictionary<string, object>
            {
                { "meetingId", meetingResponse.Data.Id }
            }
        };
    }

    public async Task EndSessionAsync(string sessionId, RealtimeSessionConfig config)
    {
        var requestBody = new
        {
            status = "INACTIVE"
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"sessions/{sessionId}", content);

        response.EnsureSuccessStatusCode();
    }

    public string GetUserToken(Account.Account account, string sessionId, bool isAdmin = false)
    {
        return GetUserTokenAsync(account, sessionId, isAdmin).GetAwaiter().GetResult();
    }

    public async Task<string> GetUserTokenAsync(Account.Account account, string sessionId, bool isAdmin = false)
    {
        try
        {
            // First try to get the participant by their custom ID
            var participantCheckResponse = await _httpClient
                .GetAsync($"meetings/{sessionId}/participants/{account.Id}");

            if (participantCheckResponse.IsSuccessStatusCode)
            {
                // Participant exists, get a new token
                var tokenResponse = await _httpClient
                    .PostAsync($"meetings/{sessionId}/participants/{account.Id}/token", null);
                tokenResponse.EnsureSuccessStatusCode();
                var tokenContent = await tokenResponse.Content.ReadAsStringAsync();
                var tokenData = JsonSerializer.Deserialize<CfResponse<CfTokenResponse>>(tokenContent);
                if (tokenData == null || !tokenData.Success)
                {
                    throw new Exception("Failed to get participant token");
                }

                return tokenData.Data?.Token ?? throw new Exception("Token is null");
            }

            // Participant doesn't exist, create a new one
            var requestBody = new
            {
                name = "@" + account.Name,
                preset_name = isAdmin ? "group_call_host" : "group_call_participant",
                custom_user_id = account.Id.ToString()
            };

            var content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json"
            );

            var createResponse = await _httpClient.PostAsync($"meetings/{sessionId}/participants", content);
            createResponse.EnsureSuccessStatusCode();

            var responseContent = await createResponse.Content.ReadAsStringAsync();
            var participantData = JsonSerializer.Deserialize<CfResponse<CfParticipantResponse>>(responseContent);
            if (participantData == null || !participantData.Success)
            {
                throw new Exception("Failed to create participant");
            }

            return participantData.Data?.Token ?? throw new Exception("Token is null");
        }
        catch (Exception ex)
        {
            // Log the error or handle it appropriately
            throw new Exception($"Failed to get or create participant: {ex.Message}", ex);
        }
    }

    public async Task ReceiveWebhook(string body, string authHeader)
    {
        if (string.IsNullOrEmpty(authHeader))
        {
            throw new ArgumentException("Auth header is missing");
        }

        if (_publicKey == null)
        {
            await GetPublicKeyAsync();
        }

        var signature = authHeader.Replace("Signature ", "");
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var signatureBytes = Convert.FromBase64String(signature);

        if (!(_publicKey?.VerifyData(bodyBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1) ??
              false))
        {
            throw new SecurityTokenException("Webhook signature validation failed");
        }

        // Process the webhook event
        var evt = JsonSerializer.Deserialize<CfWebhookEvent>(body);
        if (evt is null) return;

        switch (evt.Type)
        {
            case "meeting.ended":
                var now = SystemClock.Instance.GetCurrentInstant();
                await _db.ChatRealtimeCall
                    .Where(c => c.SessionId == evt.Event.Meeting.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.EndedAt, now)
                    );
                break;
        }
    }

    private class WebhooksConfig
    {
        [JsonPropertyName("keys")] public List<WebhookKey> Keys { get; set; } = new List<WebhookKey>();
    }

    private class WebhookKey
    {
        [JsonPropertyName("publicKeyPem")] public string PublicKeyPem { get; set; } = string.Empty;
    }

    private async Task GetPublicKeyAsync()
    {
        var response = await _httpClient.GetAsync("https://rtk.realtime.cloudflare.com/.well-known/webhooks.json");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        var webhooksConfig = JsonSerializer.Deserialize<WebhooksConfig>(content);
        var publicKeyPem = webhooksConfig?.Keys.FirstOrDefault()?.PublicKeyPem;

        if (string.IsNullOrEmpty(publicKeyPem))
        {
            throw new InvalidOperationException("Public key not found in webhooks configuration.");
        }

        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(publicKeyPem);
    }

    private class CfMeetingResponse
    {
        [JsonPropertyName("data")] public CfMeetingData Data { get; set; } = new();
    }

    private class CfMeetingData
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("roomName")] public string RoomName { get; set; } = string.Empty;
        [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    private class CfParticipant
    {
        public string Id { get; set; } = string.Empty;
        public string Token { get; set; } = string.Empty;
        public string CustomParticipantId { get; set; } = string.Empty;
    }

    public class CfResponse<T>
    {
        [JsonPropertyName("success")] public bool Success { get; set; }

        [JsonPropertyName("data")] public T? Data { get; set; }
    }

    public class CfTokenResponse
    {
        [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
    }

    public class CfParticipantResponse
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

        [JsonPropertyName("customUserId")] public string CustomUserId { get; set; } = string.Empty;

        [JsonPropertyName("presetName")] public string PresetName { get; set; } = string.Empty;

        [JsonPropertyName("isActive")] public bool IsActive { get; set; }

        [JsonPropertyName("token")] public string Token { get; set; } = string.Empty;
    }

    public class CfWebhookEvent
    {
        [JsonPropertyName("id")] public string Id { get; set; }

        [JsonPropertyName("type")] public string Type { get; set; }

        [JsonPropertyName("webhookId")] public string WebhookId { get; set; }

        [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }

        [JsonPropertyName("event")] public EventData Event { get; set; }
    }

    public class EventData
    {
        [JsonPropertyName("meeting")] public MeetingData Meeting { get; set; }

        [JsonPropertyName("participant")] public ParticipantData Participant { get; set; }
    }

    public class MeetingData
    {
        [JsonPropertyName("id")] public string Id { get; set; }

        [JsonPropertyName("roomName")] public string RoomName { get; set; }

        [JsonPropertyName("title")] public string Title { get; set; }

        [JsonPropertyName("status")] public string Status { get; set; }

        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }

        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    public class ParticipantData
    {
        [JsonPropertyName("id")] public string Id { get; set; }

        [JsonPropertyName("userId")] public string UserId { get; set; }

        [JsonPropertyName("customParticipantId")]
        public string CustomParticipantId { get; set; }

        [JsonPropertyName("presetName")] public string PresetName { get; set; }

        [JsonPropertyName("joinedAt")] public DateTime JoinedAt { get; set; }

        [JsonPropertyName("leftAt")] public DateTime? LeftAt { get; set; }
    }
}