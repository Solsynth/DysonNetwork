using DysonNetwork.Sphere.Models;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Publisher;

/// <summary>
/// Verifies publisher domain ownership via /.well-known/dyson-domains.txt
/// The file should contain one publisher name per line, confirming the domain belongs to that publisher.
/// </summary>
public class DomainVerificationService(
    AppDatabase db,
    IHttpClientFactory httpClientFactory,
    ILogger<DomainVerificationService> logger
)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<bool> VerifyDomainAsync(Guid publisherId, string domain)
    {
        var record = await db.PublisherVerifiedDomains
            .FirstOrDefaultAsync(d => d.PublisherId == publisherId && d.Domain == domain);
        if (record is null) return false;

        record.LastCheckedAt = SystemClock.Instance.GetCurrentInstant();

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = RequestTimeout;

            var url = $"https://{domain}/.well-known/dyson-domains.txt";
            logger.LogDebug("Verifying domain {Domain} for publisher {PublisherId}", domain, publisherId);
            var response = await client.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                record.Status = DomainVerificationStatus.Failed;
                record.FailedAttempts++;
                record.LastError = $"HTTP {(int)response.StatusCode}";
                await db.SaveChangesAsync();
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var publisher = await db.Publishers.FindAsync(publisherId);
            if (publisher is null)
            {
                record.Status = DomainVerificationStatus.Failed;
                record.FailedAttempts++;
                record.LastError = "Publisher not found";
                await db.SaveChangesAsync();
                return false;
            }

            // Check if the publisher name appears in the file (case-insensitive, one per line)
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isVerified = lines.Any(line =>
                string.Equals(line, publisher.Name, StringComparison.OrdinalIgnoreCase));

            if (isVerified)
            {
                record.Status = DomainVerificationStatus.Verified;
                record.VerifiedAt = SystemClock.Instance.GetCurrentInstant();
                record.LastError = null;
                logger.LogInformation("Domain {Domain} verified for publisher {PublisherId}", domain, publisherId);
            }
            else
            {
                record.Status = DomainVerificationStatus.Failed;
                record.FailedAttempts++;
                record.LastError = "Publisher name not found in dyson-domains.txt";
                logger.LogWarning("Domain {Domain} verification failed for publisher {PublisherId}: publisher name not found", domain, publisherId);
            }

            await db.SaveChangesAsync();
            return isVerified;
        }
        catch (TaskCanceledException)
        {
            record.Status = DomainVerificationStatus.Failed;
            record.FailedAttempts++;
            record.LastError = "Request timed out";
            await db.SaveChangesAsync();
            return false;
        }
        catch (HttpRequestException ex)
        {
            record.Status = DomainVerificationStatus.Failed;
            record.FailedAttempts++;
            record.LastError = ex.Message;
            await db.SaveChangesAsync();
            return false;
        }
    }

    public async Task VerifyPendingDomainsAsync()
    {
        var pending = await db.PublisherVerifiedDomains
            .Where(d => d.Status == DomainVerificationStatus.Pending)
            .ToListAsync();

        foreach (var domain in pending)
            await VerifyDomainAsync(domain.PublisherId, domain.Domain);
    }
}
