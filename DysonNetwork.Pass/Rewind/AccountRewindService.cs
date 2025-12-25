using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Net.Client;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Pass.Rewind;

public class AccountRewindService(
    IHttpClientFactory httpClientFactory,
    AppDatabase db,
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
        var channel = GrpcChannel.ForAddress($"https://_grpc.{serviceId}", new GrpcChannelOptions { HttpClient = httpClient });
        return new RewindService.RewindServiceClient(channel);
    }

    private async Task<SnRewindPoint> CreateRewindPoint(Guid accountId)
    {
        var currentYear = DateTime.UtcNow.Year;
        var rewindRequest = new RequestRewindEvent { AccountId = accountId.ToString(), Year = currentYear};

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

        return point;
    }

    public async Task<SnRewindPoint> GetOrCreateRewindPoint(Guid accountId)
    {
        var currentYear = DateTime.UtcNow.Year;

        var existingRewind = await db.RewindPoints
            .Where(p => p.AccountId == accountId && p.Year == currentYear)
            .OrderBy(p => p.CreatedAt)
            .FirstOrDefaultAsync();
        if (existingRewind is not null) return existingRewind;

        return await CreateRewindPoint(accountId);
    }
}
