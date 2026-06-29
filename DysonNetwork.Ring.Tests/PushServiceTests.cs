using DysonNetwork.Ring.Notification;
using Xunit;

namespace DysonNetwork.Ring.Tests;

public class PushServiceTests
{
    [Theory]
    [InlineData(true, 0, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, true)]
    [InlineData(false, 1, false)]
    public void ShouldQueueSopReplay_WhenNotificationIsNonSavableAndNoSopStreamIsConnected(
        bool isSavable,
        int connectedSopStreamCount,
        bool expected
    )
    {
        var connectedSopDeviceIds = Enumerable.Range(0, connectedSopStreamCount)
            .Select(i => $"device-{i}")
            .ToHashSet();

        var actual = PushService.ShouldQueueSopReplay(isSavable, connectedSopDeviceIds);

        Assert.Equal(expected, actual);
    }
}
