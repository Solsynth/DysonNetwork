using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Ring.Email;

public class EmailSendingPlanService(
    AppDatabase db,
    DyAccountService.DyAccountServiceClient accountGrpc,
    RemoteAccountContactService contactService,
    EmailService emailService,
    IClock clock,
    ILogger<EmailSendingPlanService> logger
)
{
    public sealed record CreateEmailSendingPlanCommand(
        Guid? AccountId,
        List<Guid>? AccountIds,
        bool BroadcastToAll,
        string Subject,
        string HtmlBody,
        string? SendingPlanKey,
        Instant? PlannedStartAt,
        int MaxEmailsPerInterval,
        int IntervalMinutes,
        int? MaxEmailsPerDay
    );

    public sealed class EmailSendingPlanCounts
    {
        public int Total { get; init; }
        public int Pending { get; init; }
        public int Sent { get; init; }
        public int Skipped { get; init; }
        public int Failed { get; init; }
    }

    public sealed class EmailSendingPlanAdvanceView
    {
        public Guid Id { get; init; }
        public int IntervalNumber { get; init; }
        public bool IsManual { get; init; }
        public int AttemptedCount { get; init; }
        public int SentCount { get; init; }
        public int SkippedCount { get; init; }
        public int FailedCount { get; init; }
        public int PendingCountAfter { get; init; }
        public Instant StartedAt { get; init; }
        public Instant CompletedAt { get; init; }
    }

    public sealed class EmailSendingPlanView
    {
        public Guid Id { get; init; }
        public string? SendingPlanKey { get; init; }
        public Guid CreatedByAccountId { get; init; }
        public string Subject { get; init; } = string.Empty;
        public bool BroadcastToAll { get; init; }
        public int RecipientCount { get; init; }
        public int MaxEmailsPerInterval { get; init; }
        public int IntervalMinutes { get; init; }
        public int? MaxEmailsPerDay { get; init; }
        public EmailSendingPlanStatus Status { get; init; }
        public int AdvancedIntervalsCount { get; init; }
        public Instant PlannedStartAt { get; init; }
        public Instant? NextIntervalAt { get; init; }
        public Instant? LastAdvancedAt { get; init; }
        public Instant? PausedAt { get; init; }
        public Instant? CompletedAt { get; init; }
        public EmailSendingPlanCounts Counts { get; init; } = new();
        public List<EmailSendingPlanAdvanceView> Advances { get; init; } = [];
    }

    private sealed record TargetAccount(Guid AccountId, string RecipientName);

    public async Task<EmailSendingPlanView> CreatePlanAsync(
        CreateEmailSendingPlanCommand command,
        Guid createdByAccountId,
        CancellationToken cancellationToken = default
    )
    {
        var targets = await ResolveTargetAccountsAsync(
            command.AccountId,
            command.AccountIds,
            command.BroadcastToAll,
            cancellationToken
        );

        if (targets.Count == 0)
            throw new InvalidOperationException("No valid target accounts were resolved.");

        var now = clock.GetCurrentInstant();
        var plannedStartAt = command.PlannedStartAt ?? now;
        var plan = new SnEmailSendingPlan
        {
            SendingPlanKey = string.IsNullOrWhiteSpace(command.SendingPlanKey) ? null : command.SendingPlanKey.Trim(),
            CreatedByAccountId = createdByAccountId,
            Subject = command.Subject,
            HtmlBody = command.HtmlBody,
            BroadcastToAll = command.BroadcastToAll,
            RecipientCount = targets.Count,
            MaxEmailsPerInterval = command.MaxEmailsPerInterval,
            IntervalMinutes = command.IntervalMinutes,
            MaxEmailsPerDay = command.MaxEmailsPerDay,
            PlannedStartAt = plannedStartAt,
            NextIntervalAt = plannedStartAt
        };

        db.EmailSendingPlans.Add(plan);
        db.EmailSendingPlanRecipients.AddRange(targets.Select(target => new SnEmailSendingPlanRecipient
        {
            PlanId = plan.Id,
            AccountId = target.AccountId,
            RecipientNameSnapshot = target.RecipientName
        }));

        await db.SaveChangesAsync(cancellationToken);
        return await GetPlanOrThrowAsync(plan.Id, cancellationToken: cancellationToken);
    }

    public async Task<(List<EmailSendingPlanView> Plans, int TotalCount)> ListPlansAsync(
        int offset,
        int take,
        EmailSendingPlanStatus? status = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = db.EmailSendingPlans.AsNoTracking();
        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        var totalCount = await query.CountAsync(cancellationToken);
        var plans = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(take)
            .ToListAsync(cancellationToken);

        var views = await BuildPlanViewsAsync(plans, includeAdvances: false, cancellationToken: cancellationToken);
        return (views, totalCount);
    }

    public async Task<EmailSendingPlanView?> GetPlanAsync(
        Guid planId,
        int advanceTake = 20,
        CancellationToken cancellationToken = default
    )
    {
        var plan = await db.EmailSendingPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            return null;

        return (await BuildPlanViewsAsync([plan], includeAdvances: true, advanceTake, cancellationToken)).Single();
    }

    public async Task<EmailSendingPlanView> PausePlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default
    )
    {
        var plan = await GetTrackedPlanAsync(planId, cancellationToken);
        if (plan.Status == EmailSendingPlanStatus.Completed)
            throw new InvalidOperationException("Completed plans cannot be paused.");

        plan.Status = EmailSendingPlanStatus.Paused;
        plan.PausedAt = clock.GetCurrentInstant();
        await db.SaveChangesAsync(cancellationToken);
        return await GetPlanOrThrowAsync(planId, cancellationToken: cancellationToken);
    }

    public async Task<EmailSendingPlanView> ResumePlanAsync(
        Guid planId,
        CancellationToken cancellationToken = default
    )
    {
        var plan = await GetTrackedPlanAsync(planId, cancellationToken);
        if (plan.Status == EmailSendingPlanStatus.Completed)
            throw new InvalidOperationException("Completed plans cannot be resumed.");

        var now = clock.GetCurrentInstant();
        plan.Status = EmailSendingPlanStatus.Scheduled;
        plan.PausedAt = null;
        plan.NextIntervalAt = plan.NextIntervalAt is null || plan.NextIntervalAt < now ? now : plan.NextIntervalAt;
        await db.SaveChangesAsync(cancellationToken);
        return await GetPlanOrThrowAsync(planId, cancellationToken: cancellationToken);
    }

    public async Task<EmailSendingPlanView> AdvancePlanIntervalAsync(
        Guid planId,
        bool isManual,
        CancellationToken cancellationToken = default
    )
    {
        var plan = await GetTrackedPlanAsync(planId, cancellationToken);
        var now = clock.GetCurrentInstant();

        if (plan.Status == EmailSendingPlanStatus.Completed)
            throw new InvalidOperationException("Completed plans cannot be advanced.");
        if (!isManual && plan.Status == EmailSendingPlanStatus.Paused)
            throw new InvalidOperationException("Paused plans cannot be advanced automatically.");
        if (!isManual && plan.NextIntervalAt.HasValue && plan.NextIntervalAt.Value > now)
            throw new InvalidOperationException("The next interval is not due yet.");

        var pendingCountBefore = await db.EmailSendingPlanRecipients
            .Where(r => r.PlanId == planId && r.Status == EmailSendingPlanRecipientStatus.Pending)
            .CountAsync(cancellationToken);
        if (pendingCountBefore == 0)
        {
            plan.Status = EmailSendingPlanStatus.Completed;
            plan.CompletedAt ??= now;
            plan.NextIntervalAt = null;
            await db.SaveChangesAsync(cancellationToken);
            return await GetPlanOrThrowAsync(planId, cancellationToken: cancellationToken);
        }

        var remainingDailyCapacity = await GetRemainingDailyCapacityAsync(plan, now, cancellationToken);
        if (remainingDailyCapacity == 0)
        {
            if (plan.Status != EmailSendingPlanStatus.Paused)
                plan.NextIntervalAt = GetStartOfNextUtcDay(now);

            await db.SaveChangesAsync(cancellationToken);
            return await GetPlanOrThrowAsync(planId, cancellationToken: cancellationToken);
        }

        var sendCapacity = Math.Min(plan.MaxEmailsPerInterval, remainingDailyCapacity ?? plan.MaxEmailsPerInterval);
        var recipients = await db.EmailSendingPlanRecipients
            .Where(r => r.PlanId == planId && r.Status == EmailSendingPlanRecipientStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync(cancellationToken);

        var intervalNumber = plan.AdvancedIntervalsCount + 1;
        var attemptedCount = 0;
        var sentCount = 0;
        var skippedCount = 0;
        var failedCount = 0;

        foreach (var recipient in recipients)
        {
            if (attemptedCount >= sendCapacity)
                break;

            recipient.AttemptCount += 1;
            recipient.LastIntervalNumber = intervalNumber;

            var contacts = await contactService.ListContactsAsync(
                recipient.AccountId,
                AccountContactType.Email,
                verifiedOnly: true,
                cancellationToken
            );
            var contact = contacts
                .OrderByDescending(c => c.IsPrimary)
                .ThenByDescending(c => c.VerifiedAt)
                .FirstOrDefault();

            if (contact is null)
            {
                recipient.Status = EmailSendingPlanRecipientStatus.Skipped;
                recipient.LastError = "No verified email contact is available.";
                recipient.ProcessedAt = now;
                skippedCount += 1;
                continue;
            }

            recipient.LastResolvedEmail = contact.Content;
            try
            {
                await emailService.SendEmailAsync(
                    recipient.RecipientNameSnapshot,
                    contact.Content,
                    plan.Subject,
                    plan.HtmlBody
                );

                recipient.Status = EmailSendingPlanRecipientStatus.Sent;
                recipient.LastError = null;
                recipient.ProcessedAt = now;
                attemptedCount += 1;
                sentCount += 1;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to send email plan {PlanId} interval {IntervalNumber} to account {AccountId}",
                    plan.Id,
                    intervalNumber,
                    recipient.AccountId
                );
                recipient.Status = EmailSendingPlanRecipientStatus.Failed;
                recipient.LastError = Truncate(ex.Message, 4096);
                recipient.ProcessedAt = now;
                attemptedCount += 1;
                failedCount += 1;
            }
        }

        var pendingCountAfter = recipients.Count(r => r.Status == EmailSendingPlanRecipientStatus.Pending);

        if (attemptedCount > 0 || skippedCount > 0 || failedCount > 0)
        {
            db.EmailSendingPlanAdvances.Add(new SnEmailSendingPlanAdvance
            {
                PlanId = plan.Id,
                IntervalNumber = intervalNumber,
                IsManual = isManual,
                AttemptedCount = attemptedCount,
                SentCount = sentCount,
                SkippedCount = skippedCount,
                FailedCount = failedCount,
                PendingCountAfter = pendingCountAfter,
                StartedAt = now,
                CompletedAt = clock.GetCurrentInstant()
            });

            plan.AdvancedIntervalsCount = intervalNumber;
            plan.LastAdvancedAt = clock.GetCurrentInstant();
        }

        if (pendingCountAfter == 0)
        {
            plan.Status = EmailSendingPlanStatus.Completed;
            plan.CompletedAt = clock.GetCurrentInstant();
            plan.NextIntervalAt = null;
        }
        else if (plan.Status == EmailSendingPlanStatus.Paused)
        {
            plan.NextIntervalAt = now.Plus(Duration.FromMinutes(plan.IntervalMinutes));
        }
        else
        {
            int? remainingCapacityAfter = remainingDailyCapacity.HasValue
                ? Math.Max(0, remainingDailyCapacity.Value - attemptedCount)
                : null;
            plan.Status = EmailSendingPlanStatus.Scheduled;
            plan.NextIntervalAt = plan.MaxEmailsPerDay.HasValue && remainingCapacityAfter == 0
                ? GetStartOfNextUtcDay(now)
                : now.Plus(Duration.FromMinutes(plan.IntervalMinutes));
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetPlanOrThrowAsync(planId, cancellationToken: cancellationToken);
    }

    public async Task AdvanceDuePlansAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetCurrentInstant();
        var duePlanIds = await db.EmailSendingPlans
            .AsNoTracking()
            .Where(p => p.Status == EmailSendingPlanStatus.Scheduled && p.NextIntervalAt != null && p.NextIntervalAt <= now)
            .OrderBy(p => p.NextIntervalAt)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var planId in duePlanIds)
        {
            try
            {
                await AdvancePlanIntervalAsync(planId, isManual: false, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to advance due email sending plan {PlanId}", planId);
            }
        }
    }

    private async Task<List<TargetAccount>> ResolveTargetAccountsAsync(
        Guid? accountId,
        List<Guid>? accountIds,
        bool broadcastToAll,
        CancellationToken cancellationToken
    )
    {
        if (broadcastToAll)
        {
            var results = new List<TargetAccount>();
            string pageToken = string.Empty;
            do
            {
                var listResponse = await accountGrpc.ListAccountsAsync(
                    new DyListAccountsRequest
                    {
                        PageSize = 500,
                        PageToken = pageToken
                    },
                    cancellationToken: cancellationToken
                );
                results.AddRange(listResponse.Accounts.Select(ToTargetAccount));
                pageToken = listResponse.NextPageToken;
            } while (!string.IsNullOrWhiteSpace(pageToken));

            return results
                .GroupBy(a => a.AccountId)
                .Select(g => g.First())
                .ToList();
        }

        var requestedIds = new HashSet<Guid>();
        if (accountId.HasValue)
            requestedIds.Add(accountId.Value);
        if (accountIds is { Count: > 0 })
            foreach (var id in accountIds)
                requestedIds.Add(id);

        if (requestedIds.Count == 0)
            return [];

        var request = new DyGetAccountBatchRequest();
        request.Id.AddRange(requestedIds.Select(id => id.ToString()));
        var response = await accountGrpc.GetAccountBatchAsync(request, cancellationToken: cancellationToken);
        return response.Accounts
            .Select(ToTargetAccount)
            .GroupBy(a => a.AccountId)
            .Select(g => g.First())
            .ToList();
    }

    private static TargetAccount ToTargetAccount(DyAccount account)
    {
        var recipientName = string.IsNullOrWhiteSpace(account.Nick) ? account.Name : account.Nick;
        return new TargetAccount(Guid.Parse(account.Id), recipientName);
    }

    private async Task<SnEmailSendingPlan> GetTrackedPlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await db.EmailSendingPlans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
            throw new KeyNotFoundException($"Email sending plan {planId} was not found.");
        return plan;
    }

    private async Task<EmailSendingPlanView> GetPlanOrThrowAsync(
        Guid planId,
        int advanceTake = 20,
        CancellationToken cancellationToken = default
    )
    {
        return await GetPlanAsync(planId, advanceTake, cancellationToken)
               ?? throw new KeyNotFoundException($"Email sending plan {planId} was not found.");
    }

    private async Task<List<EmailSendingPlanView>> BuildPlanViewsAsync(
        List<SnEmailSendingPlan> plans,
        bool includeAdvances,
        int advanceTake = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (plans.Count == 0)
            return [];

        var planIds = plans.Select(p => p.Id).ToList();
        var countRows = await db.EmailSendingPlanRecipients
            .AsNoTracking()
            .Where(r => planIds.Contains(r.PlanId))
            .GroupBy(r => new { r.PlanId, r.Status })
            .Select(g => new { g.Key.PlanId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countLookup = countRows
            .GroupBy(r => r.PlanId)
            .ToDictionary(
                g => g.Key,
                g => new EmailSendingPlanCounts
                {
                    Total = g.Sum(x => x.Count),
                    Pending = g.Where(x => x.Status == EmailSendingPlanRecipientStatus.Pending).Sum(x => x.Count),
                    Sent = g.Where(x => x.Status == EmailSendingPlanRecipientStatus.Sent).Sum(x => x.Count),
                    Skipped = g.Where(x => x.Status == EmailSendingPlanRecipientStatus.Skipped).Sum(x => x.Count),
                    Failed = g.Where(x => x.Status == EmailSendingPlanRecipientStatus.Failed).Sum(x => x.Count)
                }
            );

        Dictionary<Guid, List<EmailSendingPlanAdvanceView>> advanceLookup = [];
        if (includeAdvances)
        {
            var advances = await db.EmailSendingPlanAdvances
                .AsNoTracking()
                .Where(a => planIds.Contains(a.PlanId))
                .OrderByDescending(a => a.IntervalNumber)
                .ToListAsync(cancellationToken);

            advanceLookup = advances
                .GroupBy(a => a.PlanId)
                .ToDictionary(
                    g => g.Key,
                    g => g
                        .Take(advanceTake)
                        .Select(a => new EmailSendingPlanAdvanceView
                        {
                            Id = a.Id,
                            IntervalNumber = a.IntervalNumber,
                            IsManual = a.IsManual,
                            AttemptedCount = a.AttemptedCount,
                            SentCount = a.SentCount,
                            SkippedCount = a.SkippedCount,
                            FailedCount = a.FailedCount,
                            PendingCountAfter = a.PendingCountAfter,
                            StartedAt = a.StartedAt,
                            CompletedAt = a.CompletedAt
                        })
                        .ToList()
                );
        }

        return plans.Select(plan => new EmailSendingPlanView
        {
            Id = plan.Id,
            SendingPlanKey = plan.SendingPlanKey,
            CreatedByAccountId = plan.CreatedByAccountId,
            Subject = plan.Subject,
            BroadcastToAll = plan.BroadcastToAll,
            RecipientCount = plan.RecipientCount,
            MaxEmailsPerInterval = plan.MaxEmailsPerInterval,
            IntervalMinutes = plan.IntervalMinutes,
            MaxEmailsPerDay = plan.MaxEmailsPerDay,
            Status = plan.Status,
            AdvancedIntervalsCount = plan.AdvancedIntervalsCount,
            PlannedStartAt = plan.PlannedStartAt,
            NextIntervalAt = plan.NextIntervalAt,
            LastAdvancedAt = plan.LastAdvancedAt,
            PausedAt = plan.PausedAt,
            CompletedAt = plan.CompletedAt,
            Counts = countLookup.GetValueOrDefault(plan.Id, new EmailSendingPlanCounts()),
            Advances = includeAdvances ? advanceLookup.GetValueOrDefault(plan.Id, []) : []
        }).ToList();
    }

    private async Task<int?> GetRemainingDailyCapacityAsync(
        SnEmailSendingPlan plan,
        Instant now,
        CancellationToken cancellationToken
    )
    {
        if (!plan.MaxEmailsPerDay.HasValue)
            return null;

        var startOfDay = GetStartOfUtcDay(now);
        var endOfDay = GetStartOfNextUtcDay(now);
        var attemptedToday = await db.EmailSendingPlanAdvances
            .AsNoTracking()
            .Where(a => a.PlanId == plan.Id && a.StartedAt >= startOfDay && a.StartedAt < endOfDay)
            .SumAsync(a => a.AttemptedCount, cancellationToken);

        return Math.Max(0, plan.MaxEmailsPerDay.Value - attemptedToday);
    }

    private static Instant GetStartOfUtcDay(Instant instant) =>
        instant.InUtc().Date.AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

    private static Instant GetStartOfNextUtcDay(Instant instant) =>
        instant.InUtc().Date.PlusDays(1).AtStartOfDayInZone(DateTimeZone.Utc).ToInstant();

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
