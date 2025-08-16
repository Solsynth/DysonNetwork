using CorePush.Apple;
using CorePush.Firebase;
using DysonNetwork.Pusher.Connection;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Proto;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Threading.Channels;

namespace DysonNetwork.Pusher.Notification;

public class PushService : IDisposable
{
    private readonly AppDatabase _db;
    private readonly FlushBufferService _fbs;
    private readonly WebSocketService _ws;
    private readonly ILogger<PushService> _logger;
    private readonly FirebaseSender? _fcm;
    private readonly ApnSender? _apns;
    private readonly string? _apnsTopic;

    private readonly Channel<PushWorkItem> _channel;
    private readonly int _maxConcurrency;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _workers = new();

    public PushService(
        IConfiguration config,
        AppDatabase db,
        FlushBufferService fbs,
        WebSocketService ws,
        IHttpClientFactory httpFactory,
        ILogger<PushService> logger
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

        _db = db;
        _fbs = fbs;
        _ws = ws;
        _logger = logger;

        // --- Concurrency & channel config ---
        // Defaults: 8 workers, bounded capacity 2000 items.
        _maxConcurrency = Math.Max(1, cfgSection.GetValue<int?>("MaxConcurrency") ?? 8);
        var capacity = Math.Max(1, cfgSection.GetValue<int?>("ChannelCapacity") ?? 2000);

        _channel = Channel.CreateBounded<PushWorkItem>(new BoundedChannelOptions(capacity)
        {
            SingleWriter = false,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait, // apply backpressure instead of dropping
            AllowSynchronousContinuations = false
        });

        // Start background consumers
        for (int i = 0; i < _maxConcurrency; i++)
        {
            _workers.Add(Task.Run(() => WorkerLoop(_cts.Token)));
        }

        _logger.LogInformation("PushService initialized with {Workers} workers and capacity {Capacity}", _maxConcurrency, capacity);
    }

    public void Dispose()
    {
        try
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();
        }
        catch { /* ignore */ }

        try
        {
            Task.WhenAll(_workers).Wait(TimeSpan.FromSeconds(5));
        }
        catch { /* ignore */ }

        _cts.Dispose();
    }

    public async Task UnsubscribeDevice(string deviceId)
    {
        await _db.PushSubscriptions
            .Where(s => s.DeviceId == deviceId)
            .ExecuteDeleteAsync();
    }

    public async Task<PushSubscription> SubscribeDevice(
        string deviceId,
        string deviceToken,
        PushProvider provider,
        Account account
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var accountId = Guid.Parse(account.Id!);

        // Check for existing subscription with same device ID or token
        var existingSubscription = await _db.PushSubscriptions
            .Where(s => s.AccountId == accountId)
            .Where(s => s.DeviceId == deviceId || s.DeviceToken == deviceToken)
            .FirstOrDefaultAsync();

        if (existingSubscription != null)
        {
            existingSubscription.DeviceId = deviceId;
            existingSubscription.DeviceToken = deviceToken;
            existingSubscription.Provider = provider;
            existingSubscription.UpdatedAt = now;

            _db.Update(existingSubscription);
            await _db.SaveChangesAsync();
            return existingSubscription;
        }

        var subscription = new PushSubscription
        {
            DeviceId = deviceId,
            DeviceToken = deviceToken,
            Provider = provider,
            AccountId = accountId,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.PushSubscriptions.Add(subscription);
        await _db.SaveChangesAsync();

        return subscription;
    }

    public async Task SendNotification(Account account,
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
            throw new ArgumentException("Unable to send notification that completely empty.");

        if (actionUri is not null) meta["action_uri"] = actionUri;

        var accountId = Guid.Parse(account.Id!);
        var notification = new Notification
        {
            Topic = topic,
            Title = title,
            Subtitle = subtitle,
            Content = content,
            Meta = meta,
            AccountId = accountId,
        };

        if (save)
            _fbs.Enqueue(notification);

        if (!isSilent)
            await DeliveryNotification(notification); // returns quickly (does NOT wait for APNS/FCM)
    }

    private async Task DeliveryNotification(Notification notification)
    {
        _logger.LogInformation(
            "Delivering notification: {NotificationTopic} #{NotificationId} with meta {NotificationMeta}",
            notification.Topic,
            notification.Id,
            notification.Meta
        );

        // WS send: still immediate (fire-and-forget from caller perspective)
        _ws.SendPacketToAccount(notification.AccountId.ToString(), new Connection.WebSocketPacket
        {
            Type = "notifications.new",
            Data = notification
        });

        // Query subscribers and enqueue push work (non-blocking to the HTTP request)
        var subscribers = await _db.PushSubscriptions
            .Where(s => s.AccountId == notification.AccountId)
            .AsNoTracking()
            .ToListAsync();

        await EnqueuePushWork(notification, subscribers);
    }

    public async Task MarkNotificationsViewed(ICollection<Notification> notifications)
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
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ViewedAt, now));
    }

    public async Task SendNotificationBatch(Notification notification, List<Guid> accounts, bool save = false)
    {
        if (save)
        {
            accounts.ForEach(x =>
            {
                var newNotification = new Notification
                {
                    Topic = notification.Topic,
                    Title = notification.Title,
                    Subtitle = notification.Subtitle,
                    Content = notification.Content,
                    Meta = notification.Meta,
                    Priority = notification.Priority,
                    AccountId = x
                };
                _fbs.Enqueue(newNotification);
            });
        }

        _logger.LogInformation(
            "Delivering notification in batch: {NotificationTopic} #{NotificationId} with meta {NotificationMeta}",
            notification.Topic,
            notification.Id,
            notification.Meta
        );

        // WS first
        foreach (var account in accounts)
        {
            notification.AccountId = account; // keep original behavior
            _ws.SendPacketToAccount(account.ToString(), new Connection.WebSocketPacket
            {
                Type = "notifications.new",
                Data = notification
            });
        }

        // Fetch all subscribers once and enqueue to workers
        var subscribers = await _db.PushSubscriptions
            .Where(s => accounts.Contains(s.AccountId))
            .AsNoTracking()
            .ToListAsync();

        await EnqueuePushWork(notification, subscribers);
    }

    private async Task EnqueuePushWork(Notification notification, IEnumerable<PushSubscription> subscriptions)
    {
        foreach (var sub in subscriptions)
        {
            // Use the current notification reference (no mutation of content after this point).
            var item = new PushWorkItem(notification, sub);

            // Respect backpressure if channel is full.
            await _channel.Writer.WriteAsync(item, _cts.Token);
        }
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            {
                try
                {
                    await _PushSingleNotification(item.Notification, item.Subscription);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Worker handled exception for notification #{Id}", item.Notification.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private readonly record struct PushWorkItem(Notification Notification, PushSubscription Subscription);

    private async Task _PushSingleNotification(Notification notification, PushSubscription subscription)
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

                    if (fcmResult.Error != null)
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
                    if (apnResult.Error != null)
                        throw new Exception($"Notification pushed failed ({apnResult.StatusCode}) {apnResult.Error}");

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
}
