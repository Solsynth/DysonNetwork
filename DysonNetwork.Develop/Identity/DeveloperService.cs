using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Develop.Identity;

public class DeveloperService(AppDatabase db, PublisherService.PublisherServiceClient ps, ILogger<DeveloperService> logger)
{
    public async Task<Developer> LoadDeveloperPublisher(Developer developer)
    {
        var pubResponse = await ps.GetPublisherAsync(new GetPublisherRequest { Id = developer.PublisherId.ToString() });
        developer.Publisher = PublisherInfo.FromProto(pubResponse.Publisher);
        return developer;
    }


    public async Task<IEnumerable<Developer>> LoadDeveloperPublisher(IEnumerable<Developer> developers)
    {
        var enumerable = developers.ToList();
        var pubIds = enumerable.Select(d => d.PublisherId).ToList();
        var pubRequest = new GetPublisherBatchRequest();
        pubIds.ForEach(x => pubRequest.Ids.Add(x.ToString()));
        var pubResponse = await ps.GetPublisherBatchAsync(pubRequest);
        var pubs = pubResponse.Publishers.ToDictionary(p => Guid.Parse(p.Id), PublisherInfo.FromProto);

        return enumerable.Select(d =>
        {
            d.Publisher = pubs[d.PublisherId];
            return d;
        });
    }

    public async Task<Developer?> GetDeveloperByName(string name)
    {
        try
        {
            var pubResponse = await ps.GetPublisherAsync(new GetPublisherRequest { Name = name });
            var pubId = Guid.Parse(pubResponse.Publisher.Id);

            var developer = await db.Developers.FirstOrDefaultAsync(d => d.Id == pubId);
            return developer;
        }
        catch (RpcException ex)
        {
            logger.LogError(ex, "Developer {name} not found", name);
            return null;
        }
    }

    public async Task<bool> IsMemberWithRole(Guid pubId, Guid accountId, PublisherMemberRole role)
    {
        try
        {
            var permResponse = await ps.IsPublisherMemberAsync(new IsPublisherMemberRequest
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