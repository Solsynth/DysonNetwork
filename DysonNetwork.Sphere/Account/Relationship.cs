namespace DysonNetwork.Sphere.Account;

public enum RelationshipType
{
    Friend,
    Blocked
}

public class Relationship : ModelBase
{
    public long FromAccountId { get; set; }
    public Account FromAccount { get; set; } = null!;

    public long ToAccountId { get; set; }
    public Account ToAccount { get; set; } = null!;

    public RelationshipType Type { get; set; }
}