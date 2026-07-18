using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.ServiceDiscovery;
using Xunit;

namespace DysonNetwork.Shared.Tests.Registry;

public class BladeServiceEndpointProviderTests
{
    [Fact]
    public void AddBladeServiceDiscovery_DoesNotRegisterWhenDisabled()
    {
        var services = new ServiceCollection();

        services.AddBladeServiceDiscovery(_ => { });

        Assert.DoesNotContain(services, service => service.ServiceType == typeof(IBladeServiceDiscoveryClient));
    }

    [Fact]
    public async Task PopulateAsync_MapsGrpcLogicalNameToHealthyBladeInstances()
    {
        var discovery = new FakeDiscoveryClient(
        [
            new DyServiceInstance { GrpcEndpoint = "ring-1:7005", Healthy = true },
            new DyServiceInstance { GrpcEndpoint = "ring-2:7005", Healthy = true }
        ]);
        var factory = CreateFactory(discovery);
        Assert.True(ServiceEndpointQuery.TryParse("https://_grpc.ring", out var query));

        Assert.True(factory.TryCreateProvider(query, out var provider));

        var builder = new TestEndpointBuilder();
        await provider.PopulateAsync(builder, CancellationToken.None);

        Assert.Equal("ring", discovery.Service);
        Assert.Equal(2, builder.Endpoints.Count);
        Assert.All(builder.Endpoints, endpoint =>
        {
            var uriEndpoint = Assert.IsType<UriEndPoint>(endpoint.EndPoint);
            Assert.Equal("https", uriEndpoint.Uri.Scheme);
            Assert.Equal(7005, uriEndpoint.Uri.Port);
        });
        Assert.NotNull(builder.ChangeToken);
    }

    [Fact]
    public void TryCreateProvider_RejectsNonGrpcLogicalNames()
    {
        var factory = CreateFactory(new FakeDiscoveryClient([]));
        Assert.True(ServiceEndpointQuery.TryParse("https://ring", out var query));

        var created = factory.TryCreateProvider(query, out var provider);

        Assert.False(created);
        Assert.Null(provider);
    }

    [Fact]
    public async Task PopulateAsync_PreservesAbsoluteGrpcEndpoint()
    {
        var discovery = new FakeDiscoveryClient(
        [
            new DyServiceInstance { GrpcEndpoint = "http://ring-1:7005", Healthy = true }
        ]);
        var factory = CreateFactory(discovery);
        Assert.True(ServiceEndpointQuery.TryParse("https://_grpc.ring", out var query));
        Assert.True(factory.TryCreateProvider(query, out var provider));

        var builder = new TestEndpointBuilder();
        await provider.PopulateAsync(builder, CancellationToken.None);

        var endpoint = Assert.IsType<UriEndPoint>(Assert.Single(builder.Endpoints).EndPoint);
        Assert.Equal("http://ring-1:7005/", endpoint.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task PopulateAsync_MapsHttpLogicalNameToHealthyBladeInstances()
    {
        var discovery = new FakeDiscoveryClient(
        [
            new DyServiceInstance { HttpEndpoint = "http://ring-1:6000", Healthy = true }
        ]);
        var factory = CreateFactory(discovery);
        Assert.True(ServiceEndpointQuery.TryParse("http://_http.ring", out var query));
        Assert.True(factory.TryCreateProvider(query, out var provider));

        var builder = new TestEndpointBuilder();
        await provider.PopulateAsync(builder, CancellationToken.None);

        Assert.Equal("ring", discovery.Service);
        var endpoint = Assert.IsType<UriEndPoint>(Assert.Single(builder.Endpoints).EndPoint);
        Assert.Equal("http://ring-1:6000/", endpoint.Uri.AbsoluteUri);
    }

    private static BladeServiceDiscoveryOptions CreateOptions() => new()
    {
        ResolveCacheDuration = TimeSpan.FromSeconds(1)
    };

    private static BladeServiceEndpointProviderFactory CreateFactory(IBladeServiceDiscoveryClient discovery)
    {
        var services = new ServiceCollection();
        services.AddSingleton(discovery);
        return new BladeServiceEndpointProviderFactory(services.BuildServiceProvider(), CreateOptions());
    }

    private sealed class FakeDiscoveryClient(IReadOnlyList<DyServiceInstance> instances) : IBladeServiceDiscoveryClient
    {
        public string? Service { get; private set; }

        public Task<IReadOnlyList<DyServiceInstance>> ResolveAsync(
            string service,
            bool healthyOnly = true,
            CancellationToken cancellationToken = default)
        {
            Service = service;
            return Task.FromResult(instances);
        }
    }

    private sealed class TestEndpointBuilder : IServiceEndpointBuilder
    {
        public IList<ServiceEndpoint> Endpoints { get; } = [];

        public IFeatureCollection Features { get; } = new FeatureCollection();

        public IChangeToken? ChangeToken { get; private set; }

        public void AddChangeToken(IChangeToken changeToken)
        {
            ChangeToken = changeToken;
        }
    }
}
