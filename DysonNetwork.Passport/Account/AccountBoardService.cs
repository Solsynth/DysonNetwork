using System.Text.Json;
using DysonNetwork.Shared.Cache;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Passport.Account;

public class AccountBoardService(
    AppDatabase db,
    ICacheService cache,
    DyCustomAppService.DyCustomAppServiceClient customApps)
{


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
        await PurgeAccountCacheAsync(accountId);
        return materialized.OrderBy(x => x.Order).ToList();
    }

    public async Task<SnAccountBoardItem> UpdateCustomAppPayloadAsync(
        Guid accountId,
        Guid boardItemId,
        Guid customAppId,
        string customAppWidgetKey,
        Dictionary<string, object?>? payload,
        CancellationToken cancellationToken = default
    )
    {
        var item = boardItemId == Guid.Empty
            ? await db.AccountBoardItems.FirstOrDefaultAsync(
                x => x.AccountId == accountId
                     && x.Kind == SnAccountBoardItemKind.CustomApp
                     && x.CustomAppId == customAppId
                     && EF.Functions.ILike(x.CustomAppWidgetKey, customAppWidgetKey),
                cancellationToken
              )
            : await db.AccountBoardItems.FirstOrDefaultAsync(
                x => x.Id == boardItemId && x.AccountId == accountId,
                cancellationToken
              );

        if (item is null)
            throw new KeyNotFoundException("Board item not found.");
        if (item.Kind != SnAccountBoardItemKind.CustomApp)
            throw new InvalidOperationException("Only custom app board items can be updated through this endpoint.");
        if (item.CustomAppId != customAppId)
            throw new InvalidOperationException("Board item does not belong to the specified custom app.");
        if (!string.Equals(item.CustomAppWidgetKey, customAppWidgetKey, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Board item does not belong to the specified custom app widget.");

        var (ok, error, normalizedPayload) = BoardPayloadContract.ValidateAndNormalize(payload);
        if (!ok)
            throw new InvalidOperationException(error ?? "Invalid board payload.");

        item.Payload = normalizedPayload;
        await db.SaveChangesAsync(cancellationToken);
        await PurgeAccountCacheAsync(accountId);
        return item;
    }

    /// <summary>
    /// Admin-level payload update for any board item (prebuilt or custom-app).
    /// Bypasses custom-app ownership checks — the admin is acting on behalf of the user.
    /// Still validates payloads against the universal envelope contract and, for custom-app
    /// items, against the widget schema via Develop gRPC.
    /// </summary>
    public async Task<SnAccountBoardItem> AdminUpdateBoardItemPayloadAsync(
        Guid accountId,
        Guid boardItemId,
        Dictionary<string, object?>? payload,
        CancellationToken cancellationToken = default
    )
    {
        var item = await db.AccountBoardItems.FirstOrDefaultAsync(
            x => x.Id == boardItemId && x.AccountId == accountId,
            cancellationToken
        );
        if (item is null)
            throw new KeyNotFoundException("Board item not found.");

        switch (item.Kind)
        {
            case SnAccountBoardItemKind.Prebuilt:
            {
                var (ok, error, normalizedPayload) = BoardPayloadContract.ValidateAndNormalize(payload);
                if (!ok)
                    throw new InvalidOperationException(error ?? "Invalid board payload.");
                item.Payload = normalizedPayload;
                break;
            }
            case SnAccountBoardItemKind.CustomApp:
            {
                if (!item.CustomAppId.HasValue)
                    throw new InvalidOperationException("Custom app board item is missing custom_app_id.");

                var response = await customApps.ValidateBoardWidgetPayloadAsync(
                    new DyValidateBoardWidgetPayloadRequest
                    {
                        AppId = item.CustomAppId.Value.ToString(),
                        WidgetKey = item.CustomAppWidgetKey!,
                        Payload = JsonParser.Default.Parse<Struct>(JsonSerializer.Serialize(payload ?? new()))
                    },
                    cancellationToken: cancellationToken
                );

                if (!response.Valid)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(response.Message)
                        ? "Custom app board payload is invalid."
                        : response.Message);

                item.Payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(
                    JsonFormatter.Default.Format(response.NormalizedPayload),
                    Shared.Data.InfraObjectCoder.SerializerOptions
                ) ?? [];
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported board item kind '{item.Kind}'.");
        }

        await db.SaveChangesAsync(cancellationToken);
        await PurgeAccountCacheAsync(accountId);
        return item;
    }

    private Task PurgeAccountCacheAsync(Guid accountId)
    {
        return cache.RemoveGroupAsync($"{AccountService.AccountCachePrefix}{accountId}");
    }

    private async Task ValidateBoardAsync(List<SnAccountBoardItem> items, CancellationToken cancellationToken)
    {
        var duplicateOrders = items.GroupBy(x => x.Order).FirstOrDefault(x => x.Count() > 1);
        if (duplicateOrders is not null)
            throw new InvalidOperationException($"Duplicate board order '{duplicateOrders.Key}' is not allowed.");

        var singletonCustomWidgets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case SnAccountBoardItemKind.Prebuilt:
                    ValidatePrebuiltWidget(item);
                    break;
                case SnAccountBoardItemKind.CustomApp:
                    await ValidateCustomWidgetAsync(item, singletonCustomWidgets, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported board item kind '{item.Kind}'.");
            }
        }
    }

    private static void ValidatePrebuiltWidget(SnAccountBoardItem item)
    {
        // Prebuilt widget validation is controlled by the client.
        // Just clear custom-app fields to keep the data clean.
        item.CustomAppId = null;
        item.CustomAppWidgetKey = null;
    }

    private async Task ValidateCustomWidgetAsync(
        SnAccountBoardItem item,
        ISet<string> singletonCustomWidgets,
        CancellationToken cancellationToken
    )
    {
        if (!item.CustomAppId.HasValue)
            throw new InvalidOperationException("Custom app board widgets require custom_app_id.");
        if (string.IsNullOrWhiteSpace(item.CustomAppWidgetKey))
            throw new InvalidOperationException("Custom app board widgets require custom_app_widget_key.");

        var payload = item.Payload ?? [];
        var response = await customApps.ValidateBoardWidgetPayloadAsync(
            new DyValidateBoardWidgetPayloadRequest
            {
                AppId = item.CustomAppId.Value.ToString(),
                WidgetKey = item.CustomAppWidgetKey,
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
            var widgetInstanceKey = $"{item.CustomAppId.Value}:{item.CustomAppWidgetKey}";
            if (!singletonCustomWidgets.Add(widgetInstanceKey))
                throw new InvalidOperationException(
                    $"Custom app widget '{item.CustomAppId}:{item.CustomAppWidgetKey}' can only appear once."
                );
        }

        item.WidgetKey = null;
        item.CustomAppWidgetKey = response.Widget?.Key ?? item.CustomAppWidgetKey;
        item.Payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonFormatter.Default.Format(response.NormalizedPayload),
            Shared.Data.InfraObjectCoder.SerializerOptions
        ) ?? [];
    }
}
