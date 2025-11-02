using Quartz;
using DysonNetwork.Sphere.Publisher;

namespace DysonNetwork.Sphere.Publisher;

public class PublisherSettlementJob(PublisherService publisherService) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await publisherService.SettlePublisherRewards();
    }
}
