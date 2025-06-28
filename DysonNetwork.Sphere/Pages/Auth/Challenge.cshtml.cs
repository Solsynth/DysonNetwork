using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Account;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace DysonNetwork.Sphere.Pages.Auth
{
    public class ChallengeModel(
        AppDatabase db,
        AccountService accounts,
        AuthService auth,
        ActionLogService als,
        IConfiguration configuration
    )
        : PageModel
    {
        [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

        public Challenge? AuthChallenge { get; set; }

        [BindProperty] public Guid SelectedFactorId { get; set; }

        [BindProperty] [Required] public string Secret { get; set; } = string.Empty;

        public List<SelectListItem> AuthFactors { get; set; } = new();

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadChallengeAndFactors();
            if (AuthChallenge == null) return NotFound();
            if (AuthChallenge.StepRemain == 0) return await ExchangeTokenAndRedirect();
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadChallengeAndFactors();
                return Page();
            }

            var challenge = await db.AuthChallenges.Include(e => e.Account).FirstOrDefaultAsync(e => e.Id == Id);
            if (challenge is null) return NotFound("Auth challenge was not found.");

            var factor = await db.AccountAuthFactors.FindAsync(SelectedFactorId);
            if (factor is null) return NotFound("Auth factor was not found.");
            if (factor.EnabledAt is null) return BadRequest("Auth factor is not enabled.");
            if (factor.Trustworthy <= 0) return BadRequest("Auth factor is not trustworthy.");

            if (challenge.StepRemain == 0) return Page(); // Challenge already completed
            if (challenge.ExpiredAt.HasValue && challenge.ExpiredAt.Value < Instant.FromDateTimeUtc(DateTime.UtcNow))
            {
                ModelState.AddModelError(string.Empty, "Challenge expired.");
                await LoadChallengeAndFactors();
                return Page();
            }

            try
            {
                if (await accounts.VerifyFactorCode(factor, Secret))
                {
                    challenge.StepRemain -= factor.Trustworthy;
                    challenge.StepRemain = Math.Max(0, challenge.StepRemain);
                    challenge.BlacklistFactors.Add(factor.Id);
                    db.Update(challenge);
                    als.CreateActionLogFromRequest(ActionLogType.ChallengeSuccess,
                        new Dictionary<string, object>
                        {
                            { "challenge_id", challenge.Id },
                            { "factor_id", factor.Id }
                        }, Request, challenge.Account
                    );
                }
                else
                {
                    throw new ArgumentException("Invalid password.");
                }
            }
            catch (Exception ex)
            {
                challenge.FailedAttempts++;
                db.Update(challenge);
                await db.SaveChangesAsync();
                als.CreateActionLogFromRequest(ActionLogType.ChallengeFailure,
                    new Dictionary<string, object>
                    {
                        { "challenge_id", challenge.Id },
                        { "factor_id", factor.Id }
                    }, Request, challenge.Account
                );
                ModelState.AddModelError(string.Empty, ex.Message);
                await LoadChallengeAndFactors();
                return Page();
            }

            if (challenge.StepRemain == 0)
            {
                als.CreateActionLogFromRequest(ActionLogType.NewLogin,
                    new Dictionary<string, object>
                    {
                        { "challenge_id", challenge.Id },
                        { "account_id", challenge.AccountId }
                    }, Request, challenge.Account
                );
            }

            await db.SaveChangesAsync();
            AuthChallenge = challenge;

            if (AuthChallenge.StepRemain == 0)
            {
                return await ExchangeTokenAndRedirect();
            }

            await LoadChallengeAndFactors();
            return Page();
        }

        private async Task LoadChallengeAndFactors()
        {
            var challenge = await db.AuthChallenges
                .Include(e => e.Account)
                .ThenInclude(e => e.AuthFactors)
                .FirstOrDefaultAsync(e => e.Id == Id);

            AuthChallenge = challenge;

            if (AuthChallenge != null)
            {
                var factorsResponse = AuthChallenge.Account.AuthFactors
                    .Where(e => e is { EnabledAt: not null, Trustworthy: >= 1 })
                    .ToList();

                AuthFactors = factorsResponse.Select(f => new SelectListItem
                {
                    Value = f.Id.ToString(),
                    Text = f.Type.ToString() // You might want a more user-friendly display for factor types
                }).ToList();
            }
        }

        private async Task<IActionResult> ExchangeTokenAndRedirect()
        {
            var challenge = await db.AuthChallenges
                .Include(e => e.Account)
                .Where(e => e.Id == Id)
                .FirstOrDefaultAsync();

            if (challenge is null) return BadRequest("Authorization code not found or expired.");
            if (challenge.StepRemain != 0) return BadRequest("Challenge not yet completed.");

            var session = await db.AuthSessions
                .Where(e => e.Challenge == challenge)
                .FirstOrDefaultAsync();

            if (session is not null) return BadRequest("Session already exists for this challenge.");

            session = new Session
            {
                LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(30)),
                Account = challenge.Account,
                Challenge = challenge,
            };

            db.AuthSessions.Add(session);
            await db.SaveChangesAsync();

            var tk = auth.CreateToken(session);
            HttpContext.Response.Cookies.Append(AuthConstants.CookieTokenName, tk, new CookieOptions()
            {
                HttpOnly = true,
                Secure = !configuration.GetValue<bool>("Debug"),
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });
            return RedirectToPage("/Account/Profile"); // Redirect to profile page
        }
    }
}