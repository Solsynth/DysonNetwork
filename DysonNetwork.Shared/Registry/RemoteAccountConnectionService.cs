using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Logging;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Shared.Registry;

public class RemoteAccountConnectionService(
    DyAccountService.DyAccountServiceClient accountGrpc,
    ILogger<RemoteAccountConnectionService> logger
)
{
    public async Task<List<SnAccountConnection>> ListConnectionsAsync(Guid accountId, string? provider = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DyListConnectionsRequest
            {
                AccountId = accountId.ToString(),
                Provider = provider ?? string.Empty
            };

            var response = await accountGrpc.ListConnectionsAsync(request, cancellationToken: cancellationToken);
            return response.Connections.Select(FromProtoValue).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch account connections from Padlock for account {AccountId}", accountId);
            return [];
        }
    }

    public async Task<string?> GetValidAccessTokenAsync(
        Guid connectionId,
        string refreshToken,
        string? currentAccessToken = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DyGetValidAccessTokenRequest
            {
                ConnectionId = connectionId.ToString(),
                RefreshToken = refreshToken,
                CurrentAccessToken = currentAccessToken
            };

            var response = await accountGrpc.GetValidAccessTokenAsync(request, cancellationToken: cancellationToken);
            return response.AccessToken;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh/get access token for connection {ConnectionId}", connectionId);
            return currentAccessToken;
        }
    }

    private static SnAccountConnection FromProtoValue(DyAccountConnection proto)
    {
        return new SnAccountConnection
        {
            Id = Guid.Parse(proto.Id),
            Provider = proto.Provider,
            ProvidedIdentifier = proto.ProvidedIdentifier,
            Meta = Shared.Data.InfraObjectCoder.ConvertFromValueMap(proto.Meta)
                .ToDictionary(kv => kv.Key, kv => kv.Value),
            AccessToken = string.IsNullOrWhiteSpace(proto.AccessToken) ? null : proto.AccessToken,
            RefreshToken = string.IsNullOrWhiteSpace(proto.RefreshToken) ? null : proto.RefreshToken,
            LastUsedAt = proto.LastUsedAt?.ToInstant(),
            AccountId = Guid.Parse(proto.AccountId),
            CreatedAt = proto.CreatedAt.ToInstant(),
            UpdatedAt = proto.UpdatedAt.ToInstant(),
        };
    }
}
