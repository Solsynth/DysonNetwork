using DysonNetwork.Develop.Project;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Develop.Identity;

public class BotAccountService(
    AppDatabase db,
    BotAccountReceiverService.BotAccountReceiverServiceClient accountReceiver,
    AccountClientHelper accounts
)
{
    public async Task<BotAccount?> GetBotByIdAsync(Guid id)
    {
        return await db.BotAccounts
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == id);
    }

    public async Task<IEnumerable<BotAccount>> GetBotsByProjectAsync(Guid projectId)
    {
        return await db.BotAccounts
            .Where(b => b.ProjectId == projectId)
            .ToListAsync();
    }

    public async Task<BotAccount> CreateBotAsync(
        DevProject project,
        string slug,
        Account account,
        string? pictureId,
        string? backgroundId
    )
    {
        // First, check if a bot with this slug already exists in this project
        var existingBot = await db.BotAccounts
            .FirstOrDefaultAsync(b => b.ProjectId == project.Id && b.Slug == slug);

        if (existingBot != null)
            throw new InvalidOperationException("A bot with this slug already exists in this project.");

        try
        {
            var automatedId = Guid.NewGuid();
            var createRequest = new CreateBotAccountRequest
            {
                AutomatedId = automatedId.ToString(),
                Account = account,
                PictureId = pictureId,
                BackgroundId = backgroundId
            };

            var createResponse = await accountReceiver.CreateBotAccountAsync(createRequest);
            var botAccount = createResponse.Bot;

            // Then create the local bot account
            var bot = new BotAccount
            {
                Id = automatedId,
                Slug = slug,
                ProjectId = project.Id,
                Project = project,
                IsActive = botAccount.IsActive,
                CreatedAt = botAccount.CreatedAt.ToInstant(),
                UpdatedAt = botAccount.UpdatedAt.ToInstant()
            };

            db.BotAccounts.Add(bot);
            await db.SaveChangesAsync();

            return bot;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            throw new InvalidOperationException(
                "A bot account with this ID already exists in the authentication service.", ex);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            throw new ArgumentException($"Invalid bot account data: {ex.Status.Detail}", ex);
        }
        catch (RpcException ex)
        {
            throw new Exception($"Failed to create bot account: {ex.Status.Detail}", ex);
        }
    }

    public async Task<BotAccount> UpdateBotAsync(BotAccount bot,
        Account account,
        string? pictureId,
        string? backgroundId,
        string? slug = null,
        bool? isActive = null
    )
    {
        var updated = false;
        if (slug != null && bot.Slug != slug)
        {
            bot.Slug = slug;
            updated = true;
        }

        if (isActive.HasValue && bot.IsActive != isActive.Value)
        {
            bot.IsActive = isActive.Value;
            updated = true;
        }

        if (!updated) return bot;

        try
        {
            // Update the bot account in the Pass service
            var updateRequest = new UpdateBotAccountRequest
            {
                AutomatedId = bot.Id.ToString(),
                Account = account,
                PictureId = pictureId,
                BackgroundId = backgroundId
            };

            var updateResponse = await accountReceiver.UpdateBotAccountAsync(updateRequest);
            var updatedBot = updateResponse.Bot;

            // Update local bot account
            bot.UpdatedAt = updatedBot.UpdatedAt.ToInstant();
            bot.IsActive = updatedBot.IsActive;
            await db.SaveChangesAsync();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            throw new Exception("Bot account not found in the authentication service", ex);
        }
        catch (RpcException ex)
        {
            throw new Exception($"Failed to update bot account: {ex.Status.Detail}", ex);
        }

        return bot;
    }

    public async Task DeleteBotAsync(BotAccount bot)
    {
        try
        {
            // Delete the bot account from the Pass service
            var deleteRequest = new DeleteBotAccountRequest
            {
                AutomatedId = bot.Id.ToString(),
                Force = false
            };

            await accountReceiver.DeleteBotAccountAsync(deleteRequest);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            // Account not found in Pass service, continue with local deletion
        }

        // Delete the local bot account
        db.BotAccounts.Remove(bot);
        await db.SaveChangesAsync();
    }

    public async Task<BotAccount?> LoadBotAccountAsync(BotAccount bot) =>
        (await LoadBotsAccountAsync([bot])).FirstOrDefault();

    public async Task<List<BotAccount>> LoadBotsAccountAsync(IEnumerable<BotAccount> bots)
    {
        bots = bots.ToList();
        var automatedIds = bots.Select(b => b.Id).ToList();
        var data = await accounts.GetBotAccountBatch(automatedIds);

        foreach (var bot in bots)
        {
            bot.Account = data
                .Select(AccountReference.FromProtoValue)
                .FirstOrDefault(e => e.AutomatedId == bot.Id);
        }

        return bots as List<BotAccount> ?? [];
    }
}