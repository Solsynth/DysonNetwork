using Microsoft.AspNetCore.Mvc;
using DysonNetwork.Shared.Auth;
using DysonNetwork.Shared.Networking;

namespace DysonNetwork.Padlock.Startup;

[ApiController]
[AllowAnonymous]
public class WellKnownController : ControllerBase
{
    private record ErrorCodeItem(string Code, string Name);

    [HttpGet("/.well-known/permissions")]
    [Produces("application/json")]
    public IActionResult ListPermissions()
    {
        var keys = typeof(PermissionKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => new { key = (string)f.GetRawConstantValue()!, name = f.Name })
            .OrderBy(k => k.key)
            .ToList();

        return Ok(new { count = keys.Count, permissions = keys });
    }

    [HttpGet("/.well-known/error-codes")]
    [Produces("application/json")]
    public IActionResult ListErrorCodes()
    {
        var topLevel = typeof(ErrorCodes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => new ErrorCodeItem((string)f.GetRawConstantValue()!, f.Name));

        var categories = typeof(ErrorCodes)
            .GetNestedTypes(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(t => new
            {
                category = t.Name,
                codes = t
                    .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                    .Select(f => new ErrorCodeItem((string)f.GetRawConstantValue()!, f.Name))
                    .OrderBy(c => c.Code)
                    .ToList()
            })
            .Where(c => c.codes.Count > 0)
            .OrderBy(c => c.category)
            .ToList();

        var allCodes = topLevel.Select(t => t.Code)
            .Concat(categories.SelectMany(c => c.codes.Select(cd => cd.Code)))
            .ToList();

        return Ok(new
        {
            count = allCodes.Count,
            error_codes = new
            {
                general = topLevel.OrderBy(c => c.Code).ToList(),
                categories
            }
        });
    }
}
