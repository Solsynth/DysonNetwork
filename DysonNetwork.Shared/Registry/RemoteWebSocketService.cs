using DysonNetwork.Shared.Proto;

namespace DysonNetwork.Shared.Registry;

public class RemoteWebSocketService(WebSocketService.WebSocketServiceClient client)
{
    public async Task PushWebSocketPacket(string accountId, string type, byte[] data, string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketRequest
        {
            UserId = accountId,
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await client.PushWebSocketPacketAsync(request);
    }

    public async Task PushWebSocketPacket(
        string accountId,
        string type,
        byte[] data,
        IReadOnlyCollection<string>? excludedWebSocketDeviceIds,
        string? errorMessage = null
    )
    {
        var request = new DyPushWebSocketPacketRequest
        {
            UserId = accountId,
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };

        if (excludedWebSocketDeviceIds != null)
            request.ExcludedWebsocketDeviceIds.Add(excludedWebSocketDeviceIds);

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await client.PushWebSocketPacketAsync(request);
    }

    public async Task PushWebSocketPacketToUsers(List<string> userIds, string type, byte[] data,
        string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketToUsersRequest
        {
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };
        request.UserIds.AddRange(userIds);

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await client.PushWebSocketPacketToUsersAsync(request);
    }

    public async Task PushWebSocketPacketToDevice(string deviceId, string type, byte[] data,
        string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketToDeviceRequest
        {
            DeviceId = deviceId,
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await client.PushWebSocketPacketToDeviceAsync(request);
    }

    public async Task PushWebSocketPacketToDevices(List<string> deviceIds, string type, byte[] data,
        string? errorMessage = null)
    {
        var request = new DyPushWebSocketPacketToDevicesRequest
        {
            Packet = new DyWebSocketPacket
            {
                Type = type,
                Data = Google.Protobuf.ByteString.CopyFrom(data)
            }
        };
        request.DeviceIds.AddRange(deviceIds);

        if (errorMessage != null)
            request.Packet.ErrorMessage = errorMessage;

        await client.PushWebSocketPacketToDevicesAsync(request);
    }

    public async Task<bool> GetWebsocketConnectionStatus(string deviceIdOrUserId, bool isUserId = false)
    {
        var request = new DyGetWebsocketConnectionStatusRequest();
        if (isUserId)
            request.UserId = deviceIdOrUserId;
        else
            request.DeviceId = deviceIdOrUserId;

        var response = await client.GetWebsocketConnectionStatusAsync(request);
        return response.IsConnected;
    }

    public async Task<Dictionary<string, bool>> GetWebsocketConnectionStatusBatch(List<string> userIds)
    {
        var request = new DyGetWebsocketConnectionStatusBatchRequest();
        request.UsersId.AddRange(userIds);

        var response = await client.GetWebsocketConnectionStatusBatchAsync(request);
        return response.IsConnected.ToDictionary();
    }

    public async Task<List<string>> GetAllConnectedUserIds()
    {
        var response = await client.GetAllConnectedUserIdsAsync(new Google.Protobuf.WellKnownTypes.Empty());
        return response.UserIds.ToList();
    }
}
