using System.Text;
using System.Text.RegularExpressions;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace DysonNetwork.Messager.Chat;

public interface ISendMessageRequest
{
    string? Content { get; }
    string? Nonce { get; }
    string? ClientMessageId { get; }
    Guid? FundId { get; }
    Guid? PollId { get; }
    Guid? MeetId { get; }
    string? LocationName { get; }
    string? LocationAddress { get; }
    string? LocationWkt { get; }
    List<string>? AttachmentsId { get; }
    Dictionary<string, object>? Meta { get; }
    Guid? RepliedMessageId { get; }
    Guid? ForwardedMessageId { get; }
    bool IsEncrypted { get; }
    byte[]? Ciphertext { get; }
    byte[]? EncryptionHeader { get; }
    byte[]? EncryptionSignature { get; }
    string? EncryptionScheme { get; }
    long? EncryptionEpoch { get; }
    string? EncryptionMessageType { get; }
}

public static class ChatMessageHelpers
{
    public const string MlsEncryptionScheme = "chat.mls.v2";

    public static bool HasEncryptedPayload(ISendMessageRequest request)
    {
        return request.Ciphertext is { Length: > 0 } &&
               !string.IsNullOrWhiteSpace(request.EncryptionScheme) &&
               !string.IsNullOrWhiteSpace(request.EncryptionMessageType);
    }

    public static bool IsMlsPayloadValid(ISendMessageRequest request)
    {
        var schemeOk = string.Equals(request.EncryptionScheme, MlsEncryptionScheme, StringComparison.Ordinal);
        return schemeOk && request.EncryptionEpoch.HasValue;
    }

    public static string NormalizeEncryptionMessageType(string? messageType, string fallbackType)
    {
        if (string.IsNullOrWhiteSpace(messageType)) return fallbackType;
        return messageType switch
        {
            "content.new" => "text",
            "content.edit" => "messages.update",
            "content.delete" => "messages.delete",
            _ => messageType
        };
    }

    public static bool HasLocationPayload(string? locationName, string? locationAddress, string? locationWkt)
    {
        return !string.IsNullOrWhiteSpace(locationName)
            || !string.IsNullOrWhiteSpace(locationAddress)
            || !string.IsNullOrWhiteSpace(locationWkt);
    }

    public static LocationEmbed CreateLocationEmbed(
        string? locationName,
        string? locationAddress,
        Geometry? location
    )
    {
        return new LocationEmbed
        {
            Name = string.IsNullOrWhiteSpace(locationName) ? null : locationName,
            Address = string.IsNullOrWhiteSpace(locationAddress) ? null : locationAddress,
            Wkt = location?.AsText()
        };
    }

    public static bool TryParseLocation(string? locationWkt, out Geometry? location, out string? error)
    {
        location = null;
        error = null;
        if (string.IsNullOrWhiteSpace(locationWkt))
            return true;

        try
        {
            location = new WKTReader().Read(locationWkt);
            location.SRID = 4326;
            return true;
        }
        catch (Exception)
        {
            error = "Invalid location WKT.";
            return false;
        }
    }

    public static bool LooksLikePlaintextJson(byte[]? payload)
    {
        if (payload is not { Length: > 1 }) return false;
        var text = Encoding.UTF8.GetString(payload).Trim();
        if (!(text.StartsWith("{") && text.EndsWith("}"))) return false;
        return text.Contains("\"content\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("\"attachments_id\"", StringComparison.OrdinalIgnoreCase)
               || text.Contains("\"nonce\"", StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasMeetOrLocationPayload(ISendMessageRequest request)
    {
        return request.MeetId.HasValue ||
               HasLocationPayload(request.LocationName, request.LocationAddress, request.LocationWkt);
    }

    public static bool IsEmptyMessage(ISendMessageRequest request)
    {
        return string.IsNullOrWhiteSpace(request.Content) &&
               (request.AttachmentsId == null || request.AttachmentsId.Count == 0) &&
               !request.FundId.HasValue &&
               !request.MeetId.HasValue &&
               !request.PollId.HasValue &&
               !HasLocationPayload(request.LocationName, request.LocationAddress, request.LocationWkt);
    }

    public static bool HasPlaintextFields(ISendMessageRequest request)
    {
        return !string.IsNullOrWhiteSpace(request.Content) ||
               request.FundId.HasValue ||
               request.MeetId.HasValue ||
               request.PollId.HasValue ||
               HasLocationPayload(request.LocationName, request.LocationAddress, request.LocationWkt);
    }

    public static void AddEmbedToMessage(SnChatMessage message, EmbeddableBase embed)
    {
        message.Meta ??= new Dictionary<string, object>();
        if (!message.Meta.TryGetValue("embeds", out var existingEmbeds)
            || existingEmbeds is not List<EmbeddableBase>)
            message.Meta["embeds"] = new List<Dictionary<string, object>>();

        var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
        embeds.Add(EmbeddableBase.ToDictionary(embed));
        message.Meta["embeds"] = embeds;
    }

    public static void RemoveEmbedFromMessage(SnChatMessage message, string embedType)
    {
        message.Meta ??= new Dictionary<string, object>();
        if (!message.Meta.TryGetValue("embeds", out var existingEmbeds)
            || existingEmbeds is not List<EmbeddableBase>)
            message.Meta["embeds"] = new List<Dictionary<string, object>>();

        var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];
        embeds.RemoveAll(e => e.TryGetValue("type", out var type) && type.ToString() == embedType);
        message.Meta["embeds"] = embeds;
    }

    public static async Task<List<Guid>> ExtractMentionedUsersAsync(
        string? content,
        Guid? repliedMessageId,
        Guid? forwardedMessageId,
        Guid roomId,
        Guid? excludeSenderId,
        AppDatabase db,
        DyAccountService.DyAccountServiceClient accounts
    )
    {
        var mentionedUsers = new List<Guid>();

        if (repliedMessageId.HasValue)
        {
            var replyingTo = await db.ChatMessages
                .Where(m => m.Id == repliedMessageId.Value && m.ChatRoomId == roomId)
                .Include(m => m.Sender)
                .Select(m => m.Sender)
                .FirstOrDefaultAsync();
            if (replyingTo != null)
                mentionedUsers.Add(replyingTo.AccountId);
        }

        if (forwardedMessageId.HasValue)
        {
            var forwardedMessage = await db.ChatMessages
                .Where(m => m.Id == forwardedMessageId.Value)
                .Select(m => new { m.SenderId })
                .FirstOrDefaultAsync();
            if (forwardedMessage != null)
                mentionedUsers.Add(forwardedMessage.SenderId);
        }

        if (string.IsNullOrWhiteSpace(content)) return mentionedUsers.Distinct().ToList();

        var mentionRegex = new Regex(@"@(?:u/)?([A-Za-z0-9_-]+)");
        var mentionedNames = mentionRegex
            .Matches(content)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        if (mentionedNames.Count > 0)
        {
            var queryRequest = new DyLookupAccountBatchRequest();
            queryRequest.Names.AddRange(mentionedNames);
            var queryResponse = (await accounts.LookupAccountBatchAsync(queryRequest)).Accounts;
            var mentionedIds = queryResponse.Select(a => Guid.Parse(a.Id)).ToList();

            if (mentionedIds.Count > 0)
            {
                var mentionedMembers = await db.ChatMembers
                    .Where(m => m.ChatRoomId == roomId && mentionedIds.Contains(m.AccountId))
                    .Where(m => m.JoinedAt != null && m.LeaveAt == null)
                    .Where(m => excludeSenderId == null || m.AccountId != excludeSenderId.Value)
                    .Select(m => m.AccountId)
                    .ToListAsync();
                mentionedUsers.AddRange(mentionedMembers);
            }
        }

        return mentionedUsers.Distinct().ToList();
    }
}
