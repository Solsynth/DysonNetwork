using CorePush.Apple;
using CorePush.Firebase;
using DysonNetwork.Ring.Services;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Net;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DysonNetwork.Ring.Notification;

public class PushService
{
    private sealed record SopStreamSubscription(string DeviceId, Channel<SnNotification> Channel);

    private static readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, SopStreamSubscription>> SopStreams = new();
    private readonly AppDatabase _db;
    private readonly QueueService _queueService;
    private readonly ILogger<PushService> _logger;
    private readonly FirebaseSender? _fcm;
    private readonly ApnSender? _apns;
    private readonly FlushBufferService _fbs;
    private readonly string? _apnsTopic;
    private readonly HttpClient _httpClient;
    private readonly RemoteWebSocketService _ws;
    private readonly NotificationPreferenceService _preferenceService;
    private readonly RemoteActionLogService _actionLogs;
    private static readonly HashSet<string> InvalidFcmErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "InvalidRegistration",
        "NotRegistered",
        "registration-token-not-registered",
        "UNREGISTERED"
    };
    private static readonly HashSet<string> InvalidApnsErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "BadDeviceToken",
        "DeviceTokenNotForTopic",
        "Unregistered"
    };

    public PushService(
        IConfiguration config,
        AppDatabase db,
        QueueService queueService,
        FlushBufferService fbs,
        IHttpClientFactory httpFactory,
        ILogger<PushService> logger,
        RemoteWebSocketService ws,
        NotificationPreferenceService preferenceService,
        RemoteActionLogService actionLogs
    )
    {
        var cfgSection = config.GetSection("Notifications:Push");

        // Set up Firebase Cloud Messaging
        var fcmConfig = cfgSection.GetValue<string>("Google");
        if (fcmConfig != null && File.Exists(fcmConfig))
            _fcm = new FirebaseSender(File.ReadAllText(fcmConfig), httpFactory.CreateClient());

        // Set up Apple Push Notification Service
        var apnsKeyPath = cfgSection.GetValue<string>("Apple:PrivateKey");
        if (apnsKeyPath != null && File.Exists(apnsKeyPath))
        {
            _apns = new ApnSender(new ApnSettings
            {
                P8PrivateKey = File.ReadAllText(apnsKeyPath),
                P8PrivateKeyId = cfgSection.GetValue<string>("Apple:PrivateKeyId"),
                TeamId = cfgSection.GetValue<string>("Apple:TeamId"),
                AppBundleIdentifier = cfgSection.GetValue<string>("Apple:BundleIdentifier"),
                ServerType = cfgSection.GetValue<bool>("Production")
                    ? ApnServerType.Production
                    : ApnServerType.Development
            }, httpFactory.CreateClient());
            _apnsTopic = cfgSection.GetValue<string>("Apple:BundleIdentifier");
        }

        _httpClient = httpFactory.CreateClient();
        _ws = ws;
        _db = db;
        _fbs = fbs;
        _queueService = queueService;
        _logger = logger;
        _preferenceService = preferenceService;
        _actionLogs = actionLogs;
    }

    public async Task UnsubscribeDevice(string deviceId)
    {
        await _db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId)
            .ExecuteDeleteAsync();
    }

    public async Task<SnNotificationPushSubscription> SubscribeDevice(
        string deviceId,
        string deviceToken,
        PushProvider provider,
        DyAccount account,
        bool isActivated = true
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.Parse(account.Id);

        if (isActivated)
        {
            await _db.PushSubscriptions
                .Where(s => s.AccountId == accountId)
                .Where(s => s.DeviceId == deviceId)
                .Where(s => s.DeletedAt == null)
                .Where(s => s.IsActivated)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(sub => sub.IsActivated, false)
                    .SetProperty(sub => sub.UpdatedAt, now)
                );
        }

        // Reuse an existing subscription for the same device/provider pair.
        var existingSubscription = await _db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId)
            .Where(s => s.Provider == provider)
            .FirstOrDefaultAsync();

        if (existingSubscription != null)
        {
            existingSubscription.DeviceId = deviceId;
            existingSubscription.DeviceToken = deviceToken;
            existingSubscription.Provider = provider;
            existingSubscription.IsActivated = isActivated;
            existingSubscription.LastUsedAt = now;
            existingSubscription.UpdatedAt = now;

            _db.Update(existingSubscription);
            await _db.SaveChangesAsync();
            return existingSubscription;
        }

        var subscription = new SnNotificationPushSubscription
        {
            DeviceId = deviceId,
            DeviceToken = deviceToken,
            Provider = provider,
            IsActivated = isActivated,
            AccountId = accountId,
            CreatedAt = now,
            UpdatedAt = now,
            LastUsedAt = now
        };

        _db.PushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var existingCount = await _db.PushSubscriptions
            .Where(s => s.AccountId == accountId && s.DeletedAt == null)
            .CountAsync();
        if (existingCount <= 1)
        {
            _actionLogs.CreateActionLog(
                accountId,
                ActionLogType.AccountPushEnable,
                new Dictionary<string, object> { ["provider"] = provider.ToString().ToLowerInvariant() }
            );
        }

        return subscription;
    }

    public async Task<(string Token, SnNotificationPushSubscription Subscription)> RegisterSopToken(
        string deviceId,
        DyAccount account
    )
    {
        var token = $"{Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()}{Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()}";
        var subscription = await SubscribeDevice(deviceId, token, PushProvider.Sop, account);
        return (token, subscription);
    }

    public async Task<SnNotificationPushSubscription?> GetSopSubscriptionByToken(string token)
    {
        return await _db.PushSubscriptions
            .Where(s => s.Provider == PushProvider.Sop)
            .Where(s => s.DeviceToken == token)
            .Where(s => s.IsActivated)
            .FirstOrDefaultAsync();
    }

    public async Task<List<SnNotificationPushSubscription>> GetCurrentDeviceSubscriptions(Guid accountId, string deviceId)
    {
        var sopDeviceId = $"{deviceId}:sop";
        return await _db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId || s.DeviceId == sopDeviceId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<SnNotificationPushSubscription?> GetCurrentDeviceActiveSubscription(Guid accountId, string deviceId)
    {
        var subscriptions = await GetCurrentDeviceSubscriptions(accountId, deviceId);
        return subscriptions
            .Where(s => s.IsActivated)
            .OrderByDescending(s => s.Provider == PushProvider.Sop)
            .ThenByDescending(s => s.UpdatedAt)
            .FirstOrDefault();
    }

    public (Guid StreamId, ChannelReader<SnNotification> Reader) SubscribeSopStream(Guid accountId, string deviceId)
    {
        var accountStreams = SopStreams.GetOrAdd(accountId, _ => new ConcurrentDictionary<Guid, SopStreamSubscription>());
        var streamId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SnNotification>();
        accountStreams[streamId] = new SopStreamSubscription(deviceId, channel);
        return (streamId, channel.Reader);
    }

    public void UnsubscribeSopStream(Guid accountId, Guid streamId)
    {
        if (!SopStreams.TryGetValue(accountId, out var accountStreams)) return;
        if (accountStreams.TryRemove(streamId, out var stream))
            stream.Channel.Writer.TryComplete();
        if (accountStreams.IsEmpty)
            SopStreams.TryRemove(accountId, out _);
    }

    public async Task SendNotification(DyAccount account,
        string topic,
        string? title = null,
        string? subtitle = null,
        string? content = null,
        Dictionary<string, object?>? meta = null,
        string? actionUri = null,
        bool isSilent = false,
        bool save = true)
    {
        meta ??= [];
        if (title is null && subtitle is null && content is null)
            throw new ArgumentException("Unable to send notification that is completely empty.");

        if (actionUri is not null) meta["action_uri"] = actionUri;

        var accountId = Guid.Parse(account.Id);
        var preference = await _preferenceService.GetPreferenceAsync(accountId, topic);

        if (preference == NotificationPreferenceLevel.Reject)
            return;

        var notification = new SnNotification
        {
            Topic = topic,
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            AccountId = accountId,
        };

        if (save)
        {
            _db.Notifications.Add(notification);
            await _db.SaveChangesAsync();
        }

        if (!isSilent && preference == NotificationPreferenceLevel.Normal)
            _ = _queueService.EnqueuePushNotification(notification, accountId, save);
    }

    public async Task DeliverPushNotification(
        SnNotification notification,
        IReadOnlyCollection<string>? excludedWebSocketDeviceIds = null,
        CancellationToken cancellationToken = default)
    {
        BroadcastSopStream(notification);

        try
        {
            _logger.LogInformation(
                "Delivering push notification: {NotificationTopic} with meta {NotificationMeta}",
                notification.Topic,
                notification.Meta
            );

            // Get all push subscriptions for the account
            var subscriptions = await _db.PushSubscriptions
                .Where(s => s.AccountId == notification.AccountId)
                .Where(s => s.IsActivated)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
            {
                _logger.LogInformation("No push subscriptions found for account {AccountId}", notification.AccountId);
                await _ws.PushWebSocketPacket(
                    notification.AccountId.ToString(),
                    "notifications.new",
                    InfraObjectCoder.ConvertObjectToByteString(notification).ToByteArray(),
                    excludedWebSocketDeviceIds
                );
                return;
            }

            var connectedSopDeviceIds = GetConnectedSopWebSocketDeviceIds(notification.AccountId);
            var subscriptionByDevice = SelectSubscriptionsByDevice(subscriptions, connectedSopDeviceIds);

            var websocketExclusions = BuildWebSocketExclusions(
                connectedSopDeviceIds,
                excludedWebSocketDeviceIds
            );

            await _ws.PushWebSocketPacket(
                notification.AccountId.ToString(),
                "notifications.new",
                InfraObjectCoder.ConvertObjectToByteString(notification).ToByteArray(),
                websocketExclusions
            );

            var tasks = subscriptionByDevice.Values.Select(sub => SendPushNotificationAsync(sub, notification)).ToList();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in DeliverPushNotification");
            throw;
        }
    }

    public async Task MarkNotificationsViewed(ICollection<SnNotification> notifications)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var id = notifications.Where(n => n.ViewedAt == null).Select(n => n.Id).ToList();
        if (id.Count == 0) return;

        await _db.Notifications
            .Where(n => id.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now));
    }

    public async Task MarkAllNotificationsViewed(Guid accountId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await _db.Notifications
            .Where(n => n.AccountId == accountId)
            .Where(n => n.ViewedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now));
    }

    public async Task SendNotificationBatch(
        SnNotification notification,
        List<Guid> accounts,
        bool save = false,
        IReadOnlyCollection<string>? excludedWebSocketDeviceIds = null
    )
    {
        if (save)
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var notifications = accounts.Select(accountId => new SnNotification
            {
                Topic = notification.Topic,
                Title = notification.Title,
                Subtitle = notification.Subtitle,
                Content = notification.Content,
                Meta = notification.Meta,
                Priority = notification.Priority,
                AccountId = accountId,
                CreatedAt = now,
                UpdatedAt = now
            }).ToList();

            if (notifications.Count != 0)
            {
                await _db.Notifications.AddRangeAsync(notifications);
                await _db.SaveChangesAsync();
            }
        }

        _logger.LogInformation(
            "Delivering notification in batch: {NotificationTopic} #{NotificationId} with meta {NotificationMeta}",
            notification.Topic,
            notification.Id,
            notification.Meta
        );

        // Send to each account
        foreach (var account in accounts)
        {
            notification.AccountId = account;
            BroadcastSopStream(notification);

            var subscriptions = await _db.PushSubscriptions
                .Where(s => s.AccountId == account)
                .Where(s => s.IsActivated)
                .ToListAsync();

            if (subscriptions.Count == 0)
            {
                await _ws.PushWebSocketPacket(
                    notification.AccountId.ToString(),
                    "notifications.new",
                    InfraObjectCoder.ConvertObjectToByteString(notification).ToByteArray(),
                    excludedWebSocketDeviceIds
                );
                continue;
            }

            var connectedSopDeviceIds = GetConnectedSopWebSocketDeviceIds(notification.AccountId);
            var subscriptionByDevice = SelectSubscriptionsByDevice(subscriptions, connectedSopDeviceIds);

            var websocketExclusions = BuildWebSocketExclusions(
                connectedSopDeviceIds,
                excludedWebSocketDeviceIds
            );

            await _ws.PushWebSocketPacket(
                notification.AccountId.ToString(),
                "notifications.new",
                InfraObjectCoder.ConvertObjectToByteString(notification).ToByteArray(),
                websocketExclusions
            );

            var tasks = subscriptionByDevice.Values.Select(sub => SendPushNotificationAsync(sub, notification)).ToList();
            await Task.WhenAll(tasks);
        }
    }

    private async Task SendPushNotificationAsync(SnNotificationPushSubscription subscription,
        SnNotification notification)
    {
        try
        {
            _logger.LogDebug(
                $"Pushing notification {notification.Topic} #{notification.Id} to device #{subscription.DeviceId}");

            switch (subscription.Provider)
            {
                case PushProvider.Google:
                    if (_fcm == null)
                        throw new InvalidOperationException("Firebase Cloud Messaging is not initialized.");

                    var body = string.Empty;
                    if (!string.IsNullOrEmpty(notification.Subtitle) || !string.IsNullOrEmpty(notification.Content))
                    {
                        body = string.Join("\n",
                            notification.Subtitle ?? string.Empty,
                            notification.Content ?? string.Empty
                        ).Trim();
                    }

                    var fcmResult = await _fcm.SendAsync(new Dictionary<string, object>
                    {
                        ["message"] = new Dictionary<string, object>
                        {
                            ["token"] = subscription.DeviceToken,
                            ["notification"] = new Dictionary<string, object>
                            {
                                ["title"] = notification.Title ?? string.Empty,
                                ["body"] = body
                            },
                            // You can re-enable data payloads if needed.
                            // ["data"] = new Dictionary<string, object>
                            // {
                            //     ["Id"] = notification.Id,
                            //     ["Topic"] = notification.Topic,
                            //     ["Meta"] = notification.Meta
                            // }
                        }
                    });

                    if (fcmResult.StatusCode is 404 or 410 || IsInvalidFcmTokenError(fcmResult.Error))
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (fcmResult.Error != null)
                        throw new Exception($"Notification pushed failed ({fcmResult.StatusCode}) {fcmResult.Error}");
                    break;

                case PushProvider.Apple:
                    if (_apns == null)
                        throw new InvalidOperationException("Apple Push Notification Service is not initialized.");

                    var alertDict = new Dictionary<string, object>();
                    if (!string.IsNullOrEmpty(notification.Title))
                        alertDict["title"] = notification.Title;
                    if (!string.IsNullOrEmpty(notification.Subtitle))
                        alertDict["subtitle"] = notification.Subtitle;
                    if (!string.IsNullOrEmpty(notification.Content))
                        alertDict["body"] = notification.Content;

                    var payload = new Dictionary<string, object?>
                    {
                        ["topic"] = _apnsTopic,
                        ["type"] = notification.Topic,
                        ["aps"] = new Dictionary<string, object?>
                        {
                            ["alert"] = alertDict,
                            ["sound"] = notification.Priority >= 5 ? "default" : null,
                            ["mutable-content"] = 1
                        },
                        ["meta"] = notification.Meta
                    };

                    var apnResult = await _apns.SendAsync(
                        payload,
                        deviceToken: subscription.DeviceToken,
                        apnsId: notification.Id.ToString(),
                        apnsPriority: notification.Priority,
                        apnPushType: ApnPushType.Alert
                    );

                    if (apnResult.StatusCode is 404 or 410 || IsInvalidApnsTokenError(apnResult.Error))
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (apnResult.Error != null)
                        throw new Exception($"Notification pushed failed ({apnResult.StatusCode}) {apnResult.Error}");

                    break;

                case PushProvider.Sop:
                    // SOP delivers via Ring APIs (list + SSE stream), no provider push is needed here.
                    break;

                case PushProvider.UnifiedPush:
                    using (var request = new HttpRequestMessage(HttpMethod.Post, subscription.DeviceToken))
                    {
                        // Without storing Web Push encryption metadata yet, send a wake-up ping so the client can sync.
                        request.Content = new ByteArrayContent([]);
                        request.Headers.TryAddWithoutValidation("TTL", "60");
                        request.Headers.TryAddWithoutValidation("Urgency", notification.Priority >= 5 ? "high" : "normal");

                        var unifiedPushResult = await _httpClient.SendAsync(request);
                        if (unifiedPushResult.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                            _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                        else if (!unifiedPushResult.IsSuccessStatusCode)
                            throw new Exception(
                                $"Notification push failed ({(int)unifiedPushResult.StatusCode}) {unifiedPushResult.ReasonPhrase}"
                            );
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Push provider not supported: {subscription.Provider}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"Failed to push notification #{notification.Id} to device {subscription.DeviceId}. {ex.Message}");
            // Swallow here to keep worker alive; upstream is fire-and-forget.
        }

        _logger.LogInformation(
            $"Successfully pushed notification #{notification.Id} to device {subscription.DeviceId} provider {subscription.Provider}");
    }

    private static void BroadcastSopStream(SnNotification notification)
    {
        if (!SopStreams.TryGetValue(notification.AccountId, out var accountStreams)) return;
        foreach (var stream in accountStreams.Values)
            stream.Channel.Writer.TryWrite(notification);
    }

    private static Dictionary<string, SnNotificationPushSubscription> SelectSubscriptionsByDevice(
        IEnumerable<SnNotificationPushSubscription> subscriptions,
        IReadOnlySet<string> connectedSopDeviceIds
    )
    {
        return subscriptions
            .GroupBy(s => NormalizeSopDeviceId(s.DeviceId))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(s => GetSubscriptionPriority(s, connectedSopDeviceIds)).First()
            );
    }

    private static int GetSubscriptionPriority(
        SnNotificationPushSubscription subscription,
        IReadOnlySet<string> connectedSopDeviceIds
    ) => subscription.Provider switch
    {
        PushProvider.Sop => connectedSopDeviceIds.Contains(NormalizeSopDeviceId(subscription.DeviceId)) ? 5 : 0,
        PushProvider.Google => 2,
        PushProvider.UnifiedPush => 1,
        PushProvider.Apple => 4,
        _ => 0
    };

    private static IReadOnlySet<string> GetConnectedSopWebSocketDeviceIds(Guid accountId)
    {
        if (!SopStreams.TryGetValue(accountId, out var accountStreams)) return new HashSet<string>();

        return accountStreams.Values
            .Select(s => NormalizeSopDeviceId(s.DeviceId))
            .ToHashSet();
    }

    private static string NormalizeSopDeviceId(string deviceId)
    {
        const string sopSuffix = ":sop";
        return deviceId.EndsWith(sopSuffix, StringComparison.Ordinal)
            ? deviceId[..^sopSuffix.Length]
            : deviceId;
    }

    private static IReadOnlyCollection<string>? BuildWebSocketExclusions(
        IEnumerable<string> deviceIds,
        IReadOnlyCollection<string>? excludedWebSocketDeviceIds
    )
    {
        var exclusions = excludedWebSocketDeviceIds is null
            ? new HashSet<string>()
            : new HashSet<string>(excludedWebSocketDeviceIds);

        foreach (var deviceId in deviceIds)
            exclusions.Add(deviceId);

        return exclusions.Count == 0 ? null : exclusions.ToArray();
    }

    private static bool IsInvalidFcmTokenError(string? error) =>
        !string.IsNullOrWhiteSpace(error) && InvalidFcmErrors.Contains(error);

    private static bool IsInvalidApnsTokenError(string? error) =>
        !string.IsNullOrWhiteSpace(error) && InvalidApnsErrors.Contains(error);

    public async Task SaveNotification(SnNotification notification)
    {
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync();
    }

    public async Task SaveNotification(SnNotification notification, List<Guid> accounts)
    {
        _db.Notifications.AddRange(accounts.Select(a => new SnNotification
        {
            AccountId = a,
            Topic = notification.Topic,
            Content = notification.Content,
            Title = notification.Title,
            Subtitle = notification.Subtitle,
            Meta = notification.Meta,
            Priority = notification.Priority,
            CreatedAt = notification.CreatedAt,
            UpdatedAt = notification.UpdatedAt,
        }));
        await _db.SaveChangesAsync();
    }
}
