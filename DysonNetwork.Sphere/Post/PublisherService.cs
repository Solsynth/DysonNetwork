using DysonNetwork.Sphere.Storage;
using NodaTime;

namespace DysonNetwork.Sphere.Post;

public class PublisherService(AppDatabase db, FileService fs)
{
    public async Task<Publisher> CreateIndividualPublisher(
        Account.Account account,
        string? name,
        string? nick,
        string? bio,
        CloudFile? picture,
        CloudFile? background
    )
    {
        var publisher = new Publisher
        {
            PublisherType = PublisherType.Individual,
            Name = name ?? account.Name,
            Nick = nick ?? account.Nick,
            Bio = bio ?? account.Profile.Bio,
            Picture = picture ?? account.Profile.Picture,
            Background = background ?? account.Profile.Background,
            AccountId = account.Id,
            Members = new List<PublisherMember>
            {
                new()
                {
                    AccountId = account.Id,
                    Role = PublisherMemberRole.Owner,
                    JoinedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                }
            }
        };

        db.Publishers.Add(publisher);
        await db.SaveChangesAsync();

        if (publisher.Picture is not null) await fs.MarkUsageAsync(publisher.Picture, 1);
        if (publisher.Background is not null) await fs.MarkUsageAsync(publisher.Background, 1);

        return publisher;
    }

    // TODO Able to create organizational publisher when the realm system is completed
}