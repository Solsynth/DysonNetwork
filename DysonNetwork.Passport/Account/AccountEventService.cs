using System.Globalization;
using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.EventBus;
using DysonNetwork.Shared.Localization;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Queue;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Extensions;

namespace DysonNetwork.Passport.Account;

public class AccountEventService(
    AppDatabase db,
    ICacheService cache,
    ILocalizationService localizer,
    Leveling.ExperienceService experienceService,
    RemotePaymentService payment,
    RemoteSubscriptionService subscriptions,
    RemoteWebSocketService ws,
    RemoteAccountConnectionService accountConnections,
    RelationshipService relationships,
    DyAgentCompletionService.DyAgentCompletionServiceClient agentCompletion,
    NotableDaysService notableDaysService,
    IEventBus eventBus,
    ILogger<AccountEventService> logger
)
{
    private static readonly Random Random = new();
    private const int FortuneReportVersion = 2;
    private const int FortuneTipTitleMaxLength = 48;
    private const int FortuneTipContentMaxLength = 180;
    private static readonly JsonSerializerOptions FortuneReportJsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
    private const string StatusCacheKey = "account:status:";
    private const string PreviousStatusCacheKey = "account:status:prev:";
    private const string ActivityCacheKey = "account:activities:";

    private async Task<bool> GetAccountIsConnected(Guid userId)
    {
        return await ws.GetWebsocketConnectionStatus(userId.ToString(), true);
    }

    public async Task<Dictionary<string, bool>> GetAccountIsConnectedBatch(List<Guid> userIds)
    {
        return await ws.GetWebsocketConnectionStatusBatch(
            userIds.Select(x => x.ToString()).ToList()
        );
    }

    public void PurgeStatusCache(Guid userId)
    {
        var cacheKey = $"{StatusCacheKey}{userId}";
        cache.RemoveAsync(cacheKey);
        var prevCacheKey = $"{PreviousStatusCacheKey}{userId}";
        cache.RemoveAsync(prevCacheKey);
    }

    public void PurgeActivityCache(Guid userId)
    {
        var cacheKey = $"{ActivityCacheKey}{userId}";
        cache.RemoveAsync(cacheKey);
    }

    private async Task BroadcastPresenceActivitiesUpdated(Guid accountId)
    {
        var activities = await GetActiveActivities(accountId);
        var evt = new AccountPresenceActivitiesUpdatedEvent
        {
            AccountId = accountId,
            Activities = activities,
        };

        await eventBus.PublishAsync(evt);

        var friendIds = await relationships.ListAccountFriends(accountId);
        if (friendIds.Count == 0)
            return;

        await ws.PushWebSocketPacketToUsers(
            friendIds.Select(id => id.ToString()).ToList(),
            WebSocketPacketType.AccountPresenceActivitiesUpdated,
            InfraObjectCoder
                .ConvertObjectToByteString(
                    new Dictionary<string, object>
                    {
                        ["account_id"] = accountId,
                        ["activities"] = activities,
                        ["timestamp"] = evt.Timestamp,
                    }
                )
                .ToByteArray()
        );

        logger.LogDebug(
            "Broadcast account presence activities update for {AccountId} to {FriendCount} friends",
            accountId,
            friendIds.Count
        );
    }

    private static bool PresenceActivityContentEqual(SnPresenceActivity a, SnPresenceActivity b)
    {
        return a.Type == b.Type
            && a.ManualId == b.ManualId
            && a.Title == b.Title
            && a.Subtitle == b.Subtitle
            && a.Caption == b.Caption
            && a.LargeImage == b.LargeImage
            && a.SmallImage == b.SmallImage
            && a.TitleUrl == b.TitleUrl
            && a.SubtitleUrl == b.SubtitleUrl
            && JsonSerializer.Serialize(a.Meta, InfraObjectCoder.SerializerOptions)
                == JsonSerializer.Serialize(b.Meta, InfraObjectCoder.SerializerOptions);
    }

    private static SnPresenceActivity ClonePresenceActivity(SnPresenceActivity activity)
    {
        return new SnPresenceActivity
        {
            Id = activity.Id,
            AccountId = activity.AccountId,
            Type = activity.Type,
            ManualId = activity.ManualId,
            Title = activity.Title,
            Subtitle = activity.Subtitle,
            Caption = activity.Caption,
            LargeImage = activity.LargeImage,
            SmallImage = activity.SmallImage,
            TitleUrl = activity.TitleUrl,
            SubtitleUrl = activity.SubtitleUrl,
            Meta = activity.Meta is null ? null : new Dictionary<string, object>(activity.Meta),
            LeaseExpiresAt = activity.LeaseExpiresAt,
            DeletedAt = activity.DeletedAt,
        };
    }

    private static bool IsActivePresenceActivity(SnPresenceActivity activity, Instant now)
    {
        return activity.LeaseExpiresAt > now && activity.DeletedAt == null;
    }

    public async Task<SnAccountStatus?> GetPreviousStatus(Guid userId)
    {
        return await cache.GetAsync<SnAccountStatus>($"{PreviousStatusCacheKey}{userId}");
    }

    private static bool IsInvisibleStatus(SnAccountStatus status) =>
        status.Type == StatusType.Invisible;

    public async Task<SnAccountStatus> GetStatus(Guid userId)
    {
        var cacheKey = $"{StatusCacheKey}{userId}";
        var cachedStatus = await cache.GetAsync<SnAccountStatus>(cacheKey);
        SnAccountStatus? status;
        if (cachedStatus is not null)
        {
            cachedStatus!.IsOnline =
                !IsInvisibleStatus(cachedStatus) && await GetAccountIsConnected(userId);
            status = cachedStatus;
        }
        else
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            status = await db
                .AccountStatuses.Where(e => e.AccountId == userId)
                .Where(e => e.ClearedAt == null || e.ClearedAt > now)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync();
            var isOnline = await GetAccountIsConnected(userId);
            if (status is not null)
            {
                status.IsOnline = !IsInvisibleStatus(status) && isOnline;
                await cache.SetWithGroupsAsync(
                    cacheKey,
                    status,
                    [$"{AccountService.AccountCachePrefix}{status.AccountId}"],
                    TimeSpan.FromMinutes(5)
                );
            }
            else
            {
                if (isOnline)
                {
                    status = new SnAccountStatus
                    {
                        Attitude = Shared.Models.StatusAttitude.Neutral,
                        IsOnline = true,
                        IsCustomized = false,
                        Label = "Online",
                        AccountId = userId,
                    };
                }
                else
                {
                    status = new SnAccountStatus
                    {
                        Attitude = Shared.Models.StatusAttitude.Neutral,
                        IsOnline = false,
                        IsCustomized = false,
                        Label = "Offline",
                        AccountId = userId,
                    };
                }
            }
        }

        await cache.SetAsync($"{PreviousStatusCacheKey}{userId}", status, TimeSpan.FromMinutes(5));

        return status;
    }

    public async Task<Dictionary<Guid, SnAccountStatus>> GetStatuses(List<Guid> userIds)
    {
        var results = new Dictionary<Guid, SnAccountStatus>();
        var cacheMissUserIds = new List<Guid>();

        foreach (var userId in userIds)
        {
            var cacheKey = $"{StatusCacheKey}{userId}";
            var cachedStatus = await cache.GetAsync<SnAccountStatus>(cacheKey);
            if (cachedStatus != null)
            {
                cachedStatus.IsOnline =
                    !IsInvisibleStatus(cachedStatus) && await GetAccountIsConnected(userId);
                results[userId] = cachedStatus;
            }
            else
            {
                cacheMissUserIds.Add(userId);
            }
        }

        if (cacheMissUserIds.Count == 0)
            return results;
        {
            var now = SystemClock.Instance.GetCurrentInstant();
            var statusesFromDb = await db
                .AccountStatuses.Where(e => cacheMissUserIds.Contains(e.AccountId))
                .Where(e => e.ClearedAt == null || e.ClearedAt > now)
                .GroupBy(e => e.AccountId)
                .Select(g => g.OrderByDescending(e => e.CreatedAt).First())
                .ToListAsync();

            var foundUserIds = new HashSet<Guid>();

            foreach (var status in statusesFromDb)
            {
                var isOnline = await GetAccountIsConnected(status.AccountId);
                status.IsOnline = !IsInvisibleStatus(status) && isOnline;
                results[status.AccountId] = status;
                var cacheKey = $"{StatusCacheKey}{status.AccountId}";
                await cache.SetAsync(cacheKey, status, TimeSpan.FromMinutes(5));
                foundUserIds.Add(status.AccountId);
            }

            var usersWithoutStatus = cacheMissUserIds.Except(foundUserIds).ToList();
            if (usersWithoutStatus.Count == 0)
                return results;
            {
                foreach (var userId in usersWithoutStatus)
                {
                    var isOnline = await GetAccountIsConnected(userId);
                    var defaultStatus = new SnAccountStatus
                    {
                        Attitude = Shared.Models.StatusAttitude.Neutral,
                        IsOnline = isOnline,
                        IsCustomized = false,
                        Label = isOnline ? "Online" : "Offline",
                        AccountId = userId,
                    };
                    results[userId] = defaultStatus;
                }
            }
        }

        return results;
    }

    public async Task<SnAccountStatus> CreateStatus(SnAccount user, SnAccountStatus status)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        await db
            .AccountStatuses.Where(x =>
                x.AccountId == user.Id && (x.ClearedAt == null || x.ClearedAt > now)
            )
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClearedAt, now));

        db.AccountStatuses.Add(status);
        await db.SaveChangesAsync();

        return status;
    }

    public async Task ClearStatus(SnAccount user, SnAccountStatus status)
    {
        status.ClearedAt = SystemClock.Instance.GetCurrentInstant();
        db.Update(status);
        await db.SaveChangesAsync();
        PurgeStatusCache(user.Id);
    }

    private const int FortuneTipCount = 14; // This will be the max index for each type (positive/negative)
    private const string CaptchaCacheKey = "checkin:captcha:";
    private const int CaptchaProbabilityPercent = 20;

    public async Task<bool> CheckInDailyDoAskCaptcha(SnAccount user)
    {
        var perkSubscription = await subscriptions.GetPerkSubscription(user.Id);
        if (perkSubscription is not null)
            return false;

        var cacheKey = $"{CaptchaCacheKey}{user.Id}";
        var needsCaptcha = await cache.GetAsync<bool?>(cacheKey);
        if (needsCaptcha is not null)
            return needsCaptcha!.Value;

        var result = Random.Next(100) < CaptchaProbabilityPercent;
        await cache.SetAsync(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }

    public async Task<bool> CheckInDailyIsAvailable(SnAccount user)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var lastCheckIn = await db
            .AccountCheckInResults.Where(x => x.AccountId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (lastCheckIn == null)
            return true;

        var lastDate = lastCheckIn.CreatedAt.InUtc().Date;
        var currentDate = now.InUtc().Date;

        return lastDate < currentDate;
    }

    public async Task<bool> CheckInBackdatedIsAvailable(SnAccount user, Instant backdated)
    {
        var aDay = Duration.FromDays(1);
        var backdatedStart = backdated.ToDateTimeUtc().Date.ToInstant();
        var backdatedEnd = backdated.Plus(aDay).ToDateTimeUtc().Date.ToInstant();

        var backdatedDate = backdated.ToDateTimeUtc();
        var backdatedMonthStart = new DateTime(
            backdatedDate.Year,
            backdatedDate.Month,
            1,
            0,
            0,
            0
        ).ToInstant();
        var backdatedMonthEnd = new DateTime(
            backdatedDate.Year,
            backdatedDate.Month,
            DateTime.DaysInMonth(backdatedDate.Year, backdatedDate.Month),
            23,
            59,
            59
        ).ToInstant();

        // The first check, if that day already has a check-in
        var lastCheckIn = await db
            .AccountCheckInResults.Where(x => x.AccountId == user.Id)
            .Where(x => x.CreatedAt >= backdatedStart && x.CreatedAt < backdatedEnd)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
        if (lastCheckIn is not null)
            return false;

        // The second check, is the user reached the max backdated check-ins limit,
        // which is once a week, which is 4 times a month
        var backdatedCheckInMonths = await db
            .AccountCheckInResults.Where(x => x.AccountId == user.Id)
            .Where(x => x.CreatedAt >= backdatedMonthStart && x.CreatedAt < backdatedMonthEnd)
            .Where(x => x.BackdatedFrom != null)
            .CountAsync();
        return backdatedCheckInMonths < 4;
    }

    private const string CheckInLockKey = "checkin:lock:";

    public async Task<SnCheckInResult> CheckInDaily(
        SnAccount account,
        Instant? backdated = null,
        int version = 1
    )
    {
        var lockKey = $"{CheckInLockKey}{account.Id}";

        try
        {
            var lk = await cache.AcquireLockAsync(
                lockKey,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMilliseconds(100)
            );

            if (lk != null)
                await lk.ReleaseAsync();
        }
        catch
        {
            // Ignore errors from this pre-check
        }

        // Now try to acquire the lock properly
        await using var lockObj =
            await cache.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(5))
            ?? throw new InvalidOperationException("Check-in was in progress.");

        var accountProfile = await db
            .AccountProfiles.Where(x => x.AccountId == account.Id)
            .Select(x => new { x.Birthday, x.TimeZone })
            .FirstOrDefaultAsync();

        var accountBirthday = accountProfile?.Birthday;

        var now = SystemClock.Instance.GetCurrentInstant();

        var userTimeZone = DateTimeZone.Utc;
        if (!string.IsNullOrEmpty(accountProfile?.TimeZone))
        {
            userTimeZone =
                DateTimeZoneProviders.Tzdb.GetZoneOrNull(accountProfile.TimeZone)
                ?? DateTimeZone.Utc;
        }

        var todayInUserTz = now.InZone(userTimeZone).Date;
        var birthdayDate = accountBirthday?.InZone(userTimeZone).Date;

        var isBirthday =
            birthdayDate.HasValue
            && birthdayDate.Value.Month == todayInUserTz.Month
            && birthdayDate.Value.Day == todayInUserTz.Day;
        List<SnUserCalendarEvent> publicEvents = [];
        List<NotableDay> notableDays = [];
        if (version >= FortuneReportVersion)
        {
            publicEvents = await GetPublicEventsForDate(account.Id, todayInUserTz, userTimeZone);
            notableDays = await GetNotableDaysForDate(account.Region, todayInUserTz);
        }

        List<CheckInFortuneTip> tips;
        CheckInResultLevel checkInLevel;

        if (isBirthday)
        {
            // Skip random logic and tips generation for birthday
            checkInLevel = CheckInResultLevel.Special;
            tips =
            [
                new CheckInFortuneTip
                {
                    IsPositive = true,
                    Title = localizer.Get("fortuneTipSpecialTitleBirthday"),
                    Content = localizer.Get(
                        "fortuneTipSpecialContentBirthday",
                        args: new { account.Nick }
                    ),
                },
            ];
        }
        else
        {
            // Generate 2 positive tips
            var positiveIndices = Enumerable
                .Range(1, FortuneTipCount)
                .OrderBy(_ => Random.Next())
                .Take(2)
                .ToList();
            tips = positiveIndices
                .Select(index => new CheckInFortuneTip
                {
                    IsPositive = true,
                    Title = localizer.Get($"fortuneTipPositiveTitle{index}", account.Language),
                    Content = localizer.Get($"fortuneTipPositiveContent{index}", account.Language),
                })
                .ToList();

            // Generate 2 negative tips
            var negativeIndices = Enumerable
                .Range(1, FortuneTipCount)
                .Except(positiveIndices)
                .OrderBy(_ => Random.Next())
                .Take(2)
                .ToList();
            tips.AddRange(
                negativeIndices.Select(index => new CheckInFortuneTip
                {
                    IsPositive = false,
                    Title = localizer.Get($"fortuneTipNegativeTitle{index}", account.Language),
                    Content = localizer.Get($"fortuneTipNegativeContent{index}", account.Language),
                })
            );

            // The 5 is specialized, keep it alone.
            // Use weighted random distribution to make all levels reasonably achievable
            // Weights: Worst: 10%, Worse: 20%, Normal: 40%, Better: 20%, Best: 10%
            var randomValue = Random.Next(100);
            checkInLevel = randomValue switch
            {
                < 10 => CheckInResultLevel.Worst, // 0-9: 10% chance
                < 30 => CheckInResultLevel.Worse, // 10-29: 20% chance
                < 70 => CheckInResultLevel.Normal, // 30-69: 40% chance
                < 90 => CheckInResultLevel.Better, // 70-89: 20% chance
                _ => CheckInResultLevel.Best, // 90-99: 10% chance
            };
        }

        var finalTips = tips;
        CheckInFortuneReport? fortuneReport = null;
        if (version >= FortuneReportVersion)
        {
            var generation = await GenerateCheckInFortune(
                account,
                todayInUserTz,
                isBirthday,
                backdated.HasValue,
                checkInLevel,
                tips,
                publicEvents,
                notableDays
            );
            finalTips = generation.Tips;
            fortuneReport = generation.Report;
        }

        var result = new SnCheckInResult
        {
            Tips = finalTips,
            Level = checkInLevel,
            FortuneReport = fortuneReport,
            AccountId = account.Id,
            RewardExperience = 100,
            RewardPoints = backdated.HasValue ? null : 10,
            BackdatedFrom = backdated.HasValue ? SystemClock.Instance.GetCurrentInstant() : null,
            CreatedAt = backdated ?? SystemClock.Instance.GetCurrentInstant(),
        };

        try
        {
            if (result.RewardPoints.HasValue)
                await payment.CreateTransactionWithAccount(
                    null,
                    account.Id.ToString(),
                    WalletCurrency.SourcePoint,
                    result.RewardPoints.Value.ToString(CultureInfo.InvariantCulture),
                    $"Check-in reward on {now:yyyy/MM/dd}"
                );
        }
        catch
        {
            result.RewardPoints = null;
        }

        db.AccountCheckInResults.Add(result);
        await db.SaveChangesAsync(); // Remember to save changes to the database
        if (result.RewardExperience is not null)
            await experienceService.AddRecord(
                "check-in",
                $"Check-in reward on {now:yyyy/MM/dd}",
                result.RewardExperience.Value,
                account.Id
            );

        // The lock will be automatically released by the await using statement
        return result;
    }

    public SnCheckInResult PrepareCheckInResultForResponse(
        SnAccount account,
        SnCheckInResult result,
        int version = FortuneReportVersion
    )
    {
        result.FortuneReport = CompleteFortuneReport(account, result);
        return result;
    }

    private async Task<CheckInFortuneGeneration> GenerateCheckInFortune(
        SnAccount account,
        LocalDate checkInDate,
        bool isBirthday,
        bool isBackdated,
        CheckInResultLevel level,
        List<CheckInFortuneTip> tips,
        List<SnUserCalendarEvent> publicEvents,
        List<NotableDay> notableDays
    )
    {
        var fallback = new CheckInFortuneGeneration
        {
            Tips = tips,
            Report = CreateFallbackFortuneReport(account, isBirthday, level, tips),
        };
        try
        {
            var response = await agentCompletion.CompleteAsync(
                new DyAgentCompletionRequest
                {
                    Persona = DyAgentPersona.Michan,
                    AccountId = account.Id.ToString(),
                    Topic = $"每日签到运势 v{FortuneReportVersion}",
                    UserMessage = BuildCheckInFortunePrompt(
                        account,
                        checkInDate,
                        isBirthday,
                        isBackdated,
                        level,
                        tips,
                        publicEvents,
                        notableDays
                    ),
                    ReasoningEffort = "none",
                    Thinking = false,
                    EnableTools = false,
                },
                deadline: DateTime.UtcNow.AddSeconds(30)
            );

            var generation = TryParseFortuneGeneration(response.Content);
            if (generation is not null)
                return generation;

            logger.LogWarning(
                "MiChan returned an invalid check-in fortune generation for {AccountId}",
                account.Id
            );
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to generate MiChan check-in fortune report for {AccountId}",
                account.Id
            );
        }

        return fallback;
    }

    private static string BuildCheckInFortunePrompt(
        SnAccount account,
        LocalDate checkInDate,
        bool isBirthday,
        bool isBackdated,
        CheckInResultLevel level,
        List<CheckInFortuneTip> tips,
        List<SnUserCalendarEvent> publicEvents,
        List<NotableDay> notableDays
    )
    {
        // An random number to avoid the prefix matching cache from the LLM provider
        var rng = Random.Shared.Next(1, 100);

        var drawLabel = GetFortuneDrawLabel(level);
        var outputLanguage = string.IsNullOrWhiteSpace(account.Language)
            ? "zh-Hans"
            : account.Language;
        var requestTimestamp = SystemClock
            .Instance.GetCurrentInstant()
            .ToDateTimeUtc()
            .ToString("O", CultureInfo.InvariantCulture);
        var variation = CreateFortuneVariation();
        var tipLines = string.Join(
            "\n",
            tips.Select(t => $"- {(t.IsPositive ? "吉" : "忌")}：{t.Title}｜{t.Content}")
        );
        var eventLines =
            publicEvents.Count == 0
                ? "- 无公开个人事件"
                : string.Join(
                    "\n",
                    publicEvents.Select(e =>
                        $"- {e.Title}{(string.IsNullOrWhiteSpace(e.Description) ? string.Empty : $"：{e.Description}")}"
                    )
                );
        var notableDayLines =
            notableDays.Count == 0
                ? "- 无全球或地区节日"
                : string.Join(
                    "\n",
                    notableDays.Select(d =>
                        $"- {d.LocalName ?? d.GlobalName ?? d.LocalizableKey ?? "未命名纪念日"}{(string.IsNullOrWhiteSpace(d.GlobalName) || d.GlobalName == d.LocalName ? string.Empty : $" / {d.GlobalName}")}"
                    )
                );
        return $$"""
请求生成时间：{{requestTimestamp}}

你是巫女咩酱。请保持系统人格文件中定义的咩酱说话方式和边界，
正在为用户「{{account.Nick}}」写今日「{{checkInDate:yyyy-MM-dd}}」签到运势。
用户抽中了 {{rng}} 号签

用户信息：
- ID: {{account.Id}}
- 昵称：{{account.Nick}}
- 用户名：@{{account.Name}}
- 语言：{{account.Language ?? "unknown"}}
- 地区：{{account.Region ?? "unknown"}}
- 今天是否生日：{{(isBirthday ? "是" : "否")}}
- 是否补签：{{(isBackdated ? "是" : "否")}}
- 用户今日抽到了：{{drawLabel}}
- 程序化运势等级：{{level}}
- 当前程序化签文版本：{{FortuneReportVersion}}
- 本次生成变化锚点：{{variation}}
- 今日公开个人事件：
{{eventLines}}
- 今日全球或地区节日：
{{notableDayLines}}

请以咩酱本人的口吻，生成一份私人化的今日签文和提示。
它可以有轻微仪式感，但不要过于严肃古板，也不要只复述基础提示。
关于建议不能过于模糊，最好具体一点。

只输出 JSON，不要 Markdown，不要解释。字段必须全部存在，version 必须是 {{FortuneReportVersion}}：
{
  "tips": [
    { "is_positive": true, "title": "吉提示标题，16字以内", "content": "具体提示，60字以内" },
    { "is_positive": true, "title": "吉提示标题，16字以内", "content": "具体提示，60字以内" },
    { "is_positive": false, "title": "忌提示标题，16字以内", "content": "具体提醒，60字以内" },
    { "is_positive": false, "title": "忌提示标题，16字以内", "content": "具体提醒，60字以内" }
  ],
  "fortune_report": {
    "version": {{FortuneReportVersion}},
    "poem": "签诗，1到2句，有意象但自然",
    "summary": "运势总评，80字以内",
    "summary_detail": "今日建议，180到260字，像巫女基于签位给用户的具体行动建议，结合今日事件和基础提示",
    "wish": "愿望，60字以内",
    "love": "爱情，60字以内",
    "study": "学业，60字以内",
    "career": "事业，60字以内",
    "health": "健康，60字以内",
    "lost_item": "失物，60字以内",
    "lucky_color": "幸运色，短词",
    "lucky_direction": "幸运方位，短词",
    "lucky_time": "幸运时段，短词",
    "lucky_item": "幸运小物，短词",
    "lucky_action": "今日宜做，60字以内",
    "avoid_action": "今日忌做，60字以内",
    "ritual": "小仪式，80字以内，轻量、可执行、不要迷信化"
  }
}

要求：
- 使用用户偏好语言「{{outputLanguage}}」生成所有面向用户的字符串；如果该语言无法判断，使用简体中文。
- 人格优先级高于任务包装：保持 MiChan 的自然、友好、体贴、略带思考感的表达；不要为了“运势”变成夸张古风、玄学大师、神社巫女或冷冰冰的模板文案。
- 不要承诺真实预测，不要说一定会发生。
- 不要给医疗、法律、金融强建议。
- 语气可以有轻微仪式感，但要像 MiChan 给朋友写的今日提醒，不要恐吓用户。
- 必须尊重“用户今日抽到了”的签位，不能自行更改签位。
- 签位越高越明朗，签位越低越提醒谨慎。
- 可以温柔地结合生日、公开个人事件、全球或地区节日，但不要暴露或推断未提供的私密信息。
- summary_detail 更像今日建议，不要只是扩写总评；要告诉用户今天适合怎么行动、注意什么、把精力放在哪里。
- lucky_* 和 ritual 要有当天的意象感，但保持生活化，不要像抽象模板。
- tips 必须有 4 条，通常 2 吉 2 忌；生日或特别签可以 3 吉 1 忌，但仍必须 4 条。
- tips 要和 fortune_report 互相呼应，但不要逐字重复。
- 本次文案需要围绕“本次生成变化锚点”选择意象、节奏和行动建议，避免复用常见模板句式。
- 每个字段都要具体，不要重复。
- 输出必须是可被 JSON.parse 直接解析的对象。
""";
    }

    private static string CreateFortuneVariation()
    {
        string[] images =
        [
            "晨雾",
            "纸灯",
            "海风",
            "雨后石阶",
            "月影",
            "远钟",
            "窗边绿植",
            "旧书页",
            "暖茶",
            "星砂",
        ];
        string[] rhythms =
        [
            "先收束再推进",
            "先观察再表达",
            "先整理再行动",
            "轻快但不冒进",
            "安静而坚定",
            "留白中找线索",
        ];
        string[] focuses =
        [
            "沟通",
            "收尾",
            "学习",
            "身体感受",
            "人际边界",
            "小型整理",
            "计划校准",
            "情绪降噪",
        ];

        return $"{images[Random.Next(images.Length)]} / {rhythms[Random.Next(rhythms.Length)]} / {focuses[Random.Next(focuses.Length)]} / #{Random.Next(1000, 9999)}";
    }

    private static string GetFortuneDrawLabel(CheckInResultLevel level)
    {
        return level switch
        {
            CheckInResultLevel.Best => "上上签",
            CheckInResultLevel.Better => "上签",
            CheckInResultLevel.Normal => "中签",
            CheckInResultLevel.Worse => "下签",
            CheckInResultLevel.Worst => "下下签",
            CheckInResultLevel.Special => "特别签",
            _ => "中签",
        };
    }

    private static CheckInFortuneGeneration? TryParseFortuneGeneration(string content)
    {
        var json = ExtractJsonObject(content);
        if (json is null)
            return null;

        try
        {
            var generation = JsonSerializer.Deserialize<CheckInFortuneGeneration>(
                json,
                FortuneReportJsonOptions
            );
            if (
                generation is null
                || generation.Tips.Count != 4
                || generation.Tips.Any(t =>
                    string.IsNullOrWhiteSpace(t.Title) || string.IsNullOrWhiteSpace(t.Content)
                )
                || !IsValidFortuneReport(generation.Report)
            )
                return null;

            generation.Tips = generation
                .Tips.Select(t => new CheckInFortuneTip
                {
                    IsPositive = t.IsPositive,
                    Title = TrimFortuneText(t.Title, FortuneTipTitleMaxLength),
                    Content = TrimFortuneText(t.Content, FortuneTipContentMaxLength),
                })
                .ToList();

            var report = generation.Report;
            report.Version = FortuneReportVersion;
            report.Poem = TrimFortuneText(report.Poem, 120);
            report.Summary = TrimFortuneText(report.Summary, 160);
            report.SummaryDetail = TrimFortuneText(report.SummaryDetail, 360);
            report.Wish = TrimFortuneText(report.Wish, 120);
            report.Love = TrimFortuneText(report.Love, 120);
            report.Study = TrimFortuneText(report.Study, 120);
            report.Career = TrimFortuneText(report.Career, 120);
            report.Health = TrimFortuneText(report.Health, 120);
            report.LostItem = TrimFortuneText(report.LostItem, 120);
            report.LuckyColor = TrimFortuneText(report.LuckyColor, 40);
            report.LuckyDirection = TrimFortuneText(report.LuckyDirection, 40);
            report.LuckyTime = TrimFortuneText(report.LuckyTime, 40);
            report.LuckyItem = TrimFortuneText(report.LuckyItem, 40);
            report.LuckyAction = TrimFortuneText(report.LuckyAction, 120);
            report.AvoidAction = TrimFortuneText(report.AvoidAction, 120);
            report.Ritual = TrimFortuneText(report.Ritual, 160);
            return generation;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : null;
    }

    private static bool IsValidFortuneReport(CheckInFortuneReport report)
    {
        return report.Version == FortuneReportVersion
            && !string.IsNullOrWhiteSpace(report.Poem)
            && !string.IsNullOrWhiteSpace(report.Summary)
            && !string.IsNullOrWhiteSpace(report.SummaryDetail)
            && !string.IsNullOrWhiteSpace(report.Wish)
            && !string.IsNullOrWhiteSpace(report.Love)
            && !string.IsNullOrWhiteSpace(report.Study)
            && !string.IsNullOrWhiteSpace(report.Career)
            && !string.IsNullOrWhiteSpace(report.Health)
            && !string.IsNullOrWhiteSpace(report.LostItem)
            && !string.IsNullOrWhiteSpace(report.LuckyColor)
            && !string.IsNullOrWhiteSpace(report.LuckyDirection)
            && !string.IsNullOrWhiteSpace(report.LuckyTime)
            && !string.IsNullOrWhiteSpace(report.LuckyItem)
            && !string.IsNullOrWhiteSpace(report.LuckyAction)
            && !string.IsNullOrWhiteSpace(report.AvoidAction)
            && !string.IsNullOrWhiteSpace(report.Ritual);
    }

    private static string TrimFortuneText(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private class CheckInFortuneGeneration
    {
        public List<CheckInFortuneTip> Tips { get; set; } = [];
        public CheckInFortuneReport Report { get; set; } = null!;
    }

    private async Task<List<SnUserCalendarEvent>> GetPublicEventsForDate(
        Guid accountId,
        LocalDate date,
        DateTimeZone userTimeZone
    )
    {
        var start = date.AtStartOfDayInZone(userTimeZone).ToInstant();
        var end = date.PlusDays(1).AtStartOfDayInZone(userTimeZone).ToInstant();
        var events = await db
            .UserCalendarEvents.AsNoTracking()
            .Where(e =>
                e.AccountId == accountId
                && e.DeletedAt == null
                && e.Visibility == EventVisibility.Public
                && e.EndTime >= start
                && e.StartTime < end
            )
            .OrderBy(e => e.StartTime)
            .Take(5)
            .ToListAsync();

        return ExpandRecurringEvents(events, start, end)
            .Where(e => e.Visibility == EventVisibility.Public)
            .OrderBy(e => e.StartTime)
            .Take(5)
            .ToList();
    }

    private async Task<List<NotableDay>> GetNotableDaysForDate(string? regionCode, LocalDate date)
    {
        var days = await notableDaysService.GetNotableDays(
            date.Year,
            string.IsNullOrWhiteSpace(regionCode) ? "US" : regionCode
        );

        return days.Where(d => d.Date.InUtc().Date == date)
            .OrderBy(d => d.CountryCode is null ? 0 : 1)
            .ThenBy(d => d.GlobalName ?? d.LocalName ?? d.LocalizableKey)
            .Take(5)
            .ToList();
    }

    private static CheckInFortuneReport CreateFallbackFortuneReport(
        SnAccount account,
        bool isBirthday,
        CheckInResultLevel level,
        List<CheckInFortuneTip> tips
    )
    {
        var positiveTip = tips.FirstOrDefault(t => t.IsPositive) ?? tips.FirstOrDefault();
        var negativeTip = tips.FirstOrDefault(t => !t.IsPositive);
        var nick = string.IsNullOrWhiteSpace(account.Nick) ? account.Name : account.Nick;
        var tone = level switch
        {
            CheckInResultLevel.Worst => "云色稍沉，宜慢行守心",
            CheckInResultLevel.Worse => "风声偏紧，宜稳住节奏",
            CheckInResultLevel.Better => "铃音渐明，前路有微光",
            CheckInResultLevel.Best => "晴光入签，所行多得助",
            CheckInResultLevel.Special => "星灯为你而明，今日自有祝福",
            _ => "签影平和，宜顺势而行",
        };

        return new CheckInFortuneReport
        {
            Version = FortuneReportVersion,
            Poem = isBirthday ? "星灯落掌心，花影绕今日。" : "风过签筒静，铃响见微光。",
            Summary =
                $"{nick}，今日{tone}。{positiveTip?.Content ?? "把心放稳，小事也会慢慢顺起来。"}",
            SummaryDetail =
                $"{nick}，今日建议你先把节奏放稳，再选择最值得推进的一件事。{positiveTip?.Content ?? "顺手完成的小事会带来一点确定感。"} 若遇到卡顿，不必急着证明自己，先整理线索、减少分心；把注意力放在能立即收尾的小行动上，会比反复犹豫更有帮助。",
            Wish = positiveTip?.Title ?? "愿望宜从一个小动作开始。",
            Love = "温柔表达比反复猜测更有力量。",
            Study = "适合整理旧知识，细节里会有新线索。",
            Career = "先稳住手边事务，再推进新的判断。",
            Health = "留意休息和饮水，不必把自己逼得太紧。",
            LostItem = negativeTip is null
                ? "先看常用包袋和桌角附近。"
                : "失物多在顺手放下之处，慢慢回想路径。",
            LuckyColor = isBirthday ? "星白色" : "浅青色",
            LuckyDirection = "东南",
            LuckyTime = "午后",
            LuckyItem = "随身钥匙",
            LuckyAction = "把一件拖延的小事收尾。",
            AvoidAction = "避免在情绪上头时立刻做决定。",
            Ritual = "出门前整理桌面一角，给今天留出清爽的开端。",
        };
    }

    private static CheckInFortuneReport CompleteFortuneReport(
        SnAccount account,
        SnCheckInResult result
    )
    {
        var fallback = CreateFallbackFortuneReport(
            account,
            result.Level == CheckInResultLevel.Special,
            result.Level,
            result.Tips
        );
        var report = result.FortuneReport;
        if (report is null)
            return fallback;

        report.Version = FortuneReportVersion;
        report.Poem = PickFortuneText(report.Poem, fallback.Poem);
        report.Summary = PickFortuneText(report.Summary, fallback.Summary);
        report.SummaryDetail = PickFortuneText(report.SummaryDetail, fallback.SummaryDetail);
        report.Wish = PickFortuneText(report.Wish, fallback.Wish);
        report.Love = PickFortuneText(report.Love, fallback.Love);
        report.Study = PickFortuneText(report.Study, fallback.Study);
        report.Career = PickFortuneText(report.Career, fallback.Career);
        report.Health = PickFortuneText(report.Health, fallback.Health);
        report.LostItem = PickFortuneText(report.LostItem, fallback.LostItem);
        report.LuckyColor = PickFortuneText(report.LuckyColor, fallback.LuckyColor);
        report.LuckyDirection = PickFortuneText(report.LuckyDirection, fallback.LuckyDirection);
        report.LuckyTime = PickFortuneText(report.LuckyTime, fallback.LuckyTime);
        report.LuckyItem = PickFortuneText(report.LuckyItem, fallback.LuckyItem);
        report.LuckyAction = PickFortuneText(report.LuckyAction, fallback.LuckyAction);
        report.AvoidAction = PickFortuneText(report.AvoidAction, fallback.AvoidAction);
        report.Ritual = PickFortuneText(report.Ritual, fallback.Ritual);
        return report;
    }

    private static string PickFortuneText(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    public async Task<List<SnPresenceActivity>> GetActiveActivities(Guid userId)
    {
        var cacheKey = $"{ActivityCacheKey}{userId}";
        var cachedActivities = await cache.GetAsync<List<SnPresenceActivity>>(cacheKey);
        if (cachedActivities != null)
        {
            return cachedActivities;
        }

        var now = SystemClock.Instance.GetCurrentInstant();
        var activities = await db
            .PresenceActivities.Where(e =>
                e.AccountId == userId && e.LeaseExpiresAt > now && e.DeletedAt == null
            )
            .ToListAsync();

        await cache.SetWithGroupsAsync(
            cacheKey,
            activities,
            [$"{AccountService.AccountCachePrefix}{userId}"],
            TimeSpan.FromMinutes(1)
        );
        return activities;
    }

    public async Task<Dictionary<Guid, List<SnPresenceActivity>>> GetActiveActivitiesBatch(
        List<Guid> userIds
    )
    {
        var results = new Dictionary<Guid, List<SnPresenceActivity>>();
        var cacheMissUserIds = new List<Guid>();

        // Try to get activities from cache first
        foreach (var userId in userIds)
        {
            var cacheKey = $"{ActivityCacheKey}{userId}";
            var cachedActivities = await cache.GetAsync<List<SnPresenceActivity>>(cacheKey);
            if (cachedActivities != null)
            {
                results[userId] = cachedActivities;
            }
            else
            {
                cacheMissUserIds.Add(userId);
            }
        }

        // If all activities were found in cache, return early
        if (cacheMissUserIds.Count == 0)
            return results;

        // Fetch remaining activities from database in a single query
        var now = SystemClock.Instance.GetCurrentInstant();
        var activitiesFromDb = await db
            .PresenceActivities.Where(e =>
                cacheMissUserIds.Contains(e.AccountId)
                && e.LeaseExpiresAt > now
                && e.DeletedAt == null
            )
            .ToListAsync();

        // Group activities by user ID and update cache
        var activitiesByUser = activitiesFromDb
            .GroupBy(a => a.AccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var userId in cacheMissUserIds)
        {
            var userActivities = activitiesByUser.GetValueOrDefault(
                userId,
                new List<SnPresenceActivity>()
            );
            results[userId] = userActivities;

            // Update cache for this user
            var cacheKey = $"{ActivityCacheKey}{userId}";
            await cache.SetWithGroupsAsync(
                cacheKey,
                userActivities,
                [$"{AccountService.AccountCachePrefix}{userId}"],
                TimeSpan.FromMinutes(1)
            );
        }

        return results;
    }

    public async Task<(List<SnPresenceActivity>, int)> GetAllActivities(
        Guid userId,
        int offset = 0,
        int take = 20
    )
    {
        var query = db.PresenceActivities.Where(e => e.AccountId == userId && e.DeletedAt == null);

        var totalCount = await query.CountAsync();

        var activities = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return (activities, totalCount);
    }

    public async Task<(List<SnAccountStatus>, int)> GetStatusHistory(
        Guid userId,
        int offset = 0,
        int take = 20
    )
    {
        var query = db.AccountStatuses.Where(e => e.AccountId == userId && e.DeletedAt == null);

        var totalCount = await query.CountAsync();

        var statuses = await query
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync();

        return (statuses, totalCount);
    }

    public async Task<(List<AccountTimelineItem>, int)> GetTimeline(
        Guid userId,
        int offset = 0,
        int take = 20
    )
    {
        var statusQuery = db.AccountStatuses.Where(e =>
            e.AccountId == userId && e.DeletedAt == null
        );
        var activityQuery = db.PresenceActivities.Where(e =>
            e.AccountId == userId && e.DeletedAt == null
        );

        var statusCount = await statusQuery.CountAsync();
        var activityCount = await activityQuery.CountAsync();
        var totalCount = statusCount + activityCount;

        var statuses = await statusQuery.OrderByDescending(e => e.CreatedAt).ToListAsync();
        var activities = await activityQuery.OrderByDescending(e => e.CreatedAt).ToListAsync();

        var timelineItems = new List<AccountTimelineItem>();

        foreach (var status in statuses)
        {
            timelineItems.Add(
                new AccountTimelineItem
                {
                    Id = status.Id,
                    CreatedAt = status.CreatedAt,
                    EventType = TimelineEventType.StatusChange,
                    Status = status,
                }
            );
        }

        foreach (var activity in activities)
        {
            timelineItems.Add(
                new AccountTimelineItem
                {
                    Id = activity.Id,
                    CreatedAt = activity.CreatedAt,
                    EventType = TimelineEventType.Activity,
                    Activity = activity,
                }
            );
        }

        var sortedTimeline = timelineItems
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToList();

        return (sortedTimeline, totalCount);
    }

    public async Task<SnPresenceActivity> SetActivity(SnPresenceActivity activity, int leaseMinutes)
    {
        if (leaseMinutes is < 1 or > 60)
            throw new ArgumentException("Lease minutes must be between 1 and 60");

        var now = SystemClock.Instance.GetCurrentInstant();
        activity.LeaseMinutes = leaseMinutes;
        activity.LeaseExpiresAt = now + Duration.FromMinutes(leaseMinutes);

        db.PresenceActivities.Add(activity);
        await db.SaveChangesAsync();

        PurgeActivityCache(activity.AccountId);

        await BroadcastPresenceActivitiesUpdated(activity.AccountId);

        return activity;
    }

    public async Task<SnPresenceActivity> UpdateActivity(
        Guid activityId,
        Guid userId,
        Action<SnPresenceActivity> update,
        int? leaseMinutes = null
    )
    {
        var activity = await db.PresenceActivities.FindAsync(activityId);
        if (activity == null)
            throw new KeyNotFoundException("Activity not found");

        if (activity.AccountId != userId)
            throw new UnauthorizedAccessException("Activity does not belong to user");

        var before = ClonePresenceActivity(activity);
        var wasActive = IsActivePresenceActivity(
            activity,
            SystemClock.Instance.GetCurrentInstant()
        );

        if (leaseMinutes.HasValue)
        {
            if (leaseMinutes.Value < 1 || leaseMinutes.Value > 60)
                throw new ArgumentException("Lease minutes must be between 1 and 60");

            activity.LeaseMinutes = leaseMinutes.Value;
            activity.LeaseExpiresAt =
                SystemClock.Instance.GetCurrentInstant() + Duration.FromMinutes(leaseMinutes.Value);
        }

        update(activity);
        await db.SaveChangesAsync();

        PurgeActivityCache(activity.AccountId);

        if (
            wasActive
                != IsActivePresenceActivity(activity, SystemClock.Instance.GetCurrentInstant())
            || !PresenceActivityContentEqual(before, activity)
        )
            await BroadcastPresenceActivitiesUpdated(activity.AccountId);

        return activity;
    }

    public async Task<SnPresenceActivity?> UpdateActivityByManualId(
        string manualId,
        Guid userId,
        Action<SnPresenceActivity> update,
        int? leaseMinutes = null
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var activity = await db.PresenceActivities.FirstOrDefaultAsync(e =>
            e.ManualId == manualId
            && e.AccountId == userId
            && e.LeaseExpiresAt > now
            && e.DeletedAt == null
        );
        if (activity == null)
            return null;

        var before = ClonePresenceActivity(activity);
        var wasActive = IsActivePresenceActivity(activity, now);

        if (leaseMinutes.HasValue)
        {
            if (leaseMinutes.Value is < 1 or > 60)
                throw new ArgumentException("Lease minutes must be between 1 and 60");

            activity.LeaseMinutes = leaseMinutes.Value;
            activity.LeaseExpiresAt =
                SystemClock.Instance.GetCurrentInstant() + Duration.FromMinutes(leaseMinutes.Value);
        }

        update(activity);
        await db.SaveChangesAsync();

        PurgeActivityCache(activity.AccountId);

        if (
            wasActive
                != IsActivePresenceActivity(activity, SystemClock.Instance.GetCurrentInstant())
            || !PresenceActivityContentEqual(before, activity)
        )
            await BroadcastPresenceActivitiesUpdated(activity.AccountId);

        return activity;
    }

    public async Task<bool> DeleteActivityByManualId(string manualId, Guid userId)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var activity = await db.PresenceActivities.FirstOrDefaultAsync(e =>
            e.ManualId == manualId
            && e.AccountId == userId
            && e.LeaseExpiresAt > now
            && e.DeletedAt == null
        );
        if (activity == null)
            return false;
        var wasActive = IsActivePresenceActivity(activity, now);
        if (activity.LeaseExpiresAt <= now)
        {
            activity.DeletedAt = now;
        }
        else
        {
            activity.LeaseExpiresAt = now;
        }

        db.Update(activity);
        await db.SaveChangesAsync();
        PurgeActivityCache(activity.AccountId);
        if (wasActive)
            await BroadcastPresenceActivitiesUpdated(activity.AccountId);
        return true;
    }

    public async Task<bool> DeleteActivity(Guid activityId, Guid userId)
    {
        var activity = await db.PresenceActivities.FindAsync(activityId);
        if (activity == null)
            return false;

        if (activity.AccountId != userId)
            throw new UnauthorizedAccessException("Activity does not belong to user");

        var now = SystemClock.Instance.GetCurrentInstant();
        var wasActive = IsActivePresenceActivity(activity, now);
        if (activity.LeaseExpiresAt <= now)
        {
            activity.DeletedAt = now;
        }
        else
        {
            activity.LeaseExpiresAt = now;
        }

        db.Update(activity);
        await db.SaveChangesAsync();
        PurgeActivityCache(activity.AccountId);
        if (wasActive)
            await BroadcastPresenceActivitiesUpdated(activity.AccountId);
        return true;
    }

    /// <summary>
    /// Gets all user IDs that have usable connections for the specified presence providers.
    /// </summary>
    public async Task<List<Guid>> GetPresenceConnectedUsersAsync(params string[] providers)
    {
        var providerSet = providers
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim().ToLowerInvariant())
            .ToHashSet();

        if (providerSet.Count == 0)
            return [];

        var accountIds = await db
            .AccountProfiles.AsNoTracking()
            .Select(p => p.AccountId)
            .ToListAsync();

        var connected = new List<Guid>();
        foreach (var accountId in accountIds)
        {
            var hasPresenceConnection = false;
            foreach (var provider in providerSet)
            {
                var connections = await accountConnections.ListConnectionsAsync(
                    accountId,
                    provider
                );
                if (
                    connections.Any(c =>
                        !string.IsNullOrWhiteSpace(c.ProvidedIdentifier)
                        && (
                            !string.IsNullOrWhiteSpace(c.RefreshToken)
                            || !string.IsNullOrWhiteSpace(c.AccessToken)
                        )
                    )
                )
                {
                    hasPresenceConnection = true;
                    break;
                }
            }

            if (hasPresenceConnection)
            {
                connected.Add(accountId);
            }
        }

        return connected;
    }

    #region User Calendar Events

    private const string CalendarEventCacheKeyPrefix = "account:calendar:events:";
    private static readonly TimeSpan CalendarEventCacheDuration = TimeSpan.FromHours(24);

    private string GetCalendarEventCacheKey(Guid accountId, int year, int month)
    {
        return $"{CalendarEventCacheKeyPrefix}{accountId}:{year}:{month}";
    }

    public void PurgeCalendarEventCache(Guid accountId, int? year = null, int? month = null)
    {
        if (year.HasValue && month.HasValue)
        {
            var cacheKey = GetCalendarEventCacheKey(accountId, year.Value, month.Value);
            cache.RemoveAsync(cacheKey);
        }
        else
        {
            // Purge all months for this account (pattern-based removal would require cache support)
            // For now, we'll rely on the specific key pattern when known
            var currentYear = SystemClock.Instance.GetCurrentInstant().InUtc().Year;
            var currentMonth = SystemClock.Instance.GetCurrentInstant().InUtc().Month;
            for (var m = 1; m <= 12; m++)
            {
                cache.RemoveAsync(GetCalendarEventCacheKey(accountId, currentYear, m));
                cache.RemoveAsync(GetCalendarEventCacheKey(accountId, currentYear - 1, m));
                cache.RemoveAsync(GetCalendarEventCacheKey(accountId, currentYear + 1, m));
            }
        }
    }

    public async Task<SnUserCalendarEvent> CreateCalendarEventAsync(
        Guid accountId,
        CreateCalendarEventRequest request
    )
    {
        // Validate request
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required");

        if (request.EndTime <= request.StartTime)
            throw new ArgumentException("End time must be after start time");

        // Default all-day events to 24 hours if not specified
        var endTime = request.EndTime;
        if (request.IsAllDay && request.EndTime == request.StartTime)
        {
            endTime = request.StartTime.Plus(Duration.FromHours(24));
        }

        var calendarEvent = new SnUserCalendarEvent
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Location = request.Location?.Trim(),
            StartTime = request.StartTime,
            EndTime = endTime,
            IsAllDay = request.IsAllDay,
            Visibility = request.Visibility,
            Recurrence = request.Recurrence,
            Meta = request.Meta,
            AccountId = accountId,
        };

        db.UserCalendarEvents.Add(calendarEvent);
        await db.SaveChangesAsync();

        // Purge cache for affected months
        var startMonth = calendarEvent.StartTime.InUtc().Date;
        var endMonth = calendarEvent.EndTime.InUtc().Date;
        PurgeCalendarEventCache(accountId, startMonth.Year, startMonth.Month);
        if (startMonth.Month != endMonth.Month || startMonth.Year != endMonth.Year)
        {
            PurgeCalendarEventCache(accountId, endMonth.Year, endMonth.Month);
        }

        return calendarEvent;
    }

    public async Task<SnUserCalendarEvent> UpdateCalendarEventAsync(
        Guid accountId,
        Guid eventId,
        UpdateCalendarEventRequest request
    )
    {
        var calendarEvent = await db
            .UserCalendarEvents.Where(e => e.Id == eventId && e.DeletedAt == null)
            .FirstOrDefaultAsync();

        if (calendarEvent == null)
            throw new KeyNotFoundException("Calendar event not found");

        if (calendarEvent.AccountId != accountId)
            throw new UnauthorizedAccessException("Calendar event does not belong to user");

        var oldStartTime = calendarEvent.StartTime;
        var oldEndTime = calendarEvent.EndTime;

        // Update properties
        if (request.Title != null)
            calendarEvent.Title = request.Title.Trim();

        if (request.Description != null)
            calendarEvent.Description = string.IsNullOrEmpty(request.Description)
                ? null
                : request.Description.Trim();

        if (request.Location != null)
            calendarEvent.Location = string.IsNullOrEmpty(request.Location)
                ? null
                : request.Location.Trim();

        if (request.StartTime.HasValue)
            calendarEvent.StartTime = request.StartTime.Value;

        if (request.EndTime.HasValue)
            calendarEvent.EndTime = request.EndTime.Value;

        if (request.IsAllDay.HasValue)
            calendarEvent.IsAllDay = request.IsAllDay.Value;

        if (request.Visibility.HasValue)
            calendarEvent.Visibility = request.Visibility.Value;

        if (request.Recurrence != null)
            calendarEvent.Recurrence = request.Recurrence;

        if (request.Meta != null)
            calendarEvent.Meta = request.Meta;

        // Validate times
        if (calendarEvent.EndTime <= calendarEvent.StartTime)
            throw new ArgumentException("End time must be after start time");

        // Default all-day events
        if (calendarEvent.IsAllDay && calendarEvent.EndTime == calendarEvent.StartTime)
        {
            calendarEvent.EndTime = calendarEvent.StartTime.Plus(Duration.FromHours(24));
        }

        calendarEvent.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        // Purge cache for affected months (old and new)
        var oldStartMonth = oldStartTime.InUtc().Date;
        var oldEndMonth = oldEndTime.InUtc().Date;
        var newStartMonth = calendarEvent.StartTime.InUtc().Date;
        var newEndMonth = calendarEvent.EndTime.InUtc().Date;

        PurgeCalendarEventCache(accountId, oldStartMonth.Year, oldStartMonth.Month);
        if (oldStartMonth.Month != oldEndMonth.Month || oldStartMonth.Year != oldEndMonth.Year)
        {
            PurgeCalendarEventCache(accountId, oldEndMonth.Year, oldEndMonth.Month);
        }

        if (newStartMonth.Year != oldStartMonth.Year || newStartMonth.Month != oldStartMonth.Month)
        {
            PurgeCalendarEventCache(accountId, newStartMonth.Year, newStartMonth.Month);
        }
        if (
            (newEndMonth.Year != oldEndMonth.Year || newEndMonth.Month != oldEndMonth.Month)
            && (newEndMonth.Year != newStartMonth.Year || newEndMonth.Month != newStartMonth.Month)
        )
        {
            PurgeCalendarEventCache(accountId, newEndMonth.Year, newEndMonth.Month);
        }

        return calendarEvent;
    }

    public async Task<bool> DeleteCalendarEventAsync(Guid accountId, Guid eventId)
    {
        var calendarEvent = await db
            .UserCalendarEvents.Where(e => e.Id == eventId && e.DeletedAt == null)
            .FirstOrDefaultAsync();

        if (calendarEvent == null)
            return false;

        if (calendarEvent.AccountId != accountId)
            throw new UnauthorizedAccessException("Calendar event does not belong to user");

        calendarEvent.DeletedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        // Purge cache
        var startMonth = calendarEvent.StartTime.InUtc().Date;
        var endMonth = calendarEvent.EndTime.InUtc().Date;
        PurgeCalendarEventCache(accountId, startMonth.Year, startMonth.Month);
        if (startMonth.Month != endMonth.Month || startMonth.Year != endMonth.Year)
        {
            PurgeCalendarEventCache(accountId, endMonth.Year, endMonth.Month);
        }

        return true;
    }

    public async Task<SnUserCalendarEvent?> GetCalendarEventAsync(
        Guid eventId,
        Guid? viewerId = null
    )
    {
        var calendarEvent = await db
            .UserCalendarEvents.Where(e => e.Id == eventId && e.DeletedAt == null)
            .FirstOrDefaultAsync();

        if (calendarEvent == null)
            return null;

        // Check visibility
        if (viewerId.HasValue && viewerId.Value != calendarEvent.AccountId)
        {
            var isVisible = await IsEventVisibleToUserAsync(calendarEvent, viewerId.Value);
            if (!isVisible)
                return null;
        }

        return calendarEvent;
    }

    public async Task<(List<SnUserCalendarEvent>, int)> GetUserCalendarEventsAsync(
        Guid accountId,
        Guid? viewerId = null,
        Instant? startTime = null,
        Instant? endTime = null,
        int offset = 0,
        int take = 50
    )
    {
        var isOwner = viewerId == accountId;

        var query = db.UserCalendarEvents.Where(e =>
            e.AccountId == accountId && e.DeletedAt == null
        );

        // Apply visibility filter if viewer is not the owner
        if (!isOwner && viewerId.HasValue)
        {
            query = query.Where(e =>
                e.Visibility == EventVisibility.Public || e.Visibility == EventVisibility.Friends
            );
        }
        else if (!isOwner)
        {
            // Anonymous viewer - only public events
            query = query.Where(e => e.Visibility == EventVisibility.Public);
        }

        // Apply time range filter
        if (startTime.HasValue)
            query = query.Where(e => e.EndTime >= startTime.Value);

        if (endTime.HasValue)
            query = query.Where(e => e.StartTime <= endTime.Value);

        var totalCount = await query.CountAsync();

        var events = await query.OrderBy(e => e.StartTime).Skip(offset).Take(take).ToListAsync();

        // For Friends visibility, we need to check if viewer is actually a friend
        if (!isOwner && viewerId.HasValue)
        {
            var friendIds = await db
                .AccountRelationships.Where(r =>
                    r.AccountId == accountId
                    && r.RelatedId == viewerId.Value
                    && r.Status == RelationshipStatus.Friends
                )
                .Select(r => r.RelatedId)
                .ToListAsync();

            var isFriend = friendIds.Contains(viewerId.Value);

            // Filter out Friends-only events if not a friend
            events = events
                .Where(e =>
                    e.Visibility == EventVisibility.Public
                    || (e.Visibility == EventVisibility.Friends && isFriend)
                )
                .ToList();
        }

        return (events, totalCount);
    }

    private async Task<bool> IsEventVisibleToUserAsync(
        SnUserCalendarEvent calendarEvent,
        Guid viewerId
    )
    {
        if (calendarEvent.AccountId == viewerId)
            return true;

        if (calendarEvent.Visibility == EventVisibility.Public)
            return true;

        if (calendarEvent.Visibility == EventVisibility.Friends)
        {
            // Check if viewer is a friend
            var isFriend = await db.AccountRelationships.AnyAsync(r =>
                r.AccountId == calendarEvent.AccountId
                && r.RelatedId == viewerId
                && r.Status == RelationshipStatus.Friends
            );
            return isFriend;
        }

        return false;
    }

    private List<SnUserCalendarEvent> ExpandRecurringEvents(
        List<SnUserCalendarEvent> events,
        Instant rangeStart,
        Instant rangeEnd
    )
    {
        var expandedEvents = new List<SnUserCalendarEvent>();

        foreach (var evt in events)
        {
            if (evt.Recurrence == null || evt.Recurrence.Frequency == RecurrenceFrequency.None)
            {
                // Non-recurring event - check if in range
                if (evt.EndTime >= rangeStart && evt.StartTime <= rangeEnd)
                {
                    expandedEvents.Add(evt);
                }
                continue;
            }

            // Expand recurring events
            var occurrences = GetRecurringEventOccurrences(evt, rangeStart, rangeEnd);
            expandedEvents.AddRange(occurrences);
        }

        return expandedEvents;
    }

    private List<SnUserCalendarEvent> GetRecurringEventOccurrences(
        SnUserCalendarEvent evt,
        Instant rangeStart,
        Instant rangeEnd
    )
    {
        var occurrences = new List<SnUserCalendarEvent>();
        var recurrence = evt.Recurrence!;

        var maxOccurrences = recurrence.Occurrences ?? 365; // Default max 1 year of daily events
        var endDate = recurrence.EndDate ?? rangeEnd;
        var actualEnd = Instant.Min(endDate, rangeEnd);

        var currentStart = evt.StartTime;
        var currentEnd = evt.EndTime;
        var occurrenceCount = 0;

        while (currentStart < actualEnd && occurrenceCount < maxOccurrences)
        {
            // Check if this occurrence falls within the range
            if (currentEnd >= rangeStart && currentStart <= rangeEnd)
            {
                var occurrence = new SnUserCalendarEvent
                {
                    Id = evt.Id,
                    Title = evt.Title,
                    Description = evt.Description,
                    Location = evt.Location,
                    StartTime = currentStart,
                    EndTime = currentEnd,
                    IsAllDay = evt.IsAllDay,
                    Visibility = evt.Visibility,
                    Recurrence = null, // Mark as instance, not recurring
                    Meta = evt.Meta,
                    AccountId = evt.AccountId,
                    CreatedAt = evt.CreatedAt,
                    UpdatedAt = evt.UpdatedAt,
                };
                occurrences.Add(occurrence);
            }

            occurrenceCount++;

            // Calculate next occurrence
            (currentStart, currentEnd) = CalculateNextOccurrence(
                currentStart,
                currentEnd,
                recurrence
            );

            if (currentStart == default)
                break;
        }

        return occurrences;
    }

    private (Instant start, Instant end) CalculateNextOccurrence(
        Instant currentStart,
        Instant currentEnd,
        RecurrencePattern recurrence
    )
    {
        var duration = currentEnd - currentStart;
        Instant nextStart;

        switch (recurrence.Frequency)
        {
            case RecurrenceFrequency.Daily:
                nextStart = currentStart.Plus(Duration.FromDays(recurrence.Interval));
                break;

            case RecurrenceFrequency.Weekly:
                if (recurrence.DaysOfWeek?.Any() == true)
                {
                    // Find next day of week
                    var currentDay = currentStart.InUtc().DayOfWeek;
                    var daysOfWeek = recurrence.DaysOfWeek.OrderBy(d => d).ToList();

                    // Find next day in the list
                    var nextDay = daysOfWeek.FirstOrDefault(d => d > currentDay);
                    if (nextDay == default)
                    {
                        // Wrap to next week
                        nextDay = daysOfWeek.First();
                        var daysUntilNext = (7 - (int)currentDay) + (int)nextDay;
                        nextStart = currentStart.Plus(Duration.FromDays(daysUntilNext));
                    }
                    else
                    {
                        var daysUntilNext = (int)nextDay - (int)currentDay;
                        nextStart = currentStart.Plus(Duration.FromDays(daysUntilNext));
                    }
                }
                else
                {
                    nextStart = currentStart.Plus(Duration.FromDays(7 * recurrence.Interval));
                }
                break;

            case RecurrenceFrequency.Monthly:
                var currentDate = currentStart.InUtc().Date;
                var nextMonth = currentDate.PlusMonths(recurrence.Interval);
                var dayOfMonth = recurrence.DayOfMonth ?? currentDate.Day;
                var maxDay = DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month);
                dayOfMonth = Math.Min(dayOfMonth, maxDay);
                var nextLocalDate = new LocalDate(nextMonth.Year, nextMonth.Month, dayOfMonth);
                nextStart = nextLocalDate.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
                break;

            case RecurrenceFrequency.Yearly:
                var currentYearDate = currentStart.InUtc().Date;
                var nextYear = currentYearDate.PlusYears(recurrence.Interval);
                var targetMonth = recurrence.MonthOfYear ?? currentYearDate.Month;
                var targetDay = recurrence.DayOfMonth ?? currentYearDate.Day;
                var maxDayOfMonth = DateTime.DaysInMonth(nextYear.Year, targetMonth);
                targetDay = Math.Min(targetDay, maxDayOfMonth);
                var nextYearLocalDate = new LocalDate(nextYear.Year, targetMonth, targetDay);
                nextStart = nextYearLocalDate.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();
                break;

            default:
                return (default, default);
        }

        var nextEnd = nextStart.Plus(duration);
        return (nextStart, nextEnd);
    }

    public async Task<List<DailyEventResponse>> GetEventCalendar(
        SnAccount user,
        int month,
        int year = 0,
        bool replaceInvisible = false,
        Guid? viewerId = null,
        string? regionCode = null
    )
    {
        if (year == 0)
            year = SystemClock.Instance.GetCurrentInstant().InUtc().Date.Year;

        // Create start and end dates for the specified month
        var startOfMonth = new LocalDate(year, month, 1)
            .AtStartOfDayInZone(DateTimeZone.Utc)
            .ToInstant();
        var endOfMonth = startOfMonth.Plus(Duration.FromDays(DateTime.DaysInMonth(year, month)));

        // Determine the effective viewer
        var effectiveViewerId = viewerId ?? user.Id;
        var isOwner = effectiveViewerId == user.Id;

        // Fetch statuses
        var statuses = await db
            .AccountStatuses.AsNoTracking()
            .TagWith("eventcal:statuses")
            .Where(x =>
                x.AccountId == user.Id && x.CreatedAt >= startOfMonth && x.CreatedAt < endOfMonth
            )
            .Select(x => new SnAccountStatus
            {
                Id = x.Id,
                Attitude = x.Attitude,
                Type =
                    replaceInvisible && x.Type == StatusType.Invisible
                        ? StatusType.Default
                        : x.Type,
                Label = x.Label,
                Symbol = x.Symbol,
                ClearedAt = x.ClearedAt,
                AccountId = x.AccountId,
                CreatedAt = x.CreatedAt,
            })
            .OrderBy(x => x.CreatedAt)
            .ToListAsync();

        // Fetch check-ins
        var checkIn = await db
            .AccountCheckInResults.AsNoTracking()
            .TagWith("eventcal:checkin")
            .Where(x =>
                x.AccountId == user.Id && x.CreatedAt >= startOfMonth && x.CreatedAt < endOfMonth
            )
            .ToListAsync();
        foreach (var item in checkIn)
            PrepareCheckInResultForResponse(user, item);

        // Fetch user calendar events with visibility filtering
        var userEventsQuery = db
            .UserCalendarEvents.AsNoTracking()
            .TagWith("eventcal:userevents")
            .Where(x =>
                x.AccountId == user.Id
                && x.DeletedAt == null
                && x.EndTime >= startOfMonth
                && x.StartTime < endOfMonth
            );

        // Apply visibility filter
        if (!isOwner)
        {
            userEventsQuery = userEventsQuery.Where(x =>
                x.Visibility == EventVisibility.Public || x.Visibility == EventVisibility.Friends
            );
        }

        var userEvents = await userEventsQuery.ToListAsync();

        // For Friends visibility, check friendship status
        if (!isOwner)
        {
            var isFriend = await db.AccountRelationships.AnyAsync(r =>
                r.AccountId == user.Id
                && r.RelatedId == effectiveViewerId
                && r.Status == RelationshipStatus.Friends
            );

            userEvents = userEvents
                .Where(e =>
                    e.Visibility == EventVisibility.Public
                    || (e.Visibility == EventVisibility.Friends && isFriend)
                )
                .ToList();
        }

        // Expand recurring events
        var expandedEvents = ExpandRecurringEvents(userEvents, startOfMonth, endOfMonth);

        // Map to DTOs
        var userEventDtos = expandedEvents
            .Select(e => new UserCalendarEventDto
            {
                Id = e.Id,
                Title = e.Title,
                Description = e.Description,
                Location = e.Location,
                StartTime = e.StartTime,
                EndTime = e.EndTime,
                IsAllDay = e.IsAllDay,
                Visibility = e.Visibility,
                Recurrence = e.Recurrence,
                Meta = e.Meta,
                AccountId = e.AccountId,
                CreatedAt = e.CreatedAt,
                UpdatedAt = e.UpdatedAt,
            })
            .ToList();

        // Fetch notable days
        var notableDays = new List<NotableDay>();
        if (!string.IsNullOrWhiteSpace(regionCode))
        {
            // This will be populated by the caller using NotableDaysService
            // We return empty here and the controller will merge
        }

        // Group data by date
        var dates = Enumerable
            .Range(1, DateTime.DaysInMonth(year, month))
            .Select(day =>
                new LocalDate(year, month, day).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant()
            )
            .ToList();

        var statusesByDate = statuses
            .GroupBy(s => s.CreatedAt.InUtc().Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        var checkInByDate = checkIn
            .GroupBy(c => c.CreatedAt.InUtc().Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.CreatedAt).First());

        var eventsByDate = userEventDtos
            .GroupBy(e => e.StartTime.InUtc().Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        return dates
            .Select(date =>
            {
                var utcDate = date.InUtc().Date;
                return new DailyEventResponse
                {
                    Date = date,
                    CheckInResult = checkInByDate.GetValueOrDefault(utcDate),
                    Statuses = statusesByDate.GetValueOrDefault(
                        utcDate,
                        new List<SnAccountStatus>()
                    ),
                    UserEvents = eventsByDate.GetValueOrDefault(
                        utcDate,
                        new List<UserCalendarEventDto>()
                    ),
                    NotableDays = new List<DysonNetwork.Shared.Models.NotableDay>(), // Populated by caller
                };
            })
            .ToList();
    }

    public async Task<MergedDailyEventResponse> GetMergedEventCalendar(
        SnAccount user,
        int month,
        int year = 0,
        bool replaceInvisible = false,
        Guid? viewerId = null,
        string? regionCode = null,
        NotableDaysService? notableDaysService = null
    )
    {
        if (year == 0)
            year = SystemClock.Instance.GetCurrentInstant().InUtc().Date.Year;

        // Get the base calendar
        var calendar = await GetEventCalendar(
            user,
            month,
            year,
            replaceInvisible,
            viewerId,
            regionCode
        );

        // Fetch notable days if region code and service provided
        var notableDays = new List<Shared.Models.NotableDay>();
        if (!string.IsNullOrWhiteSpace(regionCode) && notableDaysService != null)
        {
            var fetchedDays = await notableDaysService.GetNotableDays(year, regionCode);
            // Filter to current month
            notableDays = fetchedDays
                .Where(d => d.Date.InUtc().Month == month && d.Date.InUtc().Year == year)
                .ToList();
        }

        // Group notable days by date
        var notableDaysByDate = notableDays
            .GroupBy(d => d.Date.InUtc().Date)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Create merged response
        var mergedResponse = new MergedDailyEventResponse
        {
            Date =
                calendar.FirstOrDefault()?.Date
                ?? new LocalDate(year, month, 1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant(),
            CheckInResult = calendar.FirstOrDefault()?.CheckInResult,
            Statuses = calendar.SelectMany(c => c.Statuses).ToList(),
            UserEvents = calendar.SelectMany(c => c.UserEvents).ToList(),
            NotableDays = notableDays,
            MergedEvents = new List<MergedCalendarEvent>(),
        };

        // Merge all events into a flat list
        var allMergedEvents = new List<MergedCalendarEvent>();

        foreach (var day in calendar)
        {
            var utcDate = day.Date.InUtc().Date;

            // Add check-in as merged event
            if (day.CheckInResult != null)
            {
                allMergedEvents.Add(
                    new MergedCalendarEvent
                    {
                        Id = day.CheckInResult.Id,
                        Type = CalendarEventType.CheckIn,
                        Title = $"Check-in: {day.CheckInResult.Level}",
                        Description = $"Daily check-in result: {day.CheckInResult.Level}",
                        StartTime = day.CheckInResult.CreatedAt,
                        EndTime = day.CheckInResult.CreatedAt,
                        IsAllDay = true,
                        Meta = new Dictionary<string, object>
                        {
                            ["level"] = day.CheckInResult.Level.ToString(),
                            ["rewardPoints"] = day.CheckInResult.RewardPoints ?? 0,
                            ["rewardExperience"] = day.CheckInResult.RewardExperience ?? 0,
                            ["tips"] = day.CheckInResult.Tips,
                            ["fortuneReport"] = day.CheckInResult.FortuneReport!,
                        },
                    }
                );
            }

            // Add statuses as merged events
            foreach (var status in day.Statuses)
            {
                allMergedEvents.Add(
                    new MergedCalendarEvent
                    {
                        Id = status.Id,
                        Type = CalendarEventType.Status,
                        Title = status.Label ?? "Status Update",
                        Description = $"Status: {status.Attitude} - {status.Type}",
                        StartTime = status.CreatedAt,
                        EndTime = status.ClearedAt ?? status.CreatedAt.Plus(Duration.FromHours(24)),
                        IsAllDay = false,
                        Meta = new Dictionary<string, object>
                        {
                            ["attitude"] = status.Attitude.ToString(),
                            ["type"] = status.Type.ToString(),
                            ["symbol"] = status.Symbol ?? "",
                        },
                    }
                );
            }

            // Add user events as merged events
            foreach (var evt in day.UserEvents)
            {
                allMergedEvents.Add(
                    new MergedCalendarEvent
                    {
                        Id = evt.Id,
                        Type = CalendarEventType.UserEvent,
                        Title = evt.Title,
                        Description = evt.Description ?? "",
                        Location = evt.Location,
                        StartTime = evt.StartTime,
                        EndTime = evt.EndTime,
                        IsAllDay = evt.IsAllDay,
                        Meta = evt.Meta ?? new Dictionary<string, object>(),
                    }
                );
            }

            // Add notable days
            if (notableDaysByDate.TryGetValue(utcDate, out var days))
            {
                foreach (var notableDay in days)
                {
                    allMergedEvents.Add(
                        new MergedCalendarEvent
                        {
                            Type = CalendarEventType.NotableDay,
                            Title = notableDay.GlobalName ?? notableDay.LocalName ?? "Holiday",
                            Description = notableDay.LocalName ?? "",
                            StartTime = notableDay.Date,
                            EndTime = notableDay.Date.Plus(Duration.FromHours(24)),
                            IsAllDay = true,
                            Meta = new Dictionary<string, object>
                            {
                                ["localName"] = notableDay.LocalName ?? "",
                                ["countryCode"] = notableDay.CountryCode ?? "",
                                ["holidayTypes"] = notableDay
                                    .Holidays.Select(h => h.ToString())
                                    .ToList(),
                            },
                        }
                    );
                }
            }
        }

        mergedResponse.MergedEvents = allMergedEvents.OrderBy(e => e.StartTime).ToList();

        return mergedResponse;
    }

    /// <summary>
    /// Gets upcoming events for countdown, including user events and notable days.
    /// No time limit - includes all future events.
    /// </summary>
    public async Task<List<EventCountdownItem>> GetUpcomingEventsAsync(
        SnAccount user,
        Guid? viewerId = null,
        string? regionCode = null,
        NotableDaysService? notableDaysService = null,
        int take = 5
    )
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var isOwner = viewerId == user.Id;

        // Get user events (respecting visibility)
        var userEventsQuery = db
            .UserCalendarEvents.AsNoTracking()
            .Where(e => e.AccountId == user.Id && e.DeletedAt == null && e.EndTime > now);

        if (!isOwner && viewerId.HasValue)
        {
            userEventsQuery = userEventsQuery.Where(e =>
                e.Visibility == EventVisibility.Public || e.Visibility == EventVisibility.Friends
            );
        }
        else if (!isOwner)
        {
            userEventsQuery = userEventsQuery.Where(e => e.Visibility == EventVisibility.Public);
        }

        var userEvents = await userEventsQuery.ToListAsync();

        // Check friendship for Friends visibility
        bool isFriend = false;
        if (!isOwner && viewerId.HasValue)
        {
            isFriend = await db.AccountRelationships.AnyAsync(r =>
                r.AccountId == user.Id
                && r.RelatedId == viewerId.Value
                && r.Status == RelationshipStatus.Friends
            );

            if (!isFriend)
            {
                userEvents = userEvents.Where(e => e.Visibility == EventVisibility.Public).ToList();
            }
        }

        // Expand recurring events
        var expandedUserEvents = ExpandRecurringEventsForCountdown(userEvents, now);

        // Get notable days if region code provided
        var notableDayItems = new List<EventCountdownItem>();
        if (!string.IsNullOrWhiteSpace(regionCode) && notableDaysService != null)
        {
            var currentYear = now.InUtc().Year;
            // Get holidays for current year and next few years to cover future events
            var holidays = await notableDaysService.GetNotableDays(currentYear, regionCode);
            holidays.AddRange(await notableDaysService.GetNotableDays(currentYear + 1, regionCode));
            holidays.AddRange(await notableDaysService.GetNotableDays(currentYear + 2, regionCode));

            notableDayItems = holidays
                .Where(h => h.Date > now)
                .Select(h => CreateCountdownItemFromNotableDay(h, now))
                .ToList();
        }

        // Combine and sort all events
        var allEvents = expandedUserEvents
            .Select(e => CreateCountdownItemFromUserEvent(e, now))
            .Concat(notableDayItems)
            .OrderBy(e => e.StartTime)
            .Take(take)
            .ToList();

        return allEvents;
    }

    private List<SnUserCalendarEvent> ExpandRecurringEventsForCountdown(
        List<SnUserCalendarEvent> events,
        Instant fromTime
    )
    {
        var expandedEvents = new List<SnUserCalendarEvent>();

        foreach (var evt in events)
        {
            if (evt.Recurrence == null || evt.Recurrence.Frequency == RecurrenceFrequency.None)
            {
                // Non-recurring event - include if in future
                if (evt.EndTime > fromTime)
                {
                    expandedEvents.Add(evt);
                }
                continue;
            }

            // Expand recurring events for countdown (next 10 occurrences max)
            var occurrences = GetRecurringEventOccurrencesForCountdown(evt, fromTime, 10);
            expandedEvents.AddRange(occurrences);
        }

        return expandedEvents;
    }

    private List<SnUserCalendarEvent> GetRecurringEventOccurrencesForCountdown(
        SnUserCalendarEvent evt,
        Instant fromTime,
        int maxOccurrences
    )
    {
        var occurrences = new List<SnUserCalendarEvent>();
        var recurrence = evt.Recurrence!;

        var maxOccurrencesLimit = recurrence.Occurrences ?? maxOccurrences;
        var endDate = recurrence.EndDate;

        var currentStart = evt.StartTime;
        var currentEnd = evt.EndTime;
        var occurrenceCount = 0;

        // Skip past occurrences
        while (currentEnd <= fromTime && occurrenceCount < maxOccurrencesLimit)
        {
            occurrenceCount++;
            (currentStart, currentEnd) = CalculateNextOccurrence(
                currentStart,
                currentEnd,
                recurrence
            );
            if (currentStart == default)
                break;
            if (endDate.HasValue && currentStart > endDate.Value)
                break;
        }

        // Generate future occurrences
        while (
            occurrenceCount < maxOccurrencesLimit
            && occurrenceCount < maxOccurrences
            && (!endDate.HasValue || currentStart <= endDate.Value)
        )
        {
            if (currentEnd > fromTime)
            {
                var occurrence = new SnUserCalendarEvent
                {
                    Id = evt.Id,
                    Title = evt.Title,
                    Description = evt.Description,
                    Location = evt.Location,
                    StartTime = currentStart,
                    EndTime = currentEnd,
                    IsAllDay = evt.IsAllDay,
                    Visibility = evt.Visibility,
                    Recurrence = null,
                    Meta = evt.Meta,
                    AccountId = evt.AccountId,
                    CreatedAt = evt.CreatedAt,
                    UpdatedAt = evt.UpdatedAt,
                };
                occurrences.Add(occurrence);
            }

            occurrenceCount++;
            (currentStart, currentEnd) = CalculateNextOccurrence(
                currentStart,
                currentEnd,
                recurrence
            );
            if (currentStart == default)
                break;
        }

        return occurrences;
    }

    private EventCountdownItem CreateCountdownItemFromUserEvent(
        SnUserCalendarEvent evt,
        Instant now
    )
    {
        var timeUntil = evt.StartTime - now;
        var daysRemaining = (int)timeUntil.TotalDays;
        var hoursRemaining = (int)timeUntil.TotalHours % 24;
        var isOngoing = evt.StartTime <= now && evt.EndTime > now;

        return new EventCountdownItem
        {
            EventId = evt.Id,
            EventType = CalendarEventType.UserEvent,
            Title = evt.Title,
            Description = evt.Description,
            Location = evt.Location,
            StartTime = evt.StartTime,
            EndTime = evt.EndTime,
            IsAllDay = evt.IsAllDay,
            DaysRemaining = Math.Max(0, daysRemaining),
            HoursRemaining = Math.Max(0, hoursRemaining),
            IsOngoing = isOngoing,
            Meta = evt.Meta,
            AccountId = evt.AccountId,
        };
    }

    private EventCountdownItem CreateCountdownItemFromNotableDay(
        DysonNetwork.Shared.Models.NotableDay day,
        Instant now
    )
    {
        var timeUntil = day.Date - now;
        var daysRemaining = (int)timeUntil.TotalDays;
        var hoursRemaining = (int)timeUntil.TotalHours % 24;
        var isOngoing = day.Date.InUtc().Date == now.InUtc().Date;

        return new EventCountdownItem
        {
            EventId = null,
            EventType = CalendarEventType.NotableDay,
            Title = day.GlobalName ?? day.LocalName ?? "Holiday",
            Description = day.LocalName,
            Location = null,
            StartTime = day.Date,
            EndTime = day.Date.Plus(Duration.FromHours(24)),
            IsAllDay = true,
            DaysRemaining = Math.Max(0, daysRemaining),
            HoursRemaining = Math.Max(0, hoursRemaining),
            IsOngoing = isOngoing,
            Meta = new Dictionary<string, object>
            {
                ["localName"] = day.LocalName ?? "",
                ["countryCode"] = day.CountryCode ?? "",
                ["holidayTypes"] = day.Holidays.Select(h => h.ToString()).ToList(),
            },
            AccountId = null,
        };
    }

    #endregion
}
