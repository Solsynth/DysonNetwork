using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using DysonNetwork.Sphere.Auth;
using DysonNetwork.Sphere.Account;
using NodaTime;

namespace DysonNetwork.Sphere.Pages.Auth
{
    public class VerifyFactorModel : PageModel
    {
        private readonly AppDatabase _db;
        private readonly AccountService _accounts;
        private readonly AuthService _auth;
        private readonly ActionLogService _als;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        [BindProperty(SupportsGet = true)]
        public Guid Id { get; set; }

        [BindProperty(SupportsGet = true)]
        public Guid FactorId { get; set; }
        
        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        [BindProperty, Required]
        public string Code { get; set; } = string.Empty;

        public Challenge? AuthChallenge { get; set; }
        public AccountAuthFactor? Factor { get; set; }
        public AccountAuthFactorType FactorType => Factor?.Type ?? AccountAuthFactorType.EmailCode;

        public VerifyFactorModel(
            AppDatabase db,
            AccountService accounts,
            AuthService auth,
            ActionLogService als,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _db = db;
            _accounts = accounts;
            _auth = auth;
            _als = als;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            await LoadChallengeAndFactor();
            if (AuthChallenge == null) return NotFound("Challenge not found or expired.");
            if (Factor == null) return NotFound("Authentication method not found.");
            if (AuthChallenge.StepRemain == 0) return await ExchangeTokenAndRedirect();
            
            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                await LoadChallengeAndFactor();
                return Page();
            }

            await LoadChallengeAndFactor();
            if (AuthChallenge == null) return NotFound("Challenge not found or expired.");
            if (Factor == null) return NotFound("Authentication method not found.");

            try
            {
                if (await _accounts.VerifyFactorCode(Factor, Code))
                {
                    AuthChallenge.StepRemain -= Factor.Trustworthy;
                    AuthChallenge.StepRemain = Math.Max(0, AuthChallenge.StepRemain);
                    AuthChallenge.BlacklistFactors.Add(Factor.Id);
                    _db.Update(AuthChallenge);

                    _als.CreateActionLogFromRequest(ActionLogType.ChallengeSuccess,
                        new Dictionary<string, object>
                        {
                            { "challenge_id", AuthChallenge.Id },
                            { "factor_id", Factor?.Id.ToString() ?? string.Empty }
                        }, Request, AuthChallenge.Account);

                    await _db.SaveChangesAsync();

                    if (AuthChallenge.StepRemain == 0)
                    {
                        _als.CreateActionLogFromRequest(ActionLogType.NewLogin,
                            new Dictionary<string, object>
                            {
                                { "challenge_id", AuthChallenge.Id },
                                { "account_id", AuthChallenge.AccountId }
                            }, Request, AuthChallenge.Account);

                        return await ExchangeTokenAndRedirect();
                    }

                    else
                    {
                        // If more steps are needed, redirect back to select factor
                        return RedirectToPage("SelectFactor", new { id = Id });
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
                    _db.Update(AuthChallenge);
                    await _db.SaveChangesAsync();

                    _als.CreateActionLogFromRequest(ActionLogType.ChallengeFailure,
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
            AuthChallenge = await _db.AuthChallenges
                .Include(e => e.Account)
                .FirstOrDefaultAsync(e => e.Id == Id);

            if (AuthChallenge?.Account != null)
            {
                Factor = await _db.AccountAuthFactors
                    .FirstOrDefaultAsync(e => e.Id == FactorId && 
                                           e.AccountId == AuthChallenge.Account.Id &&
                                           e.EnabledAt != null && 
                                           e.Trustworthy > 0);
            }
        }

        private async Task<IActionResult> ExchangeTokenAndRedirect()
        {
            var challenge = await _db.AuthChallenges
                .Include(e => e.Account)
                .FirstOrDefaultAsync(e => e.Id == Id);

            if (challenge == null) return BadRequest("Authorization code not found or expired.");
            if (challenge.StepRemain != 0) return BadRequest("Challenge not yet completed.");

            var session = await _db.AuthSessions
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
                _db.AuthSessions.Add(session);
                await _db.SaveChangesAsync();
            }

            var token = _auth.CreateToken(session);
            Response.Cookies.Append("access_token", token, new CookieOptions
            {
                HttpOnly = true,
                Secure = !_configuration.GetValue<bool>("Debug"),
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });

            // Redirect to the return URL if provided and valid, otherwise to the home page
            if (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }
            
            // Check TempData for return URL (in case it was passed through multiple steps)
            if (TempData.TryGetValue("ReturnUrl", out var tempReturnUrl) && tempReturnUrl is string returnUrl && !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToPage("/Index");
        }
    }
}
