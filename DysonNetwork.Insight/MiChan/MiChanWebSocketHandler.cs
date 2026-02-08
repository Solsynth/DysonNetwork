using System.Net.WebSockets;
using DysonNetwork.Shared.Models;

namespace DysonNetwork.Insight.MiChan;

public class MiChanWebSocketHandler : IAsyncDisposable
{
    private readonly MiChanConfig _config;
    private readonly ILogger<MiChanWebSocketHandler> _logger;
    private readonly MiChanService _miChanService;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public event EventHandler<WebSocketPacket>? OnPacketReceived;
    public event EventHandler? OnConnected;
    public event EventHandler? OnDisconnected;

    public MiChanWebSocketHandler(
        MiChanConfig config,
        ILogger<MiChanWebSocketHandler> logger,
        MiChanService miChanService)
    {
        _config = config;
        _logger = logger;
        _miChanService = miChanService;
    }

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            _logger.LogWarning("WebSocket is already connected");
            return;
        }

        try
        {
            _cts = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();
            
            // Add authorization header
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_config.AccessToken}");

            await _webSocket.ConnectAsync(new Uri(_config.WebSocketUrl), cancellationToken);
            
            _logger.LogInformation("WebSocket connected to {Url}", _config.WebSocketUrl);
            OnConnected?.Invoke(this, EventArgs.Empty);

            // Start receiving messages
            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect WebSocket to {Url}", _config.WebSocketUrl);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _cts?.Cancel();
            
            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure, 
                    "Disconnecting", 
                    CancellationToken.None
                );
            }

            if (_receiveTask != null)
            {
                await _receiveTask;
            }

            _webSocket?.Dispose();
            _webSocket = null;
            
            OnDisconnected?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("WebSocket disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during WebSocket disconnect");
        }
    }

    public async Task SendPacketAsync(WebSocketPacket packet, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send packet: WebSocket is not connected");
            return;
        }

        try
        {
            var bytes = packet.ToBytes();
            await _webSocket!.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Binary,
                true,
                cancellationToken
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send WebSocket packet");
            throw;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 16]; // 16KB buffer

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket!.ReceiveAsync(
                    new ArraySegment<byte>(buffer), 
                    cancellationToken
                );

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket closed by server");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    try
                    {
                        var packet = WebSocketPacket.FromBytes(buffer[..result.Count]);
                        OnPacketReceived?.Invoke(this, packet);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse WebSocket packet");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (WebSocketException ex)
        {
            _logger.LogError(ex, "WebSocket error in receive loop");
            OnDisconnected?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebSocket receive loop");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
