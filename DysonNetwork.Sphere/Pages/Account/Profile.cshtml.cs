using DysonNetwork.Sphere.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DysonNetwork.Sphere.Pages.Account;

public class ProfileModel : PageModel
{
    public DysonNetwork.Sphere.Account.Account? Account { get; set; }
    public string? AccessToken { get; set; }

    public Task<IActionResult> OnGetAsync()
    {
        if (HttpContext.Items["CurrentUser"] is not Sphere.Account.Account currentUser)
            return Task.FromResult<IActionResult>(RedirectToPage("/Auth/Login"));

        Account = currentUser;
        AccessToken = Request.Cookies.TryGetValue(AuthConstants.CookieTokenName, out var value) ? value : null;

        return Task.FromResult<IActionResult>(Page());
    }

    public IActionResult OnPostLogout()
    {
        HttpContext.Response.Cookies.Delete(AuthConstants.CookieTokenName);
        return RedirectToPage("/Auth/Login");
    }
}