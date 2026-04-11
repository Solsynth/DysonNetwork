using DysonNetwork.Shared.Proto;
using Google.Protobuf;

namespace DysonNetwork.Shared.Registry;

public class RemoteMlsService(DyMlsService.DyMlsServiceClient client)
{
    public async Task<SendMlsMessageResponse> SendMlsMessageAsync(
        string groupId,
        ByteString ciphertext,
        ByteString header,
        ByteString signature,
        string? clientMessageId = null,
        Dictionary<string, string>? meta = null)
    {
        var request = new SendMlsMessageRequest
        {
            GroupId = groupId,
            Ciphertext = ciphertext,
            Header = header,
            Signature = signature
        };
        if (clientMessageId is not null)
            request.ClientMessageId = clientMessageId;
        if (meta is not null)
            request.Meta.Add(meta);

        return await client.SendMlsMessageAsync(request);
    }

    public async Task<GetMlsGroupInfoResponse> GetGroupInfoAsync(string groupId)
    {
        return await client.GetGroupInfoAsync(new GetMlsGroupInfoRequest { GroupId = groupId });
    }

    public async Task<JoinMlsGroupExternalResponse> JoinGroupExternalAsync(
        string groupId,
        string senderDeviceId,
        ByteString externalCommit,
        long expectedEpoch)
    {
        return await client.JoinGroupExternalAsync(new JoinMlsGroupExternalRequest
        {
            GroupId = groupId,
            SenderDeviceId = senderDeviceId,
            ExternalCommit = externalCommit,
            ExpectedEpoch = expectedEpoch
        });
    }

    public async Task<CommitGroupChangesResponse> CommitGroupChangesAsync(
        string groupId,
        string senderDeviceId,
        CommitType type,
        IEnumerable<ByteString> proposals,
        ByteString commit,
        long expectedEpoch,
        string reason)
    {
        var request = new CommitGroupChangesRequest
        {
            GroupId = groupId,
            SenderDeviceId = senderDeviceId,
            Type = type,
            Commit = commit,
            ExpectedEpoch = expectedEpoch,
            Reason = reason
        };
        request.Proposals.AddRange(proposals.Select(p => p));

        return await client.CommitGroupChangesAsync(request);
    }

    public async Task<PublishWelcomeResponse> PublishWelcomeAsync(
        string groupId,
        string senderDeviceId,
        IEnumerable<WelcomeRecipient> recipients,
        ByteString welcomeMessage,
        long epoch)
    {
        var request = new PublishWelcomeRequest
        {
            GroupId = groupId,
            SenderDeviceId = senderDeviceId,
            WelcomeMessage = welcomeMessage,
            Epoch = epoch
        };
        request.Recipients.AddRange(recipients);

        return await client.PublishWelcomeAsync(request);
    }

    public async Task<GetMlsKeyPackagesResponse> GetKeyPackagesAsync(IEnumerable<(string accountId, string deviceId)> devices)
    {
        var request = new GetMlsKeyPackagesRequest();
        request.Devices.AddRange(devices.Select(d => new DeviceKeyPackageRequest
        {
            AccountId = d.accountId,
            DeviceId = d.deviceId
        }));

        return await client.GetKeyPackagesAsync(request);
    }

    public async Task<MarkReshareRequiredResponse> MarkReshareRequiredAsync(
        string groupId,
        string targetAccountId,
        string targetDeviceId,
        long epoch,
        string reason)
    {
        return await client.MarkReshareRequiredAsync(new MarkReshareRequiredRequest
        {
            GroupId = groupId,
            TargetAccountId = targetAccountId,
            TargetDeviceId = targetDeviceId,
            Epoch = epoch,
            Reason = reason
        });
    }

    public async Task<GetMlsGroupStateResponse> GetGroupStateAsync(string groupId)
    {
        return await client.GetGroupStateAsync(new GetMlsGroupStateRequest { GroupId = groupId });
    }

    public async Task<UploadGroupInfoResponse> UploadGroupInfoAsync(
        string groupId,
        ByteString groupInfo,
        ByteString ratchetTree)
    {
        return await client.UploadGroupInfoAsync(new UploadGroupInfoRequest
        {
            GroupId = groupId,
            GroupInfo = groupInfo,
            RatchetTree = ratchetTree
        });
    }

    public async Task<DeleteMlsGroupResponse> DeleteGroupAsync(string groupId)
    {
        return await client.DeleteGroupAsync(new DeleteMlsGroupRequest { GroupId = groupId });
    }

    public async Task<AddMlsDeviceMembershipResponse> AddMlsDeviceMembershipAsync(
        string groupId,
        string accountId,
        string deviceId,
        long epoch)
    {
        return await client.AddMlsDeviceMembershipAsync(new AddMlsDeviceMembershipRequest
        {
            GroupId = groupId,
            AccountId = accountId,
            DeviceId = deviceId,
            Epoch = epoch
        });
    }
}
