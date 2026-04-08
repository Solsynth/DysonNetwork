namespace DysonNetwork.Shared.Pagination;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PaginationMaxTakeAttribute(int maxTake) : Attribute
{
    public int MaxTake { get; } = maxTake;
}
