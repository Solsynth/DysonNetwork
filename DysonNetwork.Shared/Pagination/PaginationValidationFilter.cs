using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DysonNetwork.Shared.Pagination;

public sealed class PaginationValidationFilter(
    PaginationOptions options
) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.ActionArguments.TryGetValue("take", out var takeValue) || takeValue is not int take)
        {
            await next();
            return;
        }

        var endpoint = context.HttpContext.GetEndpoint();
        if (endpoint == null)
        {
            await next();
            return;
        }

        if (endpoint.Metadata.GetMetadata<PaginationValidationBypassAttribute>() != null)
        {
            await next();
            return;
        }

        var maxTakeAttribute = endpoint.Metadata.GetMetadata<PaginationMaxTakeAttribute>();
        int maxTake = maxTakeAttribute?.MaxTake ?? options.DefaultMaxTake;

        if (take > maxTake)
        {
            context.Result = new BadRequestObjectResult(new
            {
                error = "Validation failed",
                details = new[]
                {
                    $"The 'take' parameter ({take}) exceeds the maximum allowed value ({maxTake})."
                }
            });
            return;
        }

        await next();
    }
}
