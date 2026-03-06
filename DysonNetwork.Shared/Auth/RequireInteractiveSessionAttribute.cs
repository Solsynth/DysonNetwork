using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;

namespace DysonNetwork.Shared.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireInteractiveSessionAttribute : Attribute, IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var currentTokenType = context.HttpContext.Items["CurrentTokenType"]?.ToString();
        if (string.Equals(currentTokenType, TokenType.ApiKey.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            context.Result = new ObjectResult("Interactive session token required.")
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return Task.CompletedTask;
        }

        return next();
    }
}
