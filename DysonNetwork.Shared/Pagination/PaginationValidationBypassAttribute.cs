namespace DysonNetwork.Shared.Pagination;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PaginationValidationBypassAttribute : Attribute;
