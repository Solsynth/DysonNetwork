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
    /// Validate an NTAG424 SUN token. Used by Padlock during NFC login flow.
    /// Verifies the CMAC, decrypts the PICCData, checks counter for replay.
    /// </summary>
    public override async Task<DyValidateNfcTokenResponse> ValidateNfcToken(
        DyValidateNfcTokenRequest request,
        ServerCallContext context)
    {
        var response = new DyValidateNfcTokenResponse();

        try
        {
            var result = await nfc.ValidateSunAsync(
                request.E,
                request.C,
                request.Mac,
                cancellationToken: context.CancellationToken);

            if (result is null)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_NOT_FOUND";
                return response;
            }

            response.IsValid = true;
            response.AccountId = result.Tag.UserId.ToString();
            response.TagId = result.Tag.Id.ToString();
            response.ErrorCode = string.Empty;
        }
        catch (InvalidOperationException)
        {
            response.IsValid = false;
            response.ErrorCode = "REPLAY";
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            response.IsValid = false;
            response.ErrorCode = "MAC_FAILED";
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
    /// Resolve an NTAG424 SUN tag and return the full user profile.
    /// Used by other services to scan NFC tags via gRPC.
    /// </summary>
    public override async Task<DyResolveNfcTagResponse> ResolveNfcTag(
        DyResolveNfcTagRequest request,
        ServerCallContext context)
    {
        var response = new DyResolveNfcTagResponse();

        try
        {
            var result = await nfc.ValidateSunAsync(
                request.E,
                request.C,
                request.Mac,
                cancellationToken: context.CancellationToken);

            if (result is null)
            {
                response.IsValid = false;
                response.ErrorCode = "TAG_NOT_FOUND";
                return response;
            }

            // Enrich the account with perks and contacts
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
        catch (System.Security.Cryptography.CryptographicException)
        {
            response.IsValid = false;
            response.ErrorCode = "MAC_FAILED";
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
