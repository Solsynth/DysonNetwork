using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class DeveloperService(
    AppDatabase db,
    DyPublisherService.DyPublisherServiceClient ps,
    ILogger<DeveloperService> logger)
{
    public async Task<SnDeveloper> LoadDeveloperPublisher(SnDeveloper developer)
    {
        var pubResponse = await ps.GetPublisherAsync(new DyGetPublisherRequest { Id = developer.PublisherId.ToString() });
        developer.Publisher = SnPublisher.FromProtoValue(pubResponse.Publisher);
        return developer;
    }


    public async Task<IEnumerable<SnDeveloper>> LoadDeveloperPublisher(IEnumerable<SnDeveloper> developers)
    {
        var enumerable = developers.ToList();
        var pubRequest = new DyGetPublisherBatchRequest();
        enumerable.ForEach(d => pubRequest.Ids.Add(d.PublisherId.ToString()));
        var pubResponse = await ps.GetPublisherBatchAsync(pubRequest);
        var pubs = pubResponse.Publishers.ToDictionary(p => Guid.Parse(p.Id), SnPublisher.FromProtoValue);

        foreach (var developer in enumerable)
        {
            if (pubs.TryGetValue(developer.PublisherId, out var publisher))
                developer.Publisher = publisher;
        }

        return enumerable;
    }

    public async Task<SnDeveloper?> GetDeveloperByName(string name)
    {
        try
        {
            var pubResponse = await ps.GetPublisherAsync(new DyGetPublisherRequest { Name = name });
            var pubId = Guid.Parse(pubResponse.Publisher.Id);

            var developer = await db.Developers.FirstOrDefaultAsync(d => d.PublisherId == pubId);
            return developer;
        }
        catch (RpcException ex)
        {
            logger.LogError(ex, "Developer {name} not found", name);
            return null;
        }
    }

    public async Task<SnDeveloper?> GetDeveloperById(Guid id)
    {
        return await db.Developers.FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<bool> IsMemberWithRole(Guid pubId, Guid accountId, DyPublisherMemberRole role)
    {
        try
        {
            var permResponse = await ps.IsPublisherMemberAsync(new DyIsPublisherMemberRequest
            {
                PublisherId = pubId.ToString(),
                AccountId = accountId.ToString(),
                Role = role
            });
            return permResponse.Valid;
        }
        catch (RpcException)
        {
            return false;
        }
    }
}