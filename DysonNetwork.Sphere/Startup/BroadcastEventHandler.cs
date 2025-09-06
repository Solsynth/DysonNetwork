using System.Text.Json;
using DysonNetwork.Shared.Stream;
using DysonNetwork.Sphere.Post;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

namespace DysonNetwork.Sphere.Startup;

public class BroadcastEventHandler(
    INatsConnection nats,
    ILogger<BroadcastEventHandler> logger,
    IServiceProvider serviceProvider
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var msg in nats.SubscribeAsync<byte[]>(PaymentOrderEvent.Type, cancellationToken: stoppingToken))
        {
            PaymentOrderEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<PaymentOrderEvent>(msg.Data);

                // Every order goes into the MQ is already paid, so we skipped the status validation

                if (evt?.ProductIdentifier is null)
                    continue;

                switch (evt.ProductIdentifier)
                {
                    case "posts.award":
                    {
                        logger.LogInformation("Handling post award order: {OrderId}", evt.OrderId);

                        if (!evt.Meta.TryGetValue("account_id", out var accountIdObj) ||
                            accountIdObj is not string accountIdStr ||
                            !Guid.TryParse(accountIdStr, out var accountId))
                        {
                            logger.LogWarning("Post award order {OrderId} missing or invalid account_id", evt.OrderId);
                            break;
                        }

                        if (!evt.Meta.TryGetValue("post_id", out var postIdObj) ||
                            postIdObj is not string postIdStr ||
                            !Guid.TryParse(postIdStr, out var postId))
                        {
                            logger.LogWarning("Post award order {OrderId} missing or invalid post_id", evt.OrderId);
                            break;
                        }

                        if (!evt.Meta.TryGetValue("amount", out var amountObj) ||
                            amountObj is not string amountStr ||
                            !decimal.TryParse(amountStr, out var amount))
                        {
                            logger.LogWarning("Post award order {OrderId} missing or invalid amount", evt.OrderId);
                            break;
                        }

                        if (!evt.Meta.TryGetValue("attitude", out var attitudeObj) ||
                            attitudeObj is not string attitudeStr ||
                            !int.TryParse(attitudeStr, out var attitudeInt) ||
                            !Enum.IsDefined(typeof(PostReactionAttitude), attitudeInt))
                        {
                            logger.LogWarning("Post award order {OrderId} missing or invalid attitude", evt.OrderId);
                            break;
                        }
                        var attitude = (PostReactionAttitude)attitudeInt;

                        string? message = null;
                        if (evt.Meta.TryGetValue("message", out var messageObj) &&
                            messageObj is string messageStr)
                        {
                            message = messageStr;
                        }

                        await using var scope = serviceProvider.CreateAsyncScope();
                        var ps = scope.ServiceProvider.GetRequiredService<PostService>();

                        await ps.AwardPost(postId, accountId, amount, attitude, message);

                        logger.LogInformation("Post award for order {OrderId} handled successfully.", evt.OrderId);

                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing payment order event for order {OrderId}", evt?.OrderId);
            }
        }

        await foreach (var msg in nats.SubscribeAsync<byte[]>(AccountDeletedEvent.Type,
                           cancellationToken: stoppingToken))
        {
            try
            {
                var evt = JsonSerializer.Deserialize<AccountDeletedEvent>(msg.Data);
                if (evt == null) continue;

                logger.LogInformation("Account deleted: {AccountId}", evt.AccountId);

                using var scope = serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDatabase>();

                await db.ChatMembers
                    .Where(m => m.AccountId == evt.AccountId)
                    .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                await db.RealmMembers
                    .Where(m => m.AccountId == evt.AccountId)
                    .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken: stoppingToken);
                try
                {
                    var publishers = await db.Publishers
                        .Where(p => p.Members.All(m => m.AccountId == evt.AccountId))
                        .ToListAsync(cancellationToken: stoppingToken);

                    foreach (var publisher in publishers)
                        await db.Posts
                            .Where(p => p.PublisherId == publisher.Id)
                            .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                    var publisherIds = publishers.Select(p => p.Id).ToList();
                    await db.Publishers
                        .Where(p => publisherIds.Contains(p.Id))
                        .ExecuteDeleteAsync(cancellationToken: stoppingToken);

                    await transaction.CommitAsync(cancellationToken: stoppingToken);
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync(cancellationToken: stoppingToken);
                    throw;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing AccountDeleted");
            }
        }
    }
}