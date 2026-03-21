using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using NodaTime.Serialization.Protobuf;

namespace DysonNetwork.Passport.Account;

public class MagicSpellServiceGrpc(
    AppDatabase db,
    MagicSpellService spells
) : DyMagicSpellService.DyMagicSpellServiceBase
{
    public override async Task<DyMagicSpell> CreateMagicSpell(
        DyCreateMagicSpellRequest request,
        ServerCallContext context
    )
    {
        if (request.AccountId is null || !Guid.TryParse(request.AccountId, out var accountId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A valid account id is required."));

        var spell = await spells.CreateMagicSpell(
            new SnAccount { Id = accountId },
            request.Type switch
            {
                DyMagicSpellType.DyMagicSpellAccountActivation => MagicSpellType.AccountActivation,
                DyMagicSpellType.DyMagicSpellAccountDeactivation => MagicSpellType.AccountDeactivation,
                DyMagicSpellType.DyMagicSpellAccountRemoval => MagicSpellType.AccountRemoval,
                DyMagicSpellType.DyMagicSpellAuthPasswordReset => MagicSpellType.AuthPasswordReset,
                DyMagicSpellType.DyMagicSpellContactVerification => MagicSpellType.ContactVerification,
                _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Unsupported magic spell type."))
            },
            InfraObjectCoder.ConvertFromValueMap(request.Meta)
                .Where(x => x.Value is not null)
                .ToDictionary(x => x.Key, x => x.Value!),
            request.ExpiresAt?.ToInstant(),
            request.AffectedAt?.ToInstant(),
            request.Code,
            request.PreventRepeat
        );

        return spell.ToProtoValue();
    }

    public override async Task<Empty> NotifyMagicSpell(
        DyNotifyMagicSpellRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.SpellId, out var spellId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid spell id format."));

        var spell = await db.MagicSpells
            .FirstOrDefaultAsync(x => x.Id == spellId, context.CancellationToken);
        if (spell is null)
            throw new RpcException(new Status(StatusCode.NotFound, $"Magic spell {request.SpellId} not found."));

        await spells.NotifyMagicSpell(spell, request.BypassVerify);
        return new Empty();
    }

    public override async Task<DyMagicSpell> GetMagicSpell(
        DyGetMagicSpellRequest request,
        ServerCallContext context
    )
    {
        var spell = await ResolveSpellAsync(request.Id, request.SpellWord, context.CancellationToken);
        return spell.ToProtoValue();
    }

    public override async Task<DyApplyMagicSpellResponse> ApplyMagicSpell(
        DyApplyMagicSpellRequest request,
        ServerCallContext context
    )
    {
        var spell = await ResolveSpellAsync(request.Id, request.SpellWord, context.CancellationToken);
        IReadOnlyList<string> publishedEventTypes;

        if (spell.Type == MagicSpellType.AuthPasswordReset)
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "A new password is required."));
            publishedEventTypes = await spells.ApplyPasswordReset(spell, request.NewPassword);
        }
        else
        {
            publishedEventTypes = await spells.ApplyMagicSpell(spell);
        }

        var response = new DyApplyMagicSpellResponse
        {
            Spell = spell.ToProtoValue()
        };
        response.PublishedEventTypes.AddRange(publishedEventTypes);
        return response;
    }

    private async Task<SnMagicSpell> ResolveSpellAsync(
        string? id,
        string? spellWord,
        CancellationToken cancellationToken
    )
    {
        SnMagicSpell? spell = null;

        if (!string.IsNullOrWhiteSpace(id))
        {
            if (!Guid.TryParse(id, out var spellId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Invalid spell id format."));

            spell = await db.MagicSpells.FirstOrDefaultAsync(x => x.Id == spellId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(spellWord))
        {
            spell = await db.MagicSpells.FirstOrDefaultAsync(x => x.Spell == spellWord, cancellationToken);
        }
        else
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "A spell id or spell word is required."));
        }

        return spell ?? throw new RpcException(new Status(StatusCode.NotFound, "Magic spell not found."));
    }
}
