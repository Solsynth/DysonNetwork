using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Shared.Models;

[Index(nameof(DomainPattern))]
[Index(nameof(IsActive))]
public class SnDomainBlock : ModelBase
{
    public Guid Id { get; set; }

    [MaxLength(512)]
    public string DomainPattern { get; set; } = string.Empty;

    [MaxLength(16)]
    public string? Protocol { get; set; }

    public int? PortRestriction { get; set; }

    [MaxLength(256)]
    public string? Reason { get; set; }

    public int Priority { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public Guid? CreatedByAccountId { get; set; }
}

public class DomainBlockRule
{
    public string DomainPattern { get; set; } = string.Empty;
    public string? Protocol { get; set; }
    public int? Port { get; set; }
    public string? Reason { get; set; }
    public int Priority { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DomainBlockSettings
{
    public bool BlockHttpByDefault { get; set; } = true;
    public bool BlockIpAddressesByDefault { get; set; } = true;
    public bool BlockPrivateNetworksByDefault { get; set; } = true;
    public int[]? AdditionalBlockedPorts { get; set; }

    public static readonly int[] DefaultBlockedPorts = [21, 22, 23, 25, 53, 110, 143, 993, 995, 3306, 5432, 6379, 27017];

    public static bool IsIpAddress(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        return System.Net.IPAddress.TryParse(host, out _);
    }

    public static bool IsPrivateNetwork(string host)
    {
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return false;

        var bytes = ip.GetAddressBytes();

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   bytes[0] == 127;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal ||
                   (bytes.Length >= 2 && bytes[0] == 0xfc && bytes[1] == 0x00);
        }

        return false;
    }
}
