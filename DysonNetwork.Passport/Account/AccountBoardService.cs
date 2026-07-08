using System.Text.Json;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

public class AccountBoardService(
    AppDatabase db,
    DyCustomAppService.DyCustomAppServiceClient customApps)
{
    private static readonly Dictionary<string, bool> PrebuiltWidgets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["badges"] = false,
        ["bio"] = false,
        ["links"] = false,
        ["notable_days"] = false,
        ["social_credits"] = false
    };

    public async Task<List<SnAccountBoardItem>> GetBoardAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await db.AccountBoardItems
            .AsNoTracking()
            .Where(x => x.AccountId == accountId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
    }

    public async Task HydrateBoardAsync(SnAccountProfile profile, CancellationToken cancellationToken = default)
    {
        profile.Board = await db.AccountBoardItems
            .AsNoTracking()
            .Where(x => x.AccountId == profile.AccountId)
            .OrderBy(x => x.Order)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<SnAccountBoardItem>> ReplaceBoardAsync(
        Guid accountId,
        IEnumerable<SnAccountBoardItem> items,
        CancellationToken cancellationToken = default
    )
    {
        var materialized = items.ToList();
        await ValidateBoardAsync(materialized, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AccountBoardItems.Where(x => x.AccountId == accountId).ExecuteDeleteAsync(cancellationToken);

        foreach (var item in materialized)
        {
            item.Id = item.Id == Guid.Empty ? Guid.NewGuid() : item.Id;
            item.AccountId = accountId;
        }

        if (materialized.Count > 0)
        {
            db.AccountBoardItems.AddRange(materialized);
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return materialized.OrderBy(x => x.Order).ToList();
    }

    private async Task ValidateBoardAsync(List<SnAccountBoardItem> items, CancellationToken cancellationToken)
    {
        var duplicateOrders = items.GroupBy(x => x.Order).FirstOrDefault(x => x.Count() > 1);
        if (duplicateOrders is not null)
            throw new InvalidOperationException($"Duplicate board order '{duplicateOrders.Key}' is not allowed.");

        var singletonPrebuiltUsage = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var singletonCustomApps = new HashSet<Guid>();

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case SnAccountBoardItemKind.Prebuilt:
                    ValidatePrebuiltWidget(item, singletonPrebuiltUsage);
                    break;
                case SnAccountBoardItemKind.CustomApp:
                    await ValidateCustomWidgetAsync(item, singletonCustomApps, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported board item kind '{item.Kind}'.");
            }
        }
    }

    private static void ValidatePrebuiltWidget(
        SnAccountBoardItem item,
        ISet<string> singletonPrebuiltUsage
    )
    {
        if (string.IsNullOrWhiteSpace(item.WidgetKey))
            throw new InvalidOperationException("Prebuilt board widgets require widget_key.");

        if (!PrebuiltWidgets.TryGetValue(item.WidgetKey, out var allowMultiple))
            throw new InvalidOperationException($"Unsupported prebuilt widget '{item.WidgetKey}'.");

        if (!allowMultiple && !singletonPrebuiltUsage.Add(item.WidgetKey))
            throw new InvalidOperationException($"Prebuilt widget '{item.WidgetKey}' can only appear once.");

        item.CustomAppId = null;
        item.Payload ??= [];
    }

    private async Task ValidateCustomWidgetAsync(
        SnAccountBoardItem item,
        ISet<Guid> singletonCustomApps,
        CancellationToken cancellationToken
    )
    {
        if (!item.CustomAppId.HasValue)
            throw new InvalidOperationException("Custom app board widgets require custom_app_id.");

        var payload = item.Payload ?? [];
        var response = await customApps.ValidateBoardWidgetPayloadAsync(
            new DyValidateBoardWidgetPayloadRequest
            {
                AppId = item.CustomAppId.Value.ToString(),
                Payload = JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(payload))
            },
            cancellationToken: cancellationToken
        );

        if (!response.Valid)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message)
                ? "Custom app board payload is invalid."
                : response.Message);

        if (response.Widget is not null && !response.Widget.AllowMultiple)
        {
            if (!singletonCustomApps.Add(item.CustomAppId.Value))
                throw new InvalidOperationException(
                    $"Custom app widget '{item.CustomAppId}' can only appear once."
                );
        }

        item.WidgetKey = null;
        item.Payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonFormatter.Default.Format(response.NormalizedPayload),
            Shared.Data.InfraObjectCoder.SerializerOptions
        ) ?? [];
    }
}
