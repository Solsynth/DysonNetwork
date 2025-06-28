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

    public Challenge? AuthChallenge { get; set; }
    public List<AccountAuthFactor> AuthFactors { get; set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadChallengeAndFactors();
        if (AuthChallenge == null) return NotFound();
        if (AuthChallenge.StepRemain == 0) return await ExchangeTokenAndRedirect();
        return Page();
    }

    public async Task<IActionResult> OnPostSelectFactorAsync(Guid factorId, string? hint = null)
    {
        var challenge = await db.AuthChallenges
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == Id);

        if (challenge == null) return NotFound();

        var factor = await db.AccountAuthFactors.FindAsync(factorId);
        if (factor?.EnabledAt == null || factor.Trustworthy <= 0)
            return BadRequest("Invalid authentication method.");

        // For OTP factors that require code delivery
        try
        {
            // Validate hint for factors that require it
            if (factor.Type == AccountAuthFactorType.EmailCode 
                && string.IsNullOrWhiteSpace(hint))
            {
                ModelState.AddModelError(string.Empty, $"Please provide a {factor.Type.ToString().ToLower().Replace("code", "")} to send the code to.");
                await LoadChallengeAndFactors();
                return Page();
            }

            await accounts.SendFactorCode(challenge.Account, factor, hint);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"An error occurred while sending the verification code: {ex.Message}");
            await LoadChallengeAndFactors();
            return Page();
        }

        // Redirect to verify the page with the selected factor
        return RedirectToPage("VerifyFactor", new { id = Id, factorId });
    }

    private async Task LoadChallengeAndFactors()
    {
        AuthChallenge = await db.AuthChallenges
            .Include(e => e.Account)
            .ThenInclude(e => e.AuthFactors)
            .FirstOrDefaultAsync(e => e.Id == Id);

        if (AuthChallenge != null)
        {
            AuthFactors = AuthChallenge.Account.AuthFactors
                .Where(e => e is { EnabledAt: not null, Trustworthy: >= 1 })
                .ToList();
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