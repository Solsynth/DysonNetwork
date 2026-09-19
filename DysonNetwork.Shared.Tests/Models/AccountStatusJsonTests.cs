using System.Text.Json;
using DysonNetwork.Shared.Models;
using Xunit;

namespace DysonNetwork.Shared.Tests.Models;

public class AccountStatusJsonTests
{
    // Mirrors Passport's AddJsonOptions (Startup/ServiceCollectionExtensions.cs).
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly Guid AccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DeviceId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // A requester who is neither the owner nor a friend gets the field omitted
    // entirely, not an empty list.
    [Fact]
    public void OnlineDevices_WhenNull_IsOmittedFromJson()
    {
        var status = new SnAccountStatus
        {
            AccountId = AccountId,
            OnlineDevices = null,
        };

        var json = JsonSerializer.Serialize(status, JsonOptions);

        Assert.DoesNotContain("online_devices", json);
    }

    [Fact]
    public void OnlineDevices_WhenEmpty_IsSerialized()
    {
        var status = new SnAccountStatus { AccountId = AccountId };

        var json = JsonSerializer.Serialize(status, JsonOptions);

        Assert.Contains("\"online_devices\":[]", json);
    }

    [Fact]
    public void OnlineDevices_WhenPopulated_IsSerialized()
    {
        var status = new SnAccountStatus
        {
            AccountId = AccountId,
            OnlineDevices =
            [
                new SnOnlineDevice
                {
                    Id = DeviceId,
                    DeviceId = "auth-client-device-id",
                    DeviceName = "iPhone 15 Pro",
                }
            ]
        };

        var json = JsonSerializer.Serialize(status, JsonOptions);

        Assert.Contains("\"online_devices\"", json);
        Assert.Contains("iPhone 15 Pro", json);
    }
}
