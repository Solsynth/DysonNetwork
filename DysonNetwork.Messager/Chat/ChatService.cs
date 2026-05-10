using System.Text.Json;
using System.Text.RegularExpressions;
using DysonNetwork.Messager.Chat.Realtime;
using DysonNetwork.Messager.Chat.Voice;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Extensions;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Models.Embed;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Messager.Chat;

public partial class ChatService(
    AppDatabase db,
    ChatRoomService crs,
    IServiceScopeFactory scopeFactory,
    IRealtimeService realtime,
    ChatVoiceService voice,
    ILogger<ChatService> logger,
    RemoteWebReaderService webReader,
    IEventBus eventBus,
    ICacheService cache,
    RemoteActionLogService actionLogs,
    IHttpContextAccessor httpContextAccessor,
    ILocalizationService localization,
    LazyGrpcClientFactory<DyStickerService.DyStickerServiceClient> stickerClientFactory
)
{
    private const string ChatUseCooldownCacheKey = "actionlog:chat.use:";
    private static readonly TimeSpan ChatUseCooldown = TimeSpan.FromMinutes(1);

    private static string NormalizeEncryptionMessageType(string? messageType, string fallbackType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            return fallbackType;
        return messageType switch
        {
            "content.new" => "text",
            "content.edit" => "messages.update",
            "content.delete" => "messages.delete",
            _ => messageType,
        };
    }

    private static bool IsUserEncryptedMessage(SnChatMessage message)
    {
        if (!message.IsEncrypted)
            return false;
        return message.Type
                is not (
                    "messages.update"
                    or "messages.delete"
                    or "messages.update.links"
                    or WebSocketPacketType.MessageReactionAdded
                    or WebSocketPacketType.MessageReactionRemoved
                )
            && !message.Type.StartsWith("system.");
    }

    [GeneratedRegex(@"(?<!\]\()https?://[^\s<]+", RegexOptions.IgnoreCase)]
    private static partial Regex GetLinkRegex();

    [GeneratedRegex(@"(?<!\w)@(?:u/)?everyone\b", RegexOptions.IgnoreCase)]
    private static partial Regex GetEveryoneMentionRegex();

    [GeneratedRegex(@"^[a-z0-9._-]+\+[a-z0-9._-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex GetStickerPlaceholderRegex();

    [GeneratedRegex(@":(?<identifier>[a-z0-9._-]+\+[a-z0-9._-]+):", RegexOptions.IgnoreCase)]
    private static partial Regex GetInlineStickerPlaceholderRegex();

    private static List<string> ExtractPreviewUrls(string content, int maxLinks)
    {
        var urls = new List<string>();
        foreach (Match match in GetLinkRegex().Matches(content))
        {
            var normalizedUrl = NormalizePreviewUrl(match.Value);
            if (
                normalizedUrl is null
                || urls.Contains(normalizedUrl, StringComparer.OrdinalIgnoreCase)
            )
                continue;

            urls.Add(normalizedUrl);
            if (urls.Count >= maxLinks)
                break;
        }

        return urls;
    }

    private static string? NormalizePreviewUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return null;

        var url = rawUrl.Trim().TrimEnd('.', ',', ';', ':', '!', '?');
        while (url.Length > 0 && ")]}>\"'".Contains(url[^1]))
        {
            var trailing = url[^1];
            if (trailing == ')' && url.Count(c => c == '(') >= url.Count(c => c == ')'))
                break;

            url = url[..^1];
        }

        return
            Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.ToString()
            : null;
    }

    private static bool IsEveryoneMentioned(SnChatMessage message) =>
        !message.IsEncrypted
        && !string.IsNullOrWhiteSpace(message.Content)
        && GetEveryoneMentionRegex().IsMatch(message.Content);

    private static bool IsAccountMentioned(SnChatMessage message, Guid accountId)
    {
        if (IsEveryoneMentioned(message))
            return true;

        return message.MembersMentioned?.Contains(accountId) == true;
    }

    private static bool IsExactStickerPlaceholderMessage(SnChatMessage message)
    {
        if (
            message.IsEncrypted
            || message.Type != "text"
            || string.IsNullOrWhiteSpace(message.Content)
        )
            return false;

        return GetStickerPlaceholderRegex().IsMatch(message.Content.Trim());
    }

    private async Task NormalizeStickerPlaceholderMessageAsync(SnChatMessage message)
    {
        if (!IsExactStickerPlaceholderMessage(message))
            return;

        try
        {
            var stickerClient = stickerClientFactory.CreateClient();
            var placeholder = message.Content!.Trim();
            var stickerProto = await stickerClient.GetStickerByIdentifierAsync(
                new DyGetStickerRequest { Identifier = placeholder }
            );

            var sticker = SnSticker.FromProtoValue(stickerProto);
            message.Content = localization.Get("chatStickerBody", null);
            message.Meta ??= new Dictionary<string, object>();
            message.Meta["sticker"] = new Dictionary<string, object?>
            {
                ["id"] = sticker.Id,
                ["slug"] = sticker.Slug,
                ["name"] = sticker.Name,
                ["image_id"] = sticker.Image.Id,
                ["size"] = (int)sticker.Size,
                ["mode"] = (int)sticker.Mode,
                ["pack_id"] = sticker.PackId,
                ["pack_prefix"] = sticker.Pack?.Prefix,
                ["placeholder"] = placeholder,
            };

            if (message.Attachments.All(a => a.Id != sticker.Image.Id))
                message.Attachments.Add(sticker.Image);
        }
        catch (RpcException ex)
            when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
        {
            // Ignore invalid sticker placeholders and keep the original text message.
        }
    }

    private async Task EnrichInlineStickerPreviewAsync(SnChatMessage message)
    {
        if (
            message.IsEncrypted
            || message.Type != "text"
            || string.IsNullOrWhiteSpace(message.Content)
        )
            return;

        var matches = GetInlineStickerPlaceholderRegex()
            .Matches(message.Content)
            .Select(match => match.Groups["identifier"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
            return;

        var stickerClient = stickerClientFactory.CreateClient();
        var stickers = new Dictionary<string, SnSticker>(StringComparer.OrdinalIgnoreCase);
        foreach (var identifier in matches)
        {
            try
            {
                var stickerProto = await stickerClient.GetStickerByIdentifierAsync(
                    new DyGetStickerRequest { Identifier = identifier }
                );
                stickers[identifier] = SnSticker.FromProtoValue(stickerProto);
            }
            catch (RpcException ex)
                when (ex.StatusCode is StatusCode.NotFound or StatusCode.InvalidArgument)
            {
                // Ignore invalid sticker placeholders and leave them unchanged.
            }
        }

        if (stickers.Count == 0)
            return;

        var stickerSequence = GetInlineStickerPlaceholderRegex()
            .Matches(message.Content)
            .Select(match => match.Groups["identifier"].Value)
            .Where(identifier => stickers.ContainsKey(identifier))
            .ToList();

        var transformed = GetInlineStickerPlaceholderRegex()
            .Replace(
                message.Content,
                match =>
                {
                    var identifier = match.Groups["identifier"].Value;
                    if (
                        !stickers.TryGetValue(identifier, out var sticker)
                        || string.IsNullOrWhiteSpace(sticker.Name)
                    )
                        return match.Value;

                    return $"[{sticker.Name}]";
                }
            );

        message.Meta ??= new Dictionary<string, object>();
        message.Meta["sticker_preview_text"] = transformed;

        if (
            GetInlineStickerPlaceholderRegex().Replace(message.Content.Trim(), string.Empty).Length
                == 0
            && stickerSequence.Count > 0
        )
        {
            var stickerNames = stickerSequence
                .Select(identifier => stickers[identifier].Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            if (stickerNames.Count == stickerSequence.Count)
            {
                var firstName = stickerNames[0];
                if (
                    stickerNames.All(name =>
                        string.Equals(name, firstName, StringComparison.Ordinal)
                    )
                )
                {
                    message.Meta["sticker_only_name"] = firstName;
                    message.Meta["sticker_only_count"] = stickerNames.Count;
                }
            }
        }
    }

    private async Task EmitChatUseActionLogAsync(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string? clientIpAddress = null
    )
    {
        if (sender.AccountId == Guid.Empty || message.Type.StartsWith("system."))
            return;

        var cacheKey = $"{ChatUseCooldownCacheKey}{sender.AccountId}";
        var alreadyEmitted = await cache.GetAsync<bool?>(cacheKey);
        if (alreadyEmitted == true)
            return;

        var request = httpContextAccessor.HttpContext?.Request;
        var userAgent = request?.Headers.UserAgent.ToString();
        var ipAddress = clientIpAddress ?? request?.GetClientIpAddress();

        await cache.SetAsync(cacheKey, true, ChatUseCooldown);
        actionLogs.CreateActionLog(
            sender.AccountId,
            ActionLogType.ChatUse,
            new Dictionary<string, object>
            {
                ["room_id"] = room.Id,
                ["message_type"] = message.Type,
            },
            userAgent: string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            ipAddress: string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress
        );
    }

    /// <summary>
    /// Process link previews for a message in the background
    /// This method is designed to be called from a background task
    /// </summary>
    /// <param name="message">The message to process link previews for</param>
    private async Task CreateLinkPreviewBackgroundAsync(SnChatMessage message)
    {
        try
        {
            // Create a new scope for database operations
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDatabase>();

            // Preview the links in the message
            var updatedMessage = await CreateLinkPreviewAsync(message);

            // If embeds were added, update the message in the database
            if (
                updatedMessage.Meta != null
                && updatedMessage.Meta.TryGetValue("embeds", out var embeds)
                && embeds is List<Dictionary<string, object>> { Count: > 0 } embedsList
            )
            {
                // Get a fresh copy of the message from the database
                var dbMessage = await dbContext
                    .ChatMessages.Where(m => m.Id == message.Id)
                    .Include(m => m.Sender)
                    .Include(m => m.ChatRoom)
                    .FirstOrDefaultAsync();
                if (dbMessage != null)
                {
                    // Update the meta field with the new embeds
                    dbMessage.Meta ??= new Dictionary<string, object>();
                    dbMessage.Meta["embeds"] = embedsList;

                    // Save changes to the database
                    dbContext.Update(dbMessage);
                    await dbContext.SaveChangesAsync();

                    logger.LogDebug(
                        $"Updated message {message.Id} with {embedsList.Count} link previews"
                    );

                    // Create and store sync message for link preview update
                    var syncMessage = new SnChatMessage
                    {
                        Type = "messages.update.links",
                        ChatRoomId = dbMessage.ChatRoomId,
                        SenderId = dbMessage.SenderId,
                        Nonce = Guid.NewGuid().ToString(),
                        Meta = new Dictionary<string, object>
                        {
                            ["message_id"] = dbMessage.Id,
                            ["embeds"] = embedsList,
                        },
                        CreatedAt = dbMessage.UpdatedAt,
                        UpdatedAt = dbMessage.UpdatedAt,
                    };

                    dbContext.ChatMessages.Add(syncMessage);
                    await dbContext.SaveChangesAsync();

                    // Send sync message to clients
                    syncMessage.Sender = dbMessage.Sender;
                    syncMessage.ChatRoom = dbMessage.ChatRoom;

                    logger.LogWarning(
                        "CreateLinkPreviewBackgroundAsync: sending link preview for messageId={messageId}, embedCount={embedCount}",
                        dbMessage.Id,
                        embedsList.Count
                    );

                    using var syncScope = scopeFactory.CreateScope();

                    try
                    {
                        await DeliverMessageAsync(
                            syncMessage,
                            syncMessage.Sender,
                            syncMessage.ChatRoom,
                            type: syncMessage.Type,
                            notify: false
                        );
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            "Failed to deliver link preview: messageId={messageId}, error={error}",
                            dbMessage.Id,
                            ex.Message
                        );
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log errors but don't rethrow - this is a background task
            logger.LogError(
                $"Error processing link previews for message {message.Id}: {ex.Message} {ex.StackTrace}"
            );
        }
    }

    /// <summary>
    /// Processes a message to find and preview links in its content
    /// </summary>
    /// <param name="message">The message to process</param>
    /// <param name="webReader">The web reader service</param>
    /// <returns>The message with link previews added to its meta data</returns>
    public async Task<SnChatMessage> CreateLinkPreviewAsync(SnChatMessage message)
    {
        if (string.IsNullOrEmpty(message.Content))
            return message;

        var urls = ExtractPreviewUrls(message.Content, maxLinks: 3);
        if (urls.Count == 0)
            return message;

        // Initialize meta dictionary if null
        message.Meta ??= new Dictionary<string, object>();

        // Initialize the embeds' array if it doesn't exist
        if (
            !message.Meta.TryGetValue("embeds", out var existingEmbeds)
            || existingEmbeds is not List<Dictionary<string, object>>
        )
        {
            message.Meta["embeds"] = new List<Dictionary<string, object>>();
        }

        var embeds = (List<Dictionary<string, object>>)message.Meta["embeds"];

        foreach (var url in urls)
        {
            try
            {
                // Check if this URL is already in the embed list
                var urlAlreadyEmbedded = embeds.Any(e =>
                    e.TryGetValue("Url", out var originalUrl) && (string)originalUrl == url
                );
                if (urlAlreadyEmbedded)
                    continue;

                // Preview the link
                var linkEmbed = await webReader.GetLinkPreview(url);
                embeds.Add(EmbeddableBase.ToDictionary(linkEmbed));
            }
            catch
            {
                // ignored
            }
        }

        message.Meta["embeds"] = embeds;

        return message;
    }

    private async Task DeliverWebSocketMessage(
        SnChatMessage message,
        List<SnChatMember> members,
        IServiceScope scope
    )
    {
        var scopedWs =
            scope.ServiceProvider.GetRequiredService<WebSocketService.WebSocketServiceClient>();
        var payload = InfraObjectCoder.ConvertObjectToByteString(message);

        var request = new DyPushWebSocketPacketToUsersRequest
        {
            Packet = new DyWebSocketPacket { Type = "messages.new", Data = payload },
        };
        var memberAccounts = members.Select(a => a.Account).Where(a => a is not null).ToList();
        request.UserIds.AddRange(memberAccounts.Select(a => a!.Id.ToString()));

        logger.LogWarning(
            "DeliverWebSocketMessage: messageId={messageId}, type={type}, targetUserCount={targetUserCount}, attachmentCount={attachmentCount}, payloadBytes={payloadBytes}, userIds={userIds}",
            message.Id,
            message.Type,
            request.UserIds.Count,
            message.Attachments.Count,
            payload.Length,
            string.Join(",", request.UserIds.Take(10))
        );

        await scopedWs.PushWebSocketPacketToUsersAsync(request);

        logger.LogInformation($"Delivered message to {request.UserIds.Count} accounts.");
    }

    public async Task<SnChatMessage> SendMessageAsync(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string? clientIpAddress = null
    )
    {
        if (string.IsNullOrWhiteSpace(message.Nonce))
            message.Nonce = Guid.NewGuid().ToString();

        await NormalizeStickerPlaceholderMessageAsync(message);
        await EnrichInlineStickerPreviewAsync(message);

        // First complete the save operation
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        // Sending a message implies read-through at least to this message for the sender.
        await db
            .ChatMembers.Where(m =>
                m.Id == sender.Id
                && m.ChatRoomId == room.Id
                && m.JoinedAt != null
                && m.LeaveAt == null
            )
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(m => m.LastReadAt, message.CreatedAt)
            );

        if (
            room.RealmId.HasValue
            && sender.AccountId != Guid.Empty
            && !message.Type.StartsWith("system.")
        )
        {
            await eventBus.PublishAsync(
                new RealmActivityEvent
                {
                    RealmId = room.RealmId.Value,
                    AccountId = sender.AccountId,
                    ActivityType = "chat_message",
                    ReferenceId = $"{room.Id}:{message.Id}",
                    Delta = 2,
                }
            );
        }

        await EmitChatUseActionLogAsync(message, sender, room, clientIpAddress);

        // Copy the value to ensure the delivery is correct
        message.Sender = sender;
        message.ChatRoom = room;

        // Then start the delivery process
        var localMessage = message;
        var localSender = sender;
        var localRoom = room;
        var localLogger = logger;
        _ = Task.Run(async () =>
        {
            localLogger.LogWarning(
                "Starting background message delivery: messageId={messageId}, roomId={roomId}",
                localMessage.Id,
                localRoom.Id
            );
            try
            {
                await DeliverMessageAsync(localMessage, localSender, localRoom);
            }
            catch (Exception ex)
            {
                localLogger.LogWarning(
                    "Error delivering message: messageId={messageId}, error={error}",
                    localMessage.Id,
                    ex.Message
                );
                localLogger.LogError(
                    $"Error when delivering message: {ex.Message} {ex.StackTrace}"
                );
            }
        });

        // Process link preview in the background to avoid delaying message sending
        if (!message.IsEncrypted && message.Type == "text")
        {
            var localMessageForPreview = message;
            _ = Task.Run(async () =>
                await CreateLinkPreviewBackgroundAsync(localMessageForPreview)
            );
        }

        return message;
    }

    private static List<SnCloudFileReferenceObject> CloneAttachments(
        List<SnCloudFileReferenceObject> attachments
    )
    {
        return attachments
            .Select(attachment => new SnCloudFileReferenceObject
            {
                CreatedAt = attachment.CreatedAt,
                UpdatedAt = attachment.UpdatedAt,
                DeletedAt = attachment.DeletedAt,
                Id = attachment.Id,
                Name = attachment.Name,
                FileMeta =
                    attachment.FileMeta != null
                        ? new Dictionary<string, object?>(attachment.FileMeta)
                        : [],
                UserMeta =
                    attachment.UserMeta != null
                        ? new Dictionary<string, object?>(attachment.UserMeta)
                        : [],
                SensitiveMarks = attachment.SensitiveMarks?.ToList() ?? [],
                MimeType = attachment.MimeType,
                Hash = attachment.Hash,
                Size = attachment.Size,
                HasCompression = attachment.HasCompression,
                Url = attachment.Url,
                Width = attachment.Width,
                Height = attachment.Height,
                Blurhash = attachment.Blurhash,
            })
            .ToList();
    }

    private static Dictionary<string, object> CloneRedirectMeta(Dictionary<string, object>? meta)
    {
        if (meta is null)
            return [];

        return meta.Where(entry =>
                !string.Equals(entry.Key, "redirect", StringComparison.OrdinalIgnoreCase)
            )
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private static List<Dictionary<string, object?>> BuildRedirectAttachmentSnapshot(
        List<SnCloudFileReferenceObject> attachments
    )
    {
        return attachments
            .Select(attachment => new Dictionary<string, object?>
            {
                ["id"] = attachment.Id,
                ["name"] = attachment.Name,
                ["mime_type"] = attachment.MimeType,
                ["size"] = attachment.Size,
                ["url"] = attachment.Url,
                ["width"] = attachment.Width,
                ["height"] = attachment.Height,
                ["blurhash"] = attachment.Blurhash,
                ["has_compression"] = attachment.HasCompression,
            })
            .ToList();
    }

    private static Dictionary<string, object?>? BuildRedirectProfileSnapshot(
        SnAccountProfile? profile
    )
    {
        if (profile is null)
            return null;

        return new Dictionary<string, object?>
        {
            ["id"] = profile.Id,
            ["first_name"] = profile.FirstName,
            ["middle_name"] = profile.MiddleName,
            ["last_name"] = profile.LastName,
            ["bio"] = profile.Bio,
            ["gender"] = profile.Gender,
            ["pronouns"] = profile.Pronouns,
            ["time_zone"] = profile.TimeZone,
            ["location"] = profile.Location,
            ["birthday"] = profile.Birthday?.ToUnixTimeMilliseconds(),
            ["last_seen_at"] = profile.LastSeenAt?.ToUnixTimeMilliseconds(),
            ["experience"] = profile.Experience,
            ["level"] = profile.Level,
            ["leveling_progress"] = profile.LevelingProgress,
            ["social_credits"] = profile.SocialCredits,
            ["social_credits_level"] = profile.SocialCreditsLevel,
            ["picture"] = profile.Picture is not null
                ? BuildRedirectAttachmentSnapshot([profile.Picture]).First()
                : null,
            ["background"] = profile.Background is not null
                ? BuildRedirectAttachmentSnapshot([profile.Background]).First()
                : null,
            ["created_at"] = profile.CreatedAt.ToUnixTimeMilliseconds(),
            ["updated_at"] = profile.UpdatedAt.ToUnixTimeMilliseconds(),
        };
    }

    private static Dictionary<string, object?>? BuildRedirectAccountSnapshot(SnAccount? account)
    {
        if (account is null)
            return null;

        return new Dictionary<string, object?>
        {
            ["id"] = account.Id,
            ["name"] = account.Name,
            ["nick"] = account.Nick,
            ["language"] = account.Language,
            ["region"] = account.Region,
            ["activated_at"] = account.ActivatedAt?.ToUnixTimeMilliseconds(),
            ["is_superuser"] = account.IsSuperuser,
            ["automated_id"] = account.AutomatedId,
            ["profile"] = BuildRedirectProfileSnapshot(account.Profile),
            ["created_at"] = account.CreatedAt.ToUnixTimeMilliseconds(),
            ["updated_at"] = account.UpdatedAt.ToUnixTimeMilliseconds(),
        };
    }

    private static Dictionary<string, object?> BuildRedirectSenderSnapshot(SnChatMember sender)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = sender.Id,
            ["chat_room_id"] = sender.ChatRoomId,
            ["account_id"] = sender.AccountId,
            ["nick"] = sender.Nick,
            ["realm_nick"] = sender.RealmNick,
            ["realm_bio"] = sender.RealmBio,
            ["realm_experience"] = sender.RealmExperience,
            ["realm_level"] = sender.RealmLevel,
            ["realm_leveling_progress"] = sender.RealmLevelingProgress,
            ["notify"] = sender.Notify.ToString(),
            ["joined_at"] = sender.JoinedAt?.ToUnixTimeMilliseconds(),
            ["leave_at"] = sender.LeaveAt?.ToUnixTimeMilliseconds(),
            ["created_at"] = sender.CreatedAt.ToUnixTimeMilliseconds(),
            ["updated_at"] = sender.UpdatedAt.ToUnixTimeMilliseconds(),
            ["account"] = BuildRedirectAccountSnapshot(sender.Account),
        };
    }

    private static Dictionary<string, object> BuildRedirectSenderMapSnapshot(
        Dictionary<Guid, SnChatMember> sourceSenders
    )
    {
        return sourceSenders.ToDictionary(
            entry => entry.Key.ToString(),
            entry => (object)BuildRedirectSenderSnapshot(entry.Value)
        );
    }

    private static Dictionary<string, object?> BuildRedirectRoomSnapshot(SnChatRoom? room)
    {
        if (room is null)
        {
            return new Dictionary<string, object?>();
        }

        return new Dictionary<string, object?>
        {
            ["id"] = room.Id,
            ["name"] = room.Name,
            ["description"] = room.Description,
            ["type"] = room.Type.ToString(),
            ["is_community"] = room.IsCommunity,
            ["is_public"] = room.IsPublic,
            ["encryption_mode"] = room.EncryptionMode.ToString(),
            ["realm_id"] = room.RealmId,
            ["account_id"] = room.AccountId,
            ["picture"] = room.Picture is not null
                ? BuildRedirectAttachmentSnapshot([room.Picture]).First()
                : null,
            ["background"] = room.Background is not null
                ? BuildRedirectAttachmentSnapshot([room.Background]).First()
                : null,
            ["created_at"] = room.CreatedAt.ToUnixTimeMilliseconds(),
            ["updated_at"] = room.UpdatedAt.ToUnixTimeMilliseconds(),
        };
    }

    private static Dictionary<string, object> BuildRedirectMessageEntrySnapshot(
        SnChatMessage sourceMessage,
        SnChatMember sourceSender
    )
    {
        return new Dictionary<string, object>
        {
            ["id"] = sourceMessage.Id,
            ["type"] = sourceMessage.Type,
            ["content"] = sourceMessage.Content ?? string.Empty,
            ["meta"] = CloneRedirectMeta(sourceMessage.Meta),
            ["nonce"] = sourceMessage.Nonce,
            ["edited_at"] = sourceMessage.EditedAt?.ToUnixTimeMilliseconds(),
            ["replied_message_id"] = sourceMessage.RepliedMessageId,
            ["forwarded_message_id"] = sourceMessage.ForwardedMessageId,
            ["sender_id"] = sourceMessage.SenderId,
            ["chat_room_id"] = sourceMessage.ChatRoomId,
            ["created_at"] = sourceMessage.CreatedAt.ToUnixTimeMilliseconds(),
            ["updated_at"] = sourceMessage.UpdatedAt.ToUnixTimeMilliseconds(),
            ["deleted_at"] = sourceMessage.DeletedAt?.ToUnixTimeMilliseconds(),
            ["attachments"] = BuildRedirectAttachmentSnapshot(sourceMessage.Attachments),
            ["reactions_count"] = new Dictionary<string, int>(sourceMessage.ReactionsCount),
            ["chat_room"] = BuildRedirectRoomSnapshot(sourceMessage.ChatRoom),
        };
    }

    private static List<Dictionary<string, object>> BuildRedirectMessagesSnapshot(
        List<SnChatMessage> sourceMessages,
        Dictionary<Guid, SnChatMember> sourceSenders
    )
    {
        return sourceMessages
            .Select(message =>
                BuildRedirectMessageEntrySnapshot(message, sourceSenders[message.SenderId])
            )
            .ToList();
    }

    private static Dictionary<string, object> BuildRedirectRangeSnapshot(
        List<SnChatMessage> sourceMessages
    )
    {
        var orderedMessages = sourceMessages.OrderBy(m => m.CreatedAt).ToList();

        return new Dictionary<string, object>
        {
            ["start_message_id"] = orderedMessages.First().Id,
            ["end_message_id"] = orderedMessages.Last().Id,
            ["message_count"] = orderedMessages.Count,
            ["started_at"] = orderedMessages.First().CreatedAt.ToUnixTimeMilliseconds(),
            ["ended_at"] = orderedMessages.Last().CreatedAt.ToUnixTimeMilliseconds(),
        };
    }

    private static Dictionary<string, object> BuildRedirectSnapshot(
        List<SnChatMessage> sourceMessages,
        SnChatRoom sourceRoom,
        Dictionary<Guid, SnChatMember> sourceSenders,
        SnChatMember redirector,
        SnChatRoom destinationRoom
    )
    {
        var orderedMessages = sourceMessages.OrderBy(m => m.CreatedAt).ToList();

        if (orderedMessages.Count == 1)
        {
            var sourceMessage = orderedMessages[0];
            var sourceSender = sourceSenders[sourceMessage.SenderId];

            return new Dictionary<string, object>
            {
                ["version"] = 1,
                ["source_message_id"] = sourceMessage.Id,
                ["source_room_id"] = sourceRoom.Id,
                ["source_room"] = BuildRedirectRoomSnapshot(sourceRoom),
                ["source_sender_id"] = sourceSender.AccountId,
                ["source_sender_name"] =
                    sourceSender.Nick
                    ?? sourceSender.RealmNick
                    ?? sourceSender.Account?.Nick
                    ?? "Someone",
                ["source_type"] = sourceMessage.Type,
                ["source_content"] = sourceMessage.Content ?? string.Empty,
                ["source_created_at"] = sourceMessage.CreatedAt.ToUnixTimeMilliseconds(),
                ["source_attachments"] = BuildRedirectAttachmentSnapshot(sourceMessage.Attachments),
                ["source_meta"] = CloneRedirectMeta(sourceMessage.Meta),
                ["sender_map"] = BuildRedirectSenderMapSnapshot(sourceSenders),
                ["source_message"] = BuildRedirectMessageEntrySnapshot(sourceMessage, sourceSender),
                ["redirected_by"] = BuildRedirectSenderSnapshot(redirector),
                ["redirected_to_room"] = BuildRedirectRoomSnapshot(destinationRoom),
            };
        }

        return new Dictionary<string, object>
        {
            ["version"] = 2,
            ["kind"] = "history_segment",
            ["source_room_id"] = sourceRoom.Id,
            ["source_room"] = BuildRedirectRoomSnapshot(sourceRoom),
            ["range"] = BuildRedirectRangeSnapshot(orderedMessages),
            ["sender_map"] = BuildRedirectSenderMapSnapshot(sourceSenders),
            ["messages"] = BuildRedirectMessagesSnapshot(orderedMessages, sourceSenders),
            ["redirected_by"] = BuildRedirectSenderSnapshot(redirector),
            ["redirected_to_room"] = BuildRedirectRoomSnapshot(destinationRoom),
        };
    }

    public async Task<SnChatMessage> RedirectMessagesAsync(
        List<SnChatMessage> sourceMessages,
        SnChatRoom sourceRoom,
        Dictionary<Guid, SnChatMember> sourceSenders,
        SnChatMember redirector,
        SnChatRoom destinationRoom,
        string? clientIpAddress = null
    )
    {
        var meta = new Dictionary<string, object>
        {
            ["redirect"] = BuildRedirectSnapshot(
                sourceMessages,
                sourceRoom,
                sourceSenders,
                redirector,
                destinationRoom
            ),
        };

        var redirectMessage = new SnChatMessage
        {
            Type = "text",
            SenderId = redirector.Id,
            ChatRoomId = destinationRoom.Id,
            Nonce = Guid.NewGuid().ToString(),
            Content = null,
            Meta = meta,
            Attachments = [],
            MembersMentioned = [],
            ForwardedMessageId = sourceMessages.OrderBy(m => m.CreatedAt).First().Id,
        };

        return await SendMessageAsync(
            redirectMessage,
            redirector,
            destinationRoom,
            clientIpAddress
        );
    }

    public async Task<SnChatMessage> SendSystemMessageAsync(
        SnChatRoom room,
        SnChatMember sender,
        string type,
        string? content = null,
        Dictionary<string, object>? meta = null
    )
    {
        var systemMessage = new SnChatMessage
        {
            Type = type,
            ChatRoomId = room.Id,
            SenderId = sender.Id,
            Content = content,
            Meta = meta ?? new Dictionary<string, object>(),
            Nonce = Guid.NewGuid().ToString(),
        };

        db.ChatMessages.Add(systemMessage);
        await db.SaveChangesAsync();

        systemMessage.Sender = sender;
        systemMessage.ChatRoom = room;

        _ = DeliverMessageAsync(systemMessage, sender, room, type: type, notify: false);

        return systemMessage;
    }

    public async Task<SnChatMessage> SendMemberJoinedSystemMessageAsync(
        SnChatRoom room,
        SnChatMember member
    )
    {
        if (member.Account is null)
            member = await crs.LoadMemberAccount(member);

        var displayName = member.Nick ?? member.RealmNick ?? member.Account?.Nick ?? "Someone";
        return await SendSystemMessageAsync(
            room,
            member,
            "system.member.joined",
            $"{displayName} joined the chat.",
            new Dictionary<string, object>
            {
                ["event"] = "member_joined",
                ["member_id"] = member.Id,
                ["account_id"] = member.AccountId,
            }
        );
    }

    public async Task<SnChatMessage> SendMemberLeftSystemMessageAsync(
        SnChatRoom room,
        SnChatMember member,
        SnChatMember? operatorMember = null
    )
    {
        if (member.Account is null)
            member = await crs.LoadMemberAccount(member);

        var displayName = member.Nick ?? member.RealmNick ?? member.Account?.Nick ?? "Someone";

        string content;
        var meta = new Dictionary<string, object>
        {
            ["event"] = "member_left",
            ["member_id"] = member.Id,
            ["account_id"] = member.AccountId,
        };

        if (operatorMember is not null && operatorMember.Id != member.Id)
        {
            if (operatorMember.Account is null)
                operatorMember = await crs.LoadMemberAccount(operatorMember);

            var operatorName =
                operatorMember.Nick
                ?? operatorMember.RealmNick
                ?? operatorMember.Account?.Nick
                ?? "Moderator";
            content = $"{displayName} was removed from the chat by {operatorName}.";
            meta["operator_member_id"] = operatorMember.Id;
            meta["operator_account_id"] = operatorMember.AccountId;
            meta["reason"] = "removed";
        }
        else
        {
            content = $"{displayName} left the chat.";
            meta["reason"] = "left";
        }

        return await SendSystemMessageAsync(room, member, "system.member.left", content, meta);
    }

    public async Task<SnChatMessage> SendChatInfoUpdatedSystemMessageAsync(
        SnChatRoom room,
        SnChatMember operatorMember,
        Dictionary<string, object> changes
    )
    {
        if (operatorMember.Account is null)
            operatorMember = await crs.LoadMemberAccount(operatorMember);

        var operatorName =
            operatorMember.Nick
            ?? operatorMember.RealmNick
            ?? operatorMember.Account?.Nick
            ?? "Someone";
        var changedKeys = string.Join(", ", changes.Keys.OrderBy(k => k));

        return await SendSystemMessageAsync(
            room,
            operatorMember,
            "system.chat.updated",
            $"{operatorName} updated chat info ({changedKeys}).",
            new Dictionary<string, object>
            {
                ["event"] = "chat_info_updated",
                ["changes"] = changes,
            }
        );
    }

    public async Task<SnChatMessage> SendE2eeEnabledSystemMessageAsync(
        SnChatRoom room,
        SnChatMember sender,
        ChatRoomEncryptionMode mode,
        string? mlsGroupId = null
    )
    {
        var content =
            mode == ChatRoomEncryptionMode.E2eeMls
                ? "This chat now uses MLS."
                : "This chat now uses E2EE.";
        return await SendSystemMessageAsync(
            room,
            sender,
            "system.e2ee.enabled",
            content,
            new Dictionary<string, object>
            {
                ["event"] = "e2ee_enabled",
                ["room_id"] = room.Id,
                ["mode"] = mode.ToString(),
                ["mls_group_id"] = mlsGroupId,
            }
        );
    }

    public async Task<SnChatMessage> SendMlsEpochChangedSystemMessageAsync(
        SnChatRoom room,
        SnChatMember sender,
        long epoch,
        string reason
    )
    {
        return await SendSystemMessageAsync(
            room,
            sender,
            "system.mls.epoch_changed",
            "MLS epoch updated.",
            new Dictionary<string, object>
            {
                ["event"] = "mls_epoch_changed",
                ["room_id"] = room.Id,
                ["mls_group_id"] = room.MlsGroupId,
                ["epoch"] = epoch,
                ["reason"] = reason,
            }
        );
    }

    public async Task<SnChatMessage> SendMlsReshareRequiredSystemMessageAsync(
        SnChatRoom room,
        SnChatMember sender,
        Guid targetAccountId,
        string targetDeviceId,
        long epoch,
        string reason
    )
    {
        return await SendSystemMessageAsync(
            room,
            sender,
            "system.mls.reshare_required",
            "MLS re-share required for a device.",
            new Dictionary<string, object>
            {
                ["event"] = "mls_reshare_required",
                ["room_id"] = room.Id,
                ["mls_group_id"] = room.MlsGroupId,
                ["target_account_id"] = targetAccountId,
                ["target_device_id"] = targetDeviceId,
                ["epoch"] = epoch,
                ["reason"] = reason,
            }
        );
    }

    public async Task<SnChatMessage> SendMemberTimedOutSystemMessageAsync(
        SnChatRoom room,
        SnChatMember operatorMember,
        SnChatMember targetMember,
        string? reason,
        Instant timeoutUntil
    )
    {
        if (operatorMember.Account is null)
            operatorMember = await crs.LoadMemberAccount(operatorMember);
        if (targetMember.Account is null)
            targetMember = await crs.LoadMemberAccount(targetMember);

        var operatorName = operatorMember.Nick ?? operatorMember.Account?.Nick ?? "Moderator";
        var targetName = targetMember.Nick ?? targetMember.Account?.Nick ?? "Someone";

        var content = string.IsNullOrEmpty(reason)
            ? $"{targetName} was timed out by {operatorName}."
            : $"{targetName} was timed out by {operatorName}: {reason}.";

        return await SendSystemMessageAsync(
            room,
            operatorMember,
            "system.member.timed_out",
            content,
            new Dictionary<string, object>
            {
                ["event"] = "member_timed_out",
                ["target_member_id"] = targetMember.Id,
                ["target_account_id"] = targetMember.AccountId,
                ["operator_member_id"] = operatorMember.Id,
                ["operator_account_id"] = operatorMember.AccountId,
                ["reason"] = reason ?? "",
                ["timeout_until"] = timeoutUntil.ToUnixTimeMilliseconds(),
            }
        );
    }

    public async Task<SnChatMessage> SendMemberTimeoutRemovedSystemMessageAsync(
        SnChatRoom room,
        SnChatMember operatorMember,
        SnChatMember targetMember
    )
    {
        if (operatorMember.Account is null)
            operatorMember = crs.LoadMemberAccount(operatorMember).Result;
        if (targetMember.Account is null)
            targetMember = crs.LoadMemberAccount(targetMember).Result;

        var operatorName =
            operatorMember.Nick
            ?? operatorMember.RealmNick
            ?? operatorMember.Account?.Nick
            ?? "Moderator";
        var targetName =
            targetMember.Nick ?? targetMember.RealmNick ?? targetMember.Account?.Nick ?? "Someone";

        var content = $"{targetName}'s timeout was removed by {operatorName}.";

        return await SendSystemMessageAsync(
            room,
            operatorMember,
            "system.member.timeout_removed",
            content,
            new Dictionary<string, object>
            {
                ["event"] = "member_timeout_removed",
                ["target_member_id"] = targetMember.Id,
                ["target_account_id"] = targetMember.AccountId,
                ["operator_member_id"] = operatorMember.Id,
                ["operator_account_id"] = operatorMember.AccountId,
            }
        );
    }

    private async Task DeliverMessageAsync(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string type = WebSocketPacketType.MessageNew,
        bool notify = true
    )
    {
        using var scope = scopeFactory.CreateScope();
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();

        // Reload sender within the new scope to avoid disposed context issues
        sender = await scopedCrs.LoadMemberAccount(sender);

        if (room.RealmId != null)
            sender = await scopedCrs.HydrateRealmIdentity(sender, room.Id);

        // Ensure sender has Account loaded
        if (sender.Account is null)
        {
            logger.LogError(
                "DeliverMessageAsync: sender.Account is null after LoadMemberAccount! senderId={senderId}",
                sender.Id
            );
            throw new InvalidOperationException(
                $"Sender account could not be loaded for sender {sender.Id}"
            );
        }

        message.Sender = sender;
        message.ChatRoom = room;

        var members = await scopedCrs.ListRoomMembers(room.Id);

        logger.LogWarning(
            "DeliverMessageAsync: roomId={roomId}, messageId={messageId}, senderId={senderId}, senderAccountId={senderAccountId}, memberCount={memberCount}, type={type}",
            room.Id,
            message.Id,
            sender.Id,
            sender.AccountId,
            members.Count,
            type
        );

        await DeliverWebSocketMessage(message, members, scope);

        if (notify)
            await SendPushNotificationsAsync(message, sender, room, type, members, scope);
    }

    private async Task SendPushNotificationsAsync(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string type,
        List<SnChatMember> members,
        IServiceScope scope
    )
    {
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();
        var scopedNty =
            scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

        var roomSubject =
            room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM"
            : room.Realm is not null ? $"{room.Name ?? "Unknown"}, {room.Realm.Name}"
            : room.Name ?? "Unknown";

        if (sender.Account is null)
            sender = await scopedCrs.LoadMemberAccount(sender);
        else if (room.RealmId != null)
            sender = await scopedCrs.HydrateRealmIdentity(sender, room.Id);
        if (sender.Account is null)
            throw new InvalidOperationException(
                "Sender account is null, this should never happen. Sender id: " + sender.Id
            );

        var accountsToNotify = FilterAccountsForNotification(members, message, sender);

        // Filter out subscribed users from push notifications
        var subscribedMemberIds = new List<Guid>();
        foreach (var member in members)
        {
            if (await scopedCrs.IsSubscribedChatRoom(member.ChatRoomId, member.Id))
                subscribedMemberIds.Add(member.AccountId);
        }

        accountsToNotify = accountsToNotify
            .Where(a => !subscribedMemberIds.Contains(Guid.Parse(a.Id)))
            .ToList();

        logger.LogWarning(
            "SendPushNotificationsAsync: messageId={messageId}, totalMembers={totalMembers}, filteredCount={filteredCount}, notifyingCount={notifyingCount}",
            message.Id,
            members.Count,
            accountsToNotify.Count,
            accountsToNotify.Count
        );

        logger.LogInformation(
            "Trying to deliver message to {count} accounts...",
            accountsToNotify.Count
        );

        if (accountsToNotify.Count > 0)
        {
            foreach (
                var targetGroup in accountsToNotify.GroupBy(a => new
                {
                    Mentioned = IsAccountMentioned(message, Guid.Parse(a.Id)),
                    a.Language,
                })
            )
            {
                var notification = BuildNotification(
                    message,
                    sender,
                    room,
                    roomSubject,
                    type,
                    locale: targetGroup.Key.Language,
                    mentioned: targetGroup.Key.Mentioned
                );
                var ntyRequest = new DySendPushNotificationToUsersRequest
                {
                    Notification = notification,
                };
                ntyRequest.UserIds.AddRange(targetGroup.Select(a => a.Id.ToString()));
                await scopedNty.SendPushNotificationToUsersAsync(ntyRequest);
            }
        }

        logger.LogInformation("Delivered message to {count} accounts.", accountsToNotify.Count);
    }

    private async Task SendReactionNotificationAsync(
        SnChatMessage message,
        SnChatMember reactor,
        SnChatRoom room,
        bool isAdded,
        string symbol
    )
    {
        using var scope = scopeFactory.CreateScope();
        var scopedCrs = scope.ServiceProvider.GetRequiredService<ChatRoomService>();
        var scopedNty =
            scope.ServiceProvider.GetRequiredService<DyRingService.DyRingServiceClient>();

        var messageAuthor = await db
            .ChatMembers.Where(m => m.Id == message.SenderId && m.ChatRoomId == room.Id)
            .FirstOrDefaultAsync();

        if (messageAuthor is null)
            return;

        if (messageAuthor.AccountId == reactor.AccountId)
            return;

        if (messageAuthor.Notify == ChatMemberNotify.None)
            return;

        var now = SystemClock.Instance.GetCurrentInstant();
        if (messageAuthor.BreakUntil is not null && messageAuthor.BreakUntil > now)
            return;

        messageAuthor = await scopedCrs.LoadMemberAccount(messageAuthor);
        if (messageAuthor.Account is null)
            return;

        var locale = messageAuthor.Account.Language;
        var roomSubject =
            room is { Type: ChatRoomType.DirectMessage, Name: null } ? "DM"
            : room.Realm is not null ? $"{room.Name ?? "Unknown"}, {room.Realm.Name}"
            : room.Name ?? "Unknown";

        var notification = new DyPushNotification
        {
            Topic = isAdded ? "messages.reaction.added" : "messages.reaction.removed",
            Title =
                $"{reactor.Nick ?? reactor.RealmNick ?? reactor.Account?.Nick ?? "Someone"} ({roomSubject})",
            Meta = InfraObjectCoder.ConvertObjectToByteString(
                new Dictionary<string, object>
                {
                    ["user_id"] = reactor.AccountId,
                    ["reactor_id"] = reactor.Id,
                    ["reactor_name"] = reactor.Nick ?? reactor.RealmNick ?? reactor.Account?.Nick,
                    ["message_id"] = message.Id,
                    ["room_id"] = room.Id,
                    ["symbol"] = symbol ?? "",
                }
            ),
            ActionUri = $"/chat/{room.Id}",
            IsSavable = false,
            Body = isAdded
                ? localization.Get(
                    "chatReactionNotificationBodyAdded",
                    locale,
                    new
                    {
                        senderNick = reactor.Nick
                            ?? reactor.RealmNick
                            ?? reactor.Account?.Nick
                            ?? "Someone",
                        symbol,
                    }
                )
                : localization.Get(
                    "chatReactionNotificationBodyRemoved",
                    locale,
                    new
                    {
                        senderNick = reactor.Nick
                            ?? reactor.RealmNick
                            ?? reactor.Account?.Nick
                            ?? "Someone",
                        symbol,
                    }
                ),
        };

        var ntyRequest = new DySendPushNotificationToUsersRequest { Notification = notification };
        ntyRequest.UserIds.Add(messageAuthor.AccountId.ToString());
        await scopedNty.SendPushNotificationToUsersAsync(ntyRequest);

        logger.LogInformation(
            "Sent reaction notification to message author {AuthorId}",
            messageAuthor.AccountId
        );
    }

    private Dictionary<string, object> BuildNotificationMeta(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room
    )
    {
        var metaDict = new Dictionary<string, object>
        {
            ["sender_name"] = sender.Nick ?? sender.RealmNick ?? sender.Account!.Nick,
            ["user_id"] = sender.AccountId,
            ["sender_id"] = sender.Id,
            ["message_id"] = message.Id,
            ["room_id"] = room.Id,
        };

        var imageIds = message
            .Attachments.Where(a => a.MimeType?.StartsWith("image") ?? false)
            .Select(a => a.Id)
            .ToList();

        if (
            imageIds.Count == 0
            && message.Meta?.TryGetValue("sticker", out var stickerMeta) == true
            && stickerMeta is Dictionary<string, object?> stickerMetaDict
            && stickerMetaDict.TryGetValue("image_id", out var stickerImageId)
            && Guid.TryParse(stickerImageId?.ToString(), out var stickerImageGuid)
        )
        {
            imageIds.Add(stickerImageGuid.ToString());
        }

        if (imageIds.Count > 0)
        {
            metaDict["images"] = imageIds;
            metaDict["image"] = imageIds.First().ToString();
        }

        if (sender.Account!.Profile is not { Picture: null })
            metaDict["pfp"] = sender.Account!.Profile.Picture.Id;
        if (!string.IsNullOrEmpty(room.Name))
            metaDict["room_name"] = room.Name;

        return metaDict;
    }

    private DyPushNotification BuildNotification(
        SnChatMessage message,
        SnChatMember sender,
        SnChatRoom room,
        string roomSubject,
        string type,
        string? locale = null,
        bool mentioned = false
    )
    {
        var metaDict = BuildNotificationMeta(message, sender, room);
        if (mentioned)
            metaDict["mentioned"] = true;

        var notification = new DyPushNotification
        {
            Topic = "messages.new",
            Title = $"{sender.Nick ?? sender.RealmNick ?? sender.Account.Nick} ({roomSubject})",
            Meta = InfraObjectCoder.ConvertObjectToByteString(metaDict),
            ActionUri = $"/chat/{room.Id}",
            IsSavable = false,
            Body = BuildNotificationBody(message, type, locale, mentioned),
        };

        return notification;
    }

    private string BuildNotificationBody(
        SnChatMessage message,
        string type,
        string? locale = null,
        bool mentioned = false
    )
    {
        string body;

        if (IsUserEncryptedMessage(message))
            body = "Encrypted message";
        else if (message.DeletedAt is not null)
            body = "Deleted a message";
        else
        {
            switch (message.Type)
            {
                case "call.ended":
                    body = "Call ended";
                    break;
                case "call.start":
                    body = "Call begun";
                    break;
                case "voice":
                    body = "Voice message";
                    break;
                default:
                    if (
                        message.Meta?.TryGetValue("sticker_only_name", out var stickerOnlyName)
                            == true
                        && stickerOnlyName is string singleStickerName
                        && !string.IsNullOrWhiteSpace(singleStickerName)
                    )
                    {
                        var stickerCount = 1;
                        if (message.Meta.TryGetValue("sticker_only_count", out var countValue))
                            stickerCount = Convert.ToInt32(countValue);

                        body =
                            stickerCount > 1
                                ? localization.Get(
                                    "chatStickerOnlyNotificationBodyPlural",
                                    locale,
                                    new { name = singleStickerName, count = stickerCount }
                                )
                                : localization.Get(
                                    "chatStickerOnlyNotificationBody",
                                    locale,
                                    new { name = singleStickerName }
                                );
                    }
                    else if (
                        message.Meta?.TryGetValue(
                            "sticker_preview_text",
                            out var stickerPreviewText
                        ) == true
                        && stickerPreviewText is string previewText
                        && !string.IsNullOrWhiteSpace(previewText)
                    )
                    {
                        body = previewText[..Math.Min(previewText.Length, 100)];
                    }
                    else
                    {
                        body = !string.IsNullOrEmpty(message.Content)
                            ? message.Content[..Math.Min(message.Content.Length, 100)]
                            : localization.Get(
                                message.Attachments.Count == 1
                                    ? "chatNotificationAttachmentOnlyBodySingular"
                                    : "chatNotificationAttachmentOnlyBodyPlural",
                                locale,
                                new { count = message.Attachments.Count }
                            );
                    }

                    switch (type)
                    {
                        case WebSocketPacketType.MessageUpdate:
                            body += " (edited)";
                            break;
                        case WebSocketPacketType.MessageDelete:
                            body = "Deleted a message";
                            break;
                    }

                    break;
            }
        }

        return mentioned ? $"[@] {body}" : body;
    }

    private static List<DyAccount> FilterAccountsForNotification(
        List<SnChatMember> members,
        SnChatMessage message,
        SnChatMember sender
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var everyoneMentioned = IsEveryoneMentioned(message);

        var accountsToNotify = new List<DyAccount>();
        foreach (var member in members.Where(member => member.Notify != ChatMemberNotify.None))
        {
            // Skip if mentioned but not in mentions-only mode or if break is active
            if (
                !everyoneMentioned
                && (
                    message.MembersMentioned is null
                    || !message.MembersMentioned.Contains(member.AccountId)
                )
            )
            {
                if (member.BreakUntil is not null && member.BreakUntil > now)
                    continue;
                if (member.Notify == ChatMemberNotify.Mentions)
                    continue;
            }

            if (member.Account is not null)
                accountsToNotify.Add(member.Account.ToProtoValue());
        }

        return accountsToNotify.Where(a => a.Id != sender.AccountId.ToString()).ToList();
    }

    /// <summary>
    /// This method will instant update the LastReadAt field for chat member,
    /// for better performance, using the flush buffer one instead
    /// </summary>
    /// <param name="roomId">The user chat room</param>
    /// <param name="userId">The user id</param>
    /// <exception cref="ArgumentException"></exception>
    public async Task ReadChatRoomAsync(Guid roomId, Guid userId)
    {
        var sender = await db
            .ChatMembers.Where(m =>
                m.AccountId == userId
                && m.ChatRoomId == roomId
                && m.JoinedAt != null
                && m.LeaveAt == null
            )
            .FirstOrDefaultAsync();
        if (sender is null)
            throw new ArgumentException("User is not a member of the chat room.");

        sender.LastReadAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();
    }

    public async Task<int> CountUnreadMessage(Guid userId, Guid chatRoomId)
    {
        var member = await db
            .ChatMembers.Where(m =>
                m.AccountId == userId
                && m.ChatRoomId == chatRoomId
                && m.JoinedAt != null
                && m.LeaveAt == null
            )
            .Select(m => new { m.Id, m.LastReadAt })
            .FirstOrDefaultAsync();
        if (member is null)
            return 0;

        var query = db
            .ChatMessages.Where(m => m.ChatRoomId == chatRoomId)
            .Where(m => m.SenderId != member.Id);

        if (member.LastReadAt is not null)
            query = query.Where(m => m.CreatedAt > member.LastReadAt.Value);

        return await query.CountAsync();
    }

    public async Task<Dictionary<Guid, int>> CountUnreadMessageForUser(Guid userId)
    {
        var members = await db
            .ChatMembers.Where(m => m.LeaveAt == null && m.JoinedAt != null)
            .Where(m => m.AccountId == userId)
            .Select(m => new
            {
                m.Id,
                m.ChatRoomId,
                m.LastReadAt,
            })
            .ToListAsync();
        if (members.Count == 0)
            return new Dictionary<Guid, int>();

        var counts = await (
            from member in db.ChatMembers
            join msg in db.ChatMessages on member.ChatRoomId equals msg.ChatRoomId
            where
                member.AccountId == userId
                && member.LeaveAt == null
                && member.JoinedAt != null
                && msg.SenderId != member.Id
                && (member.LastReadAt == null || msg.CreatedAt > member.LastReadAt)
            group msg by member.ChatRoomId into grouped
            select new { grouped.Key, Count = grouped.Count() }
        ).ToDictionaryAsync(x => x.Key, x => x.Count);

        var result = new Dictionary<Guid, int>(members.Count);
        foreach (var member in members)
            result[member.ChatRoomId] = counts.GetValueOrDefault(member.ChatRoomId, 0);

        return result;
    }

    public async Task<Dictionary<Guid, SnChatMessage?>> ListLastMessageForUser(Guid userId)
    {
        var userRooms = await db
            .ChatMembers.Where(m => m.LeaveAt == null && m.JoinedAt != null)
            .Where(m => m.AccountId == userId)
            .Select(m => m.ChatRoomId)
            .ToListAsync();

        var messages = await db
            .ChatMessages.IgnoreQueryFilters()
            .Include(m => m.Sender)
            .Where(m => userRooms.Contains(m.ChatRoomId))
            .GroupBy(m => m.ChatRoomId)
            .Select(g => g.OrderByDescending(m => m.CreatedAt).FirstOrDefault())
            .ToDictionaryAsync(m => m!.ChatRoomId, m => m);

        var messageSenders = messages.Select(m => m.Value!.Sender).DistinctBy(x => x.Id).ToList();
        messageSenders = await crs.LoadMemberAccounts(messageSenders);
        messageSenders = messageSenders.Where(x => x.Account is not null).ToList();

        var messagesToRemove = messages
            .Where(m => messageSenders.All(s => s.Id != m.Value!.SenderId))
            .Select(m => m.Key)
            .ToList();

        foreach (var key in messagesToRemove)
            messages.Remove(key);

        foreach (var message in messages)
            message.Value!.Sender = messageSenders.First(x => x.Id == message.Value.SenderId);

        await HydrateMessageReactionsAsync(
            messages.Values.OfType<SnChatMessage>().ToList(),
            userId
        );

        return messages;
    }

    public async Task<SyncResponse> GetSyncDataAsync(
        Guid roomId,
        Guid? accountId,
        long lastSyncTimestamp,
        int limit = 500
    )
    {
        var lastSyncInstant = Instant.FromUnixTimeMilliseconds(lastSyncTimestamp);

        // Count total newer messages
        var totalCount = await db
            .ChatMessages.Where(m => m.ChatRoomId == roomId && m.CreatedAt > lastSyncInstant)
            .CountAsync();

        // Get up to limit messages that have been created since the last sync
        var syncMessages = await db
            .ChatMessages.Where(m => m.ChatRoomId == roomId)
            .Where(m => m.CreatedAt > lastSyncInstant)
            .OrderBy(m => m.CreatedAt)
            .Take(limit)
            .Include(m => m.Sender)
            .ToListAsync();

        if (syncMessages.Count > 0)
        {
            var senders = syncMessages.Select(m => m.Sender).DistinctBy(s => s.Id).ToList();

            senders = await crs.LoadMemberAccounts(senders);
            senders = await crs.HydrateRealmIdentity(senders, roomId);

            foreach (var message in syncMessages)
            {
                var sender = senders.FirstOrDefault(s => s.Id == message.SenderId);
                if (sender != null)
                    message.Sender = sender;
            }
        }
        await HydrateMessageReactionsAsync(syncMessages, accountId);

        var latestTimestamp =
            syncMessages.Count > 0
                ? syncMessages.Last().CreatedAt
                : SystemClock.Instance.GetCurrentInstant();

        return new SyncResponse
        {
            Messages = syncMessages,
            CurrentTimestamp = latestTimestamp,
            TotalCount = totalCount,
        };
    }

    public async Task<SnChatMessage> UpdateMessageAsync(
        SnChatMessage message,
        Dictionary<string, object>? meta = null,
        string? content = null,
        Guid? repliedMessageId = null,
        Guid? forwardedMessageId = null,
        List<string>? attachmentsId = null,
        bool? isEncrypted = null,
        byte[]? ciphertext = null,
        byte[]? encryptionHeader = null,
        byte[]? encryptionSignature = null,
        string? encryptionScheme = null,
        long? encryptionEpoch = null,
        string? encryptionMessageType = null,
        string? clientMessageId = null
    )
    {
        // Only allow editing regular text messages
        if (message.Type != "text")
            throw new InvalidOperationException("Only regular messages can be edited.");

        var prevIsEncrypted = message.IsEncrypted;
        var isContentChanged = content is not null && content != message.Content;
        var isAttachmentsChanged = attachmentsId is not null;
        var isCiphertextChanged = ciphertext is not null;
        var isEncryptedFlagChanged = isEncrypted.HasValue && isEncrypted.Value != prevIsEncrypted;

        string? prevContent = null;
        if (isContentChanged && !prevIsEncrypted)
            prevContent = message.Content;

        if (content is not null)
            message.Content = content;
        if (isEncrypted.HasValue)
            message.IsEncrypted = isEncrypted.Value;
        if (ciphertext is not null)
            message.Ciphertext = ciphertext;
        if (encryptionHeader is not null)
            message.EncryptionHeader = encryptionHeader;
        if (encryptionSignature is not null)
            message.EncryptionSignature = encryptionSignature;
        if (encryptionScheme is not null)
            message.EncryptionScheme = encryptionScheme;
        if (encryptionEpoch.HasValue)
            message.EncryptionEpoch = encryptionEpoch.Value;
        if (encryptionMessageType is not null)
            message.EncryptionMessageType = NormalizeEncryptionMessageType(
                encryptionMessageType,
                "messages.update"
            );
        if (!string.IsNullOrWhiteSpace(clientMessageId))
            message.ClientMessageId = clientMessageId;

        // Update do not override meta, replies to and forwarded to

        // Mark as edited if content or attachments changed
        if (
            isContentChanged
            || isAttachmentsChanged
            || isCiphertextChanged
            || isEncryptedFlagChanged
        )
            message.EditedAt = SystemClock.Instance.GetCurrentInstant();

        db.Update(message);
        await db.SaveChangesAsync();

        // Create and store sync message for the update
        var syncMessage = new SnChatMessage
        {
            Type = "messages.update",
            ChatRoomId = message.ChatRoomId,
            SenderId = message.SenderId,
            Content = message.Content,
            IsEncrypted = message.IsEncrypted,
            Ciphertext = message.Ciphertext,
            EncryptionHeader = message.EncryptionHeader,
            EncryptionSignature = message.EncryptionSignature,
            EncryptionScheme = message.EncryptionScheme,
            EncryptionEpoch = message.EncryptionEpoch,
            EncryptionMessageType = NormalizeEncryptionMessageType(
                message.EncryptionMessageType,
                "messages.update"
            ),
            ClientMessageId = message.ClientMessageId,
            Attachments = message.Attachments,
            Nonce = Guid.NewGuid().ToString(),
            Meta =
                message.Meta != null
                    ? new Dictionary<string, object>(message.Meta) { ["message_id"] = message.Id }
                    : new Dictionary<string, object> { ["message_id"] = message.Id },
            CreatedAt = message.UpdatedAt,
            UpdatedAt = message.UpdatedAt,
        };

        if (!message.IsEncrypted && isContentChanged && prevContent is not null)
            syncMessage.Meta["previous_content"] = prevContent;

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        // Process link preview in the background if content was updated
        if (!message.IsEncrypted && isContentChanged)
            _ = Task.Run(async () => await CreateLinkPreviewBackgroundAsync(message));

        if (message.Sender.Account is null)
            message.Sender = await crs.LoadMemberAccount(message.Sender);

        // Send sync message to clients
        syncMessage.Sender = message.Sender;
        syncMessage.ChatRoom = message.ChatRoom;

        _ = DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            type: syncMessage.Type,
            notify: false
        );

        return message;
    }

    /// <summary>
    /// Soft deletes a message and notifies other chat members
    /// </summary>
    /// <param name="message">The message to delete</param>
    public async Task DeleteMessageAsync(
        SnChatMessage message,
        byte[]? ciphertext = null,
        byte[]? encryptionHeader = null,
        byte[]? encryptionSignature = null,
        string? encryptionScheme = null,
        long? encryptionEpoch = null,
        string? encryptionMessageType = null,
        string? clientMessageId = null
    )
    {
        // Allow deleting user-authored text and voice messages only
        if (message.Type is not ("text" or "voice"))
        {
            throw new InvalidOperationException("Only regular text/voice messages can be deleted.");
        }

        // Soft delete by setting DeletedAt timestamp
        message.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        message.UpdatedAt = message.DeletedAt.Value;

        db.Update(message);
        await db.SaveChangesAsync();

        // Best effort cleanup for voice messages.
        // Missing objects or storage failures should not break message deletion.
        if (message.Type == "voice")
            await CleanupVoiceAssetsForDeletedMessageAsync(message);

        // Create and store sync message for the deletion
        var syncMessage = new SnChatMessage
        {
            Type = "messages.delete",
            ChatRoomId = message.ChatRoomId,
            SenderId = message.SenderId,
            IsEncrypted = message.IsEncrypted,
            Ciphertext = ciphertext ?? message.Ciphertext,
            EncryptionHeader = encryptionHeader ?? message.EncryptionHeader,
            EncryptionSignature = encryptionSignature ?? message.EncryptionSignature,
            EncryptionScheme = encryptionScheme ?? message.EncryptionScheme,
            EncryptionEpoch = encryptionEpoch ?? message.EncryptionEpoch,
            EncryptionMessageType = message.IsEncrypted
                ? NormalizeEncryptionMessageType(encryptionMessageType, "messages.delete")
                : null,
            ClientMessageId = clientMessageId ?? message.ClientMessageId,
            Nonce = Guid.NewGuid().ToString(),
            Meta = new Dictionary<string, object> { ["message_id"] = message.Id },
            CreatedAt = message.DeletedAt.Value,
            UpdatedAt = message.DeletedAt.Value,
        };

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        // Send sync message to clients
        if (message.Sender.Account is null)
            message.Sender = await crs.LoadMemberAccount(message.Sender);

        syncMessage.Sender = message.Sender;
        syncMessage.ChatRoom = message.ChatRoom;

        await DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            type: syncMessage.Type,
            notify: false
        );
    }

    private async Task CleanupVoiceAssetsForDeletedMessageAsync(SnChatMessage message)
    {
        try
        {
            if (!TryGetMetaGuid(message.Meta, "voice_clip_id", out var clipId))
                return;

            var clip = await db
                .ChatVoiceClips.Where(v => v.Id == clipId && v.ChatRoomId == message.ChatRoomId)
                .FirstOrDefaultAsync();
            if (clip is null)
                return;

            try
            {
                await voice.DeleteVoiceObjectByKeyAsync(clip.StoragePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed deleting voice object for clip {ClipId}", clip.Id);
            }

            db.ChatVoiceClips.Remove(clip);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed voice asset cleanup for deleted message {MessageId}",
                message.Id
            );
        }
    }

    private static bool TryGetMetaGuid(Dictionary<string, object>? meta, string key, out Guid value)
    {
        value = Guid.Empty;
        if (meta is null || !meta.TryGetValue(key, out var raw))
            return false;

        if (raw is Guid guid)
        {
            value = guid;
            return true;
        }

        if (raw is string s && Guid.TryParse(s, out var fromString))
        {
            value = fromString;
            return true;
        }

        if (
            raw is JsonElement je
            && je.ValueKind == JsonValueKind.String
            && Guid.TryParse(je.GetString(), out var fromJson)
        )
        {
            value = fromJson;
            return true;
        }

        return false;
    }

    public async Task<SnChatReaction> AddReactionAsync(
        SnChatRoom room,
        SnChatMessage message,
        SnChatReaction reaction,
        SnChatMember sender
    )
    {
        reaction.MessageId = message.Id;
        reaction.SenderId = sender.Id;

        db.ChatReactions.Add(reaction);

        if (message.ReactionsCount.TryGetValue(reaction.Symbol, out var count))
            message.ReactionsCount[reaction.Symbol] = count + 1;
        else
            message.ReactionsCount[reaction.Symbol] = 1;

        await db.SaveChangesAsync();

        var syncMessage = new SnChatMessage
        {
            Type = WebSocketPacketType.MessageReactionAdded,
            ChatRoomId = message.ChatRoomId,
            SenderId = sender.Id,
            Nonce = Guid.NewGuid().ToString(),
            Meta = new Dictionary<string, object>
            {
                ["message_id"] = message.Id,
                ["symbol"] = reaction.Symbol,
                ["reaction"] = new Dictionary<string, object>
                {
                    ["id"] = reaction.Id,
                    ["symbol"] = reaction.Symbol,
                    ["attitude"] = reaction.Attitude,
                    ["message_id"] = reaction.MessageId,
                    ["sender_id"] = reaction.SenderId,
                },
                ["reactions_count"] = new Dictionary<string, int>(message.ReactionsCount),
            },
        };

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        if (sender.Account is null)
        {
            logger.LogWarning(
                "AddReactionAsync: sender.Account is null, loading account for senderId={senderId}",
                sender.Id
            );
            sender = await crs.LoadMemberAccount(sender);
        }

        if (sender.Account is null)
        {
            logger.LogError(
                "AddReactionAsync: sender.Account is still null after LoadMemberAccount! senderId={senderId}",
                sender.Id
            );
            throw new InvalidOperationException(
                $"Sender account could not be loaded for sender {sender.Id}"
            );
        }

        syncMessage.Sender = sender;
        syncMessage.ChatRoom = room;

        logger.LogWarning(
            "AddReactionAsync: delivering reaction sync message, syncMessageId={syncMessageId}, senderId={senderId}, senderAccountId={senderAccountId}",
            syncMessage.Id,
            sender.Id,
            sender.AccountId
        );

        await DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            type: WebSocketPacketType.MessageNew,
            notify: false
        );

        // Explicitly update ReactionsCount in database to avoid entity tracking issues
        await db
            .ChatMessages.Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(m => m.ReactionsCount, message.ReactionsCount)
            );

        await HydrateMessageReactionsAsync([message], sender.AccountId);

        // Ensure the original message sender has Account loaded before delivery
        if (message.Sender.Account is null)
            message.Sender = await crs.LoadMemberAccount(message.Sender);
        message.ChatRoom = room;
        await DeliverMessageAsync(
            message,
            message.Sender,
            room,
            type: WebSocketPacketType.MessageNew,
            notify: false
        );

        _ = SendReactionNotificationAsync(message, sender, room, isAdded: true, reaction.Symbol);

        return reaction;
    }

    public async Task RemoveReactionAsync(
        SnChatRoom room,
        SnChatMessage message,
        string symbol,
        SnChatMember sender
    )
    {
        var sd = sender;
        var reaction = await db
            .ChatReactions.Where(r =>
                r.MessageId == message.Id && r.SenderId == sd.Id && r.Symbol == symbol
            )
            .FirstOrDefaultAsync();

        if (reaction is null)
            return;

        db.ChatReactions.Remove(reaction);

        if (message.ReactionsCount.TryGetValue(symbol, out var count))
        {
            if (count > 1)
                message.ReactionsCount[symbol] = count - 1;
            else
                message.ReactionsCount.Remove(symbol);
        }

        await db.SaveChangesAsync();

        var syncMessage = new SnChatMessage
        {
            Type = WebSocketPacketType.MessageReactionRemoved,
            ChatRoomId = message.ChatRoomId,
            SenderId = sender.Id,
            Nonce = Guid.NewGuid().ToString(),
            Meta = new Dictionary<string, object>
            {
                ["message_id"] = message.Id,
                ["symbol"] = symbol,
                ["reactions_count"] = new Dictionary<string, int>(message.ReactionsCount),
            },
        };

        db.ChatMessages.Add(syncMessage);
        await db.SaveChangesAsync();

        if (sender.Account is null)
        {
            logger.LogWarning(
                "RemoveReactionAsync: sender.Account is null, loading account for senderId={senderId}",
                sender.Id
            );
            sender = await crs.LoadMemberAccount(sender);
        }

        if (sender.Account is null)
        {
            logger.LogError(
                "RemoveReactionAsync: sender.Account is still null after LoadMemberAccount! senderId={senderId}",
                sender.Id
            );
            throw new InvalidOperationException(
                $"Sender account could not be loaded for sender {sender.Id}"
            );
        }

        syncMessage.Sender = sender;
        syncMessage.ChatRoom = room;

        logger.LogWarning(
            "RemoveReactionAsync: delivering reaction sync message, syncMessageId={syncMessageId}, senderId={senderId}, senderAccountId={senderAccountId}",
            syncMessage.Id,
            sender.Id,
            sender.AccountId
        );

        await DeliverMessageAsync(
            syncMessage,
            syncMessage.Sender,
            syncMessage.ChatRoom,
            type: WebSocketPacketType.MessageNew,
            notify: false
        );

        // Explicitly update ReactionsCount in database to avoid entity tracking issues
        await db
            .ChatMessages.Where(m => m.Id == message.Id)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(m => m.ReactionsCount, message.ReactionsCount)
            );

        await HydrateMessageReactionsAsync([message], sender.AccountId);

        // Ensure the original message sender has Account loaded before delivery
        if (message.Sender.Account is null)
            message.Sender = await crs.LoadMemberAccount(message.Sender);
        message.ChatRoom = room;
        if (message.Sender is null)
            return;
        await DeliverMessageAsync(
            message,
            message.Sender,
            room,
            type: WebSocketPacketType.MessageNew,
            notify: false
        );

        _ = SendReactionNotificationAsync(message, sender, room, isAdded: false, symbol);
    }

    public async Task HydrateMessageReactionsAsync(
        List<SnChatMessage> messages,
        Guid? accountId = null
    )
    {
        if (messages.Count == 0)
            return;

        var messageIds = messages.Select(m => m.Id).Distinct().ToList();

        Dictionary<Guid, Dictionary<string, bool>> reactionMadeMap = new();
        if (accountId.HasValue)
        {
            var reactionsMade = await db
                .ChatReactions.Where(r =>
                    messageIds.Contains(r.MessageId) && r.Sender.AccountId == accountId.Value
                )
                .Select(r => new { r.MessageId, r.Symbol })
                .ToListAsync();

            reactionMadeMap = reactionsMade
                .GroupBy(r => r.MessageId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.Symbol, _ => true));
        }

        foreach (var message in messages)
        {
            message.ReactionsMade = accountId.HasValue
                ? reactionMadeMap.GetValueOrDefault(message.Id, [])
                : null;
        }
    }
}

public class SyncResponse
{
    public List<SnChatMessage> Messages { get; set; } = [];
    public Instant CurrentTimestamp { get; set; }
    public int TotalCount { get; set; } = 0;
}
