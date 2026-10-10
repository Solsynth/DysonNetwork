using System.Net;
using System.Net.Sockets;
using DysonNetwork.Sphere.Networking;
using Xunit;

namespace DysonNetwork.Sphere.Tests.Networking;

public class SsrfGuardTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.255")]
    [InlineData("0.0.0.0")]
    [InlineData("0.1.2.3")]
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("192.0.2.1")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("2002:0a00:0001::1")]
    [InlineData("2001:0:4136:e378:8000:63bf:3fff:fdd2")]
    public void IsBlockedAddress_RejectsNonPublicRanges(string address)
    {
        Assert.True(SsrfGuard.IsBlockedAddress(IPAddress.Parse(address)), address);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("11.0.0.1")]
    [InlineData("126.255.255.255")]
    [InlineData("128.0.0.1")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.63.255.255")]
    [InlineData("100.128.0.1")]
    [InlineData("169.253.0.1")]
    [InlineData("169.255.0.1")]
    [InlineData("192.167.255.255")]
    [InlineData("192.169.0.1")]
    [InlineData("223.255.255.255")]
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsBlockedAddress_AcceptsPublicRanges(string address)
    {
        Assert.False(SsrfGuard.IsBlockedAddress(IPAddress.Parse(address)), address);
    }

    [Theory]
    [InlineData("http://example.com/")]
    [InlineData("https://example.com/")]
    public void IsAllowedScheme_AcceptsHttpAndHttps(string url)
    {
        Assert.True(SsrfGuard.IsAllowedScheme(new Uri(url)));
    }

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://example.com/")]
    [InlineData("ws://example.com/")]
    public void IsAllowedScheme_RejectsOtherSchemes(string url)
    {
        Assert.False(SsrfGuard.IsAllowedScheme(new Uri(url)));
    }

    [Fact]
    public void IsHttpsDowngrade_OnlyFlagsHttpsToHttp()
    {
        Assert.True(
            SsrfGuard.IsHttpsDowngrade(new Uri("https://example.com/"), new Uri("http://example.com/"))
        );
        Assert.False(
            SsrfGuard.IsHttpsDowngrade(new Uri("https://example.com/"), new Uri("https://example.com/"))
        );
        Assert.False(
            SsrfGuard.IsHttpsDowngrade(new Uri("http://example.com/"), new Uri("http://example.com/"))
        );
    }

    [Fact]
    public void CreateHandler_KeepsRedirectBehaviourConfigurable()
    {
        using var following = SsrfGuard.CreateHandler();
        using var manual = SsrfGuard.CreateHandler(allowAutoRedirect: false);

        Assert.True(following.AllowAutoRedirect);
        Assert.False(manual.AllowAutoRedirect);
        Assert.NotNull(following.ConnectCallback);
        Assert.NotNull(manual.ConnectCallback);
    }

    [Fact]
    public async Task ConnectGuard_RefusesLoopbackListener_WithoutConnecting()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var accepted = 0;
        using var acceptCts = new CancellationTokenSource();
        var acceptLoop = Task.Run(async () =>
        {
            while (!acceptCts.IsCancellationRequested)
            {
                try
                {
                    using var client = await listener.AcceptTcpClientAsync(acceptCts.Token);
                    Interlocked.Increment(ref accepted);
                }
                catch
                {
                    return;
                }
            }
        });

        try
        {
            using var handler = SsrfGuard.CreateHandler(allowAutoRedirect: false);
            using var client = new HttpClient(handler);

            var error = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync($"http://127.0.0.1:{port}/")
            );

            Assert.Contains("non-public", error.ToString());
        }
        finally
        {
            await acceptCts.CancelAsync();
            listener.Stop();
        }

        Assert.Equal(0, Volatile.Read(ref accepted));
        await acceptLoop;
    }

    [Fact]
    public async Task ConnectGuard_RefusesHostnameThatResolvesToLoopback()
    {
        var resolved = await Dns.GetHostAddressesAsync("localhost");
        Assert.NotEmpty(resolved);
        Assert.All(resolved, address => Assert.True(SsrfGuard.IsBlockedAddress(address)));

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            using var handler = SsrfGuard.CreateHandler(allowAutoRedirect: false);
            using var client = new HttpClient(handler);

            var error = await Assert.ThrowsAsync<HttpRequestException>(
                () => client.GetAsync($"http://localhost:{port}/")
            );

            Assert.Contains("non-public", error.ToString());
        }
        finally
        {
            listener.Stop();
        }
    }
}
