using DysonNetwork.Shared.Models;
using DysonNetwork.Shared.Queue;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Startup;

public sealed class FileMetadataReferenceUpdater(AppDatabase db)
{
    public async Task<int> ApplyAsync(FileMetadataUpdatedEvent evt, CancellationToken cancellationToken)
    {
        if (evt.File is null || string.IsNullOrWhiteSpace(evt.FileId))
            return 0;

        var changed = 0;

        foreach (var post in await db.Posts.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(post.Attachments, evt.File)) changed++;
        }

        foreach (var survey in await db.Surveys.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(survey.Attachments, evt.File)) changed++;
        }

        foreach (var publisher in await db.Publishers.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(publisher.Picture, evt.File)) changed++;
            if (Apply(publisher.Background, evt.File)) changed++;
        }

        foreach (var collection in await db.PostCollections.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(collection.Background, evt.File)) changed++;
            if (Apply(collection.Icon, evt.File)) changed++;
        }

        foreach (var sticker in await db.Stickers.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(sticker.Image, evt.File)) changed++;
        }

        foreach (var pack in await db.StickerPacks.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(pack.Icon, evt.File)) changed++;
        }

        foreach (var livestream in await db.LiveStreams.AsTracking().ToListAsync(cancellationToken))
        {
            if (Apply(livestream.Thumbnail, evt.File)) changed++;
        }

        if (changed > 0)
            await db.SaveChangesAsync(cancellationToken);

        return changed;
    }

    private static bool Apply(SnCloudFileReferenceObject? reference, FileMetadataSnapshot snapshot)
    {
        if (reference is null || !string.Equals(reference.Id, snapshot.Id, StringComparison.Ordinal))
            return false;
        if (snapshot.UpdatedAt != default && reference.UpdatedAt >= NodaTime.Instant.FromDateTimeOffset(snapshot.UpdatedAt))
            return false;
        reference.ApplyMetadata(snapshot);
        return true;
    }

    private static bool Apply(IEnumerable<SnCloudFileReferenceObject> references, FileMetadataSnapshot snapshot)
    {
        var changed = false;
        foreach (var reference in references)
            changed |= Apply(reference, snapshot);
        return changed;
    }
}
