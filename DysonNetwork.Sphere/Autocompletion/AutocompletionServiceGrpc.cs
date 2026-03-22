using System.Text.Json;
using DysonNetwork.Shared.Proto;
using Grpc.Core;

namespace DysonNetwork.Sphere.Autocompletion;

public class AutocompletionServiceGrpc(AutocompletionService aus) : DyAutocompletionService.DyAutocompletionServiceBase
{
    public override async Task<DyAutocompletionResponse> Autocomplete(DyAutocompletionRequest request, ServerCallContext context)
    {
        Guid? chatId = Guid.TryParse(request.ChatId, out var parsedChatId) ? parsedChatId : null;
        Guid? realmId = Guid.TryParse(request.RealmId, out var parsedRealmId) ? parsedRealmId : null;
        var limit = request.Limit > 0 ? request.Limit : 10;

        var results = await aus.GetAutocompletion(request.Content, chatId, realmId, limit);

        var response = new DyAutocompletionResponse();
        response.Results.AddRange(results.Select(r => new DyAutocompletionResult
        {
            Type = r.Type,
            Keyword = r.Keyword,
            Data = JsonSerializer.Serialize(r.Data)
        }));

        return response;
    }
}
