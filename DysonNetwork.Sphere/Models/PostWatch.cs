using DysonNetwork.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Models;

public enum PostWatchSource
{
    Bookmark,
    Reaction,
    Reply,
}

public enum PostWatchEvent
{
    Reactions,
    Replies,
    Chains,
    Forwards,
    Edits,
}

[Index(nameof(AccountId), nameof(Source), nameof(DeletedAt), IsUnique = true)]
public class SnPostWatchPreference : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public PostWatchSource Source { get; set; }
    public bool NotifyReactions { get; set; }
    public bool NotifyReplies { get; set; }
    public bool NotifyChains { get; set; }
    public bool NotifyForwards { get; set; }
    public bool NotifyEdits { get; set; }

    public bool Allows(PostWatchEvent evt) => evt switch
    {
        PostWatchEvent.Reactions => NotifyReactions,
        PostWatchEvent.Replies => NotifyReplies,
        PostWatchEvent.Chains => NotifyChains,
        PostWatchEvent.Forwards => NotifyForwards,
        PostWatchEvent.Edits => NotifyEdits,
        _ => false,
    };

    public static SnPostWatchPreference DefaultFor(Guid accountId, PostWatchSource source) => new()
    {
        AccountId = accountId,
        Source = source,
        NotifyReactions = source == PostWatchSource.Bookmark,
        NotifyReplies = source == PostWatchSource.Bookmark,
        NotifyChains = true,
        NotifyForwards = source == PostWatchSource.Bookmark,
        NotifyEdits = true,
    };
}
