using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Account;

namespace DysonNetwork.Sphere.Pages.Auth;

public class SelectFactorModel(
    AppDatabase db,
    AccountService accounts
)
    : PageModel
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }
    [BindProperty] public Guid SelectedFactorId { get; set; }
    [BindProperty] public string? Hint { get; set; }

    public Challenge? AuthChallenge { get; set; }
    public List<AccountAuthFactor> AuthFactors { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadChallengeAndFactors();
        if (AuthChallenge == null) return NotFound();
        if (AuthChallenge.StepRemain == 0) return await ExchangeTokenAndRedirect();
        return Page();
    }

    public async Task<IActionResult> OnPostSelectFactorAsync()
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == Id);

        if (challenge == null) return NotFound();

        var factor = await db.AccountAuthFactors.FindAsync(SelectedFactorId);
        if (factor?.EnabledAt == null || factor.Trustworthy <= 0)
            return BadRequest("Invalid authentication method.");

        // Store return URL in TempData to pass to the next step
        if (!string.IsNullOrEmpty(ReturnUrl))
        {
            TempData["ReturnUrl"] = ReturnUrl;
        }

        // For OTP factors that require code delivery
        try
        {
            // For OTP factors that require code delivery
            if (
                factor.Type == AccountAuthFactorType.EmailCode
                && string.IsNullOrWhiteSpace(Hint)
            )
            {
                ModelState.AddModelError(string.Empty,
                    $"Please provide a {factor.Type.ToString().ToLower().Replace("code", "")} to send the code to."
                );
                await LoadChallengeAndFactors();
                return Page();
            }

            await accounts.SendFactorCode(challenge.Account, factor, Hint);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty,
                $"An error occurred while sending the verification code: {ex.Message}");
            await LoadChallengeAndFactors();
            return Page();
        }

        // Redirect to verify page with return URL if available
        return !string.IsNullOrEmpty(ReturnUrl)
            ? RedirectToPage("VerifyFactor", new { id = Id, factorId = factor.Id, returnUrl = ReturnUrl })
            : RedirectToPage("VerifyFactor", new { id = Id, factorId = factor.Id });
    }

    private async Task LoadChallengeAndFactors()
    {
        AuthChallenge = await db.AuthChallenges
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == Id);

        if (AuthChallenge != null)
        {
            AuthFactors = await db.AccountAuthFactors
                .Where(e => e.AccountId == AuthChallenge.Account.Id)
                .Where(e => e.EnabledAt != null && e.Trustworthy >= 1)
                .ToListAsync();
        }
    }

    private async Task<IActionResult> ExchangeTokenAndRedirect()
    {
        // This method is kept for backward compatibility
        // The actual token exchange is now handled in the VerifyFactor page
        await Task.CompletedTask; // Add this to fix the async warning
        return RedirectToPage("/Account/Profile");
    }
}