using DysonNetwork.Shared.Cache;
using Quartz;

namespace DysonNetwork.Sphere.Post;

public class PostInterestFlushHandler(IServiceProvider serviceProvider) : IFlushHandler<PostInterestSignal>
{
    public async Task FlushAsync(IReadOnlyList<PostInterestSignal> items)
    {
        using var scope = serviceProvider.CreateScope();
        var postService = scope.ServiceProvider.GetRequiredService<PostService>();
        await postService.ApplyInterestSignalsAsync(items);
    }
}

public class PostInterestFlushJob(FlushBufferService fbs, PostInterestFlushHandler hdl) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await fbs.FlushAsync(hdl);
    }
}
