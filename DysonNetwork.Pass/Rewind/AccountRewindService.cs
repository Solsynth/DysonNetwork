using System.Security.Cryptography;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Rewind;

public class AccountRewindService(
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
    Account.AccountService accounts,
    PassRewindService passRewindSrv
)
{
    private static string CapitalizeFirstLetter(string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        // Capitalize the first character and append the rest of the string in lowercase
        return char.ToUpper(str[0]) + str[1..].ToLower();
    }

    private RewindService.RewindServiceClient CreateRewindServiceClient(string serviceId)
    {
        var httpClient = httpClientFactory.CreateClient(
            $"{nameof(AccountRewindService)}+{CapitalizeFirstLetter(serviceId)}"
        );
        var channel = GrpcChannel.ForAddress($"https://_grpc.{serviceId}", new GrpcChannelOptions
        {
            HttpClient = httpClient,
        });
        return new RewindService.RewindServiceClient(channel);
    }

    private async Task<SnRewindPoint> CreateRewindPoint(Guid accountId)
    {
        const int currentYear = 2025;
        var rewindRequest = new RequestRewindEvent { AccountId = accountId.ToString(), Year = currentYear };

        var rewindEventTasks = new List<Task<RewindEvent>>
        {
            passRewindSrv.CreateRewindEvent(accountId, currentYear),
            CreateRewindServiceClient("sphere").GetRewindEventAsync(rewindRequest).ResponseAsync
        };
        var rewindEvents = await Task.WhenAll(rewindEventTasks);

        var rewindData = rewindEvents.ToDictionary<RewindEvent, string, Dictionary<string, object?>>(
            rewindEvent => rewindEvent.ServiceId,
            rewindEvent => GrpcTypeHelper.ConvertByteStringToObject<Dictionary<string, object?>>(rewindEvent.Data) ??
                           new Dictionary<string, object?>()
        );

        var point = new SnRewindPoint
        {
            SchemaVersion = 1,
            AccountId = accountId,
            Data = rewindData.ToDictionary(kvp => kvp.Key, object? (kvp) => kvp.Value)
        };

        db.RewindPoints.Add(point);
        await db.SaveChangesAsync();

        var account = await accounts.GetAccount(accountId);
        if (account is not null)
            point.Account = account; // Fill the data

        return point;
    }

    public async Task<SnRewindPoint> GetOrCreateRewindPoint(Guid accountId)
    {
        var currentYear = DateTime.UtcNow.Year;

        var existingRewind = await db.RewindPoints
            .Where(p => p.AccountId == accountId && p.Year == currentYear)
            .Include(p => p.Account)
            .ThenInclude(p => p.Profile)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        if (existingRewind is not null) return existingRewind;

        return await CreateRewindPoint(accountId);
    }

    public async Task<SnRewindPoint?> GetPublicRewindPoint(string code)
    {
        var point = await db.RewindPoints
            .Where(p => p.SharableCode == code)
            .Include(p => p.Account)
            .ThenInclude(p => p.Profile)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        return point;
    }

    public async Task<SnRewindPoint> SetRewindPointPublic(Guid accountId, int year)
    {
        var point = await db.RewindPoints
            .Where(p => p.AccountId == accountId && p.Year == year)
            .Include(p => p.Account)
            .ThenInclude(p => p.Profile)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        if (point is null) throw new InvalidOperationException("No rewind point was found.");
        point.SharableCode = _GenerateRandomString(16);
        db.RewindPoints.Update(point);
        await db.SaveChangesAsync();

        return point;
    }

    public async Task<SnRewindPoint> SetRewindPointPrivate(Guid accountId, int year)
    {
        var point = await db.RewindPoints
            .Where(p => p.AccountId == accountId && p.Year == year)
            .Include(p => p.Account)
            .ThenInclude(p => p.Profile)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        if (point is null) throw new InvalidOperationException("No rewind point was found.");
        point.SharableCode = null;
        db.RewindPoints.Update(point);
        await db.SaveChangesAsync();

        return point;
    }

    private static string _GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var result = new char[length];
        using var rng = RandomNumberGenerator.Create();
        for (var i = 0; i < length; i++)
        {
            var bytes = new byte[1];
            rng.GetBytes(bytes);
            result[i] = chars[bytes[0] % chars.Length];
        }

        return new string(result);
    }
}