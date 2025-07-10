using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Account;
using NodaTime;

namespace DysonNetwork.Sphere.Pages.Auth
{
    public class VerifyFactorModel(
        AppDatabase db,
        AccountService accounts,
        AuthService auth,
        ActionLogService als,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory
    )
        : PageModel
    {
        [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

        [BindProperty(SupportsGet = true)] public Guid FactorId { get; set; }

        [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

        [BindProperty, Required] public string Code { get; set; } = string.Empty;

        public Challenge? AuthChallenge { get; set; }
        public AccountAuthFactor? Factor { get; set; }
        public AccountAuthFactorType FactorType => Factor?.Type ?? AccountAuthFactorType.EmailCode;

        public async Task<IActionResult> OnGetAsync()
        {
            if (!string.IsNullOrEmpty(ReturnUrl))
            {
                TempData["ReturnUrl"] = ReturnUrl;
            }
            await LoadChallengeAndFactor();
            if (AuthChallenge == null) return NotFound("Challenge not found or expired.");
            if (Factor == null) return NotFound("Authentication method not found.");
            if (AuthChallenge.StepRemain == 0) return await ExchangeTokenAndRedirect(AuthChallenge);

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!string.IsNullOrEmpty(ReturnUrl))
            {
                TempData["ReturnUrl"] = ReturnUrl;
            }
            if (!ModelState.IsValid)
            {
                await LoadChallengeAndFactor();
                return Page();
            }

            await LoadChallengeAndFactor();
            if (AuthChallenge == null) return NotFound("Challenge not found or expired.");
            if (Factor == null) return NotFound("Authentication method not found.");

            if (AuthChallenge.BlacklistFactors.Contains(Factor.Id))
            {
                ModelState.AddModelError(string.Empty, "This authentication method has already been used for this challenge.");
                return Page();
            }

            try
            {
                if (await accounts.VerifyFactorCode(Factor, Code))
                {
                    AuthChallenge.StepRemain -= Factor.Trustworthy;
                    AuthChallenge.StepRemain = Math.Max(0, AuthChallenge.StepRemain);
                    AuthChallenge.BlacklistFactors.Add(Factor.Id);
                    db.Update(AuthChallenge);

                    als.CreateActionLogFromRequest(ActionLogType.ChallengeSuccess,
                        new Dictionary<string, object>
                        {
                            { "challenge_id", AuthChallenge.Id },
                            { "factor_id", Factor?.Id.ToString() ?? string.Empty }
                        }, Request, AuthChallenge.Account);

                    await db.SaveChangesAsync();

                    if (AuthChallenge.StepRemain == 0)
                    {
                        als.CreateActionLogFromRequest(ActionLogType.NewLogin,
                            new Dictionary<string, object>
                            {
                                { "challenge_id", AuthChallenge.Id },
                                { "account_id", AuthChallenge.AccountId }
                            }, Request, AuthChallenge.Account);

                        return await ExchangeTokenAndRedirect(AuthChallenge);
                    }

                    else
                    {
                        // If more steps are needed, redirect back to select factor
                        return RedirectToPage("SelectFactor", new { id = Id, returnUrl = ReturnUrl });
                    }
                }
                else
                {
                    throw new InvalidOperationException("Invalid verification code.");
                }
            }
            catch (Exception ex)
            {
                if (AuthChallenge != null)
                {
                    AuthChallenge.FailedAttempts++;
                    db.Update(AuthChallenge);
                    await db.SaveChangesAsync();

                    als.CreateActionLogFromRequest(ActionLogType.ChallengeFailure,
                        new Dictionary<string, object>
                        {
                            { "challenge_id", AuthChallenge.Id },
                            { "factor_id", Factor?.Id.ToString() ?? string.Empty }
                        }, Request, AuthChallenge.Account);
                }


                ModelState.AddModelError(string.Empty, ex.Message);
                return Page();
            }
        }

        private async Task LoadChallengeAndFactor()
        {
            AuthChallenge = await db.AuthChallenges
                .Include(e => e.Account)
                .FirstOrDefaultAsync(e => e.Id == Id);

            if (AuthChallenge?.Account != null)
            {
                Factor = await db.AccountAuthFactors
                    .FirstOrDefaultAsync(e => e.Id == FactorId &&
                                              e.AccountId == AuthChallenge.Account.Id &&
                                              e.EnabledAt != null &&
                                              e.Trustworthy > 0);
            }
        }

        private async Task<IActionResult> ExchangeTokenAndRedirect(Challenge challenge)
        {
            await db.Entry(challenge).ReloadAsync();
            if (challenge.StepRemain != 0) return BadRequest($"Challenge not yet completed. Remaining steps: {challenge.StepRemain}");

            var session = await db.AuthSessions
                .FirstOrDefaultAsync(e => e.ChallengeId == challenge.Id);

            if (session == null)
            {
                session = new Session
                {
                    LastGrantedAt = Instant.FromDateTimeUtc(DateTime.UtcNow),
                    ExpiredAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(30)),
                    Account = challenge.Account,
                    Challenge = challenge,
                };
                db.AuthSessions.Add(session);
                await db.SaveChangesAsync();
            }

            var token = auth.CreateToken(session);
            Response.Cookies.Append(AuthConstants.CookieTokenName, token, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });

            // Redirect to the return URL if provided and valid, otherwise to the home page
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }

            // Check TempData for return URL (in case it was passed through multiple steps)
            if (TempData.TryGetValue("ReturnUrl", out var tempReturnUrl) && tempReturnUrl is string returnUrl &&
                !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToPage("/Index");
        }
    }
}