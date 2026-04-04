using DysonNetwork.Passport.Account;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Networking;
using DysonNetwork.Shared.Proto;
using DysonNetwork.Shared.Registry;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Nfc;

/// <summary>
/// gRPC server implementation of DyNfcService.
/// Provides NFC token validation (for Padlock login) and tag resolution via gRPC.
/// </summary>
public class NfcServiceGrpc(
    NfcService nfc,
    AppDatabase db,
    AccountService accounts,
    RemoteSubscriptionService remoteSubscription,
    RemoteAccountContactService remoteContacts,
    ILogger<NfcServiceGrpc> logger
) : DyNfcService.DyNfcServiceBase
{
    /// <summary>
    /// Validate an SDM NFC token. Used by Padlock during NFC login flow.
    /// Only claimed tags (with a valid owner) can be used for login.
    /// </summary>
    public override async Task<DyValidateNfcTokenResponse> ValidateNfcToken(
        DyValidateNfcTokenRequest request,
        ServerCallContext context)
    {
        var response = new DyValidateNfcTokenResponse();

        try
        {
            // request.UidHex contains the hex-encoded encrypted PICCData
            var uidHex = request.UidHex;
            if (string.IsNullOrWhiteSpace(uidHex))
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_NOT_FOUND";
                return response;
            }

            var result = await nfc.ValidateSunAsync(
                uidHex,
                cancellationToken: context.CancellationToken);

            if (result is null)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_NOT_FOUND";
                return response;
            }

            // Check claim status — only tags with owners can be used for login
            if (result.ClaimStatus == NfcTagClaimStatus.NeedsAuth ||
                result.ClaimStatus == NfcTagClaimStatus.Unclaimed)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_UNCLAIMED";
                return response;
            }

            if (result.ClaimStatus == NfcTagClaimStatus.PreAssignedMismatch)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_PRE_ASSIGNED";
                return response;
            }

            if (result.Account is null || result.Tag.AccountId == Guid.Empty || result.Tag.AccountId == default)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_UNCLAIMED";
                return response;
            }

            response.IsValid = true;
            response.AccountId = result.Tag.AccountId.ToString();
            response.TagId = result.Tag.Id.ToString();
            response.ErrorCode = string.Empty;
        }
        catch (InvalidOperationException)
        {
            response.IsValid = false;
            response.ErrorCode = "REPLAY";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error validating NFC token");
            response.IsValid = false;
            response.ErrorCode = "INTERNAL_ERROR";
        }

        return response;
    }

    /// <summary>
    /// Resolve an SDM NFC tag and return the full user profile.
    /// Used by other services to scan NFC tags via gRPC.
    /// </summary>
    public override async Task<DyResolveNfcTagResponse> ResolveNfcTag(
        DyResolveNfcTagRequest request,
        ServerCallContext context)
    {
        var response = new DyResolveNfcTagResponse();

        try
        {
            var uidHex = request.UidHex;
            if (string.IsNullOrWhiteSpace(uidHex))
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_NOT_FOUND";
                return response;
            }

            var result = await nfc.ValidateSunAsync(
                uidHex,
                cancellationToken: context.CancellationToken);

            if (result is null)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_NOT_FOUND";
                return response;
            }

            // Check for unclaimed/pre-assigned states
            if (result.ClaimStatus == NfcTagClaimStatus.NeedsAuth ||
                result.ClaimStatus == NfcTagClaimStatus.Unclaimed)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_UNCLAIMED";
                return response;
            }

            if (result.ClaimStatus == NfcTagClaimStatus.PreAssignedMismatch)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_PRE_ASSIGNED";
                return response;
            }

            if (result.Account is null)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_UNCLAIMED";
                return response;
            }

            await PopulatePerkSubscriptionAsync(result.Account);
            await remoteContacts.PopulateContactsAsync(result.Account, cancellationToken: context.CancellationToken);

            response.IsValid = true;
            response.Account = result.Account.ToProtoValue();
            response.Profile = result.Profile?.ToProtoValue();
            response.IsFriend = result.IsFriend;
            response.Actions.AddRange(result.Actions);
            response.ErrorCode = string.Empty;
        }
        catch (InvalidOperationException)
        {
            response.IsValid = false;
            response.ErrorCode = "REPLAY";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving NFC tag");
            response.IsValid = false;
            response.ErrorCode = "INTERNAL_ERROR";
        }

        return response;
    }

    private async Task PopulatePerkSubscriptionAsync(SnAccount account)
    {
        try
        {
            var subscription = await remoteSubscription.GetPerkSubscription(account.Id);
            if (subscription is not null)
            {
                account.PerkSubscription = SnWalletSubscription.FromProtoValue(subscription).ToReference();
                account.PerkLevel = account.PerkSubscription.PerkLevel;
            }
            else
            {
                account.PerkSubscription = null;
                account.PerkLevel = 0;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to populate PerkSubscription for account {AccountId}", account.Id);
        }
    }
}
