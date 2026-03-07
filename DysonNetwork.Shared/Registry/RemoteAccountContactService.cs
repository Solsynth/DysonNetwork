using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Microsoft.Extensions.Logging;

namespace DysonNetwork.Shared.Registry;

public class RemoteAccountContactService(
    DyAccountService.DyAccountServiceClient accountGrpc,
    ILogger<RemoteAccountContactService> logger
)
{
    public async Task<List<SnAccountContact>> ListContactsAsync(Guid accountId, AccountContactType? type = null,
        bool verifiedOnly = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new DyListContactsRequest
            {
                AccountId = accountId.ToString(),
                VerifiedOnly = verifiedOnly,
                Type = type switch
                {
                    AccountContactType.Email => DyAccountContactType.DyEmail,
                    AccountContactType.PhoneNumber => DyAccountContactType.DyPhoneNumber,
                    AccountContactType.Address => DyAccountContactType.DyAddress,
                    _ => DyAccountContactType.Unspecified
                }
            };

            var response = await accountGrpc.ListContactsAsync(request, cancellationToken: cancellationToken);
            return response.Contacts.Select(SnAccountContact.FromProtoValue).ToList();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch account contacts from Padlock for account {AccountId}", accountId);
            return [];
        }
    }

    public async Task PopulateContactsAsync(SnAccount account, bool verifiedOnly = false,
        CancellationToken cancellationToken = default)
    {
        account.Contacts = await ListContactsAsync(account.Id, verifiedOnly: verifiedOnly,
            cancellationToken: cancellationToken);
    }
}
