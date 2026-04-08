using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DysonNetwork.Shared.Pagination;

public static class PaginationExtensions
{
    public static PaginationOptions DefaultOptions { get; } = new();

    public static void AddPaginationValidationFilter(this MvcOptions options, int? defaultMaxTake = null)
    {
        var opts = new PaginationOptions();
        if (defaultMaxTake.HasValue)
        {
            opts.DefaultMaxTake = defaultMaxTake.Value;
        }
        options.Filters.Add(new PaginationValidationFilter(opts));
    }

    public static void AddPaginationValidationFilter(this MvcOptions options, PaginationOptions paginationOptions)
    {
        options.Filters.Add(new PaginationValidationFilter(paginationOptions));
    }
}
