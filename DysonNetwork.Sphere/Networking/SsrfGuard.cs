using System.Net;
using System.Net.Sockets;

namespace DysonNetwork.Sphere.Networking;

/// <summary>
/// Protects outbound requests against server-side request forgery: it classifies
/// addresses that user-controlled fetches must never reach and refuses to dial
/// them while the connection is established. Because the check runs on the
/// address that is actually resolved and connected to, it also covers DNS
/// rebinding and redirect hops.
/// </summary>
public static class SsrfGuard
{
    /// <summary>
    /// True when the address belongs to a range that a user-controlled outbound
    /// request must never reach: loopback, private, link-local (including the
    /// cloud metadata endpoint), CGNAT, unspecified, multicast and reserved space.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        // ::ffff:10.0.0.1 style addresses are routed by the IPv4 stack, classify them as IPv4.
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                                   // 0.0.0.0/8 "this network", includes 0.0.0.0
                10 => true,                                  // 10.0.0.0/8 private
                100 => b[1] >= 64 && b[1] <= 127,            // 100.64.0.0/10 CGNAT
                127 => true,                                 // 127.0.0.0/8 loopback
                169 => b[1] == 254,                          // 169.254.0.0/16 link-local, includes 169.254.169.254
                172 => b[1] >= 16 && b[1] <= 31,             // 172.16.0.0/12 private
                192 => b[1] == 168                           // 192.168.0.0/16 private
                       || (b[1] == 0 && b[2] is 0 or 2)      // 192.0.0.0/24 reserved, 192.0.2.0/24 TEST-NET-1
                       || (b[1] == 88 && b[2] == 99),        // 192.88.99.0/24 6to4 relay anycast
                198 => b[1] is 18 or 19                      // 198.18.0.0/15 benchmarking
                       || (b[1] == 51 && b[2] == 100),       // 198.51.100.0/24 TEST-NET-2
                203 => b[1] == 0 && b[2] == 113,             // 203.0.113.0/24 TEST-NET-3
                >= 224 => true,                              // 224.0.0.0/4 multicast, 240.0.0.0/4 reserved, 255.255.255.255
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();

            if (address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6None)) return true;
            if (address.Equals(IPAddress.IPv6Loopback)) return true; // ::1
            if (address.IsIPv6LinkLocal) return true;                // fe80::/10
            if (address.IsIPv6SiteLocal) return true;                // fec0::/10 (deprecated)
            if (address.IsIPv6UniqueLocal) return true;              // fc00::/7
            if (address.IsIPv6Multicast) return true;                // ff00::/8
            if (address.IsIPv6Teredo) return true;                   // 2001::/32, tunnels IPv4
            if (b[0] == 0x20 && b[1] == 0x02) return true;           // 2002::/16 6to4, embeds IPv4

            return false;
        }

        return true; // unknown address family: fail closed
    }

    /// <summary>True when the URI uses a scheme that an outbound fetch may use.</summary>
    public static bool IsAllowedScheme(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>True when following <paramref name="next"/> downgrades an HTTPS request to plain HTTP.</summary>
    public static bool IsHttpsDowngrade(Uri current, Uri next) =>
        current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp;

    /// <summary>
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> that refuses to open a
    /// socket to a non-public address. It runs on the endpoint that is actually
    /// dialed, so redirect hops and DNS answers that change between validation
    /// and connect cannot sneak past it.
    /// </summary>
    public static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken
    )
    {
        var endpoint = context.DnsEndPoint;

        var addresses = IPAddress.TryParse(endpoint.Host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(endpoint.Host, cancellationToken);

        if (addresses.Length == 0)
            throw new HttpRequestException($"Host '{endpoint.Host}' did not resolve to any address.");

        // Fail closed when any answer is non-public, so a host that mixes public
        // and internal records is never contacted at all.
        var blocked = addresses.FirstOrDefault(IsBlockedAddress);
        if (blocked is not null)
            throw new HttpRequestException(
                $"Refusing to connect to non-public address {blocked} (host '{endpoint.Host}')."
            );

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };

            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex)
            {
                socket.Dispose();
                lastError = ex;
                if (cancellationToken.IsCancellationRequested)
                    break;
            }
        }

        throw new HttpRequestException($"Failed to connect to host '{endpoint.Host}'.", lastError);
    }

    /// <summary>
    /// Creates the primary handler for outbound HTTP clients with the connect-time
    /// guard installed. Callers that validate redirect hops themselves (the link
    /// reader) turn <paramref name="allowAutoRedirect"/> off; elsewhere redirects
    /// stay enabled but every hop still dials through <see cref="ConnectAsync"/>.
    /// </summary>
    public static SocketsHttpHandler CreateHandler(bool allowAutoRedirect = true) => new()
    {
        AllowAutoRedirect = allowAutoRedirect,
        ConnectCallback = ConnectAsync,
    };
}
