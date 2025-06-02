using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Storage;

public class FileReferenceMigrationService(AppDatabase db)
{
    public async Task ScanAndMigrateReferences()
    {
        // Scan Posts for file references
        await ScanPosts();

        // Scan Messages for file references
        await ScanMessages();

        // Scan Profiles for file references
        await ScanProfiles();

        // Scan Chat entities for file references
        await ScanChatRooms();

        // Scan Realms for file references
        await ScanRealms();

        // Scan Publishers for file references
        await ScanPublishers();

        // Scan Stickers for file references
        await ScanStickers();
    }

    private async Task ScanPosts()
    {
        var posts = await db.Posts
            .Include(p => p.OutdatedAttachments)
            .ToListAsync();

        var attachmentsId = posts.SelectMany(p => p.OutdatedAttachments.Select(a => a.Id)).ToList();
        var attachments =
            await db.Files.Where(f => attachmentsId.Contains(f.Id)).ToDictionaryAsync(x => x.Id);

        var fileReferences = posts.SelectMany(post => post.Attachments.Select(attachment =>
            {
                var value = attachments.TryGetValue(attachment.Id, out var file) ? file : null;
                if (value is null) return null;
                return new CloudFileReference
                {
                    FileId = value.Id,
                    File = value,
                    Usage = "post",
                    ResourceId = post.Id.ToString(),
                    CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                    UpdatedAt = SystemClock.Instance.GetCurrentInstant()
                };
            }).Where(x => x != null).Select(x => x!)
        ).ToList();

        foreach (var post in posts)
        {
            var updatedAttachments = new List<CloudFileReferenceObject>();
            foreach (var attachment in post.OutdatedAttachments)
            {
                if (await db.Files.AnyAsync(f => f.Id == attachment.Id))
                    updatedAttachments.Add(attachment.ToReferenceObject());
                else
                    updatedAttachments.Add(attachment.ToReferenceObject());
            }

            post.Attachments = updatedAttachments;
            db.Posts.Update(post);
        }

        await db.BulkInsertAsync(fileReferences);
        await db.SaveChangesAsync();
    }

    private async Task ScanMessages()
    {
        var messages = await db.ChatMessages
            .Include(m => m.OutdatedAttachments)
            .Where(m => m.OutdatedAttachments.Any())
            .ToListAsync();

        var fileReferences = messages.SelectMany(message => message.OutdatedAttachments.Select(attachment =>
            new CloudFileReference
            {
                FileId = attachment.Id,
                File = attachment,
                Usage = "chat",
                ResourceId = message.Id.ToString(),
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                UpdatedAt = SystemClock.Instance.GetCurrentInstant()
            })
        ).ToList();

        foreach (var message in messages)
        {
            message.Attachments = message.OutdatedAttachments.Select(a => a.ToReferenceObject()).ToList();
            db.ChatMessages.Update(message);
        }

        await db.BulkInsertAsync(fileReferences);
        await db.SaveChangesAsync();
    }

    private async Task ScanProfiles()
    {
        var profiles = await db.AccountProfiles
            .Where(p => p.PictureId != null || p.BackgroundId != null)
            .ToListAsync();

        foreach (var profile in profiles)
        {
            if (profile is { PictureId: not null, Picture: null })
            {
                var avatarFile = await db.Files.FirstOrDefaultAsync(f => f.Id == profile.PictureId);
                if (avatarFile != null)
                {
                    // Create a reference for the avatar file
                    var reference = new CloudFileReference
                    {
                        FileId = avatarFile.Id,
                        File = avatarFile,
                        Usage = "profile.picture",
                        ResourceId = profile.Id.ToString()
                    };

                    await db.FileReferences.AddAsync(reference);
                    profile.Picture = avatarFile.ToReferenceObject();
                    db.AccountProfiles.Update(profile);
                }
            }

            // Also check for the banner if it exists
            if (profile is not { BackgroundId: not null, Background: null }) continue;
            var bannerFile = await db.Files.FirstOrDefaultAsync(f => f.Id == profile.BackgroundId);
            if (bannerFile == null) continue;
            {
                // Create a reference for the banner file
                var reference = new CloudFileReference
                {
                    FileId = bannerFile.Id,
                    File = bannerFile,
                    Usage = "profile.background",
                    ResourceId = profile.Id.ToString()
                };

                await db.FileReferences.AddAsync(reference);
                profile.Background = bannerFile.ToReferenceObject();
                db.AccountProfiles.Update(profile);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ScanChatRooms()
    {
        var chatRooms = await db.ChatRooms
            .Where(c => c.PictureId != null || c.BackgroundId != null)
            .ToListAsync();

        foreach (var chatRoom in chatRooms)
        {
            if (chatRoom is { PictureId: not null, Picture: null })
            {
                var avatarFile = await db.Files.FirstOrDefaultAsync(f => f.Id == chatRoom.PictureId);
                if (avatarFile != null)
                {
                    // Create a reference for the avatar file
                    var reference = new CloudFileReference
                    {
                        FileId = avatarFile.Id,
                        File = avatarFile,
                        Usage = "chatroom.picture",
                        ResourceId = chatRoom.Id.ToString()
                    };

                    await db.FileReferences.AddAsync(reference);
                    chatRoom.Picture = avatarFile.ToReferenceObject();
                    db.ChatRooms.Update(chatRoom);
                }
            }

            if (chatRoom is not { BackgroundId: not null, Background: null }) continue;
            var bannerFile = await db.Files.FirstOrDefaultAsync(f => f.Id == chatRoom.BackgroundId);
            if (bannerFile == null) continue;
            {
                // Create a reference for the banner file
                var reference = new CloudFileReference
                {
                    FileId = bannerFile.Id,
                    File = bannerFile,
                    Usage = "chatroom.background",
                    ResourceId = chatRoom.Id.ToString()
                };

                await db.FileReferences.AddAsync(reference);
                chatRoom.Background = bannerFile.ToReferenceObject();
                db.ChatRooms.Update(chatRoom);
            }
        }

        await db.SaveChangesAsync();
    }

    private async Task ScanRealms()
    {
        var realms = await db.Realms
            .Where(r => r.PictureId != null && r.BackgroundId != null)
            .ToListAsync();

        foreach (var realm in realms)
        {
            // Process avatar if it exists
            if (realm is { PictureId: not null, Picture: null })
            {
                var avatarFile = await db.Files.FirstOrDefaultAsync(f => f.Id == realm.PictureId);
                if (avatarFile != null)
                {
                    // Create a reference for the avatar file
                    var reference = new CloudFileReference
                    {
                        FileId = avatarFile.Id,
                        File = avatarFile,
                        Usage = "realm.picture",
                        ResourceId = realm.Id.ToString()
                    };

                    await db.FileReferences.AddAsync(reference);
                    realm.Picture = avatarFile.ToReferenceObject();
                }
            }

            // Process banner if it exists
            if (realm is { BackgroundId: not null, Background: null })
            {
                var bannerFile = await db.Files.FirstOrDefaultAsync(f => f.Id == realm.BackgroundId);
                if (bannerFile != null)
                {
                    // Create a reference for the banner file
                    var reference = new CloudFileReference
                    {
                        FileId = bannerFile.Id,
                        File = bannerFile,
                        Usage = "realm.background",
                        ResourceId = realm.Id.ToString()
                    };

                    await db.FileReferences.AddAsync(reference);
                    realm.Background = bannerFile.ToReferenceObject();
                }
            }

            db.Realms.Update(realm);
        }

        await db.SaveChangesAsync();
    }

    private async Task ScanPublishers()
    {
        var publishers = await db.Publishers
            .Where(p => p.PictureId != null || p.BackgroundId != null)
            .ToListAsync();

        foreach (var publisher in publishers)
        {
            if (publisher is { PictureId: not null, Picture: null })
            {
                var pictureFile = await db.Files.FirstOrDefaultAsync(f => f.Id == publisher.PictureId);
                if (pictureFile != null)
                {
                    // Create a reference for the picture file
                    var reference = new CloudFileReference
                    {
                        FileId = pictureFile.Id,
                        File = pictureFile,
                        Usage = "publisher.picture",
                        ResourceId = publisher.Id.ToString()
                    };

                    await db.FileReferences.AddAsync(reference);
                    publisher.Picture = pictureFile.ToReferenceObject();
                }
            }

            if (publisher is { BackgroundId: not null, Background: null })
            {
                var backgroundFile = await db.Files.FirstOrDefaultAsync(f => f.Id == publisher.BackgroundId);
                if (backgroundFile != null)
                {
                    // Create a reference for the background file
                    var reference = new CloudFileReference
                    {
                        FileId = backgroundFile.Id,
                        File = backgroundFile,
                        Usage = "publisher.background",
                        ResourceId = publisher.Id.ToString()
                    };

                    await db.FileReferences.AddAsync(reference);
                    publisher.Background = backgroundFile.ToReferenceObject();
                }
            }

            db.Publishers.Update(publisher);
        }

        await db.SaveChangesAsync();
    }

    private async Task ScanStickers()
    {
        var stickers = await db.Stickers
            .Where(s => s.ImageId != null && s.Image == null)
            .ToListAsync();

        foreach (var sticker in stickers)
        {
            var imageFile = await db.Files.FirstOrDefaultAsync(f => f.Id == sticker.ImageId);
            if (imageFile != null)
            {
                // Create a reference for the sticker image file
                var reference = new CloudFileReference
                {
                    FileId = imageFile.Id,
                    File = imageFile,
                    Usage = "sticker.image",
                    ResourceId = sticker.Id.ToString()
                };

                await db.FileReferences.AddAsync(reference);
                sticker.Image = imageFile.ToReferenceObject();
                db.Stickers.Update(sticker);
            }
        }

        await db.SaveChangesAsync();
    }
}