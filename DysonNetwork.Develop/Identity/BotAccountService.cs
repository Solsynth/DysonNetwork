using DysonNetwork.Develop.Project;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Develop.Identity;

public class BotAccountService(AppDatabase db, ILogger<BotAccountService> logger)
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

    public async Task<BotAccount> CreateBotAsync(DevProject project, string slug)
    {
        var bot = new BotAccount
        {
            Slug = slug,
            ProjectId = project.Id,
            Project = project,
            IsActive = true
        };

        db.BotAccounts.Add(bot);
        await db.SaveChangesAsync();

        return bot;
    }

    public async Task<BotAccount> UpdateBotAsync(BotAccount bot, string? slug = null, bool? isActive = null)
    {
        if (slug != null)
            bot.Slug = slug;
        if (isActive.HasValue)
            bot.IsActive = isActive.Value;

        bot.UpdatedAt = SystemClock.Instance.GetCurrentInstant();
        await db.SaveChangesAsync();

        return bot;
    }

    public async Task DeleteBotAsync(BotAccount bot)
    {
        db.BotAccounts.Remove(bot);
        await db.SaveChangesAsync();
    }
}
