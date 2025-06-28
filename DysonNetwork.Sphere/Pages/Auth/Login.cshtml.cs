using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Account;
using DysonNetwork.Sphere.Connection;
using NodaTime;
using Microsoft.EntityFrameworkCore;

namespace DysonNetwork.Sphere.Pages.Auth
{
    public class LoginModel(
        AppDatabase db,
        AccountService accounts,
        AuthService auth,
        GeoIpService geo,
        ActionLogService als
    ) : PageModel
    {
        [BindProperty] [Required] public string Username { get; set; } = string.Empty;
        
        [BindProperty]
        [FromQuery]
        public string? ReturnUrl { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var account = await accounts.LookupAccount(Username);
            if (account is null)
            {
                ModelState.AddModelError(string.Empty, "Account was not found.");
                return Page();
            }
            
            // Store the return URL in TempData to preserve it during the login flow
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                TempData["ReturnUrl"] = ReturnUrl;
            }

            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

            var existingChallenge = await db.AuthChallenges
                .Where(e => e.Account == account)
                .Where(e => e.IpAddress == ipAddress)
                .Where(e => e.UserAgent == userAgent)
                .Where(e => e.StepRemain > 0)
                .Where(e => e.ExpiredAt != null && now < e.ExpiredAt)
                .FirstOrDefaultAsync();

            if (existingChallenge is not null)
            {
                return RedirectToPage("Challenge", new { id = existingChallenge.Id });
            }

            var challenge = new Challenge
            {
                ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddHours(1)),
                StepTotal = await auth.DetectChallengeRisk(Request, account),
                Platform = ChallengePlatform.Web,
                Audiences = new List<string>(),
                Scopes = new List<string>(),
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Location = geo.GetPointFromIp(ipAddress),
                DeviceId = "web-browser",
                AccountId = account.Id
            }.Normalize();

            await db.AuthChallenges.AddAsync(challenge);
            await db.SaveChangesAsync();

            // If we have a return URL, pass it to the verify page
            if (TempData.TryGetValue("ReturnUrl", out var returnUrl) && returnUrl is string url)
            {
                return RedirectToPage("SelectFactor", new { id = challenge.Id, returnUrl = url });
            }
            
            return RedirectToPage("SelectFactor", new { id = challenge.Id });
        }
    }
}