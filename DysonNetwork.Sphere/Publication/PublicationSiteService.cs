using System.Text.RegularExpressions;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Registry;
using DysonNetwork.Sphere.Publisher;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Publication;

public class PublicationSiteService(
    AppDatabase db,
    PublisherService publisherService,
    RemoteAccountService remoteAccounts
)
{
    public async Task<SnPublicationSite?> GetSiteById(Guid id)
    {
        return await db.PublicationSites
            .Include(s => s.Pages)
            .ThenInclude(p => p.Site)
            .Include(s => s.Publisher)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<SnPublicationSite?> GetSiteBySlug(string slug)
    {
        return await db.PublicationSites
            .Include(s => s.Pages)
            .ThenInclude(p => p.Site)
            .Include(s => s.Publisher)
            .FirstOrDefaultAsync(s => s.Slug == slug);
    }

    public async Task<List<SnPublicationSite>> GetSitesByPublisherIds(List<Guid> publisherIds)
    {
        return await db.PublicationSites
            .Include(s => s.Pages)
            .ThenInclude(p => p.Site)
            .Include(s => s.Publisher)
            .Where(s => publisherIds.Contains(s.PublisherId))
            .ToListAsync();
    }

    public async Task<SnPublicationSite> CreateSite(SnPublicationSite site, Guid accountId)
    {
        var perk = (await remoteAccounts.GetAccount(accountId)).PerkSubscription;
        var perkLevel = perk is not null ? PerkSubscriptionPrivilege.GetPrivilegeFromIdentifier(perk.Identifier) : 0;

        var maxSite = (perkLevel) switch
        {
            1 => 2,
            2 => 3,
            3 => 5,
            _ => 1
        };

        // Check if account has reached the maximum number of sites
        var existingSitesCount = await db.PublicationSites.CountAsync(s => s.AccountId == accountId);
        if (existingSitesCount >= maxSite)
            throw new InvalidOperationException("Account has reached the maximum number of sites allowed.");

        // Check if account is member of the publisher
        var isMember = await publisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
        if (!isMember)
            throw new UnauthorizedAccessException("Account is not a member of the publisher with sufficient role.");

        db.PublicationSites.Add(site);
        await db.SaveChangesAsync();
        return site;
    }

    public async Task<SnPublicationSite> UpdateSite(SnPublicationSite site, Guid accountId)
    {
        // Check permission
        var isMember = await publisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
        if (!isMember)
            throw new UnauthorizedAccessException("Account is not a member of the publisher with sufficient role.");

        db.PublicationSites.Update(site);
        await db.SaveChangesAsync();
        return site;
    }

    public async Task DeleteSite(Guid id, Guid accountId)
    {
        var site = await db.PublicationSites.FirstOrDefaultAsync(s => s.Id == id);
        if (site != null)
        {
            // Check permission
            var isMember =
                await publisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Owner);
            if (!isMember)
                throw new UnauthorizedAccessException("Account is not an owner of the publisher.");

            db.PublicationSites.Remove(site);
            await db.SaveChangesAsync();
        }
    }

    public async Task<SnPublicationPage?> GetPageById(Guid id)
    {
        return await db.PublicationPages
            .Include(p => p.Site)
            .ThenInclude(s => s.Publisher)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<List<SnPublicationPage>> GetPagesForSite(Guid siteId)
    {
        return await db.PublicationPages
            .Include(p => p.Site)
            .Where(p => p.SiteId == siteId)
            .ToListAsync();
    }

    public async Task<SnPublicationPage> CreatePage(SnPublicationPage page, Guid accountId)
    {
        var site = await db.PublicationSites.FirstOrDefaultAsync(s => s.Id == page.SiteId);
        if (site == null)
            throw new InvalidOperationException("Site not found.");

        // Check permission
        var isMember = await publisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
        if (!isMember)
            throw new UnauthorizedAccessException("Account is not a member of the publisher with sufficient role.");

        db.PublicationPages.Add(page);
        await db.SaveChangesAsync();
        return page;
    }

    public async Task<SnPublicationPage> UpdatePage(SnPublicationPage page, Guid accountId)
    {
        // Fetch current site
        var site = await db.PublicationSites.FirstOrDefaultAsync(s => s.Id == page.SiteId);
        if (site == null)
            throw new InvalidOperationException("Site not found.");

        // Check permission
        var isMember = await publisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
        if (!isMember)
            throw new UnauthorizedAccessException("Account is not a member of the publisher with sufficient role.");

        db.PublicationPages.Update(page);
        await db.SaveChangesAsync();
        return page;
    }

    public async Task DeletePage(Guid id, Guid accountId)
    {
        var page = await db.PublicationPages.FirstOrDefaultAsync(p => p.Id == id);
        if (page != null)
        {
            var site = await db.PublicationSites.FirstOrDefaultAsync(s => s.Id == page.SiteId);
            if (site != null)
            {
                // Check permission
                var isMember =
                    await publisherService.IsMemberWithRole(site.PublisherId, accountId, PublisherMemberRole.Editor);
                if (!isMember)
                    throw new UnauthorizedAccessException(
                        "Account is not a member of the publisher with sufficient role.");

                db.PublicationPages.Remove(page);
                await db.SaveChangesAsync();
            }
        }
    }

    // Special retrieval method

    public async Task<SnPublicationPage?> GetPageBySlugAndPath(string slug, string path)
    {
        var site = await GetSiteBySlug(slug);
        if (site == null) return null;

        foreach (var page in site.Pages)
        {
            if (Regex.IsMatch(path, page.Path))
            {
                return page;
            }
        }

        return null;
    }

    public async Task<SnPublicationPage?> RenderPage(string slug, string path)
    {
        var site = await GetSiteBySlug(slug);
        if (site == null) return null;

        // Find exact match first
        var exactPage = site.Pages.FirstOrDefault(p => p.Path == path);
        if (exactPage != null) return exactPage;

        // Then wildcard match
        var wildcardPage = site.Pages.FirstOrDefault(p => Regex.IsMatch(path, p.Path));
        if (wildcardPage != null) return wildcardPage;

        // Finally, default page (e.g., "/")
        var defaultPage = site.Pages.FirstOrDefault(p => p.Path == "/");
        return defaultPage;
    }
}
