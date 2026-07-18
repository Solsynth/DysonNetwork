using DysonNetwork.Shared.Capabilities;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Options;
using Xunit;

namespace DysonNetwork.Shared.Tests.Capabilities;

public sealed class CapabilityGrpcServiceTests
{
    [Fact]
    public async Task GetCapabilities_MapsCollectedFeaturesToTheSharedContract()
    {
        var registry = new CapabilityRegistry();
        registry.Replace([
            new ApiFeature("voice", 17, false, "/api/chat/voice", "Voice"),
            new ApiFeature("voice", 18, true, "/api/chat/voice-v2", "Voice V2"),
            new ApiFeature("not-yet-defined", 1, false, "/api/test", "Test"),
        ]);
        var service = new CapabilityGrpcService(
            registry,
            Options.Create(new CapabilityOptions { MinimumRevision = 16 }));

        var response = await service.GetCapabilities(new(), null!);

        Assert.Equal((uint)18, response.ApiRevision);
        Assert.Equal((uint)16, response.MinimumRevision);
        var capability = Assert.Single(response.Capabilities);
        Assert.Equal(DyCapability.Voice, capability.Capability);
        Assert.True(capability.Enabled);
        Assert.Equal((uint)18, capability.Revision);
        Assert.False(capability.Experimental);
    }
}
