using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Padlock.Account;

public class AccountServiceGrpc(AppDatabase db) : DyAccountService.DyAccountServiceBase
{
    public override async Task<DyListContactsResponse> ListContacts(DyListContactsRequest request,
        ServerCallContext context)
    {
        if (!Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid account ID format"));

        var query = db.AccountContacts
            .AsNoTracking()
            .Where(c => c.AccountId == accountId);

        if (request.VerifiedOnly)
            query = query.Where(c => c.VerifiedAt != null);

        if (request.Type != DyAccountContactType.Unspecified)
        {
            var contactType = request.Type switch
            {
                DyAccountContactType.DyEmail => AccountContactType.Email,
                DyAccountContactType.DyPhoneNumber => AccountContactType.PhoneNumber,
                DyAccountContactType.DyAddress => AccountContactType.Address,
                _ => AccountContactType.Email
            };
            query = query.Where(c => c.Type == contactType);
        }

        var contacts = await query.ToListAsync(context.CancellationToken);
        var response = new DyListContactsResponse();
        response.Contacts.AddRange(contacts.Select(c => c.ToProtoValue()));
        return response;
    }
}
