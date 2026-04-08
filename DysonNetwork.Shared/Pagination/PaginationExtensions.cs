using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace DysonNetwork.Shared.Pagination;

public static class PaginationExtensions
{
    public static PaginationOptions DefaultOptions { get; } = new();

    public static IServiceCollection AddPaginationValidation(this IServiceCollection services, int? defaultMaxTake = null)
    {
        var opts = new PaginationOptions();
        if (defaultMaxTake.HasValue)
        {
            opts.DefaultMaxTake = defaultMaxTake.Value;
        }
        services.Configure<MvcOptions>(options => options.Filters.Add(new PaginationValidationFilter(opts)));
        return services;
    }

    public static IServiceCollection AddPaginationValidation(this IServiceCollection services, PaginationOptions paginationOptions)
    {
        services.Configure<MvcOptions>(options => options.Filters.Add(new PaginationValidationFilter(paginationOptions)));
        return services;
    }

    public static IMvcBuilder AddPaginationValidationFilter(this IMvcBuilder builder, int? defaultMaxTake = null)
    {
        var opts = new PaginationOptions();
        if (defaultMaxTake.HasValue)
        {
            opts.DefaultMaxTake = defaultMaxTake.Value;
        }
        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(new PaginationValidationFilter(opts)));
        return builder;
    }

    public static IMvcBuilder AddPaginationValidationFilter(this IMvcBuilder builder, PaginationOptions paginationOptions)
    {
        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(new PaginationValidationFilter(paginationOptions)));
        return builder;
    }

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
