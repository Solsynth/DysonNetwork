using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;
using DysonNetwork.Shared.Data;
using DysonNetwork.Shared.Proto;
using Google.Protobuf.WellKnownTypes;
using NodaTime;

namespace DysonNetwork.Shared.Models;

public class SnPoll : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<SnPollQuestion> Questions { get; set; } = new();

    [MaxLength(1024)] public string? Title { get; set; }
    [MaxLength(4096)] public string? Description { get; set; }

    public Instant? EndedAt { get; set; }
    public bool IsAnonymous { get; set; }

    public Guid PublisherId { get; set; }
    [JsonIgnore] public SnPublisher? Publisher { get; set; }

    public Poll ToProtoValue()
    {
        var proto = new Poll
        {
            Id = Id.ToString(),
            IsAnonymous = IsAnonymous,
            PublisherId = PublisherId.ToString(),
            Publisher = Publisher?.ToProtoValue(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset()),
        };

        if (Title != null)
            proto.Title = Title;

        if (Description != null)
            proto.Description = Description;

        if (EndedAt.HasValue)
            proto.EndedAt = Timestamp.FromDateTimeOffset(EndedAt.Value.ToDateTimeOffset());

        proto.Questions.AddRange(Questions.Select(q => q.ToProtoValue()));

        if (DeletedAt.HasValue)
            proto.DeletedAt = Timestamp.FromDateTimeOffset(DeletedAt.Value.ToDateTimeOffset());

        return proto;
    }

    public static SnPoll FromProtoValue(Poll proto)
    {
        var poll = new SnPoll
        {
            Id = Guid.Parse(proto.Id),
            Title = proto.Title != null ? proto.Title : null,
            Description = proto.Description != null ? proto.Description : null,
            IsAnonymous = proto.IsAnonymous,
            PublisherId = Guid.Parse(proto.PublisherId),
            Publisher = proto.Publisher != null ? SnPublisher.FromProtoValue(proto.Publisher) : null,
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset()),
        };

        if (proto.EndedAt != null)
            poll.EndedAt = Instant.FromDateTimeOffset(proto.EndedAt.ToDateTimeOffset());

        poll.Questions.AddRange(proto.Questions.Select(SnPollQuestion.FromProtoValue));

        if (proto.DeletedAt != null)
            poll.DeletedAt = Instant.FromDateTimeOffset(proto.DeletedAt.ToDateTimeOffset());

        return poll;
    }
}

public enum PollQuestionType
{
    SingleChoice,
    MultipleChoice,
    YesNo,
    Rating,
    FreeText
}

public class SnPollQuestion : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public PollQuestionType Type { get; set; }
    [Column(TypeName = "jsonb")] public List<SnPollOption>? Options { get; set; }

    [MaxLength(1024)] public string Title { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public int Order { get; set; } = 0;
    public bool IsRequired { get; set; }

    public Guid PollId { get; set; }
    [JsonIgnore] public SnPoll Poll { get; set; } = null!;

    public PollQuestion ToProtoValue()
    {
        var proto = new PollQuestion
        {
            Id = Id.ToString(),
            Type = (DyPollQuestionType)((int)Type + 1),
            Title = Title,
            Order = Order,
            IsRequired = IsRequired,
        };

        if (Description != null)
            proto.Description = Description;

        if (Options != null)
            proto.Options.AddRange(Options.Select(o => o.ToProtoValue()));

        return proto;
    }

    public static SnPollQuestion FromProtoValue(PollQuestion proto)
    {
        var question = new SnPollQuestion
        {
            Id = Guid.Parse(proto.Id),
            Type = (PollQuestionType)((int)proto.Type - 1),
            Title = proto.Title,
            Order = proto.Order,
            IsRequired = proto.IsRequired,
        };

        if (proto.Description != null)
            question.Description = proto.Description;

        if (proto.Options.Count > 0)
            question.Options = proto.Options.Select(SnPollOption.FromProtoValue).ToList();

        return question;
    }
}

public class SnPollOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required][MaxLength(1024)] public string Label { get; set; } = null!;
    [MaxLength(4096)] public string? Description { get; set; }
    public int Order { get; set; } = 0;

    public PollOption ToProtoValue()
    {
        var proto = new PollOption
        {
            Id = Id.ToString(),
            Label = Label,
            Order = Order,
        };

        if (Description != null)
            proto.Description = Description;

        return proto;
    }

    public static SnPollOption FromProtoValue(PollOption proto)
    {
        return new SnPollOption
        {
            Id = Guid.Parse(proto.Id),
            Label = proto.Label,
            Description = proto.Description != null ? proto.Description : null,
            Order = proto.Order,
        };
    }
}

public class SnPollAnswer : ModelBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Column(TypeName = "jsonb")] public Dictionary<string, JsonElement> Answer { get; set; } = null!;

    public Guid AccountId { get; set; }
    public Guid PollId { get; set; }
    [JsonIgnore] public SnPoll? Poll { get; set; }
    [NotMapped] public SnAccount? Account { get; set; }

    public PollAnswer ToProtoValue()
    {
        var proto = new PollAnswer
        {
            Id = Id.ToString(),
            Answer = InfraObjectCoder.ConvertObjectToByteString(Answer),
            AccountId = AccountId.ToString(),
            PollId = PollId.ToString(),
            CreatedAt = Timestamp.FromDateTimeOffset(CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Timestamp.FromDateTimeOffset(UpdatedAt.ToDateTimeOffset()),
        };

        if (DeletedAt.HasValue)
            proto.DeletedAt = Timestamp.FromDateTimeOffset(DeletedAt.Value.ToDateTimeOffset());

        return proto;
    }

    public static SnPollAnswer FromProtoValue(PollAnswer proto)
    {
        var answer = new SnPollAnswer
        {
            Id = Guid.Parse(proto.Id),
            Answer = InfraObjectCoder.ConvertByteStringToObject<Dictionary<string, JsonElement>>(proto.Answer),
            AccountId = Guid.Parse(proto.AccountId),
            PollId = Guid.Parse(proto.PollId),
            CreatedAt = Instant.FromDateTimeOffset(proto.CreatedAt.ToDateTimeOffset()),
            UpdatedAt = Instant.FromDateTimeOffset(proto.UpdatedAt.ToDateTimeOffset()),
        };

        if (proto.DeletedAt != null)
            answer.DeletedAt = Instant.FromDateTimeOffset(proto.DeletedAt.ToDateTimeOffset());

        return answer;
    }
}
