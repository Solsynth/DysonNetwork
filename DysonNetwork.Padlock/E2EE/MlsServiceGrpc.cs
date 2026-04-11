using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Padlock.E2EE;

public class MlsServiceGrpc(
    AppDatabase db,
    E2EeService e2ee,
    ILogger<MlsServiceGrpc> logger
) : DyMlsService.DyMlsServiceBase
{
    public override async Task<SendMlsMessageResponse> SendMlsMessage(SendMlsMessageRequest request, ServerCallContext context)
    {
        var payloads = new List<DeviceCiphertextEnvelope>
        {
            new(
                string.Empty,
                request.ClientMessageId,
                request.Ciphertext.ToByteArray(),
                request.Header.ToByteArray(),
                request.Signature.ToByteArray(),
                request.Meta.Count > 0 ? request.Meta.ToDictionary(x => x.Key, x => (object)x.Value) : null
            )
        };

        var fanoutRequest = new SendE2EeFanoutRequest(
            Guid.Empty,
            null,
            SnE2eeEnvelopeType.MlsApplication,
            request.GroupId,
            null,
            IncludeSenderCopy: false,
            payloads
        );

        var envelopes = await e2ee.SendFanoutEnvelopesAsync(Guid.Empty, string.Empty, fanoutRequest);

        var response = new SendMlsMessageResponse();
        response.Envelopes.AddRange(envelopes.Select(e => new MlsEnvelope
        {
            Id = e.Id.ToString(),
            SenderId = e.SenderId.ToString(),
            SenderDeviceId = e.SenderDeviceId,
            RecipientId = e.RecipientId.ToString(),
            RecipientDeviceId = e.RecipientDeviceId ?? string.Empty,
            Type = e.Type.ToString(),
            GroupId = e.GroupId ?? string.Empty,
            ClientMessageId = e.ClientMessageId ?? string.Empty,
            Sequence = e.Sequence,
            Ciphertext = Google.Protobuf.ByteString.CopyFrom(e.Ciphertext),
            Header = Google.Protobuf.ByteString.CopyFrom(e.Header ?? []),
            Signature = Google.Protobuf.ByteString.CopyFrom(e.Signature ?? []),
            CreatedAtUnixMs = e.CreatedAt.ToUnixTimeMilliseconds()
        }));

        return response;
    }

    public override async Task<GetMlsGroupInfoResponse> GetGroupInfo(GetMlsGroupInfoRequest request, ServerCallContext context)
    {
        var state = await e2ee.GetMlsGroupStateByGroupIdAsync(request.GroupId);
        if (state is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Group {request.GroupId} not found"));

        return new GetMlsGroupInfoResponse
        {
            GroupId = state.MlsGroupId,
            Epoch = state.Epoch,
            GroupInfo = Google.Protobuf.ByteString.CopyFrom(state.GroupInfo),
            RatchetTree = Google.Protobuf.ByteString.CopyFrom(state.RatchetTree)
        };
    }

    public override async global::System.Threading.Tasks.Task<global::DysonNetwork.Shared.Proto.UploadGroupInfoResponse> UploadGroupInfo(global::DysonNetwork.Shared.Proto.UploadGroupInfoRequest request, ServerCallContext context)
    {
        var result = await e2ee.UploadGroupInfoAsync(request.GroupId, request.GroupInfo.ToByteArray(), request.RatchetTree.ToByteArray());

        return new global::DysonNetwork.Shared.Proto.UploadGroupInfoResponse
        {
            Success = result.Success,
            GroupId = result.GroupId,
            Epoch = result.Epoch
        };
    }

    public override async Task<JoinMlsGroupExternalResponse> JoinGroupExternal(JoinMlsGroupExternalRequest request, ServerCallContext context)
    {
        try
        {
            var membership = await e2ee.MarkMlsReshareRequiredAsync(Guid.Empty, new MarkMlsReshareRequiredRequest(
                request.GroupId,
                Guid.Empty,
                request.SenderDeviceId,
                request.ExpectedEpoch,
                "external_join"
            ));

            return new JoinMlsGroupExternalResponse
            {
                Success = true,
                GroupId = request.GroupId,
                NewEpoch = membership.LastSeenEpoch ?? request.ExpectedEpoch
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to join group externally: {GroupId}", request.GroupId);
            return new JoinMlsGroupExternalResponse
            {
                Success = false,
                GroupId = request.GroupId,
                Error = ex.Message
            };
        }
    }

    public override async Task<CommitGroupChangesResponse> CommitGroupChanges(CommitGroupChangesRequest request, ServerCallContext context)
    {
        try
        {
            var commitRequest = new CommitMlsGroupRequest(
                request.GroupId,
                request.ExpectedEpoch,
                request.Reason,
                null
            );

            var state = await e2ee.CommitMlsGroupAsync(Guid.Empty, commitRequest);

            var response = new CommitGroupChangesResponse
            {
                Success = true,
                GroupId = request.GroupId,
                NewEpoch = state.Epoch
            };

            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to commit group changes: {GroupId}", request.GroupId);
            return new CommitGroupChangesResponse
            {
                Success = false,
                GroupId = request.GroupId,
                Error = ex.Message
            };
        }
    }

    public override async Task<PublishWelcomeResponse> PublishWelcome(PublishWelcomeRequest request, ServerCallContext context)
    {
        var payloads = request.Recipients.Select(r => new DeviceCiphertextEnvelope(
            r.DeviceId,
            null,
            r.EncryptedWelcome.ToByteArray(),
            null,
            null,
            null
        )).ToList();

        var fanoutRequest = new SendE2EeFanoutRequest(
            Guid.Empty,
            null,
            SnE2eeEnvelopeType.MlsWelcome,
            request.GroupId,
            null,
            IncludeSenderCopy: false,
            payloads
        );

        var envelopes = await e2ee.FanoutMlsWelcomeAsync(Guid.Empty, request.SenderDeviceId, new FanoutMlsWelcomeRequest(
            request.GroupId,
            Guid.Empty,
            null,
            payloads
        ));

        return new PublishWelcomeResponse
        {
            Epoch = request.Epoch
        };
    }

    public override async Task<GetMlsKeyPackagesResponse> GetKeyPackages(GetMlsKeyPackagesRequest request, ServerCallContext context)
    {
        var response = new GetMlsKeyPackagesResponse();

        foreach (var device in request.Devices)
        {
            if (!Guid.TryParse(device.AccountId, out var accountId))
                continue;

            var packages = await e2ee.ListMlsDeviceKeyPackagesAsync(accountId, Guid.Empty, consume: false);

            foreach (var package in packages)
            {
                response.Results.Add(new KeyPackageResult
                {
                    AccountId = package.AccountId.ToString(),
                    DeviceId = package.DeviceId,
                    KeyPackage = Google.Protobuf.ByteString.CopyFrom(package.KeyPackage),
                    Ciphersuite = package.Ciphersuite
                });
            }
        }

        return response;
    }

    public override async Task<MarkReshareRequiredResponse> MarkReshareRequired(MarkReshareRequiredRequest request, ServerCallContext context)
    {
        if (!Guid.TryParse(request.TargetAccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID"));

        await e2ee.MarkMlsReshareRequiredAsync(accountId, new MarkMlsReshareRequiredRequest(
            request.GroupId,
            accountId,
            request.TargetDeviceId,
            request.Epoch,
            request.Reason
        ));

        return new MarkReshareRequiredResponse { Success = true };
    }

    public override async Task<GetMlsGroupStateResponse> GetGroupState(GetMlsGroupStateRequest request, ServerCallContext context)
    {
        var state = await e2ee.GetMlsGroupStateByGroupIdAsync(request.GroupId);
        if (state is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Group {request.GroupId} not found"));

        return new GetMlsGroupStateResponse
        {
            GroupId = state.MlsGroupId,
            Epoch = state.Epoch,
            StateVersion = state.StateVersion,
            LastCommitAtUnixMs = state.LastCommitAt?.ToUnixTimeMilliseconds() ?? 0
        };
    }

    public override async Task<DeleteMlsGroupResponse> DeleteGroup(DeleteMlsGroupRequest request, ServerCallContext context)
    {
        var deletedCount = await e2ee.DeleteMlsGroupAsync(request.GroupId);

        return new DeleteMlsGroupResponse
        {
            Success = true,
            DeletedStateCount = deletedCount
        };
    }
}
