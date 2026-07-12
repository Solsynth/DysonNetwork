using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
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

namespace DysonNetwork.Ring.Notification;

public class PushAppConfig
{
    public bool Production { get; set; }
    public string? FcmKeyPath { get; set; }
    public ApnsAppConfig? Apns { get; set; }
    public Dictionary<string, string> Topics { get; set; } = new();
}

public class ApnsAppConfig
{
    public string PrivateKeyPath { get; set; } = null!;
    public string PrivateKeyId { get; set; } = null!;
    public string TeamId { get; set; } = null!;
    public string BundleIdentifier { get; set; } = null!;
}

public class PushService
{
    private sealed record SopStreamSubscription(string DeviceId, Channel<SnNotification> Channel);

    private sealed record AppSenders(
        FirebaseSender? Fcm,
        Dictionary<string, ApnSender> ApnsByTopic,
        string? DefaultApnsTopic,
        Dictionary<string, string> Topics
    );

    private static readonly ConcurrentDictionary<
        Guid,
        ConcurrentDictionary<Guid, SopStreamSubscription>
    > SopStreams = new();
    private readonly AppDatabase _db;
    private readonly QueueService _queueService;
    private readonly ILogger<PushService> _logger;
    private readonly Dictionary<string, AppSenders> _appSenders = new();
    private readonly string? _defaultAppId;
    private readonly FlushBufferService _fbs;
    private readonly HttpClient _httpClient;
    private readonly RemoteWebSocketService _ws;
    private readonly NotificationPreferenceService _preferenceService;
    private readonly RemoteActionLogService _actionLogs;
    private readonly SopNotificationReplayBuffer _sopReplayBuffer;
    private static readonly HashSet<string> InvalidFcmErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "InvalidRegistration",
        "NotRegistered",
        "registration-token-not-registered",
        "UNREGISTERED",
    };
    private static readonly HashSet<string> InvalidApnsErrors = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "BadDeviceToken",
        "Unregistered",
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
        RemoteActionLogService actionLogs,
        SopNotificationReplayBuffer sopReplayBuffer
    )
    {
        var cfgSection = config.GetSection("Notifications:Push");
        var appsSection = cfgSection.GetSection("Apps");
        if (appsSection.Exists())
        {
            foreach (var appChild in appsSection.GetChildren())
            {
                var appId = appChild.Key;
                var appConfig = appChild.Get<PushAppConfig>();
                if (appConfig is null)
                    continue;

                var senders = BuildAppSenders(appConfig, httpFactory);
                _appSenders[appId] = senders;
            }

            _defaultAppId = cfgSection.GetValue<string>("DefaultApp");
        }
        else
        {
            // ponytail: backwards compat for old flat config (Google / Apple keys)
            var legacy = new PushAppConfig { Production = cfgSection.GetValue<bool>("Production") };
            var fcmPath = cfgSection.GetValue<string>("Google");
            if (fcmPath != null)
                legacy.FcmKeyPath = fcmPath;

            var apnsSection = cfgSection.GetSection("Apple");
            if (apnsSection.Exists())
            {
                legacy.Apns = new ApnsAppConfig
                {
                    PrivateKeyPath = apnsSection.GetValue<string>("PrivateKey") ?? "",
                    PrivateKeyId = apnsSection.GetValue<string>("PrivateKeyId") ?? "",
                    TeamId = apnsSection.GetValue<string>("TeamId") ?? "",
                    BundleIdentifier = apnsSection.GetValue<string>("BundleIdentifier") ?? "",
                };
            }

            var legacyId = "_default";
            _appSenders[legacyId] = BuildAppSenders(legacy, httpFactory);
            _defaultAppId = legacyId;
        }

        _defaultAppId ??= _appSenders.Keys.FirstOrDefault();

        _httpClient = httpFactory.CreateClient();
        _ws = ws;
        _db = db;
        _fbs = fbs;
        _queueService = queueService;
        _logger = logger;
        _preferenceService = preferenceService;
        _actionLogs = actionLogs;
        _sopReplayBuffer = sopReplayBuffer;
    }

    private static AppSenders BuildAppSenders(PushAppConfig config, IHttpClientFactory httpFactory)
    {
        FirebaseSender? fcm = null;
        if (config.FcmKeyPath != null && File.Exists(config.FcmKeyPath))
            fcm = new FirebaseSender(
                File.ReadAllText(config.FcmKeyPath),
                httpFactory.CreateClient()
            );

        string? apnsTopic = null;
        var topics = new Dictionary<string, string>(config.Topics);
        var apnsByTopic = new Dictionary<string, ApnSender>(StringComparer.Ordinal);
        if (config.Apns is { PrivateKeyPath: var keyPath } && File.Exists(keyPath))
        {
            apnsTopic = config.Apns.BundleIdentifier;
            if (apnsTopic is not null && !topics.ContainsKey("Alert"))
                topics["Alert"] = apnsTopic;

            var privateKey = File.ReadAllText(keyPath);
            var serverType = config.Production
                ? ApnServerType.Production
                : ApnServerType.Development;
            foreach (var topic in topics.Values.Distinct(StringComparer.Ordinal))
            {
                apnsByTopic[topic] = new ApnSender(
                    new ApnSettings
                    {
                        P8PrivateKey = privateKey,
                        P8PrivateKeyId = config.Apns.PrivateKeyId,
                        TeamId = config.Apns.TeamId,
                        AppBundleIdentifier = topic,
                        ServerType = serverType,
                    },
                    httpFactory.CreateClient()
                );
            }
        }

        return new AppSenders(fcm, apnsByTopic, apnsTopic, topics);
    }

    private AppSenders? ResolveAppSenders(string? appId)
    {
        if (!string.IsNullOrEmpty(appId) && _appSenders.TryGetValue(appId, out var senders))
            return senders;
        return _defaultAppId is not null ? _appSenders.GetValueOrDefault(_defaultAppId) : null;
    }

    public string? GetDefaultAppId() => _defaultAppId;

    public string? ResolveAppId(string? appId, bool useDefaultIfMissing = false)
    {
        if (!string.IsNullOrWhiteSpace(appId))
            return appId;
        return useDefaultIfMissing ? _defaultAppId : null;
    }

    public IQueryable<SnNotification> ApplyNotificationAppFilter(
        IQueryable<SnNotification> query,
        string? appId,
        bool useDefaultIfMissing = false
    )
    {
        var resolvedAppId = ResolveAppId(appId, useDefaultIfMissing);
        if (resolvedAppId is null)
            return query;

        if (_defaultAppId is not null && resolvedAppId == _defaultAppId)
            return query.Where(s => s.AppId == resolvedAppId || s.AppId == null || s.AppId == "");

        return query.Where(s => s.AppId == resolvedAppId);
    }

    public IQueryable<SnNotificationPushSubscription> ApplySubscriptionAppFilter(
        IQueryable<SnNotificationPushSubscription> query,
        string? appId,
        bool useDefaultIfMissing = false
    )
    {
        var resolvedAppId = ResolveAppId(appId, useDefaultIfMissing);
        if (resolvedAppId is null)
            return query;

        if (_defaultAppId is not null && resolvedAppId == _defaultAppId)
            return query.Where(s => s.AppId == resolvedAppId || s.AppId == null || s.AppId == "");

        return query.Where(s => s.AppId == resolvedAppId);
    }

    public async Task UnsubscribeDevice(string deviceId)
    {
        await _db.PushSubscriptions.Where(s => s.DeviceId == deviceId).ExecuteDeleteAsync();
    }

    public async Task<SnNotificationPushSubscription> SubscribeDevice(
        string deviceId,
        string deviceToken,
        string? deviceName,
        PushProvider provider,
        DyAccount account,
        bool isActivated = true,
        string? appId = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.Parse(account.Id);
        appId = ResolveAppId(appId, useDefaultIfMissing: true);

        if (isActivated)
        {
            await _db
                .PushSubscriptions.Where(s => s.AccountId == accountId)
                .Where(s => s.DeviceId == deviceId)
                .Where(s => s.DeletedAt == null)
                .Where(s => s.IsActivated)
                .Where(s => s.Provider == provider)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(sub => sub.IsActivated, false)
                        .SetProperty(sub => sub.UpdatedAt, now)
                );
        }

        // Reuse an existing subscription for the same device/provider pair.
        var existingSubscription = await _db
            .PushSubscriptions.Where(s => s.AccountId == accountId)
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
            existingSubscription.DeviceName = deviceName;
            existingSubscription.AppId = appId;

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
            AppId = appId,
            DeviceName = deviceName,
            CreatedAt = now,
            UpdatedAt = now,
            LastUsedAt = now,
        };

        _db.PushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        var existingCount = await _db
            .PushSubscriptions.Where(s => s.AccountId == accountId && s.DeletedAt == null)
            .CountAsync();
        if (existingCount <= 1)
        {
            _actionLogs.CreateActionLog(
                accountId,
                ActionLogType.AccountPushEnable,
                new Dictionary<string, object>
                {
                    ["provider"] = provider.ToString().ToLowerInvariant(),
                }
            );
        }

        return subscription;
    }

    public async Task<(string Token, SnNotificationPushSubscription Subscription)> RegisterSopToken(
        string deviceId,
        string? deviceName,
        DyAccount account,
        string? appId = null
    )
    {
        var token =
            $"{Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()}{Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant()}";
        var subscription = await SubscribeDevice(
            deviceId,
            token,
            deviceName,
            PushProvider.Sop,
            account,
            appId: appId
        );
        return (token, subscription);
    }

    public async Task<SnNotificationPushSubscription?> GetSopSubscriptionByToken(string token)
    {
        return await _db
            .PushSubscriptions.Where(s => s.Provider == PushProvider.Sop)
            .Where(s => s.DeviceToken == token)
            .Where(s => s.IsActivated)
            .FirstOrDefaultAsync();
    }

    public async Task<List<SnNotificationPushSubscription>> GetCurrentDeviceSubscriptions(
        Guid accountId,
        string deviceId
    )
    {
        var sopDeviceId = $"{deviceId}:sop";
        return await _db
            .PushSubscriptions.Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId || s.DeviceId == sopDeviceId)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync();
    }

    public async Task<SnNotificationPushSubscription?> GetCurrentDeviceActiveSubscription(
        Guid accountId,
        string deviceId
    )
    {
        var subscriptions = await GetCurrentDeviceSubscriptions(accountId, deviceId);
        return subscriptions
            .Where(s => s.IsActivated)
            .OrderByDescending(s => s.Provider == PushProvider.Sop)
            .ThenByDescending(s => s.UpdatedAt)
            .FirstOrDefault();
    }

    public (Guid StreamId, ChannelReader<SnNotification> Reader) SubscribeSopStream(
        Guid accountId,
        string deviceId
    )
    {
        var accountStreams = SopStreams.GetOrAdd(
            accountId,
            _ => new ConcurrentDictionary<Guid, SopStreamSubscription>()
        );
        var streamId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<SnNotification>();
        accountStreams[streamId] = new SopStreamSubscription(deviceId, channel);
        return (streamId, channel.Reader);
    }

    public void UnsubscribeSopStream(Guid accountId, Guid streamId)
    {
        if (!SopStreams.TryGetValue(accountId, out var accountStreams))
            return;
        if (accountStreams.TryRemove(streamId, out var stream))
            stream.Channel.Writer.TryComplete();
        if (accountStreams.IsEmpty)
            SopStreams.TryRemove(accountId, out _);
    }

    public async Task SendNotification(
        DyAccount account,
        string topic,
        string? title = null,
        string? subtitle = null,
        string? content = null,
        Dictionary<string, object?>? meta = null,
        string? actionUri = null,
        bool isSilent = false,
        bool save = true,
        string? appId = null,
        string? pushType = null
    )
    {
        meta ??= [];
        if (title is null && subtitle is null && content is null)
            throw new ArgumentException("Unable to send notification that is completely empty.");

        if (actionUri is not null)
            meta["action_uri"] = actionUri;

        var accountId = Guid.Parse(account.Id);
        var preference = await _preferenceService.GetPreferenceAsync(accountId, topic);

        if (preference == NotificationPreferenceLevel.Reject)
            return;

        appId = ResolveAppId(appId, useDefaultIfMissing: true);

        var notification = new SnNotification
        {
            Topic = topic,
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            AccountId = accountId,
            AppId = appId,
            PushType = pushType,
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
        bool isSavable = false,
        CancellationToken cancellationToken = default
    )
    {
        var connectedSopDeviceIds = GetConnectedSopWebSocketDeviceIds(notification.AccountId);
        if (ShouldQueueSopReplay(isSavable, connectedSopDeviceIds))
            await _sopReplayBuffer.AppendNotification(notification);

        BroadcastSopStream(notification);

        try
        {
            _logger.LogInformation(
                "Delivering push notification: {NotificationTopic} with meta {NotificationMeta}",
                notification.Topic,
                notification.Meta
            );

            // Get all push subscriptions for the account
            var subscriptions = await _db
                .PushSubscriptions.Where(s => s.AccountId == notification.AccountId)
                .Where(s => s.IsActivated)
                .ToListAsync(cancellationToken);

            if (subscriptions.Count == 0)
            {
                _logger.LogInformation(
                    "No push subscriptions found for account {AccountId}",
                    notification.AccountId
                );
                await _ws.PushWebSocketPacket(
                    notification.AccountId.ToString(),
                    "notifications.new",
                    InfraObjectCoder.ConvertObjectToByteString(notification).ToByteArray(),
                    excludedWebSocketDeviceIds
                );
                return;
            }

            var subscriptionByDevice = SelectSubscriptionsByDevice(
                subscriptions,
                connectedSopDeviceIds,
                notification
            );

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

            var tasks = subscriptionByDevice
                .Values.Select(sub => SendPushNotificationAsync(sub, notification))
                .ToList();
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
        if (id.Count == 0)
            return;

        await _db
            .Notifications.Where(n => id.Contains(n.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now));
    }

    public async Task MarkAllNotificationsViewed(Guid accountId, string? appId = null)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await ApplyNotificationAppFilter(
                _db.Notifications.Where(n => n.AccountId == accountId)
                    .Where(n => n.ViewedAt == null),
                appId,
                useDefaultIfMissing: true
            )
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
            var notifications = accounts
                .Select(accountId => new SnNotification
                {
                    Topic = notification.Topic,
                    Title = notification.Title,
                    Subtitle = notification.Subtitle,
                    Content = notification.Content,
                    Meta = notification.Meta,
                    Priority = notification.Priority,
                    AccountId = accountId,
                    AppId = notification.AppId,
                    PushType = notification.PushType,
                    CreatedAt = now,
                    UpdatedAt = now,
                })
                .ToList();

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
            var connectedSopDeviceIds = GetConnectedSopWebSocketDeviceIds(notification.AccountId);
            if (ShouldQueueSopReplay(save, connectedSopDeviceIds))
                await _sopReplayBuffer.AppendNotification(notification);
            BroadcastSopStream(notification);

            var subscriptions = await _db
                .PushSubscriptions.Where(s => s.AccountId == account)
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

            var subscriptionByDevice = SelectSubscriptionsByDevice(
                subscriptions,
                connectedSopDeviceIds,
                notification
            );

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

            var tasks = subscriptionByDevice
                .Values.Select(sub => SendPushNotificationAsync(sub, notification))
                .ToList();
            await Task.WhenAll(tasks);
        }
    }

    private async Task SendPushNotificationAsync(
        SnNotificationPushSubscription subscription,
        SnNotification notification
    )
    {
        try
        {
            var senders = ResolveAppSenders(subscription.AppId);

            _logger.LogDebug(
                $"Pushing notification {notification.Topic} #{notification.Id} to device #{subscription.DeviceId}"
            );

            switch (subscription.Provider)
            {
                case PushProvider.Google:
                    if (senders?.Fcm == null)
                        throw new InvalidOperationException(
                            "Firebase Cloud Messaging is not initialized."
                        );

                    var body = string.Empty;
                    if (
                        !string.IsNullOrEmpty(notification.Subtitle)
                        || !string.IsNullOrEmpty(notification.Content)
                    )
                    {
                        body = string.Join(
                                "\n",
                                notification.Subtitle ?? string.Empty,
                                notification.Content ?? string.Empty
                            )
                            .Trim();
                    }

                    var fcmResult = await senders.Fcm.SendAsync(
                        new Dictionary<string, object>
                        {
                            ["message"] = new Dictionary<string, object>
                            {
                                ["token"] = subscription.DeviceToken,
                                ["notification"] = new Dictionary<string, object>
                                {
                                    ["title"] = notification.Title ?? string.Empty,
                                    ["body"] = body,
                                },
                            },
                        }
                    );

                    if (
                        fcmResult.StatusCode is 404 or 410
                        || IsInvalidFcmTokenError(fcmResult.Error)
                    )
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (fcmResult.Error != null)
                        throw new Exception(
                            $"Notification pushed failed ({fcmResult.StatusCode}) {fcmResult.Error}"
                        );
                    break;

                case PushProvider.Apple:
                    var apnsTopicKey = IsVoipPush(notification)
                        ? "Alert"
                        : notification.PushType ?? "Alert";
                    var apnsPushTopic =
                        senders?.Topics.GetValueOrDefault(apnsTopicKey)
                        ?? senders?.DefaultApnsTopic;
                    if (
                        senders is null
                        || string.IsNullOrWhiteSpace(apnsPushTopic)
                        || !senders.ApnsByTopic.TryGetValue(apnsPushTopic, out var appleApns)
                    )
                        throw new InvalidOperationException(
                            "Apple Push Notification Service is not initialized."
                        );

                    var alertDict = new Dictionary<string, object>();
                    if (!string.IsNullOrEmpty(notification.Title))
                        alertDict["title"] = notification.Title;
                    if (!string.IsNullOrEmpty(notification.Subtitle))
                        alertDict["subtitle"] = notification.Subtitle;
                    if (!string.IsNullOrEmpty(notification.Content))
                        alertDict["body"] = notification.Content;

                    var isVoip = false;

                    // VoIP pushes use minimal aps; alert pushes include full alert/sound.
                    var apsDict = isVoip
                        ? new Dictionary<string, object?>()
                        : new Dictionary<string, object?>
                        {
                            ["alert"] = alertDict,
                            ["sound"] = notification.Priority >= 5 ? "default" : null,
                            ["mutable-content"] = 1,
                        };

                    var payload = new Dictionary<string, object?>
                    {
                        ["topic"] = apnsPushTopic,
                        ["type"] = notification.Topic,
                        ["aps"] = apsDict,
                        ["meta"] = notification.Meta,
                    };

                    var apnPushType = isVoip ? ApnPushType.Voip : ApnPushType.Alert;

                    _logger.LogInformation(
                        "Sending Apple push: notificationId={NotificationId}, subscriptionId={SubscriptionId}, deviceId={DeviceId}, pushType={PushType}, apnsPushType={ApnsPushType}, apnsTopic={ApnsTopic}, apsHasAlert={ApsHasAlert}, apsHasSound={ApsHasSound}, metaKeys={MetaKeys}",
                        notification.Id,
                        subscription.Id,
                        subscription.DeviceId,
                        notification.PushType,
                        apnPushType,
                        apnsPushTopic,
                        apsDict.ContainsKey("alert"),
                        apsDict.ContainsKey("sound") && apsDict["sound"] is not null,
                        string.Join(",", notification.Meta.Keys)
                    );

                    var apnResult = await appleApns.SendAsync(
                        payload,
                        deviceToken: subscription.DeviceToken,
                        apnsId: notification.Id.ToString(),
                        apnsPriority: notification.Priority,
                        apnPushType: apnPushType
                    );

                    _logger.LogInformation(
                        "Apple push result: notificationId={NotificationId}, subscriptionId={SubscriptionId}, deviceId={DeviceId}, statusCode={StatusCode}, error={Error}",
                        notification.Id,
                        subscription.Id,
                        subscription.DeviceId,
                        apnResult.StatusCode,
                        apnResult.Error
                    );

                    if (
                        apnResult.StatusCode is 404 or 410
                        || IsInvalidApnsTokenError(apnResult.Error)
                    )
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (apnResult.Error != null)
                        throw new Exception(
                            $"Notification pushed failed ({apnResult.StatusCode}) {apnResult.Error}"
                        );

                    break;

                case PushProvider.Appk:
                    var appkTopic = senders?.Topics.GetValueOrDefault("VoIP");
                    if (
                        senders is null
                        || string.IsNullOrWhiteSpace(appkTopic)
                        || !senders.ApnsByTopic.TryGetValue(appkTopic, out var appkApns)
                    )
                        throw new InvalidOperationException("Apple PushKit is not initialized.");

                    var appkPayload = new Dictionary<string, object?>(notification.Meta)
                    {
                        ["aps"] = new Dictionary<string, object?> { ["content-available"] = 1 },
                    };
                    appkPayload.TryAdd("uuid", notification.Id.ToString());

                    _logger.LogInformation(
                        "Sending Appk VoIP push: notificationId={NotificationId}, subscriptionId={SubscriptionId}, deviceId={DeviceId}, pushType={PushType}, apnsPushType={ApnsPushType}, apnsTopic={ApnsTopic}, apsHasAlert={ApsHasAlert}, apsHasSound={ApsHasSound}, metaKeys={MetaKeys}",
                        notification.Id,
                        subscription.Id,
                        subscription.DeviceId,
                        notification.PushType,
                        ApnPushType.Voip,
                        appkTopic,
                        false,
                        false,
                        string.Join(",", appkPayload.Keys.Where(key => key != "aps"))
                    );

                    var appkResult = await appkApns.SendAsync(
                        appkPayload,
                        deviceToken: subscription.DeviceToken,
                        apnsId: notification.Id.ToString(),
                        apnsPriority: notification.Priority,
                        apnPushType: ApnPushType.Voip
                    );

                    _logger.LogInformation(
                        "Appk VoIP push result: notificationId={NotificationId}, subscriptionId={SubscriptionId}, deviceId={DeviceId}, statusCode={StatusCode}, error={Error}",
                        notification.Id,
                        subscription.Id,
                        subscription.DeviceId,
                        appkResult.StatusCode,
                        appkResult.Error
                    );

                    if (
                        appkResult.StatusCode is 404 or 410
                        || IsInvalidApnsTokenError(appkResult.Error)
                    )
                        _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                    else if (appkResult.Error != null)
                        throw new Exception(
                            $"Notification pushed failed ({appkResult.StatusCode}) {appkResult.Error}"
                        );

                    break;

                case PushProvider.Sop:
                    // SOP delivers via Ring APIs (list + SSE stream), no provider push is needed here.
                    break;

                case PushProvider.UnifiedPush:
                    using (
                        var request = new HttpRequestMessage(
                            HttpMethod.Post,
                            subscription.DeviceToken
                        )
                    )
                    {
                        // Without storing Web Push encryption metadata yet, send a wake-up ping so the client can sync.
                        request.Content = new ByteArrayContent([]);
                        request.Headers.TryAddWithoutValidation("TTL", "60");
                        request.Headers.TryAddWithoutValidation(
                            "Urgency",
                            notification.Priority >= 5 ? "high" : "normal"
                        );

                        var unifiedPushResult = await _httpClient.SendAsync(request);
                        if (
                            unifiedPushResult.StatusCode
                            is HttpStatusCode.NotFound
                                or HttpStatusCode.Gone
                        )
                            _fbs.Enqueue(new PushSubRemovalRequest { SubId = subscription.Id });
                        else if (!unifiedPushResult.IsSuccessStatusCode)
                            throw new Exception(
                                $"Notification push failed ({(int)unifiedPushResult.StatusCode}) {unifiedPushResult.ReasonPhrase}"
                            );
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Push provider not supported: {subscription.Provider}"
                    );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                $"Failed to push notification #{notification.Id} to device {subscription.DeviceId}. {ex.Message}"
            );
            // Swallow here to keep worker alive; upstream is fire-and-forget.
        }

        _logger.LogInformation(
            $"Successfully pushed notification #{notification.Id} to device {subscription.DeviceId} provider {subscription.Provider}"
        );
    }

    private static void BroadcastSopStream(SnNotification notification)
    {
        if (!SopStreams.TryGetValue(notification.AccountId, out var accountStreams))
            return;
        foreach (var stream in accountStreams.Values)
            stream.Channel.Writer.TryWrite(notification);
    }

    private static Dictionary<string, SnNotificationPushSubscription> SelectSubscriptionsByDevice(
        IEnumerable<SnNotificationPushSubscription> subscriptions,
        IReadOnlySet<string> connectedSopDeviceIds,
        SnNotification notification
    )
    {
        var subscriptionList = subscriptions.ToList();
        var hasAnyAppk =
            IsVoipPush(notification) && subscriptionList.Any(s => s.Provider == PushProvider.Appk);

        return subscriptions
            .Where(s => IsProviderCompatibleWithNotification(s.Provider, notification, hasAnyAppk))
            .GroupBy(s => NormalizeSopDeviceId(s.DeviceId))
            .ToDictionary(
                g => g.Key,
                g =>
                    g.OrderByDescending(s =>
                            GetSubscriptionPriority(s, connectedSopDeviceIds, notification)
                        )
                        .First()
            );
    }

    private static int GetSubscriptionPriority(
        SnNotificationPushSubscription subscription,
        IReadOnlySet<string> connectedSopDeviceIds,
        SnNotification notification
    ) =>
        subscription.Provider switch
        {
            PushProvider.Sop => connectedSopDeviceIds.Contains(
                NormalizeSopDeviceId(subscription.DeviceId)
            )
                ? 5
                : 0,
            PushProvider.Appk => IsVoipPush(notification) ? 4 : 0,
            PushProvider.Google => 2,
            PushProvider.UnifiedPush => 1,
            PushProvider.Apple => IsVoipPush(notification) ? 3 : 4,
            _ => 0,
        };

    private static bool IsProviderCompatibleWithNotification(
        PushProvider provider,
        SnNotification notification,
        bool hasAnyAppk
    )
    {
        if (!IsVoipPush(notification))
            return provider != PushProvider.Appk;

        if (hasAnyAppk && provider == PushProvider.Apple)
            return false;

        return true;
    }

    private static bool IsVoipPush(SnNotification notification) =>
        string.Equals(notification.PushType, "VoIP", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlySet<string> GetConnectedSopWebSocketDeviceIds(Guid accountId)
    {
        if (!SopStreams.TryGetValue(accountId, out var accountStreams))
            return new HashSet<string>();

        return accountStreams.Values.Select(s => NormalizeSopDeviceId(s.DeviceId)).ToHashSet();
    }

    private static string NormalizeSopDeviceId(string deviceId)
    {
        const string sopSuffix = ":sop";
        return deviceId.EndsWith(sopSuffix, StringComparison.Ordinal)
            ? deviceId[..^sopSuffix.Length]
            : deviceId;
    }

    internal static bool ShouldQueueSopReplay(
        bool isSavable,
        IReadOnlyCollection<string> connectedSopDeviceIds
    ) => !isSavable && connectedSopDeviceIds.Count == 0;

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
        _db.Notifications.AddRange(
            accounts.Select(a => new SnNotification
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
            })
        );
        await _db.SaveChangesAsync();
    }

    public async Task<(List<SnNotification> Notifications, int TotalCount)> ListSopNotifications(
        Guid accountId,
        int offset,
        int take,
        string? appId = null,
        CancellationToken cancellationToken = default
    )
    {
        var replayNotifications = await _sopReplayBuffer.GetNotifications(accountId);
        var replayIds = replayNotifications.Select(n => n.Id).ToHashSet();
        appId = ResolveAppId(appId, useDefaultIfMissing: true);

        var dbTotalCount = await ApplyNotificationAppFilter(
                _db.Notifications.Where(s => s.AccountId == accountId),
                appId
            )
            .CountAsync(cancellationToken);

        var duplicateCount =
            replayIds.Count == 0
                ? 0
                : await ApplyNotificationAppFilter(
                        _db.Notifications.Where(s => s.AccountId == accountId)
                            .Where(s => replayIds.Contains(s.Id)),
                        appId
                    )
                    .CountAsync(cancellationToken);

        var dbFetchCount = offset + take + replayNotifications.Count;
        var dbNotifications = await ApplyNotificationAppFilter(
                _db.Notifications.Where(s => s.AccountId == accountId),
                appId
            )
            .OrderByDescending(e => e.CreatedAt)
            .Take(dbFetchCount)
            .ToListAsync(cancellationToken);

        var notifications = replayNotifications
            .Concat(dbNotifications)
            .GroupBy(n => n.Id)
            .Select(g => g.OrderByDescending(n => n.CreatedAt).First())
            .OrderByDescending(n => n.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToList();

        return (notifications, dbTotalCount + replayNotifications.Count - duplicateCount);
    }

    public Task RemoveSopReplayNotifications(Guid accountId, IEnumerable<Guid> notificationIds) =>
        _sopReplayBuffer.RemoveNotifications(accountId, notificationIds);
}
