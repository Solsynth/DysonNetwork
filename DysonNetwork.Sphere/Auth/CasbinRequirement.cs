using Casbin;
using Microsoft.AspNetCore.Authorization;

namespace DysonNetwork.Sphere.Auth;

public class CasbinRequirement(string domain, string obj, string act) : IAuthorizationRequirement
{
    public string Domain { get; } = domain;
    public string Object { get; } = obj;
    public string Action { get; } = act;
}

public class CasbinAuthorizationHandler(IEnforcer enforcer)
    : AuthorizationHandler<CasbinRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CasbinRequirement requirement)
    {
        var userId = context.User.FindFirst("user_id")?.Value;
        if (userId == null) return;
        var isSuperuser = context.User.FindFirst("is_superuser")?.Value == "1";
        if (isSuperuser) userId = "super:" + userId;

        var allowed = await enforcer.EnforceAsync(
            userId,
            requirement.Domain,
            requirement.Object,
            requirement.Action
        );

        if (allowed) context.Succeed(requirement);
    }
}