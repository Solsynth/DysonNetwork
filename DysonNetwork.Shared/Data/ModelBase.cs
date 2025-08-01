using NodaTime;

namespace DysonNetwork.Shared.Data;

public interface IIdentifiedResource
{
    public string ResourceIdentifier { get; }
}

public abstract class ModelBase
{
    public Instant CreatedAt { get; set; }
    public Instant UpdatedAt { get; set; }
    public Instant? DeletedAt { get; set; }
}
