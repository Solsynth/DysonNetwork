using System.Collections.Concurrent;
using System.Net.WebSockets;
using dotnet_etcd.interfaces;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Pusher.Connection;

public class WebSocketService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebSocketService> _logger;
    private readonly IEtcdClient _etcdClient;
    private readonly IDictionary<string, IWebSocketPacketHandler> _handlerMap;

    public WebSocketService(
        IEnumerable<IWebSocketPacketHandler> handlers,
        IEtcdClient etcdClient,
        ILogger<WebSocketService> logger,
        IConfiguration configuration
    )
    {
        _etcdClient = etcdClient;
        _logger = logger;
        _configuration = configuration;
        _handlerMap = handlers.ToDictionary(h => h.PacketType);
    }

    private static readonly ConcurrentDictionary<
        (string AccountId, string DeviceId),
        (WebSocket Socket, CancellationTokenSource Cts)
    > ActiveConnections = new();

    private static readonly ConcurrentDictionary<string, string> ActiveSubscriptions = new(); // deviceId -> chatRoomId

    public bool TryAdd(
        (string AccountId, string DeviceId) key,
        WebSocket socket,
        CancellationTokenSource cts
    )
    {
        if (ActiveConnections.TryGetValue(key, out _))
            Disconnect(key,
                "Just connected somewhere else with the same identifier."); // Disconnect the previous one using the same identifier
        return ActiveConnections.TryAdd(key, (socket, cts));
    }

    public void Disconnect((string AccountId, string DeviceId) key, string? reason = null)
    {
        if (!ActiveConnections.TryGetValue(key, out var data)) return;
        try
        {
            data.Socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                reason ?? "Server just decided to disconnect.",
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing WebSocket for {AccountId}:{DeviceId}", key.AccountId, key.DeviceId);
        }
        data.Cts.Cancel();
        ActiveConnections.TryRemove(key, out _);
    }

    public bool GetDeviceIsConnected(string deviceId)
    {
        return ActiveConnections.Any(c => c.Key.DeviceId == deviceId);
    }

    public bool GetAccountIsConnected(string accountId)
    {
        return ActiveConnections.Any(c => c.Key.AccountId == accountId);
    }

    public void SendPacketToAccount(string userId, WebSocketPacket packet)
    {
        var connections = ActiveConnections.Where(c => c.Key.AccountId == userId);
        var packetBytes = packet.ToBytes();
        var segment = new ArraySegment<byte>(packetBytes);

        foreach (var connection in connections)
        {
            connection.Value.Socket.SendAsync(
                segment,
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
        }
    }

    public void SendPacketToDevice(string deviceId, WebSocketPacket packet)
    {
        var connections = ActiveConnections.Where(c => c.Key.DeviceId == deviceId);
        var packetBytes = packet.ToBytes();
        var segment = new ArraySegment<byte>(packetBytes);

        foreach (var connection in connections)
        {
            connection.Value.Socket.SendAsync(
                segment,
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
        }
    }

    public async Task HandlePacket(
        Account currentUser,
        string deviceId,
        WebSocketPacket packet,
        WebSocket socket
    )
    {
        if (packet.Type == WebSocketPacketType.Ping)
        {
            await socket.SendAsync(
                new ArraySegment<byte>(new WebSocketPacket
                {
                    Type = WebSocketPacketType.Pong
                }.ToBytes()),
                WebSocketMessageType.Binary,
                true,
                CancellationToken.None
            );
            return;
        }

        if (_handlerMap.TryGetValue(packet.Type, out var handler))
        {
            await handler.HandleAsync(currentUser, deviceId, packet, socket, this);
            return;
        }

        if (packet.Endpoint is not null)
        {
            try
            {
                // Get the service URL from etcd for the specified endpoint
                var serviceKey = $"/services/{packet.Endpoint}";
                var response = await _etcdClient.GetAsync(serviceKey);

                if (response.Kvs.Count > 0)
                {
                    var serviceUrl = response.Kvs[0].Value.ToStringUtf8();

                    var clientCertPath = _configuration["Service:ClientCert"]!;
                    var clientKeyPath = _configuration["Service:ClientKey"]!;
                    var clientCertPassword = _configuration["Service:CertPassword"];

                    var callInvoker =
                        GrpcClientHelper.CreateCallInvoker(
                            serviceUrl,
                            clientCertPath,
                            clientKeyPath,
                            clientCertPassword
                        );
                    var client = new PusherHandlerService.PusherHandlerServiceClient(callInvoker);

                    try
                    {
                        await client.ReceiveWebSocketPacketAsync(new ReceiveWebSocketPacketRequest
                        {
                            Account = currentUser,
                            DeviceId = deviceId,
                            Packet = packet.ToProtoValue()
                        });
                    }
                    catch (RpcException ex)
                    {
                        _logger.LogError(ex, $"Error forwarding packet to endpoint: {packet.Endpoint}");
                    }

                    return;
                }

                _logger.LogWarning($"No service registered for endpoint: {packet.Endpoint}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error forwarding packet to endpoint: {packet.Endpoint}");
            }
        }

        await socket.SendAsync(
            new ArraySegment<byte>(new WebSocketPacket
            {
                Type = WebSocketPacketType.Error,
                ErrorMessage = $"Unprocessable packet: {packet.Type}"
            }.ToBytes()),
            WebSocketMessageType.Binary,
            true,
            CancellationToken.None
        );
    }
}
